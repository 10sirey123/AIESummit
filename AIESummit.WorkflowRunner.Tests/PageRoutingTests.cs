using System.Text.Json;
using AIESummit.Domain;
using AIESummit.WorkflowRunner;
using Blueshift.Domain.LLM;
using Xunit;

namespace AIESummit.WorkflowRunner.Tests;

public sealed class PageRoutingTests
{
    private const string Target = "liability-cap";
    private static WorkflowDefinition Graph() => new() { Nodes = [
        new() { Key = "other-targets", Targets = ExtractionCatalog.Defaults().Where(t => t.Tag != Target).Select(t => t.Tag).ToList(), SystemPrompt = "Extract the supplied facts." },
        new() { Key = "extract-liability", Targets = [Target], SelectedPagesFrom = "detect-liability", SystemPrompt = "Extract from this page only." },
        new() { Key = "detect-liability", Kind = "detect-pages", Targets = [Target], SystemPrompt = "Classify presence on this page." }
    ] };
    private static DocumentAnalysis Analysis(params string[] tags) => new() {
        TargetsJson = JsonSerializer.Serialize(tags.Select(tag => new { tag })),
        Pages = [new() { Number = 7, Markdown = "The aggregate liability shall not exceed $100. Exact source." }]
    };

    [Fact] public void DetectorOverlapDoesNotDuplicateFinalTargetPublication()
    {
        var graph = Graph(); var ordered = graph.OrderedNodes();
        Assert.True(Array.FindIndex(ordered, n => n.Key == "detect-liability") < Array.FindIndex(ordered, n => n.Key == "extract-liability"));
        Assert.Equal(10, graph.Nodes.Where(n => n.NodeKind == "extract").Sum(n => n.Targets.Count));
        Assert.Contains("detect-liability", graph.Nodes[1].EffectiveDependencies);
    }

    [Fact] public void DetectorCanCombineTypesWhileSpecializedExtractorsRemainSingleTarget()
    {
        var graph = Graph(); graph.Nodes[2].Targets.Add("audit-rights");
        graph.OrderedNodes();
        graph.Nodes[1].Targets.Add("audit-rights");
        Assert.Throws<InvalidDataException>(() => graph.OrderedNodes());
    }

    [Fact] public void SelectorMustReferenceDetectorCoveringTheSameTarget()
    {
        var graph = Graph(); graph.Nodes[2].Targets = ["audit-rights"];
        Assert.Throws<InvalidDataException>(() => graph.OrderedNodes());
        graph = Graph(); graph.Nodes[1].SelectedPagesFrom = "other-targets";
        Assert.Throws<InvalidDataException>(() => graph.OrderedNodes());
    }

    [Fact] public void DetectorCannotBecomeAnImplicitFindingDependency()
    {
        var graph = Graph(); graph.Nodes[0].DependsOn = ["detect-liability"];
        Assert.Throws<InvalidDataException>(() => graph.OrderedNodes());
    }

    [Fact] public void PresenceRequiresAnchoredEvidenceAndAllRequestedTagsExactlyOnce()
    {
        var analysis = Analysis(Target, "audit-rights");
        var valid = PageTargetPresenceResult.Validate(analysis, [
            new() { Tag = Target, Outcome = "present", Quote = "liability shall not exceed $100" },
            new() { Tag = "audit-rights", Outcome = "absent" }
        ]);
        Assert.Equal("liability shall not exceed $100", valid[0].Quote);
        Assert.Equal("exact", valid[0].MatchMethod);
        Assert.Throws<InvalidDataException>(() => PageTargetPresenceResult.Validate(analysis, [new() { Tag = Target, Outcome = "present" }]));
        Assert.Throws<InvalidDataException>(() => PageTargetPresenceResult.Validate(Analysis(Target), [new() { Tag = Target, Outcome = "present", Quote = "invisible zygotes orbit Mars" }]));
    }

    [Fact] public void UncertainRoutesToExtractionWithoutInventingEvidence()
    {
        var validated = PageTargetPresenceResult.Validate(Analysis(Target), [new() { Tag = Target, Outcome = "uncertain" }]);
        Assert.Null(validated.Single().Quote);
        Assert.True(PageRouting.ShouldExtract(new(Target, 7, "uncertain", true, "detector/7", 1, 2)));
        Assert.True(PageRouting.ShouldExtract(new(Target, 7, "present", true, "detector/7", 1, 2)));
        Assert.False(PageRouting.ShouldExtract(new(Target, 7, "absent", true, "detector/7", 1, 2)));
        Assert.False(PageRouting.ShouldExtract(new(Target, 7, "present", false, "detector/7", 1, 2)));
    }

