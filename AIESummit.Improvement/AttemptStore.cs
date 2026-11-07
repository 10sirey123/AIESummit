using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using AIESummit.Evaluation;

namespace AIESummit.Improvement;

public sealed class AttemptStore(WorkbenchOptions options)
{
    private readonly SemaphoreSlim gate = new(1);
    public async Task ReplayJournal()
    {
        var file = Path.Combine(options.State, "attempts-outbox.ndjson");
        if (!File.Exists(file)) return;
        await gate.WaitAsync();
        try
        {
            var settings = MongoInstrumentation.ReadSettings(options.EnvFile);
            if (!settings.TryGetValue("MONGODB_URI", out var uri)) return;
            var config = MongoClientSettings.FromConnectionString(uri); config.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
            var url = new MongoUrl(uri);
            var db = new MongoClient(config).GetDatabase(settings.GetValueOrDefault("MONGODB_DATABASE", url.DatabaseName ?? "aiesummit_evaluation"));
            foreach (var line in File.ReadLines(file))
            {
                var command = BsonDocument.Parse(line);
                if (command["collection"] != "improvement_attempts" || command["operation"] != "replace")
                    throw new InvalidDataException("Invalid attempt journal command.");
                await db.GetCollection<BsonDocument>("improvement_attempts").ReplaceOneAsync(new BsonDocument("_id", command["id"]),
                    command["document"].AsBsonDocument, new ReplaceOptions { IsUpsert = true });
            }
        }
        finally { gate.Release(); }
    }
    public async Task<List<Attempt>> All()
    {
        var directory = Path.Combine(options.State, "attempts");
        if (!Directory.Exists(directory)) return [];
        var list = new List<Attempt>();
        foreach (var path in Directory.EnumerateFiles(directory, "attempt.json", SearchOption.AllDirectories))
        {
            var item = JsonSerializer.Deserialize<Attempt>(await File.ReadAllTextAsync(path), ImprovementJson.Options);
            if (item != null) list.Add(item);
        }
        return list.OrderBy(x => x.CreatedAt).ToList();
    }
    public async Task<Attempt> Get(string id)
    {
        if (id.Length != 32 || !id.All(Uri.IsHexDigit)) throw new KeyNotFoundException("Unknown attempt.");
        var path = Path.Combine(options.AttemptFolder(id), "attempt.json");
        if (!File.Exists(path)) throw new KeyNotFoundException("Unknown attempt.");
        var item = JsonSerializer.Deserialize<Attempt>(await File.ReadAllTextAsync(path), ImprovementJson.Options)!;
        return item;
    }
    public async Task Save(Attempt attempt)
    {
        await gate.WaitAsync();
        try
        {
            attempt.UpdatedAt = DateTime.UtcNow;
            Directory.CreateDirectory(options.AttemptFolder(attempt.Id));
            var json = JsonSerializer.Serialize(attempt, ImprovementJson.Options);
            var file = Path.Combine(options.AttemptFolder(attempt.Id), "attempt.json");
            await File.WriteAllTextAsync(file + ".new", json); File.Move(file + ".new", file, true);
            // Durable command first; every update is replayable using the original attempt identity.
            var data = BsonDocument.Parse(json); data["_id"] = attempt.Id; data["schemaVersion"] = 1;
            var command = new BsonDocument { { "collection", "improvement_attempts" }, { "id", attempt.Id },
                { "operation", "replace" }, { "document", data } };
            await File.AppendAllTextAsync(Path.Combine(options.State, "attempts-outbox.ndjson"), command.ToJson() + "\n");
            try
            {
                var settings = MongoInstrumentation.ReadSettings(options.EnvFile);
                if (!settings.TryGetValue("MONGODB_URI", out var uri)) return;
                var config = MongoClientSettings.FromConnectionString(uri); config.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
                var url = new MongoUrl(uri);
                var db = new MongoClient(config).GetDatabase(settings.GetValueOrDefault("MONGODB_DATABASE", url.DatabaseName ?? "aiesummit_evaluation"));
                await db.GetCollection<BsonDocument>("improvement_attempts").ReplaceOneAsync(new BsonDocument("_id", attempt.Id), data, new ReplaceOptions { IsUpsert = true });
            }
            catch { /* Local durable outbox remains authoritative until replay. UI reports connectivity separately. */ }
        }
        finally { gate.Release(); }
    }
}
