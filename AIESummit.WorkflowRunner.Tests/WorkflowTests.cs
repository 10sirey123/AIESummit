using AIESummit.Domain;
using AIESummit.WorkflowRunner;
using Blueshift.Agents.LLM;
using Blueshift.Domain.Entities;
using System.Text.Json;
using Runner = AIESummit.WorkflowRunner.WorkflowRunner;
using Xunit;

namespace AIESummit.WorkflowRunner.Tests;

public sealed class WorkflowTests
{
    private static WorkflowDefinition Graph() => new() { Nodes = [new() {
        Key = "dates", Targets = ExtractionCatalog.Defaults().Take(3).Select(t => t.Tag).ToList(),
        SystemPrompt = "Extract source facts only." }, new() {
        Key = "clauses", Targets = ExtractionCatalog.Defaults().Skip(3).Select(t => t.Tag).ToList(),
        SystemPrompt = "Extract contractual facts." }] };

    [Fact] public void TopologicalOrderHonorsDependenciesRegardlessOfFileOrder()
    {
        var graph = Graph(); graph.Nodes[0].DependsOn.Add("clauses");
        Assert.Equal(new[] { "clauses", "dates" }, graph.OrderedNodes().Select(n => n.Key));
    }

    [Fact] public void CyclesAreRejectedBeforeAnyWork()
    {
        var graph = Graph(); graph.Nodes[0].DependsOn.Add("clauses"); graph.Nodes[1].DependsOn.Add("dates");
        Assert.Throws<InvalidDataException>(() => graph.OrderedNodes());
    }

    [Fact] public void MissingOrDuplicateTargetAssignmentsAreRejected()
    {
        var graph = Graph(); graph.Nodes[0].Targets.Add(graph.Nodes[1].Targets[0]);
        Assert.Throws<InvalidDataException>(() => graph.OrderedNodes());
        graph = Graph(); graph.Nodes[0].Targets.RemoveAt(0);
        Assert.Throws<InvalidDataException>(() => graph.OrderedNodes());
    }

    [Fact] public void CandidateCannotSelectAnotherProviderModel()
    {
        var graph = Graph(); graph.Nodes[0].Model = "gpt-4.1-mini";
        Assert.Throws<InvalidDataException>(() => graph.OrderedNodes());
    }

    [Fact] public void ChangingIndependentNodePreservesOtherNodesReuseKey()
    {
        var graph = Graph(); var before = graph.Nodes[0].CacheKey(21, "frozen input", "schema"); var graphBefore = graph.Hash;
        graph.Nodes[1].SystemPrompt += " Use shorter quotes.";
        Assert.NotEqual(graphBefore, graph.Hash);
        Assert.Equal(before, graph.Nodes[0].CacheKey(21, "frozen input", "schema"));
    }

    [Fact] public void InputSchemaSettingsAndDocumentIdentityInvalidateReuse()
    {
        var node = Graph().Nodes[0]; var original = node.CacheKey(21, "input", "schema");
        Assert.NotEqual(original, node.CacheKey(22, "input", "schema"));
        Assert.NotEqual(original, node.CacheKey(21, "changed prerequisite output", "schema"));
        Assert.NotEqual(original, node.CacheKey(21, "input", "schema-v2"));
        node.ReasoningEffort = "high";
        Assert.NotEqual(original, node.CacheKey(21, "input", "schema"));
    }

    [Fact] public void NativeTraceDirectionalUsageIsPreservedEvenForRejectedEvidence()
    {
        var prompt = new LLMPrompt { ProcessingStatus = PromptProcessingStatus.PermanentError,
            ApiAttemps = 1, Tokens = 37, AccumulatedTokens = 37 };
        InstrumentedPromptPolicy.ApplyUsage(prompt, new LlmPromptTrace { InputTokens = 29, OutputTokens = 8 });
        Assert.Equal(29, prompt.InputTokens); Assert.Equal(8, prompt.OutputTokens);
        Assert.True(InstrumentedPromptPolicy.BreakdownAvailable(prompt));
        Assert.True(InstrumentedPromptPolicy.TotalAvailable(prompt));
    }

    [Fact] public void MissingHistoricalBreakdownIsNotInferredFromTotal()
    {
        var prompt = new LLMPrompt { ApiAttemps = 1, Tokens = 6422, AccumulatedTokens = 6422 };
        Assert.False(InstrumentedPromptPolicy.BreakdownAvailable(prompt));
        Assert.True(InstrumentedPromptPolicy.TotalAvailable(prompt));
        Assert.Equal(0, prompt.InputTokens); Assert.Equal(0, prompt.OutputTokens);
    }

    [Fact] public void UnreportedFailedAttemptAndIncompleteRetryCountersRemainUnknown()
    {
        var prompt = new LLMPrompt { ApiAttemps = 1, Tokens = 0 };
        Assert.False(InstrumentedPromptPolicy.BreakdownAvailable(prompt));
        Assert.False(InstrumentedPromptPolicy.TotalAvailable(prompt));
        prompt.ApiAttemps = 2; prompt.Tokens = 10; prompt.AccumulatedTokens = 20;
        InstrumentedPromptPolicy.ApplyUsage(prompt, new LlmPromptTrace { InputTokens = 7, OutputTokens = 3 });
        Assert.False(InstrumentedPromptPolicy.BreakdownAvailable(prompt));
        Assert.False(InstrumentedPromptPolicy.TotalAvailable(prompt));
    }

    [Fact] public void MissingFrozenSplitHasExplicitCodeAndKnownZeroNewUsage()
    {
        var progress = new Runner.SampleProgress { UploadId = 17 };
        progress.Mark("fixture_split", "fixture_split_unavailable");
        var result = Runner.FailedSample(progress, nameof(InvalidDataException), 0.5);
        Assert.Equal("fixture_split_unavailable", result.GetProperty("errorCode").GetString());
        Assert.Equal("fixture_split", result.GetProperty("errorStage").GetString());
        Assert.Equal(17, result.GetProperty("uploadId").GetInt32());
        Assert.Equal(0, result.GetProperty("metrics").GetProperty("newlyConsumedTotalTokens").GetInt32());
        Assert.True(result.GetProperty("metrics").GetProperty("inputOutputBreakdownAvailable").GetBoolean());
    }

    [Fact] public void InterruptedLaterNodePreservesCompletedFindingsAndKnownSubtotals()
    {
        var progress = new Runner.SampleProgress { UploadId = 17, ProviderStarted = true,
            LastResult = JsonSerializer.SerializeToElement(new {
                prediction = new { analyses = new[] { new { id = 123, status = "ready" } } },
                metrics = new { prompts = Array.Empty<object>(), knownInputTokensSubtotal = 12, knownOutputTokensSubtotal = 5, unknownUsagePromptCount = 0 },
                graph = new { nodes = Array.Empty<object>(), edges = Array.Empty<object>() }
            }) };
        progress.Mark("native_execution");
        var result = Runner.FailedSample(progress, nameof(InvalidOperationException), 2);
        Assert.Equal(123, result.GetProperty("prediction").GetProperty("analyses")[0].GetProperty("id").GetInt32());
        Assert.Equal(12, result.GetProperty("metrics").GetProperty("knownInputTokensSubtotal").GetInt32());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("metrics").GetProperty("newlyConsumedInputTokens").ValueKind);
        Assert.Equal(1, result.GetProperty("metrics").GetProperty("unknownUsagePromptCount").GetInt32());
        Assert.Equal("native_execution_failed", result.GetProperty("errorCode").GetString());
    }
}
