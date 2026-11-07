using System.Text.Json;
using AIESummit.Evaluation;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace AIESummit.Extraction.Tests;

public sealed class MongoDatabaseFact : FactAttribute
{
    public MongoDatabaseFact() { if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AIESUMMIT_MONGO_TEST_URI"))) Skip = "Run scripts/test-extractions.ps1 for disposable MongoDB coverage."; }
}
public sealed class InstrumentationTests
{
    [Fact] public async Task OfflineModeRetainsReplayableGraphAndDoesNotIncludePredictionContent()
    {
        var root = Path.Combine(Path.GetFullPath("artifacts"), "instrumentation-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var store = await MongoInstrumentation.OpenAsync(Path.Combine(root, "missing.env"), root, new { synthetic = true }, true);
        var resultFile = Path.Combine(root, "result.json"); var result = Result(); await File.WriteAllTextAsync(resultFile, JsonSerializer.Serialize(result));
        var id = await store.BeginDocumentAsync("synthetic", Path.Combine(root, "synthetic.pdf"), "synthetic-hash", resultFile);
        await store.FinishDocumentAsync(id, resultFile, result, Metrics()); await store.FinishRunAsync(1, 0, new { success = true });
        var text = await File.ReadAllTextAsync(Path.Combine(root, "telemetry-outbox.ndjson"));
        Assert.Contains("evaluation_steps", text); Assert.Contains("parentNodeIds", text); Assert.Contains("systemPromptHash", text);
        Assert.DoesNotContain("synthetic-private-content", text); Assert.Contains("synthetic-private-content", await File.ReadAllTextAsync(resultFile));
        Assert.All(File.ReadLines(Path.Combine(root, "telemetry-outbox.ndjson")), line => Assert.NotNull(BsonDocument.Parse(line)));
    }

    [MongoDatabaseFact] public async Task MongoWritesAndReplayPreserveIdsAndGraphWithoutDuplicateEvents()
    {
        var uri = Environment.GetEnvironmentVariable("AIESUMMIT_MONGO_TEST_URI")!;
        var databaseName = "aiesummit_instrumentation_test";
        var root = Path.Combine(Path.GetFullPath("artifacts"), "mongo-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var settings = Path.Combine(root, "synthetic.env");
        await File.WriteAllTextAsync(settings, $"MONGODB_URI={uri}\nMONGODB_DATABASE={databaseName}\n");
        var store = await MongoInstrumentation.OpenAsync(settings, root, new { synthetic = true });
        var file = Path.Combine(root, "result.json"); var result = Result(); await File.WriteAllTextAsync(file, JsonSerializer.Serialize(result));
        var id = await store.BeginDocumentAsync("synthetic", Path.Combine(root, "synthetic.pdf"), "hash", file);
        await store.FinishDocumentAsync(id, file, result, Metrics()); await store.FinishRunAsync(1, 0, new { completed = true });
        var db = new MongoClient(uri).GetDatabase(databaseName);
        var events = await db.GetCollection<BsonDocument>("evaluation_events").CountDocumentsAsync(new BsonDocument());
        var outbox = Path.Combine(root, "telemetry-outbox.ndjson"); await store.ReplayAsync(outbox); await store.ReplayAsync(outbox);
        Assert.Equal(events, await db.GetCollection<BsonDocument>("evaluation_events").CountDocumentsAsync(new BsonDocument()));
        Assert.Equal(4, await db.GetCollection<BsonDocument>("evaluation_steps").CountDocumentsAsync(new BsonDocument("documentRunId", id)));
        var doc = await db.GetCollection<BsonDocument>("evaluation_documents").Find(new BsonDocument("_id", id)).SingleAsync();
        Assert.Equal("ready", doc["status"].AsString); Assert.False(doc["result"].AsBsonDocument.Contains("prediction"));
        Assert.Equal(3, doc["graph"]["edges"].AsBsonArray.Count);
    }

    private static object Result() => new { status = "ready", totalSeconds = 3.5, prediction = "synthetic-private-content" };
    private static JsonElement Metrics() => JsonSerializer.SerializeToElement(new { prompts = new[] {
        new { id = 1, step = "split-upload", model = "synthetic-v1", documentType = "UploadedFile", documentID = 1, systemPromptHash = "hash" },
        new { id = 2, step = "extract-contract-targets", model = "synthetic-v1", documentType = "DocumentAnalysis", documentID = 1, systemPromptHash = "hash" }
    } });
}
