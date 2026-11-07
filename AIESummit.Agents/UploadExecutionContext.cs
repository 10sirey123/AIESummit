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
        if (subjectType == typeof(DocumentAnalysis))
        {
            var uploadId = services.GetRequiredService<IBasePersistenceRepository<DocumentAnalysis>>().Objects
                .Where(a => a.ID == subjectId).Select(a => a.Document.UploadId).SingleOrDefault();
            return new(nameof(UploadedFile), uploadId, ContractExtractionStep.Tag);
        }
        if (subjectType == typeof(OcrPage))
        {
            var uploadId = services.GetRequiredService<IBasePersistenceRepository<OcrPage>>().Objects
                .Where(p => p.ID == subjectId).Select(p => p.UploadId).SingleOrDefault();
            return new(nameof(UploadedFile), uploadId, PageSectionsStep.Tag);
        }
        if (subjectType != typeof(LLMPrompt)) return new();
        var prompt = services.GetRequiredService<IBasePersistenceRepository<LLMPrompt>>().Objects
            .Where(p => p.ID == subjectId)
            .Select(p => new { p.DocumentID, p.DocumentType, p.LLMStep.Tag }).SingleOrDefault();
        if (prompt?.DocumentType == nameof(DocumentAnalysis))
        {
            var uploadId = services.GetRequiredService<IBasePersistenceRepository<DocumentAnalysis>>().Objects
                .Where(a => a.ID == prompt.DocumentID).Select(a => a.Document.UploadId).SingleOrDefault();
            return new(nameof(UploadedFile), uploadId, prompt.Tag);
        }
        if (prompt?.DocumentType == nameof(OcrPage))
        {
            var uploadId = services.GetRequiredService<IBasePersistenceRepository<OcrPage>>().Objects
                .Where(p => p.ID == prompt.DocumentID).Select(p => p.UploadId).SingleOrDefault();
            return new(nameof(UploadedFile), uploadId, prompt.Tag);
        }
        return prompt?.DocumentType == nameof(UploadedFile) ? new(nameof(UploadedFile), prompt.DocumentID, prompt.Tag) : new();
    }
}
