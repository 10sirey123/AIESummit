using System.Diagnostics;

namespace AIESummit.Improvement;

public sealed record ProcessResult(int ExitCode, string Output);

public sealed class ProcessRunner
{
    public async Task<ProcessResult> Run(string executable, IEnumerable<string> arguments, string cwd,
        string? input = null, Dictionary<string, string>? environment = null, Action<string>? onLine = null,
        int timeoutSeconds = 1800, CancellationToken cancellationToken = default)
    {
        var info = new ProcessStartInfo(executable) { WorkingDirectory = cwd, RedirectStandardOutput = true,
            RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in arguments) info.ArgumentList.Add(arg);
        if (environment != null) foreach (var pair in environment) info.Environment[pair.Key] = pair.Value;
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start the requested tool.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        var output = new List<string>();
        async Task Read(StreamReader reader)
        {
            while (await reader.ReadLineAsync(timeout.Token) is { } line)
            {
                lock (output) output.Add(line);
                onLine?.Invoke(line);
            }
        }
        try
        {
            var stdout = Read(process.StandardOutput); var stderr = Read(process.StandardError);
            if (input != null) await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token);
            process.StandardInput.Close();
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(timeout.Token));
            return new(process.ExitCode, string.Join('\n', output));
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
    }
}
