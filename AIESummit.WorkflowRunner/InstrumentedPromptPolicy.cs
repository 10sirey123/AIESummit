using AIESummit.Agents;
using Blueshift.Agents.LLM;
using Blueshift.Domain.Abstract;
using Blueshift.Domain.Entities;
using Blueshift.Domain.LLM;

namespace AIESummit.WorkflowRunner;

/// <summary>
/// Preserves provider-reported directional counters exposed by the native completion hook.
/// The pinned worker persists total tokens, but does not copy these two trace counters to the prompt.
/// </summary>
public sealed class InstrumentedPromptPolicy : ILlmPromptPolicy
{
    private readonly SplitPolicy inner = new();
    public ILlmCapable Hydrate(Type type, int id, IBasePersistenceRepository<LLMPrompt> repository) => inner.Hydrate(type, id, repository);
    public void PrepareAttachments(ILlmCapable subject, IBasePersistenceRepository<LLMPrompt> repository) => inner.PrepareAttachments(subject, repository);
    public void ResetOutputs(LLMPrompt prompt, IBasePersistenceRepository<LLMPrompt> repository) => inner.ResetOutputs(prompt, repository);
    public IMetadataExecutionContext CreateMetadataContext(LLMPrompt prompt, IBasePersistenceRepository<LLMPrompt> repository) => inner.CreateMetadataContext(prompt, repository);
    public object BeforeConnect(ILlmCapable subject, LLMPrompt prompt, BaseMetadata metadata, IMetadataExecutionContext context)
        => inner.BeforeConnect(subject, prompt, metadata, context);
    public void AfterConnect(ILlmCapable subject, LLMPrompt prompt, BaseMetadata metadata, IMetadataExecutionContext context, object token)
        => inner.AfterConnect(subject, prompt, metadata, context, token);
    public string GetConnectionError(ILlmCapable subject, LLMPrompt prompt, BaseMetadata metadata, Exception exception)
        => inner.GetConnectionError(subject, prompt, metadata, exception);
    public void OnFinished(LLMPrompt prompt, LlmPromptTrace trace, IBasePersistenceRepository<LLMPrompt> repository)
    {
        ApplyUsage(prompt, trace);
        inner.OnFinished(prompt, trace, repository);
    }

    public static void ApplyUsage(LLMPrompt prompt, LlmPromptTrace trace)
    {
        // Both completed and rejected-evidence calls reach this hook. Never derive I/O from a total.
        // Zero/absent trace counters remain unavailable when a nonzero total was reported.
        prompt.InputTokens = trace.InputTokens;
        prompt.OutputTokens = trace.OutputTokens;
    }

    public static bool BreakdownAvailable(LLMPrompt prompt)
        => prompt.ApiAttemps <= 1 && (prompt.ApiAttemps == 0 || prompt.InputTokens + prompt.OutputTokens > 0)
           && prompt.AccumulatedTokens <= prompt.Tokens;

    public static bool TotalAvailable(LLMPrompt prompt)
        => prompt.ApiAttemps <= 1 && (prompt.ApiAttemps == 0 || prompt.Tokens > 0);
}
