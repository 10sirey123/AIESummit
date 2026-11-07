using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using AIESummit.Evaluation;
using AIESummit.Improvement;

var bridgeIndex = Array.IndexOf(args, "--history-bridge");
if (bridgeIndex >= 0)
{
    if (bridgeIndex + 2 >= args.Length) throw new InvalidDataException("The history reader requires its root and instance identity.");
    await HistoryBridge.Serve(Path.GetFullPath(args[bridgeIndex + 1]), args[bridgeIndex + 2]);
    return;
}

var mcpIndex = Array.IndexOf(args, "--mcp");
if (mcpIndex >= 0)
{
    if (mcpIndex + 1 >= args.Length) throw new InvalidDataException("MCP requires the repository root.");
    await HistoryMcp.RunAsync(Path.GetFullPath(args[mcpIndex + 1]));
    return;
}

var builder = WebApplication.CreateBuilder(args);
var root = builder.Configuration["repo"] ?? Directory.GetCurrentDirectory();
if (!File.Exists(Path.Combine(root, "AIESummit.slnx"))) root = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, ".."));
var options = new WorkbenchOptions(root);
builder.WebHost.UseUrls("http://127.0.0.1:18110");
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<ProcessRunner>();
builder.Services.AddSingleton<AttemptStore>();
builder.Services.AddSingleton<ImprovementService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<ImprovementService>());
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
var app = builder.Build();
app.Use(async (context, next) =>
{
    if (context.Connection.RemoteIpAddress is { } address && !IPAddress.IsLoopback(address)
        || context.Request.Host.Host is not ("localhost" or "127.0.0.1"))
    { context.Response.StatusCode = 403; return; }
    if (context.Request.Method != "GET" && context.Request.Method != "HEAD")
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (origin.Length > 0 && origin != "http://" + context.Request.Host
            || !context.Request.HasJsonContentType())
        { context.Response.StatusCode = 403; return; }
    }
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'";
    try { await next(); }
    catch (KeyNotFoundException e) { await Results.NotFound(new { error = e.Message }).ExecuteAsync(context); }
    catch (InvalidDataException e) { await Results.Conflict(new { error = e.Message }).ExecuteAsync(context); }
    catch (Exception e)
    {
        app.Logger.LogWarning("Workbench request failed ({Type})", e.GetType().Name);
        await Results.Problem("The workbench could not complete this request. Check local services and try again.").ExecuteAsync(context);
    }
});
app.UseDefaultFiles(); app.UseStaticFiles();
app.MapGet("/health", () => new { status = "ok", service = "AIESummit Improvement" });
app.MapGet("/api/dashboard", async (AttemptStore store) =>
{
    var attempts = await store.All();
    try
    {
        var history = await MongoHistory.OpenAsync(options.EnvFile);
        var runs = await history.QueryAsync("runs", limit: 100);
        // Older retrospective runs retain their original summary; enrich presentation from verified local score artifacts.
        var displayedRuns = JsonNode.Parse(runs.GetRawText())!.AsArray();
        var baselinePath = Path.Combine(options.State, "baseline.json");
        var baselineRunId = File.Exists(baselinePath) ? JsonNode.Parse(await File.ReadAllTextAsync(baselinePath))?["runId"]?.GetValue<string>() : null;
        foreach (var run in displayedRuns)
        {
            if (run?["runId"]?.GetValue<string>() == baselineRunId && baselineRunId != null) run!["isBaseline"] = true;
            var artifactRoot = run?["artifactRoot"]?.GetValue<string>();
            if (artifactRoot == null) continue;
            var folder = Path.GetFullPath(artifactRoot);
            var permitted = Path.GetFullPath(Path.Combine(options.Root, "artifacts")) + Path.DirectorySeparatorChar;
            if (!folder.StartsWith(permitted, StringComparison.OrdinalIgnoreCase)) continue;
            var scoresFile = Path.Combine(folder, "scores.json");
            if (!File.Exists(scoresFile)) continue;
            var score = JsonNode.Parse(await File.ReadAllTextAsync(scoresFile));
            if (score?["validatedFindings"] != null) { run!["summary"] = score.DeepClone(); run["summarySource"] = "retained scores.json artifact"; }
        }
        var graph = File.Exists(options.Workflow) ? JsonNode.Parse(await File.ReadAllTextAsync(options.Workflow)) : null;
        var docs = await history.QueryAsync("documents", limit: 200);
        var documents = docs.EnumerateArray().GroupBy(d => d.TryGetProperty("sourceSha256", out var hash) ? hash.GetString() : "")
            .Where(g => !string.IsNullOrEmpty(g.Key)).Select(g =>
            {
                var d = g.First();
                return new { id = g.Key, sourceSha256 = g.Key, sampleId = d.GetProperty("sampleId").GetString(),
                    filename = d.TryGetProperty("filename", out var name) ? name.GetString() : d.GetProperty("sampleId").GetString() + ".pdf" };
            }).OrderBy(d => d.sampleId).ToArray();
        return Results.Ok(new { runs = displayedRuns, attempts, documents, graph, status = new { mongoConnected = true, model = "muse-wonder-1.0", error = (string?)null } });
    }
    catch
    {
        return Results.Ok(new { runs = Array.Empty<object>(), attempts, documents = Array.Empty<object>(),
            status = new { mongoConnected = false, model = "muse-wonder-1.0", error = "MongoDB is unavailable. Attempts remain saved locally; check the configured connection." } });
    }
});
app.MapGet("/api/history", async (string? kind, string? runId, string? sampleId, string? sourceSha256, int? limit) =>
{
    var history = await MongoHistory.OpenAsync(options.EnvFile);
    return Results.Ok(await history.QueryAsync(kind ?? "runs", runId, sampleId, sourceSha256, limit ?? 50));
});
app.MapGet("/api/documents/{id}/matrix", async (string id) =>
{
    var history = await MongoHistory.OpenAsync(options.EnvFile);
    var hash = id;
    if (id.Length != 64)
    {
        var docs = await history.QueryAsync("documents", sampleId: id, limit: 1);
        if (docs.GetArrayLength() == 0) return Results.NotFound();
        hash = docs[0].GetProperty("sourceSha256").GetString()!;
    }
    return Results.Ok(await history.MatrixAsync(hash));
});
app.MapGet("/api/runs/{id}", async (string id) =>
{
    var history = await MongoHistory.OpenAsync(options.EnvFile);
    return Results.Ok(new { runs = await history.QueryAsync("runs", runId: id), documents = await history.QueryAsync("documents", runId: id, limit: 200), steps = await history.QueryAsync("steps", runId: id, limit: 200) });
});
app.MapGet("/api/attempts/{id}", async (string id, AttemptStore store) =>
{
    var item = await store.Get(id);
    var live = Path.Combine(options.AttemptFolder(id), "live.log");
    if (File.Exists(live))
        foreach (var line in File.ReadLines(live).TakeLast(80))
        {
            try
            {
                var message = JsonNode.Parse(line);
                var text = message?["item"]?["text"]?.GetValue<string>();
                if (message?["sampleId"] != null) text = $"Document {message["sampleId"]}: {message["status"]} · {message["completed"]} completed";
                if (text != null) item.Logs.Add(text.Length > 3000 ? text[..3000] + "…" : text);
            }
            catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException) { }
        }
    return Results.Ok(item);
});
app.MapPost("/api/attempts", async (CreateAttempt request, ImprovementService service) => Results.Ok(await service.Create(request)));
app.MapPost("/api/attempts/{id}/approve", async (string id, ApproveAttempt request, ImprovementService service) => Results.Ok(await service.Approve(id, request.ProposalHash)));
app.MapPost("/api/attempts/{id}/reject", async (string id, ImprovementService service) => Results.Ok(await service.Reject(id)));
app.MapPost("/api/attempts/{id}/promote", async (string id, ImprovementService service) => Results.Ok(await service.Promote(id)));
app.Run();

public partial class Program;
