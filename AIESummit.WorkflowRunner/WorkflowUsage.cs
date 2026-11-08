using AIESummit.Domain;
using System.Text.Json;

namespace AIESummit.WorkflowRunner;

internal sealed class WorkflowUsage
{
    private readonly List<object> prompts = [];
    private long input, output, total, newInput, newOutput, newTotal, knownInput, knownOutput;
    private bool breakdown = true, newBreakdown = true, totalAvailable = true, newTotalAvailable = true;
    private int unknown;

    public object? Add(NativeWorkflowCall call, WorkflowNode node)
    {
        var prompt = call.Prompt;
        if (prompt == null) return null;
        var ioKnown = InstrumentedPromptPolicy.BreakdownAvailable(prompt);
        var totalKnown = InstrumentedPromptPolicy.TotalAvailable(prompt);
        var tokens = Math.Max(prompt.Tokens, prompt.AccumulatedTokens);
        input += prompt.InputTokens; output += prompt.OutputTokens; total += tokens;
        breakdown &= ioKnown; totalAvailable &= totalKnown;
        if (!call.CacheHit) {
            newInput += prompt.InputTokens; newOutput += prompt.OutputTokens; newTotal += tokens;
            newBreakdown &= ioKnown; newTotalAvailable &= totalKnown;
        }
        if (ioKnown) { knownInput += prompt.InputTokens; knownOutput += prompt.OutputTokens; } else unknown++;
        var metrics = new { id = prompt.ID, documentType = prompt.DocumentType, documentID = prompt.DocumentID,
            step = WorkflowRunner.StepTag(node), stepId = prompt.LLMStep_ID, model = prompt.UsedModel?.Name ?? node.Model,
            modelTag = prompt.UsedModel?.Tag, provider = prompt.UsedModel?.Provider.ToString() ?? "Meta", modelVersion = (string?)null,
            systemPromptHash = WorkflowDefinition.HashText(prompt.SystemPrompt), inputPromptHash = WorkflowDefinition.HashText(prompt.Prompt),
            outputSchemaHash = WorkflowDefinition.HashText(prompt.ExpectedResponseJsonFormat),
            generationParameters = new { temperature = prompt.Temperature, node.ReasoningEffort },
            prompt.InputTokens, prompt.OutputTokens, prompt.Tokens, prompt.AccumulatedTokens, prompt.ApiAttemps,
            prompt.ProcessingStatus, prompt.DateCreated, prompt.LastModified, cacheHit = call.CacheHit, sourcePromptId = prompt.ID,
            inputOutputBreakdownAvailable = ioKnown,
            incrementalInputTokens = call.CacheHit ? 0 : prompt.InputTokens, incrementalOutputTokens = call.CacheHit ? 0 : prompt.OutputTokens };
        prompts.Add(metrics);
        return metrics;
    }

    public object Snapshot() => new { prompts,
        reportedInputTokens = breakdown ? (long?)input : null, reportedOutputTokens = breakdown ? (long?)output : null,
        reportedTotalTokens = totalAvailable ? (long?)total : null,
        newlyConsumedInputTokens = newBreakdown ? (long?)newInput : null, newlyConsumedOutputTokens = newBreakdown ? (long?)newOutput : null,
        newlyConsumedTotalTokens = newTotalAvailable ? (long?)newTotal : null,
        inputOutputBreakdownAvailable = breakdown, newInputOutputBreakdownAvailable = newBreakdown,
        knownInputTokensSubtotal = knownInput, knownOutputTokensSubtotal = knownOutput, unknownUsagePromptCount = unknown,
        ocrTokens = (long?)null,
        note = "Includes all detector and extraction calls. Logical usage includes retained responses; new usage counts only new calls. Provider counters are not billing verification." };
}
