using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace AIESummit.Evaluation;

/// <summary>Bounded read-only views for the improvement UI and Codex. No arbitrary commands or pipelines.</summary>
public sealed class MongoHistory(IMongoDatabase database)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static Task<MongoHistory> OpenAsync(string settingsFile)
    {
        try
        {
            var env = MongoInstrumentation.ReadSettings(settingsFile);
            if (!env.TryGetValue("MONGODB_URI", out var uri) || string.IsNullOrWhiteSpace(uri)) throw new InvalidOperationException();
            var url = new MongoUrl(uri); var settings = MongoClientSettings.FromConnectionString(uri);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(15); settings.ConnectTimeout = TimeSpan.FromSeconds(10);
            settings.ApplicationName = "AIESummit.Improvement.ReadOnly";
            if (env.GetValueOrDefault("MONGODB_TLS12") == "true") settings.SslSettings = new SslSettings { EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 };
            return Task.FromResult(new MongoHistory(new MongoClient(settings).GetDatabase(env.GetValueOrDefault("MONGODB_DATABASE", url.DatabaseName ?? "aiesummit_evaluation"))));
        }
        catch { throw new InvalidOperationException("MongoDB history configuration is unavailable; credentials are omitted."); }
    }

    public async Task<JsonElement> QueryAsync(string kind = "runs", string? runId = null, string? sampleId = null, string? sourceSha256 = null, int limit = 50)
    {
        var collection = kind switch {
            "runs" => "evaluation_runs", "documents" => "evaluation_documents", "steps" => "evaluation_steps",
            "workflows" => "evaluation_workflows", "expectations" => "evaluation_expectations", "observations" or "missing" => "evaluation_observations",
            "events" => "evaluation_events", _ => throw new InvalidDataException("Unknown read-only history view.") };
        var filter = Builders<BsonDocument>.Filter.Ne("partition", "confirmation") & Builders<BsonDocument>.Filter.Ne("configuration.partition", "confirmation");
        // Label-bearing records require an explicit development designation; missing metadata is not permission to expose gold.
        if (kind is "expectations" or "observations" or "missing") filter &= Builders<BsonDocument>.Filter.Eq("partition", "development");
        if (!string.IsNullOrWhiteSpace(runId)) filter &= Builders<BsonDocument>.Filter.Eq("runId", runId);
        if (!string.IsNullOrWhiteSpace(sampleId)) filter &= Builders<BsonDocument>.Filter.Eq("sampleId", sampleId);
        if (!string.IsNullOrWhiteSpace(sourceSha256)) filter &= Builders<BsonDocument>.Filter.Eq("sourceSha256", sourceSha256.ToLowerInvariant());
        if (kind == "missing") filter &= Builders<BsonDocument>.Filter.ElemMatch<BsonDocument>("targets", new BsonDocument {
            { "expected", true }, { "found", false } });
        try
        {
            var stages = new List<BsonDocument> { new("$match", filter.Render(new RenderArgs<BsonDocument>(BsonSerializer.SerializerRegistry.GetSerializer<BsonDocument>(), BsonSerializer.SerializerRegistry))) };
            if (kind == "expectations")
            {
                // Snapshots have no runId: require a development document using them in a visible run.
                stages.Add(new("$lookup", new BsonDocument {
                    { "from", "evaluation_documents" }, { "let", new BsonDocument("expectation", "$_id") },
                    { "pipeline", new BsonArray {
                        new BsonDocument("$match", new BsonDocument { { "$expr", new BsonDocument("$eq", new BsonArray { "$expectationId", "$$expectation" }) }, { "partition", "development" } }),
                        VisibleRunLookup("runId", "_id"), new BsonDocument("$match", new BsonDocument("_visibleRuns.0", new BsonDocument("$exists", true))),
                        new BsonDocument("$limit", 1), new BsonDocument("$project", new BsonDocument("_id", 1)) } }, { "as", "_visibleUsage" } }));
                stages.Add(new("$match", new BsonDocument("_visibleUsage.0", new BsonDocument("$exists", true))));
                stages.Add(new("$unset", "_visibleUsage"));
            }
            else if (kind != "runs")
            {
                stages.Add(kind == "workflows" ? VisibleRunLookup("_id", "workflowId") : VisibleRunLookup("runId", "_id"));
                stages.Add(new("$match", new BsonDocument("_visibleRuns.0", new BsonDocument("$exists", true))));
                stages.Add(new("$unset", "_visibleRuns"));
            }
            stages.Add(new("$sort", new BsonDocument { { "startedAt", -1 }, { "completedAt", -1 }, { "_id", -1 } }));
            stages.Add(new("$limit", Math.Clamp(limit, 1, 200)));
            var rows = await database.GetCollection<BsonDocument>(collection).Aggregate<BsonDocument>(stages, new AggregateOptions { MaxTime = TimeSpan.FromSeconds(10) }).ToListAsync();
            return Serialize(rows);
        }
        catch { throw new InvalidOperationException("MongoDB history query failed; check connectivity. Credentials are omitted."); }
    }

    public async Task<JsonElement> MatrixAsync(string sourceSha256, string? expectationId = null)
    {
        if (string.IsNullOrWhiteSpace(sourceSha256)) throw new InvalidDataException("Matrix requires a document checksum.");
        var source = sourceSha256.ToLowerInvariant();
        var snapshots = await QueryAsync("expectations", sourceSha256: source, limit: 20);
        var expectations = snapshots.EnumerateArray().Where(e => string.IsNullOrWhiteSpace(expectationId) || e.GetProperty("_id").GetString() == expectationId)
            .Select(e => BsonDocument.Parse(e.GetRawText())).ToList();
        if (expectations.Count == 0) return JsonSerializer.SerializeToElement(new { sampleId = "", sourceSha256 = source, columns = Array.Empty<object>(), rows = Array.Empty<object>(), expectationVersions = Array.Empty<object>() }, Json);
        if (expectations.Count > 1 && string.IsNullOrWhiteSpace(expectationId))
            throw new InvalidDataException("Multiple label versions exist for this document; select an expectation ID.");
        var expected = expectations[0]; var id = expected["_id"].AsString;
        var observations = await database.GetCollection<BsonDocument>("evaluation_observations")
            .Find(new BsonDocument { { "expectationId", id }, { "partition", "development" } }).Limit(200).ToListAsync();
        var runIds = observations.Select(o => o["runId"].AsString).Distinct().ToArray();
        var runs = await database.GetCollection<BsonDocument>("evaluation_runs").Find(Builders<BsonDocument>.Filter.In("_id", runIds)
            & Builders<BsonDocument>.Filter.Ne("partition", "confirmation") & Builders<BsonDocument>.Filter.Ne("configuration.partition", "confirmation")).Limit(200).ToListAsync();
        var dates = runs.ToDictionary(r => r["_id"].AsString, r => r.GetValue("startedAt", BsonNull.Value));
        observations = observations.Where(o => dates.ContainsKey(o["runId"].AsString)).OrderBy(o => dates[o["runId"].AsString]).ThenBy(o => o["_id"].AsString).ToList();
        var columns = observations.Select((o, index) => new { id = o["_id"].AsString, label = index == 0 ? "Baseline" : $"Attempt {index}",
            runId = o["runId"].AsString, workflowId = o["workflowId"].AsString, status = o["status"].AsString }).ToArray();
        var rows = expected["targets"].AsBsonArray.Select(t => {
            var target = t.AsBsonDocument; var tag = target["tag"].AsString;
            return new { tag, name = target["category"].AsString, expected = target["expectation"].AsString, goldFindings = target["goldFindings"].ToInt32(),
                cells = observations.Select(o => {
                    var found = o["targets"].AsBsonArray.FirstOrDefault(x => x["tag"].AsString == tag)?.AsBsonDocument;
                    var state = found?.GetValue("state", "not_run").AsString ?? "not_run";
                    return new { columnId = o["_id"].AsString, status = state == "not_analyzed" ? "not_run" : state,
                        count = found?.GetValue("findingCount", 0).ToInt32() ?? 0, detail = found?.GetValue("outcome", "not_analyzed").AsString ?? "not_analyzed" };
                }).ToArray() };
        }).ToArray();
        return JsonSerializer.SerializeToElement(new { sampleId = expected["sampleId"].AsString, filename = expected["sampleId"].AsString + ".pdf", sourceSha256 = source,
            expectationId = id, labelSha256 = expected["labelSha256"].AsString, columns, rows,
            limitation = "Found means validated target presence. Individual gold spans are retained but not independently matched.",
            expectationVersions = expectations.Select(e => e["_id"].AsString).ToArray() }, Json);
    }

    private static JsonElement Serialize(IEnumerable<BsonDocument> rows) => JsonSerializer.SerializeToElement(rows.Select(row => BsonTypeMapper.MapToDotNetValue(row)), Json);

    private static BsonDocument VisibleRunLookup(string localField, string foreignField) => new("$lookup", new BsonDocument {
        { "from", "evaluation_runs" }, { "localField", localField }, { "foreignField", foreignField },
        { "pipeline", new BsonArray { new BsonDocument("$match", new BsonDocument {
            { "partition", new BsonDocument("$ne", "confirmation") }, { "configuration.partition", new BsonDocument("$ne", "confirmation") } }),
            new BsonDocument("$limit", 1), new BsonDocument("$project", new BsonDocument("_id", 1)) } }, { "as", "_visibleRuns" } });
}