    [Fact] public void NegativeDerivationRequiresEveryPageSuccessfullyAbsent()
    {
        PageRoutingDecision A(int page, string outcome, bool success = true) => new(Target, page, outcome, success, "detector/" + page, 1, 2);
        Assert.True(PageRouting.CanAssertAbsent([A(1, "absent"), A(2, "absent")]));
        Assert.False(PageRouting.CanAssertAbsent([]));
        Assert.False(PageRouting.CanAssertAbsent([A(1, "absent"), A(2, "unknown", false)]));
        Assert.False(PageRouting.CanAssertAbsent([A(1, "absent"), A(2, "uncertain")]));
    }

    [Fact] public void SpecializedPageInputNeverContainsOffPageTextOrDetectorPayload()
    {
        using var definitions = JsonDocument.Parse("[{\"tag\":\"liability-cap\"}]");
        var page = new PageTextRevision { Number = 7, Markdown = "ONLY THIS PAGE" };
        var input = PageRouting.PageInput(definitions.RootElement.EnumerateArray(), page);
        using var parsed = JsonDocument.Parse(input);
        Assert.Equal(1, parsed.RootElement.GetProperty("pages").GetArrayLength());
        Assert.Equal(7, parsed.RootElement.GetProperty("pages")[0].GetProperty("page").GetInt32());
        Assert.Equal(0, parsed.RootElement.GetProperty("prerequisites").GetArrayLength());
        Assert.False(parsed.RootElement.TryGetProperty("decisions", out _));
        var node = Graph().Nodes[1]; var original = node.CacheKey(21, input, "schema");
        node.SelectedPagesFrom = "a-different-detector-version";
        Assert.Equal(original, node.CacheKey(21, input, "schema"));
        page.Markdown += " changed OCR";
        Assert.NotEqual(original, node.CacheKey(21, PageRouting.PageInput(definitions.RootElement.EnumerateArray(), page), "schema"));
    }

    [Fact] public void DefaultNodeSerializationAndDefinitionHashRemainLegacyCompatible()
    {
        var node = new WorkflowNode { Targets = ExtractionCatalog.Defaults().Select(t => t.Tag).ToList(), SystemPrompt = "Frozen role" };
        var graph = new WorkflowDefinition { Nodes = [node] };
        var legacyNode = new { node.Key, node.Targets, node.DependsOn, node.SystemPrompt, node.Prompt, node.Model, node.Temperature, node.ReasoningEffort };
        var legacyHash = WorkflowDefinition.HashText(JsonSerializer.Serialize(new { graph.Version, graph.Name, Nodes = new[] { legacyNode } }, WorkflowDefinition.Json));
        Assert.Equal(legacyHash, graph.Hash);
        var oldDefinition = WorkflowDefinition.HashText(JsonSerializer.Serialize(new { Targets = node.Targets.Order(StringComparer.Ordinal),
            node.SystemPrompt, node.Prompt, node.Model, node.Temperature, node.ReasoningEffort,
            responseContract = "ContractExtractionResult/v1", validation = "evidence-word-dice-50/v1" }));
        Assert.Equal(oldDefinition, node.DefinitionHash);
        var telemetryDefinition = JsonSerializer.SerializeToElement(AIESummit.WorkflowRunner.WorkflowRunner.DescribeWorkflow(graph, graph.OrderedNodes()), WorkflowDefinition.Json);
        Assert.False(telemetryDefinition.GetProperty("nodes")[0].TryGetProperty("nodeKind", out _));
        Assert.False(telemetryDefinition.GetProperty("nodes")[0].TryGetProperty("selectedPagesFrom", out _));
    }

    [Fact] public void DetectorSchemaIsRoutingOnlyAndCarriesNoFindingContract()
    {
        var types = new LlmTypeRegistry([typeof(DocumentAnalysis)], [typeof(PageTargetPresenceResult)]);
        var schema = new TemplateManipulation(types).GetJsonSchema(typeof(PageTargetPresenceResult).FullName!);
        Assert.Contains("decisions", schema); Assert.Contains("outcome", schema);
        Assert.DoesNotContain("findings", schema); Assert.DoesNotContain("values", schema);
    }
}
