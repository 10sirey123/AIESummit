using System.Net;
using System.Text.Json;
using AIESummit.Evaluation;

namespace AIESummit.Improvement;

/// <summary>A separate WSL loopback address exposes only bounded history reads to the coding process.</summary>
public static class HistoryBridge
{
    public const string Address = "http://127.0.0.2:18112";
    public static async Task Serve(string root, string instance)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders(); builder.WebHost.UseUrls(Address);
        var app = builder.Build(); var options = new WorkbenchOptions(root);
        app.Use(async (context, next) =>
        {
            if (context.Request.Method != "GET" || context.Request.Host.Host != "127.0.0.2"
                || context.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip))
            { context.Response.StatusCode = 403; return; }
            try { await next(); }
            catch { await Results.Problem("Read-only history query failed. Check the requested view and local connection.").ExecuteAsync(context); }
        });
        app.MapGet("/health", () => new { status = "ok", instance, processId = Environment.ProcessId });
        app.MapGet("/history", async (string? kind, string? runId, string? sampleId, string? sourceSha256, int? limit) =>
        {
            var reader = await MongoHistory.OpenAsync(options.EnvFile);
            return Results.Ok(await reader.QueryAsync(kind ?? "runs", runId, sampleId, sourceSha256, limit ?? 50));
        });
        app.MapGet("/matrix/{sha}", async (string sha) =>
        {
            if (sha.Length != 64 || !sha.All(Uri.IsHexDigit)) return Results.BadRequest();
            return Results.Ok(await (await MongoHistory.OpenAsync(options.EnvFile)).MatrixAsync(sha));
        });
        app.MapGet("/attempts", async () => Results.Ok((await new AttemptStore(options).All()).TakeLast(100)));
        await app.RunAsync();
    }
}

public sealed class HistoryBridgeLease : IAsyncDisposable
{
    private readonly ProcessRunner processes;
    private readonly string root;
    private readonly string distribution;
    private readonly CancellationTokenSource cancellation;
    private readonly Task<ProcessResult> execution;
    private int pid;
    private HistoryBridgeLease(ProcessRunner processes, string root, string distribution,
        CancellationTokenSource cancellation, Task<ProcessResult> execution)
        => (this.processes, this.root, this.distribution, this.cancellation, this.execution) = (processes, root, distribution, cancellation, execution);

    public static async Task<HistoryBridgeLease> Start(ProcessRunner processes, WorkbenchOptions options, CancellationToken token)
    {
        var distribution = Environment.GetEnvironmentVariable("AIESUMMIT_CODEX_DISTRIBUTION") ?? "Ubuntu-Codex";
        var runtime = Environment.GetEnvironmentVariable("AIESUMMIT_WSL_DOTNET");
        if (runtime == null)
        {
            var found = await processes.Run("wsl.exe", ["-d", distribution, "--exec", "sh", "-lc", "command -v dotnet || printf '%s/.dotnet/dotnet' \"$HOME\""], options.Root, cancellationToken: token);
            runtime = found.Output.Trim();
        }
        var instance = Guid.NewGuid().ToString("N");
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var execution = processes.Run("wsl.exe", ["-d", distribution, "--exec", "env", "DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1",
            runtime, ImprovementService.ToWslPath(typeof(HistoryBridge).Assembly.Location), "--history-bridge", ImprovementService.ToWslPath(options.Root), instance], options.Root,
            timeoutSeconds: 1500, cancellationToken: cancellation.Token);
        var lease = new HistoryBridgeLease(processes, options.Root, distribution, cancellation, execution);
        try
        {
            for (var i = 0; i < 20; i++)
            {
                if (execution.IsCompleted) throw new InvalidDataException("The isolated history reader could not start. Check the WSL .NET 10 runtime.");
                var health = await processes.Run("wsl.exe", ["-d", distribution, "--exec", "curl", "--max-time", "2", "-fsS", HistoryBridge.Address + "/health"], options.Root,
                    timeoutSeconds: 5, cancellationToken: token);
                if (health.ExitCode == 0)
                {
                    using var doc = JsonDocument.Parse(health.Output);
                    if (doc.RootElement.GetProperty("instance").GetString() != instance)
                        throw new InvalidDataException("The isolated history port is already in use.");
                    lease.pid = doc.RootElement.GetProperty("processId").GetInt32();
                    return lease;
                }
                await Task.Delay(100, token);
            }
            throw new InvalidDataException("The isolated history reader did not become ready.");
        }
        catch { await lease.DisposeAsync(); throw; }
    }
    public async ValueTask DisposeAsync()
    {
        if (!execution.IsCompleted && pid > 0)
            await processes.Run("wsl.exe", ["-d", distribution, "--exec", "kill", "-TERM", pid.ToString(System.Globalization.CultureInfo.InvariantCulture)], root, timeoutSeconds: 5);
        cancellation.Cancel();
        try { await execution; } catch (OperationCanceledException) { }
        cancellation.Dispose();
    }
}
