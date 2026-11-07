using AIESummit.Evaluation;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace AIESummit.Extraction.Tests;

public sealed class MongoHistoryTests
{
    [MongoDatabaseFact]
    public async Task NativeStepAndEventQueriesResolveDocumentIdentityAndPageSelectorsPrecisely()
    {
        var db = new MongoClient(Environment.GetEnvironmentVariable("AIESUMMIT_MONGO_TEST_URI")!)
            .GetDatabase("aiesummit_history_page_" + Guid.NewGuid().ToString("N"));
        await db.GetCollection<BsonDocument>("evaluation_runs").InsertManyAsync([
            new() { { "_id", "run" }, { "runId", "run" }, { "partition", "development" } },
            new() { { "_id", "secret-run" }, { "runId", "secret-run" }, { "configuration", new BsonDocument("partition", "confirmation") } }
        ]);
        await db.GetCollection<BsonDocument>("evaluation_documents").InsertManyAsync([
            new() { { "_id", "document-a" }, { "runId", "run" }, { "sampleId", "0201" }, { "sourceSha256", "abc" },
                { "graph", new BsonDocument { { "exact", true }, { "nodeIds", new BsonArray { "page-one", "page-two", "other-type" } } } } },
            new() { { "_id", "document-b" }, { "runId", "run" }, { "sampleId", "0202" }, { "sourceSha256", "def" } },
            new() { { "_id", "secret-document" }, { "runId", "secret-run" }, { "sampleId", "0201" }, { "sourceSha256", "abc" } }
        ]);
        var pageOne = Step("page-one", "document-a", 10); pageOne["pageNumber"] = 1; pageOne["stepTag"] = "detect-liability";
        var pageTwo = Step("page-two", "document-a", 10); pageTwo["pageNumber"] = 2; pageTwo["stepTag"] = "detect-liability";
        var otherType = Step("other-type", "document-a", 10); otherType["pageNumber"] = 2; otherType["stepTag"] = "detect-renewal";
        var stray = Step("orphan-page-two", "document-a", 10); stray["pageNumber"] = 2; stray["stepTag"] = "detect-liability";
        var otherDocument = Step("other-document", "document-b", 10); otherDocument["pageNumber"] = 2; otherDocument["stepTag"] = "detect-liability";
        var hidden = Step("secret-node", "secret-document", 10); hidden["runId"] = "secret-run"; hidden["pageNumber"] = 2; hidden["stepTag"] = "detect-liability";
        await db.GetCollection<BsonDocument>("evaluation_steps").InsertManyAsync([pageOne, pageTwo, otherType, stray, otherDocument, hidden]);
        await db.GetCollection<BsonDocument>("evaluation_events").InsertManyAsync([
            new() { { "_id", "event-a" }, { "runId", "run" }, { "documentRunId", "document-a" } },
            new() { { "_id", "event-b" }, { "runId", "run" }, { "documentRunId", "document-b" } },
            new() { { "_id", "secret-event" }, { "runId", "secret-run" }, { "documentRunId", "secret-document" } }
        ]);
        var history = new MongoHistory(db);
        var selected = await history.QueryAsync("steps", sampleId: "0201", sourceSha256: "ABC", stepTag: "detect-liability", pageNumber: 2);
        Assert.Equal("page-two", Assert.Single(selected.EnumerateArray()).GetProperty("_id").GetString());
        Assert.Equal(3, (await history.QueryAsync("steps", sampleId: "0201")).GetArrayLength());
        var events = await history.QueryAsync("events", sampleId: "0201", sourceSha256: "ABC");
        Assert.Equal("event-a", Assert.Single(events.EnumerateArray()).GetProperty("_id").GetString());
        Assert.Empty((await history.QueryAsync("steps", sampleId: "0201", sourceSha256: "def")).EnumerateArray());
        Assert.Empty((await history.QueryAsync("events", sampleId: "0201", sourceSha256: "def")).EnumerateArray());
        await Assert.ThrowsAsync<InvalidDataException>(() => history.QueryAsync("steps", pageNumber: 0));
        await Assert.ThrowsAsync<InvalidDataException>(() => history.QueryAsync("events", pageNumber: 1));
    }

    [MongoDatabaseFact]
    public async Task StepViewSelectsAuthoritativeExactMembershipAndPreservesLegacyGraphs()
    {
        var db = new MongoClient(Environment.GetEnvironmentVariable("AIESUMMIT_MONGO_TEST_URI")!)
            .GetDatabase("aiesummit_history_graph_" + Guid.NewGuid().ToString("N"));
        await db.GetCollection<BsonDocument>("evaluation_runs").InsertOneAsync(new() { { "_id", "run" }, { "runId", "run" }, { "partition", "development" } });
        await db.GetCollection<BsonDocument>("evaluation_documents").InsertManyAsync([
            new() { { "_id", "exact-document" }, { "runId", "run" }, { "graph", new BsonDocument { { "exact", true }, { "nodeIds", new BsonArray { "exact-node" } } } } },
            new() { { "_id", "legacy-document" }, { "runId", "run" }, { "graph", new BsonDocument("edges", new BsonArray()) } }
        ]);
        await db.GetCollection<BsonDocument>("evaluation_steps").InsertManyAsync([
            Step("exact-node", "exact-document", 10), Step("orphan-inferred-prompt", "exact-document", 1000),
            Step("orphan-inferred-ocr", "exact-document", 0), Step("legacy-prompt", "legacy-document", 20)
        ]);
        var selected = await new MongoHistory(db).QueryAsync("steps", runId: "run");
        var rows = selected.EnumerateArray().ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Contains(rows, row => row.GetProperty("_id").GetString() == "exact-node");
        Assert.Contains(rows, row => row.GetProperty("_id").GetString() == "legacy-prompt");
        Assert.DoesNotContain("orphan-inferred", selected.GetRawText());
        Assert.Equal(30, rows.Sum(row => row.GetProperty("prompt").GetProperty("tokens").GetInt32()));
    }

    private static BsonDocument Step(string id, string document, int tokens) => new() {
        { "_id", id }, { "runId", "run" }, { "documentRunId", document }, { "kind", "llm" }, { "prompt", new BsonDocument("tokens", tokens) }
    };

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
