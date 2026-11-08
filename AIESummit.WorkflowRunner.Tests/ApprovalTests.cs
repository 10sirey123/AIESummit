using System.Text.Json.Nodes;
using AIESummit.Improvement;
using Xunit;

namespace AIESummit.WorkflowRunner.Tests;

public sealed class ApprovalTests
{
    [Fact] public void ApprovalBindsToProposalAndFrozenInputs()
    {
        var attempt = new Attempt { Status = "awaiting_approval", Proposal = JsonNode.Parse("{\"summary\":\"One change\"}"),
            BaseCommit = "base-a", ParentGraphHash = "graph-a", ManifestHash = "dataset-a" };
        attempt.ProposalHash = attempt.CalculateApprovalHash();
        ImprovementService.ValidateApproval(attempt, attempt.ProposalHash);
        attempt.ManifestHash = "dataset-b";
        Assert.Throws<InvalidDataException>(() => ImprovementService.ValidateApproval(attempt, attempt.ProposalHash));
    }
    [Fact] public void ApprovalCannotExecuteTwice()
    {
        var attempt = new Attempt { Status = "approved", Proposal = JsonNode.Parse("{}") };
        attempt.ProposalHash = attempt.CalculateApprovalHash();
        Assert.Throws<InvalidDataException>(() => ImprovementService.ValidateApproval(attempt, attempt.ProposalHash));
    }
    [Fact] public void CandidateCannotChangeScorerOrValidators()
    {
        ImprovementService.ValidateScope(["improvement/workflow.json"]);
        Assert.Throws<InvalidDataException>(() => ImprovementService.ValidateScope(["improvement/workflow.json", "AIESummit.Evaluation/BenchmarkEvaluator.cs"]));
        Assert.Throws<InvalidDataException>(() => ImprovementService.ValidateScope(["improvement/../workflow.json"]));
    }
    [Fact] public void PromotionRejectsUnevaluatedGraphContent()
    {
        var attempt = new Attempt { Graph = JsonNode.Parse("{\"nodes\":[{\"key\":\"tested\"}]}") };
        ImprovementService.ValidateEvaluatedGraph(attempt, "{\"nodes\":[{\"key\":\"tested\"}]}");
        Assert.Throws<InvalidDataException>(() => ImprovementService.ValidateEvaluatedGraph(attempt, "{\"nodes\":[{\"key\":\"edited\"}]}"));
    }
    [Fact] public void PromotionCanSkipAnUnpromotedIntermediateWithoutAcceptingUnrelatedBaselines()
    {
        var original = JsonNode.Parse("{\"generation\":0}");
        var parent = new Attempt { Id = "parent", ParentGraph = original, Graph = JsonNode.Parse("{\"generation\":1}"), CandidateCommit = "parent-commit" };
        var child = new Attempt { Id = "child", ParentAttemptId = parent.Id, ParentGraph = parent.Graph!.DeepClone(), BaseCommit = parent.CandidateCommit! };
        var history = new Dictionary<string, Attempt> { [parent.Id] = parent };
        ImprovementService.ValidatePromotionBaseline(child, original, history);
        ImprovementService.ValidatePromotionBaseline(child, parent.Graph, history);
        Assert.Throws<InvalidDataException>(() => ImprovementService.ValidatePromotionBaseline(child, JsonNode.Parse("{\"generation\":9}"), history));
        child.BaseCommit = "unverified-commit";
        Assert.Throws<InvalidDataException>(() => ImprovementService.ValidatePromotionBaseline(child, original, history));
    }
    [Fact] public async Task ApprovalVerifiesGoldAndFixtureBytes()
    {
        var folder = Path.Combine(Path.GetFullPath("artifacts"), "approval-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "gold")); Directory.CreateDirectory(Path.Combine(folder, "fixtures"));
        var gold = Path.Combine(folder, "gold", "test.json"); var fixture = Path.Combine(folder, "fixtures", "test.json");
        await File.WriteAllTextAsync(gold, "gold"); await File.WriteAllTextAsync(fixture, "fixture");
        static string Hash(string text) => ImprovementJson.Hash(text);
        var manifest = new JsonObject { ["samples"] = new JsonArray { new JsonObject { ["id"] = "test", ["goldSha256"] = Hash("gold"), ["fixtureSha256"] = Hash("fixture") } } };
        await File.WriteAllTextAsync(Path.Combine(folder, "manifest.json"), manifest.ToJsonString());
        await ImprovementService.VerifyFrozenArtifacts(folder);
        await File.WriteAllTextAsync(gold, "changed");
        await Assert.ThrowsAsync<InvalidDataException>(() => ImprovementService.VerifyFrozenArtifacts(folder));
    }
    [Fact] public void UnknownTokenCountersCannotProveEquivalence()
    {
        var baseline = JsonNode.Parse("""{"validatedFindings":{"micro":{"tp":4,"fp":1,"fn":2}},"metrics":{"reportedInputTokens":null,"reportedOutputTokens":null}}""");
        Assert.Equal("token_comparison_unavailable", Comparison.Compare(baseline, baseline)["classification"]!.GetValue<string>());
    }
    [Fact] public void CachedExecutionCostCannotWinEfficiencyComparison()
    {
        var baseline = JsonNode.Parse("""{"validatedFindings":{"micro":{"tp":4,"fp":1,"fn":2}},"metrics":{"reportedInputTokens":100,"reportedOutputTokens":50,"newlyConsumedInputTokens":100}}""");
        var candidate = baseline!.DeepClone(); candidate["metrics"]!["newlyConsumedInputTokens"] = 0;
        Assert.Equal("equivalent", Comparison.Compare(baseline, candidate)["classification"]!.GetValue<string>());
        candidate["metrics"]!["reportedOutputTokens"] = 40;
        Assert.Equal("efficiency_improvement", Comparison.Compare(baseline, candidate)["classification"]!.GetValue<string>());
        candidate["validatedFindings"]!["micro"]!["fp"] = 2;
        Assert.Equal("regression_or_tradeoff", Comparison.Compare(baseline, candidate)["classification"]!.GetValue<string>());
    }
}
