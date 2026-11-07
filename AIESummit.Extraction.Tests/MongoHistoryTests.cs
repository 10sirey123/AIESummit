using AIESummit.Evaluation;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace AIESummit.Extraction.Tests;

public sealed class MongoHistoryTests
{
    [MongoDatabaseFact]
    public async Task RunSelectionResolvesSnapshotsAndWorkflowsThroughUsageWithoutExposingConfirmation()
    {
        var db = new MongoClient(Environment.GetEnvironmentVariable("AIESUMMIT_MONGO_TEST_URI")!)
            .GetDatabase("aiesummit_history_selection_" + Guid.NewGuid().ToString("N"));
        await db.GetCollection<BsonDocument>("evaluation_runs").InsertManyAsync([
            new() { { "_id", "run-a" }, { "runId", "run-a" }, { "partition", "development" }, { "workflowId", "workflow-a" } },
            new() { { "_id", "run-b" }, { "runId", "run-b" }, { "partition", "development" }, { "workflowId", "workflow-b" } },
            new() { { "_id", "secret-run" }, { "runId", "secret-run" }, { "workflowId", "workflow-a" }, { "configuration", new BsonDocument("partition", "confirmation") } }
        ]);
        await db.GetCollection<BsonDocument>("evaluation_workflows").InsertManyAsync([
            new() { { "_id", "workflow-a" } }, new() { { "_id", "workflow-b" } }
        ]);
        await db.GetCollection<BsonDocument>("evaluation_expectations").InsertManyAsync([
            Expected("label-a", "development"), Expected("label-b", "development")
        ]);
        await db.GetCollection<BsonDocument>("evaluation_documents").InsertManyAsync([
            Document("document-a", "run-a", "label-a"), Document("document-b", "run-b", "label-b"),
            Document("secret-document", "secret-run", "label-a")
        ]);
        var history = new MongoHistory(db);
        var selectedLabels = await history.QueryAsync("expectations", runId: "run-a");
        Assert.Equal("label-a", Assert.Single(selectedLabels.EnumerateArray()).GetProperty("_id").GetString());
        var selectedWorkflows = await history.QueryAsync("workflows", runId: "run-b");
        Assert.Equal("workflow-b", Assert.Single(selectedWorkflows.EnumerateArray()).GetProperty("_id").GetString());
        Assert.Empty((await history.QueryAsync("expectations", runId: "secret-run")).EnumerateArray());
        Assert.Empty((await history.QueryAsync("workflows", runId: "secret-run")).EnumerateArray());
        Assert.Empty((await history.QueryAsync("expectations", runId: "unknown")).EnumerateArray());
        Assert.Empty((await history.QueryAsync("workflows", runId: "unknown")).EnumerateArray());
        await Assert.ThrowsAsync<InvalidDataException>(() => history.QueryAsync("workflows", sampleId: "0001"));
        await Assert.ThrowsAsync<InvalidDataException>(() => history.QueryAsync("workflows", sourceSha256: "abc"));
    }

    [MongoDatabaseFact]
    public async Task EveryHistoryViewHidesConfirmationRunsIncludingLegacyRowsAndUnclassifiedGold()
    {
        var db = new MongoClient(Environment.GetEnvironmentVariable("AIESUMMIT_MONGO_TEST_URI")!)
            .GetDatabase("aiesummit_history_test_" + Guid.NewGuid().ToString("N"));
        await db.GetCollection<BsonDocument>("evaluation_runs").InsertManyAsync([
            new() { { "_id", "development" }, { "runId", "development" }, { "partition", "development" }, { "workflowId", "visible-workflow" } },
            new() { { "_id", "confirmation-secret" }, { "runId", "confirmation-secret" }, { "workflowId", "secret-workflow" }, { "configuration", new BsonDocument("partition", "confirmation") } }
        ]);
        await db.GetCollection<BsonDocument>("evaluation_workflows").InsertManyAsync([
            new() { { "_id", "visible-workflow" } }, new() { { "_id", "secret-workflow" } }
        ]);
        await db.GetCollection<BsonDocument>("evaluation_expectations").InsertManyAsync([
            Expected("visible-label", "development"), Expected("secret-label", "development"), Expected("unknown-label", null)
        ]);
        await db.GetCollection<BsonDocument>("evaluation_documents").InsertManyAsync([
            Document("visible-document", "development", "visible-label"),
            Document("secret-document", "confirmation-secret", "secret-label"),
            Document("unclassified-label-document", "development", "unknown-label")
        ]);
        foreach (var collection in new[] { "evaluation_steps", "evaluation_events" })
            await db.GetCollection<BsonDocument>(collection).InsertManyAsync([
                new() { { "_id", "visible" }, { "runId", "development" } },
                new() { { "_id", "secret" }, { "runId", "confirmation-secret" } }
            ]);
        await db.GetCollection<BsonDocument>("evaluation_observations").InsertManyAsync([
            Observation("visible-observation", "development", "visible-label", "development"),
            Observation("secret-observation", "confirmation-secret", "visible-label", "development"),
            Observation("unclassified-observation", "development", "visible-label", null)
        ]);
        var history = new MongoHistory(db);
        foreach (var kind in new[] { "runs", "documents", "steps", "events", "workflows", "expectations", "observations", "missing" })
        {
            var result = await history.QueryAsync(kind);
            Assert.DoesNotContain("secret", result.GetRawText());
            if (kind is "expectations" or "observations" or "missing") Assert.DoesNotContain("unclassified", result.GetRawText());
        }
        var matrix = await history.MatrixAsync("abc");
        Assert.Single(matrix.GetProperty("columns").EnumerateArray());
        Assert.DoesNotContain("secret", matrix.GetRawText());
    }

    private static BsonDocument Expected(string id, string? partition)
    {
        var document = new BsonDocument {
            { "_id", id }, { "sourceSha256", "abc" }, { "sampleId", "0001" }, { "labelSha256", "label" },
            { "targets", new BsonArray { new BsonDocument { { "tag", "agreement-effective-date" }, { "category", "Effective Date" }, { "expectation", "present" }, { "goldFindings", 1 } } } }
        };
        if (partition != null) document["partition"] = partition;
        return document;
    }
    private static BsonDocument Document(string id, string run, string expectation) => new() {
        { "_id", id }, { "runId", run }, { "expectationId", expectation }, { "partition", "development" }
    };
    private static BsonDocument Observation(string id, string run, string expectation, string? partition)
    {
        var document = new BsonDocument {
            { "_id", id }, { "runId", run }, { "expectationId", expectation }, { "sourceSha256", "abc" }, { "workflowId", "workflow" }, { "status", "ready" },
            { "targets", new BsonArray { new BsonDocument { { "tag", "agreement-effective-date" }, { "expected", true }, { "found", false }, { "state", "missed" }, { "outcome", "not_found_in_reviewed_text" }, { "findingCount", 0 } } } }
        };
        if (partition != null) document["partition"] = partition;
        return document;
    }
}
