using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AIESummit.Evaluation;

/// <summary>Tester-only telemetry. No application persistence or credentials are copied into records.</summary>
public sealed class MongoInstrumentation
{
    public const int SchemaVersion = 1;
    private readonly IMongoDatabase? db;
    private readonly string folder;
    public string RunId { get; }
    public string WorkflowId { get; }
    public bool IsOffline => db is null;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private MongoInstrumentation(IMongoDatabase? database, string outputFolder, string runId, string workflowId)
        => (db, folder, RunId, WorkflowId) = (database, outputFolder, runId, workflowId);

    public static Dictionary<string, string> ReadSettings(string file)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (File.Exists(file)) foreach (var line in File.ReadLines(file))
        {
            var text = line.Trim(); if (text.StartsWith('#')) continue;
            var at = text.IndexOf('='); if (at <= 0) continue;
            var key = text[..at].Trim(); if (key.StartsWith("MONGODB_", StringComparison.Ordinal)) values[key] = text[(at + 1)..].Trim().Trim('"', '\'');
        }
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
            if (e.Key.ToString()!.StartsWith("MONGODB_", StringComparison.Ordinal)) values[e.Key.ToString()!] = e.Value?.ToString() ?? "";
        return values;
    }

    public static async Task<MongoInstrumentation> OpenAsync(string settingsFile, string outputFolder, object configuration, bool offline = false)
    {
        var heartbeat = "no heartbeat diagnostic";
        try
        {
            var env = ReadSettings(settingsFile);
            if (offline && !env.ContainsKey("MONGODB_URI")) env["MONGODB_URI"] = "mongodb://localhost";
            if (!env.TryGetValue("MONGODB_URI", out var uri) || string.IsNullOrWhiteSpace(uri))
                throw new InvalidOperationException("MongoDB URI is missing.");
            // The supplied URI is authoritative: do not reconstruct credentials/options or change read preference.
            var url = new MongoUrl(uri);
            var settings = MongoClientSettings.FromConnectionString(uri);
            if (env.GetValueOrDefault("MONGODB_TLS12") == "true")
                settings.SslSettings = new SslSettings { EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 };
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(20); settings.ConnectTimeout = TimeSpan.FromSeconds(15);
            settings.ApplicationName = "AIESummit.Evaluation";
            settings.ClusterConfigurator = cluster => cluster.Subscribe<MongoDB.Driver.Core.Events.ServerHeartbeatFailedEvent>(e => {
                var errors = new List<string>();
                for (Exception? x = e.Exception; x != null; x = x.InnerException) errors.Add(x.GetType().Name +
                    (x is System.ComponentModel.Win32Exception win ? ":" + win.NativeErrorCode : ""));
                heartbeat = string.Join("/", errors);
                var message = e.Exception.ToString();
                foreach (var hint in new[] { "alert internal error", "certificate", "Authentication failed", "actively refused", "No such host", "unexpected or badly formatted" })
                    if (message.Contains(hint, StringComparison.OrdinalIgnoreCase)) heartbeat += " [" + hint + "]";
            });
            var client = new MongoClient(settings);
            var database = offline ? null : client.GetDatabase(env.GetValueOrDefault("MONGODB_DATABASE", url.DatabaseName ?? "aiesummit_evaluation"));
            if (database != null) await client.GetDatabase("admin").RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), ReadPreference.Primary);
            var workflow = new {
                name = "ocr-split-page-sections-contract-extraction", version = 1,
                nodes = new[] {
                    new { key = "intake", kind = "http", stepTag = "upload", scope = "upload" },
                    new { key = "ocr", kind = "ocr", stepTag = "mistral-ocr", scope = "upload" },
                    new { key = "split", kind = "llm", stepTag = "split-upload", scope = "upload" },
                    new { key = "sections", kind = "llm", stepTag = "classify-page-sections", scope = "page" },
                    new { key = "extract", kind = "llm", stepTag = "extract-contract-targets", scope = "logical-document" }
                },
                edges = new[] { new { from = "intake", to = "ocr" }, new { from = "ocr", to = "split" },
                    new { from = "ocr", to = "sections" }, new { from = "split", to = "extract" } },
                targetCatalog = Domain.ExtractionCatalog.Version,
                note = "Sections and split branch after OCR; extraction depends on split, not section summaries. One extraction call covers all ten targets per logical document."
            };
            var workflowId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(workflow, Json)))).ToLowerInvariant();
            var store = new MongoInstrumentation(database, Path.GetFullPath(outputFolder), Guid.NewGuid().ToString("N"), workflowId);
            await store.Upsert("evaluation_workflows", workflowId, workflow);
            await store.Upsert("evaluation_runs", store.RunId, new { schemaVersion = SchemaVersion, runId = store.RunId,
                workflowId, status = "running", startedAt = DateTime.UtcNow, artifactRoot = store.folder,
                configuration, runnerVersion = typeof(MongoInstrumentation).Assembly.GetName().Version?.ToString(),
                runnerInformationalVersion = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(MongoInstrumentation).Assembly)?.InformationalVersion,
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription });
            if (database != null) {
            await database.GetCollection<BsonDocument>("evaluation_documents").Indexes.CreateManyAsync([
                new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("runId").Ascending("sampleId")),
                new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("sourceSha256"))]);
            await database.GetCollection<BsonDocument>("evaluation_steps").Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("documentRunId").Ascending("stepTag")));
            await database.GetCollection<BsonDocument>("evaluation_events").Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("runId").Ascending("occurredAt")));
            }
            return store;
        }
        catch (Exception ex) { throw new InvalidOperationException($"MongoDB initialization failed ({ex.GetType().Name}, inner {ex.InnerException?.GetType().Name ?? "none"}, heartbeat {heartbeat}). Check local settings, access and network; credentials are omitted from diagnostics."); }
    }

    private async Task Upsert(string collection, string id, object value)
    {
        // Serialize records through JSON to avoid driver-specific class maps and hidden graph traversal.
        var data = ToBson(value); data["_id"] = id;
        data["schemaVersion"] = SchemaVersion;
        await Command(collection, id, "replace", data);
    }

    private static BsonDocument ToBson(object value)
    {
        var result = BsonDocument.Parse(JsonSerializer.Serialize(value, Json));
        void Normalize(BsonValue node)
        {
            if (node is BsonArray array) { foreach (var child in array) Normalize(child); return; }
            if (node is not BsonDocument document) return;
            foreach (var name in document.Names.ToArray())
            {
                var child = document[name];
                if (child.IsString && new[] { "startedAt", "completedAt", "occurredAt", "dateCreated", "lastModified", "diagnosticsRecoveredAt" }.Contains(name)
                    && DateTimeOffset.TryParse(child.AsString, out var at)) document[name] = new BsonDateTime(at.UtcDateTime);
                else Normalize(child);
            }
        }
        Normalize(result); return result;
    }

    private async Task Command(string collection, string id, string operation, BsonDocument data)
    {
        Directory.CreateDirectory(folder);
        var record = new BsonDocument { { "collection", collection }, { "id", id }, { "operation", operation }, { "document", data } };
        await File.AppendAllTextAsync(Path.Combine(folder, "telemetry-outbox.ndjson"), record.ToJson(new MongoDB.Bson.IO.JsonWriterSettings {
            OutputMode = MongoDB.Bson.IO.JsonOutputMode.CanonicalExtendedJson, Indent = false }) + "\n");
        if (db is null) return;
        try
        {
            if (operation == "replace") await db.GetCollection<BsonDocument>(collection).ReplaceOneAsync(new BsonDocument("_id", id), data, new ReplaceOptions { IsUpsert = true });
            else await db.GetCollection<BsonDocument>(collection).UpdateOneAsync(new BsonDocument("_id", id), new BsonDocument("$set", data));
        }
        catch { throw new InvalidOperationException("MongoDB telemetry write failed; local artifacts and outbox are retained for replay."); }
    }

    public async Task ReplayAsync(string file)
    {
        if (db is null) throw new InvalidOperationException("Replay requires an online MongoDB connection.");
        // Replay stored IDs; never creates duplicate run/document/event identities.
        foreach (var line in File.ReadLines(file))
        {
            var row = BsonDocument.Parse(line); var collection = row["collection"].AsString;
            if (!new[] { "evaluation_runs", "evaluation_documents", "evaluation_steps", "evaluation_events", "evaluation_workflows", "evaluation_artifacts" }.Contains(collection))
                throw new InvalidDataException("Unknown telemetry collection.");
            var id = row["id"].AsString; var data = row["document"].AsBsonDocument;
            if (row["operation"].AsString == "replace") await db.GetCollection<BsonDocument>(collection).ReplaceOneAsync(new BsonDocument("_id", id), data, new ReplaceOptions { IsUpsert = true });
            else await db.GetCollection<BsonDocument>(collection).UpdateOneAsync(new BsonDocument("_id", id), new BsonDocument("$set", data));
        }
    }

    public async Task<string> BeginDocumentAsync(string sampleId, string sourcePath, string sourceHash, string resultFile)
    {
        var id = Guid.NewGuid().ToString("N");
        await Upsert("evaluation_documents", id, new { documentRunId = id, runId = RunId, workflowId = WorkflowId,
            sampleId, sourceSha256 = sourceHash, sourcePath = Path.GetFullPath(sourcePath), filename = Path.GetFileName(sourcePath),
            resultPath = Path.GetFullPath(resultFile), status = "running", startedAt = DateTime.UtcNow });
        await CheckpointAsync(id, "intake", "started", new { }); return id;
    }

    public async Task CheckpointAsync(string documentRunId, string nodeKey, string state, object details)
        => await Upsert("evaluation_events", Guid.NewGuid().ToString("N"), new { runId = RunId, documentRunId,
            workflowId = WorkflowId, nodeKey, state, occurredAt = DateTime.UtcNow, details });

    public async Task FinishDocumentAsync(string documentRunId, string resultFile, object result, JsonElement? metrics = null)
    {
        var payload = JsonSerializer.SerializeToElement(result, Json);
        var filter = new BsonDocument("_id", documentRunId);
        var telemetry = ToBson(payload); telemetry.Remove("prediction");
        await Command("evaluation_documents", documentRunId, "update", new BsonDocument {
            { "status", payload.GetProperty("status").GetString() }, { "completedAt", DateTime.UtcNow }, { "result", telemetry } });
        if (metrics is { } m && m.TryGetProperty("prompts", out var prompts))
        {
            var edges = new List<object>();
            var splitNodes = prompts.EnumerateArray().Where(p => p.GetProperty("step").GetString() == "split-upload")
                .Select(p => documentRunId + "/prompt/" + p.GetProperty("id").GetInt32()).ToArray();
            foreach (var prompt in prompts.EnumerateArray())
            {
                var nodeId = documentRunId + "/prompt/" + prompt.GetProperty("id").GetInt32();
                var extraction = prompt.GetProperty("step").GetString() == "extract-contract-targets";
                var parents = extraction ? splitNodes : [documentRunId + "/ocr"];
                JsonElement? definitions = null;
                if (extraction && payload.TryGetProperty("prediction", out var prediction) && prediction.ValueKind == JsonValueKind.Object)
                    definitions = prediction.GetProperty("analyses").EnumerateArray()
                        .Where(a => a.GetProperty("id").GetInt32() == prompt.GetProperty("documentID").GetInt32())
                        .Select(a => (JsonElement?)a.GetProperty("definitions").Clone()).SingleOrDefault();
                edges.AddRange(parents.Select(parent => new { from = parent, to = nodeId, relation = "depends_on" }));
                var nativeStatus = prompt.TryGetProperty("processingStatus", out var status) ? status.GetInt32() : -1;
                double? lifecycleSeconds = prompt.TryGetProperty("dateCreated", out var created) && prompt.TryGetProperty("lastModified", out var modified)
                    && modified.ValueKind == JsonValueKind.String ? (DateTimeOffset.Parse(modified.GetString()!) - DateTimeOffset.Parse(created.GetString()!)).TotalSeconds : null;
                await Upsert("evaluation_steps", nodeId, new { runId = RunId, documentRunId, workflowId = WorkflowId,
                    stepTag = prompt.GetProperty("step").GetString(), kind = "llm", parentNodeIds = parents, prompt,
                    state = nativeStatus == 200 ? "succeeded" : nativeStatus == 500 ? "failed" : nativeStatus == 0 ? "queued" : "unknown",
                    observedLifecycleSeconds = lifecycleSeconds,
                    model = prompt.GetProperty("model").GetString(),
                    targetDefinitions = definitions,
                    modelVersion = (string?)null, modelVersionStatus = "Configured model name retained; immutable provider model revision is not exposed by the current framework.",
                    timingBasis = "prompt DateCreated/LastModified are persistence timestamps, not exact provider latency" });
            }
            await Upsert("evaluation_steps", documentRunId + "/ocr", new { runId = RunId, documentRunId, workflowId = WorkflowId,
                stepTag = "mistral-ocr", kind = "ocr", tokens = (int?)null, parentNodeIds = new[] { documentRunId + "/intake" },
                timingBasis = "See document intakeSeconds for combined upload/OCR/split wall time; exact OCR latency unavailable." });
            await Upsert("evaluation_steps", documentRunId + "/intake", new { runId = RunId, documentRunId, workflowId = WorkflowId,
                stepTag = "upload", kind = "http", parentNodeIds = Array.Empty<string>() });
            edges.Add(new { from = documentRunId + "/intake", to = documentRunId + "/ocr", relation = "depends_on" });
            await Command("evaluation_documents", documentRunId, "update", new BsonDocument("graph",
                BsonDocument.Parse(JsonSerializer.Serialize(new { nodesCollection = "evaluation_steps", edges }, Json))));
        }
        var bytes = await File.ReadAllBytesAsync(resultFile);
        await Upsert("evaluation_artifacts", documentRunId + "/result", new { runId = RunId, documentRunId, kind = "result-json",
            path = Path.GetFullPath(resultFile), relativePath = Path.GetRelativePath(folder, resultFile),
            sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes = bytes.Length,
            contentStoredInMongo = false });
        await CheckpointAsync(documentRunId, "document", payload.GetProperty("status").GetString()!, new { resultFile = Path.GetFullPath(resultFile) });
    }

    public async Task FinishRunAsync(int total, int failed, object summary)
    {
        await Command("evaluation_runs", RunId, "update", new BsonDocument {
            { "status", failed == 0 ? "completed" : "completed_with_failures" }, { "completedAt", DateTime.UtcNow },
            { "documentsAttempted", total }, { "documentsFailed", failed }, { "summary", BsonDocument.Parse(JsonSerializer.Serialize(summary, Json)) } });
    }
}
