using System.Text;
using System.Text.Json;
using AIESummit.Evaluation;
using Xunit;

namespace AIESummit.Extraction.Tests;

public sealed class ExpectedResultsTests
{
    [Fact]
    public void SnapshotPreservesEveryCategoryAndDistinguishesAbsentFromUnknown()
    {
        var snapshot = ExpectedResults.Create(Labels(), "abc");
        Assert.Equal(3, snapshot.ExpectedDetections!.Value.GetArrayLength());
        Assert.True(snapshot.Targets.Single(t => t.Tag == "agreement-effective-date").Expected);
        Assert.False(snapshot.Targets.Single(t => t.Tag == "non-compete").Expected);
        Assert.Null(snapshot.Targets.Single(t => t.Tag == "liability-cap").Expected);
        Assert.Contains("Upstream category outside current target catalog", snapshot.ExpectedDetections.Value.GetRawText());
        Assert.Equal(1, snapshot.Targets.Single(t => t.Tag == "agreement-effective-date").GoldFindings);
    }

    [Fact]
    public void UnlabeledDocumentDoesNotCreateNegativeGold()
    {
        var snapshot = ExpectedResults.Create(Encoding.UTF8.GetBytes("""{"schema_version":1,"dataset":"edgar-corpus","sample_id":"0001","document":{"sha256":"abc"},"expected_detections":null}"""), "abc");
        Assert.All(snapshot.Targets, t => { Assert.Null(t.Expected); Assert.Equal("unknown", t.Expectation); });
    }

    [Fact]
    public void SnapshotIdentityIncludesExactLabelBytesAndPartition()
    {
        var original = ExpectedResults.Create(Labels(), "abc");
        var revised = ExpectedResults.Create(Labels().Concat(new byte[] { 32 }).ToArray(), "abc");
        var confirmation = ExpectedResults.Create(Labels(), "abc", "confirmation");
        Assert.NotEqual(original.Id, revised.Id); Assert.NotEqual(original.Id, confirmation.Id);
        Assert.Throws<InvalidDataException>(() => ExpectedResults.Create(Labels(), "different"));
    }

    [Fact]
    public void MatrixDistinguishesRejectedEvidenceFromMissedAndFalsePositive()
    {
        var snapshot = ExpectedResults.Create(Labels(), "abc");
        var result = JsonSerializer.SerializeToElement(new { status = "ready", prediction = new { analyses = new[] {
            new { isCurrent = true, status = "failed", targets = new[] {
                new { tag = "agreement-effective-date", outcome = "not_found_in_reviewed_text", extractions = Array.Empty<object>() },
                new { tag = "non-compete", outcome = "detected", extractions = new object[] { new { id = 7, title = "Restriction", values = new { activity = "compete" }, evidence = new[] { new { id = 8, page = 1, quote = "private quote", startOffset = 3, endOffset = 6 } } } } }
            }, modelReportedOutcomes = new[] { new { tag = "agreement-effective-date", outcome = "detected" } } }
        } } });
        var observations = ExpectedResults.Observe(snapshot, result);
        Assert.Equal("rejected_evidence", observations.Single(t => t.Tag == "agreement-effective-date").State);
        Assert.Equal("false_positive", observations.Single(t => t.Tag == "non-compete").State);
        Assert.DoesNotContain("private quote", JsonSerializer.Serialize(observations));
        Assert.Equal(1, observations.Single(t => t.Tag == "non-compete").FindingCount);
    }

    [Fact]
    public void FailureInIndependentBranchDoesNotHideSuccessfulTargetAbsence()
    {
        var snapshot = ExpectedResults.Create(Labels(), "abc");
        using var result = JsonDocument.Parse("""
            {"status":"failed","prediction":{"analyses":[
              {"isCurrent":true,"status":"ready","targets":[
                {"tag":"agreement-effective-date","outcome":"not_found_in_reviewed_text","extractions":[]},
                {"tag":"non-compete","outcome":"not_found_in_reviewed_text","extractions":[]}],
               "modelReportedOutcomes":[{"tag":"agreement-effective-date","outcome":"detected"}]},
              {"isCurrent":true,"status":"failed","definitions":[{"tag":"liability-cap"},{"tag":"audit-rights"}],
               "targets":[{"tag":"liability-cap","outcome":"not_analyzed","extractions":[]}],
               "modelReportedOutcomes":[{"tag":"liability-cap","outcome":"detected"}]}]}}
            """);
        var rows = ExpectedResults.Observe(snapshot, result.RootElement);
        Assert.Equal("missed", rows.Single(t => t.Tag == "agreement-effective-date").State);
        Assert.False(rows.Single(t => t.Tag == "agreement-effective-date").ModelReported);
        Assert.Equal("correct_absence", rows.Single(t => t.Tag == "non-compete").State);
        Assert.Equal("rejected_evidence", rows.Single(t => t.Tag == "liability-cap").State);
        Assert.Equal("failed", rows.Single(t => t.Tag == "audit-rights").State);
    }

