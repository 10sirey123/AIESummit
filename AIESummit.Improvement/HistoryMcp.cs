using System.Text.Json;
using AIESummit.Evaluation;

namespace AIESummit.Improvement;

/// <summary>Read-only stdio MCP surface for Codex. Standard output contains JSON-RPC messages only.</summary>
public static class HistoryMcp
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] Versions = ["2025-06-18", "2025-03-26", "2024-11-05"];
    private static readonly string[] Kinds = ["runs", "documents", "steps", "workflows", "expectations", "observations", "missing", "events"];
    private static readonly object ReadOnlyAnnotations = new { readOnlyHint = true, destructiveHint = false, idempotentHint = true, openWorldHint = false };

    public static async Task RunAsync(string root, CancellationToken cancellationToken = default)
    {
        var options = new WorkbenchOptions(root);
        while (!cancellationToken.IsCancellationRequested)
        {
            string? line;
            try { line = await Console.In.ReadLineAsync(cancellationToken); }
            catch (OperationCanceledException) { return; }
            if (line is null) return;
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.Length > 1_048_576) { await Error(null, -32600, "Request exceeds the supported size."); continue; }
            JsonDocument request;
            try { request = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 32 }); }
            catch (JsonException) { await Error(null, -32700, "Invalid JSON."); continue; }
            using (request)
            {
                var message = request.RootElement;
                if (message.ValueKind != JsonValueKind.Object)
                { await Error(null, -32600, "Expected a JSON-RPC request object."); continue; }
                var hasId = message.TryGetProperty("id", out var requestId);
                JsonElement? id = hasId && requestId.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.Null ? requestId.Clone() : null;
                // Notifications (including initialized/cancelled notifications) never produce responses.
                if (!hasId) continue;
                if (id is null || !message.TryGetProperty("jsonrpc", out var version) || version.ValueKind != JsonValueKind.String || version.GetString() != "2.0"
                    || !message.TryGetProperty("method", out var method) || method.ValueKind != JsonValueKind.String)
                { await Error(id, -32600, "Invalid JSON-RPC request."); continue; }
                try
                {
                    var parameters = message.TryGetProperty("params", out var supplied) && supplied.ValueKind == JsonValueKind.Object ? supplied : default;
                    switch (method.GetString())
                    {
                        case "initialize":
                            var requested = String(parameters, "protocolVersion");
                            await Result(id, new {
                                protocolVersion = requested != null && Versions.Contains(requested, StringComparer.Ordinal) ? requested : Versions[0],
                                capabilities = new { tools = new { listChanged = false } },
                                serverInfo = new { name = "aiesummit-history", version = "1.0.0" },
                                instructions = "Read-only development benchmark and attempt history. Found means evidence-validated target presence. Confirmation details are excluded."
                            });
                            break;
                        case "ping": await Result(id, new { }); break;
                        case "tools/list": await Result(id, new { tools = Tools() }); break;
                        case "tools/call":
                            var name = String(parameters, "name");
                            var arguments = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("arguments", out var args)
                                && args.ValueKind == JsonValueKind.Object ? args : default;
                            try
                            {
                                object output;
                                switch (name)
                                {
                                    case "query_history":
                                        var kind = String(arguments, "kind") ?? "runs";
                                        if (!Kinds.Contains(kind, StringComparer.Ordinal)) throw new InvalidDataException("Unsupported history view.");
                                        var history = await MongoHistory.OpenAsync(options.EnvFile);
                                        output = await history.QueryAsync(kind, String(arguments, "runId"), String(arguments, "sampleId"),
                                            String(arguments, "sourceSha256"), Integer(arguments, "limit", 50));
                                        break;
                                    case "document_matrix":
                                        var source = String(arguments, "sourceSha256");
                                        if (source is null || source.Length != 64 || !source.All(Uri.IsHexDigit))
                                            throw new InvalidDataException("Provide the document's 64-character SHA-256 checksum.");
                                        output = await (await MongoHistory.OpenAsync(options.EnvFile)).MatrixAsync(source);
                                        break;
                                    case "attempt_history": output = await new AttemptStore(options).All(); break;
                                    default: throw new InvalidDataException("Unknown read-only history tool.");
                                }
                                await Result(id, new { content = new[] { new { type = "text", text = JsonSerializer.Serialize(output, Json) } }, isError = false });
                            }
                            catch (Exception exception) when (exception is not OperationCanceledException)
                            {
                                // Exception details may contain connection strings, file content, or provider messages.
                                var detail = exception is InvalidDataException ? "Invalid history request. Check the tool arguments."
                                    : "History is unavailable. Check the local benchmark history and MongoDB connection.";
                                await Result(id, new { content = new[] { new { type = "text", text = detail } }, isError = true });
                            }
                            break;
                        default: await Error(id, -32601, "Method not found."); break;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
                catch { await Error(id, -32602, "Invalid request parameters."); }
            }
        }
    }

    private static object[] Tools() => [
        new {
            name = "query_history", description = "Query bounded read-only development benchmark history, including expected targets, observed findings, misses, metrics, graphs and execution events. Missing selects gold-positive targets that were not found.",
            annotations = ReadOnlyAnnotations,
            inputSchema = new {
                type = "object", properties = new {
                    kind = new { type = "string", description = "History view to query.", @enum = Kinds },
                    runId = new { type = new[] { "string", "null" }, description = "Evaluation run ID, or null for all development runs." },
                    sampleId = new { type = new[] { "string", "null" }, description = "Benchmark sample ID, for example 0201, or null for all samples." },
                    sourceSha256 = new { type = new[] { "string", "null" }, description = "Exact PDF SHA-256 checksum, or null for all development documents." },
                    limit = new { type = new[] { "integer", "null" }, minimum = 1, maximum = 200, description = "Maximum records, or null for default 50." }
                }, required = new[] { "kind", "runId", "sampleId", "sourceSha256", "limit" }, additionalProperties = false
            }
        },
        new {
            name = "document_matrix", description = "Read expected extraction targets and found/missed/failed states across development attempts for one exact source PDF.",
            annotations = ReadOnlyAnnotations,
            inputSchema = new {
                type = "object", properties = new { sourceSha256 = new { type = "string", description = "Exact 64-character source PDF SHA-256 checksum." } },
                required = new[] { "sourceSha256" }, additionalProperties = false
            }
        },
        new {
            name = "attempt_history", description = "Read saved local improvement attempts, proposals, decisions and results to learn from earlier experiments.",
            annotations = ReadOnlyAnnotations,
            inputSchema = new { type = "object", properties = new { }, additionalProperties = false }
        }
    ];

    private static string? String(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var property)) return null;
        if (property.ValueKind == JsonValueKind.Null) return null;
        if (property.ValueKind != JsonValueKind.String) throw new InvalidDataException("Expected a string argument.");
        var result = property.GetString();
        if (result?.Length > 256) throw new InvalidDataException("Argument is too long.");
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }
    private static int Integer(JsonElement value, string name, int fallback)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var property)) return fallback;
        if (property.ValueKind == JsonValueKind.Null) return fallback;
        if (!property.TryGetInt32(out var number) || number is < 1 or > 200) throw new InvalidDataException("Limit must be between 1 and 200.");
        return number;
    }
    private static Task Result(JsonElement? id, object result) => Send(new { jsonrpc = "2.0", id, result });
    private static Task Error(JsonElement? id, int code, string message) => Send(new { jsonrpc = "2.0", id, error = new { code, message } });
    private static async Task Send(object response)
    {
        await Console.Out.WriteLineAsync(JsonSerializer.Serialize(response, Json));
        await Console.Out.FlushAsync();
    }
}
