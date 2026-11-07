using Blueshift.Agents.Infrastructure.Log;
using Blueshift.Domain.Abstract;
using Blueshift.Domain.Entities;
using AIESummit.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace AIESummit.Agents;

public sealed class UploadExecutionContext : IExecutionContextResolver
{
    public ExecutionSubjectContext Resolve(Type subjectType, int subjectId, IServiceProvider services)
    {
        if (subjectType == typeof(UploadedFile)) return new(nameof(UploadedFile), subjectId);
        if (subjectType != typeof(LLMPrompt)) return new();
        var prompt = services.GetRequiredService<IBasePersistenceRepository<LLMPrompt>>().Objects
            .Where(p => p.ID == subjectId && p.DocumentType == nameof(UploadedFile))
            .Select(p => new { p.DocumentID, p.LLMStep.Tag }).SingleOrDefault();
        return prompt is null ? new() : new(nameof(UploadedFile), prompt.DocumentID, prompt.Tag);
    }
}