    [Fact]
    public void WholeRejectedLegacyResponseKeepsTargetsFailed()
    {
        var snapshot = ExpectedResults.Create(Labels(), "abc");
        using var result = JsonDocument.Parse("""{"status":"failed","prediction":{"analyses":[{"isCurrent":true,"targets":[{"tag":"non-compete","outcome":"not_analyzed","extractions":[]}]}]}}""");
        Assert.Equal("failed", ExpectedResults.Observe(snapshot, result.RootElement).Single(t => t.Tag == "non-compete").State);
    }

    [Fact]
    public void FailedPageRoutingPreventsClaimedAbsenceButKeepsValidatedPositiveFindings()
    {
        var snapshot = ExpectedResults.Create(Labels(), "abc");
        using var result = JsonDocument.Parse("""
            {"status":"failed","prediction":{"analyses":[
              {"isCurrent":true,"status":"ready","targets":[
                {"tag":"agreement-effective-date","outcome":"detected","extractions":[{"id":1,"title":"Date","values":{},"evidence":[]}]},
                {"tag":"non-compete","outcome":"not_found_in_reviewed_text","extractions":[]}]}]},
             "graph":{"nodes":[
               {"state":"failed","nodeKind":"detect-pages","pageNumber":2,"targetDefinitions":[{"tag":"agreement-effective-date"},{"tag":"non-compete"}]},
               {"state":"blocked_detection_failed","nodeKind":"extract","pageNumber":2,"targetRouting":{"targets":["non-compete"]}}],"edges":[]}}
            """);
        var rows = ExpectedResults.Observe(snapshot, result.RootElement);
        Assert.Equal("found", rows.Single(t => t.Tag == "agreement-effective-date").State);
        Assert.Equal("failed", rows.Single(t => t.Tag == "non-compete").State);
        Assert.Equal("not_found_in_reviewed_text", rows.Single(t => t.Tag == "non-compete").Outcome);
    }

    [Theory]
    [InlineData("cache_miss")]
    [InlineData("runner_failed")]
    [InlineData("skipped")]
    public void IncompleteTargetGraphCannotPublishACompleteNegative(string state)
    {
        var snapshot = ExpectedResults.Create(Labels(), "abc");
        var result = JsonSerializer.SerializeToElement(new { status = "failed", prediction = new { analyses = new[] {
            new { isCurrent = true, status = "ready", targets = new[] { new { tag = "non-compete", outcome = "not_found_in_reviewed_text", extractions = Array.Empty<object>() } } }
        } }, graph = new { nodes = new[] { new { state, targetRouting = new { targets = new[] { "non-compete" } } } } } });
        Assert.Equal("failed", ExpectedResults.Observe(snapshot, result).Single(t => t.Tag == "non-compete").State);
    }

    [Fact]
    public async Task OfflineInstrumentationRetainsExpectedAndObservedRecordsForReplay()
    {
        var root = Path.Combine(Path.GetFullPath("artifacts"), "expected-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var store = await MongoInstrumentation.OpenAsync(Path.Combine(root, "missing.env"), root, new { synthetic = true }, true);
        var gold = Path.Combine(root, "gold.json"); await File.WriteAllBytesAsync(gold, Labels());
        var file = Path.Combine(root, "result.json"); var result = new { status = "failed", prediction = (object?)null }; await File.WriteAllTextAsync(file, JsonSerializer.Serialize(result));
        var documentId = await store.BeginDocumentAsync("0001", Path.Combine(root, "test.pdf"), "abc", file);
        var expectationId = await store.RegisterExpectedAsync(documentId, gold, "abc");
        await store.FinishDocumentAsync(documentId, file, result);
        var commands = await File.ReadAllTextAsync(Path.Combine(root, "telemetry-outbox.ndjson"));
        Assert.Contains("evaluation_expectations", commands); Assert.Contains("evaluation_observations", commands);
        Assert.Contains(expectationId, commands); Assert.Contains("expected_detections", commands.Replace("expectedDetections", "expected_detections"));
        Assert.Contains("failed", commands); Assert.Contains("upstream gold quote", commands);
    }

    private static byte[] Labels() => Encoding.UTF8.GetBytes("""
        {"schema_version":1,"dataset":"cuad","sample_id":"0001","document":{"sha256":"abc"},"expected_detections":[
        {"category":"Effective Date","expected_present":true,"answers":[{"text":"upstream gold quote","start":1,"end":20}]},
        {"category":"Non-Compete","expected_present":false,"answers":[]},
        {"category":"Upstream category outside current target catalog","expected_present":true,"answers":[]}]}
        """);
}
