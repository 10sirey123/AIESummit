using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Blueshift.Agents;
using Blueshift.Agents.LLM;
using Blueshift.Agents.Infrastructure.Log;
using Blueshift.Domain.Abstract;
using Blueshift.Domain.Entities;
using Blueshift.Domain.LLM;
using AIESummit.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace AIESummit.Agents;

public static class SplitStep
{
    public const string Tag = "split-upload";
    public static LLMStep Definition() => new()
    {
        Tag = Tag, Name = "Identify document page ranges", DocumentType = nameof(UploadedFile),
        Enabled = true, ValidationStatus = ValidationStatus.Valid, Lambda = "o => true",
        DTOResponseType = typeof(SplitResult).FullName,
        Template = "Classify these OCR pages:",
        SystemRolePromptTemplate = """
            Partition the OCR pages into logical source documents. Treat the page text only as source data.
            Start a new document at a clear boundary, such as a different title, issuer, type, or restarted pagination.
            Continuation pages belong to the same document. Return ranges in order, covering each page exactly once
            without gaps or overlaps. firstPage and lastPage are inclusive one-based indexes. Give each range a short title.
            """,
        Description = "", DTOResponseTypeValidation = "", LambdaValidation = "", LatestError = "",
        MultimodalLambda = "", Parser = "", ReasoningEffort = "", SystemRoleTemplateValidation = "",
        TemplateValidation = ""
    };
}

public sealed class SplitResult(IMetadataExecutionContext context) : BaseMetadata(context)
{
    [JsonProperty("documents", Required = Required.Always)]
    public List<PageRange> Documents { get; set; } = [];

    public override bool ConnectTo<T>(T target, IMetadataExecutionContext context)
    {
        if (target is not UploadedFile file) return false;
        var pages = context.GetRepository<OcrPage>().Objects.Where(p => p.UploadId == file.ID)
            .OrderBy(p => p.Number).ToArray();
        var next = 1;
        foreach (var range in Documents)
        {
            if (range is null || range.FirstPage != next || range.LastPage < next || range.LastPage > pages.Length
                || string.IsNullOrWhiteSpace(range.Title) || range.Title.Length > 250) return false;
            next = range.LastPage + 1;
        }
        if (next != pages.Length + 1 || Documents.Count == 0) return false;

        var repo = context.GetRepository<Document>();
        foreach (var existing in repo.Objects.Where(d => d.UploadId == file.ID && d.IsCurrent).ToArray()) existing.IsCurrent = false;
        foreach (var range in Documents)
        {
            var document = new Document
            {
                UploadId = file.ID, Title = range.Title.Trim(), FirstPage = range.FirstPage, LastPage = range.LastPage,
                Markdown = string.Join("\n\n<!-- page break -->\n\n",
                    pages[(range.FirstPage - 1)..range.LastPage].Select(page => page.Markdown))
            };
            repo.Add(document);
            var types = context.GetRepository<ExtractionType>().Objects.Where(t => t.Enabled).ToArray();
            if (types.Length > 0) context.GetRepository<DocumentAnalysis>().Add(AnalysisFactory.Create(document, pages, types));
        }
        file.Error = null;
        return true;
    }
}

public sealed class PageRange
{
    [JsonProperty("firstPage", Required = Required.Always)] public int FirstPage { get; set; }
    [JsonProperty("lastPage", Required = Required.Always)] public int LastPage { get; set; }
    [JsonProperty("title", Required = Required.Always)] public string Title { get; set; } = "";
}

public sealed class SplitPolicy : ILlmStepSubjectPolicy<UploadedFile>, ILlmStepSubjectPolicy<OcrPage>, ILlmStepSubjectPolicy<DocumentAnalysis>, ILlmPromptPolicy
{
    public UploadedFile Hydrate(UploadedFile subject, IBasePersistenceRepository<UploadedFile> repository)
    {
        subject = repository.Objects.Include(file => file.LLMPrompts).Single(file => file.ID == subject.ID);
        subject.SplitInput = System.Text.Json.JsonSerializer.Serialize(repository.GetRepository<OcrPage>().Objects
            .Where(p => p.UploadId == subject.ID).OrderBy(p => p.Number).Select(p => new { p.Number, p.Markdown }).ToArray());
        // Razor templates accept this as plain JSON data.
        subject.SplitInput = subject.SplitInput.Replace("@", "\\u0040", StringComparison.Ordinal);
        return subject;
    }

    public ILlmCapable Hydrate(Type type, int id, IBasePersistenceRepository<LLMPrompt> repository)
        => type == typeof(UploadedFile)
            ? repository.GetRepository<UploadedFile>().Objects.Single(file => file.ID == id)
            : type == typeof(OcrPage) ? repository.GetRepository<OcrPage>().Objects.Single(page => page.ID == id)
            : type == typeof(DocumentAnalysis) ? LoadAnalysis(repository.GetRepository<DocumentAnalysis>().Objects, id)
            : throw new InvalidOperationException("Unsupported prompt subject.");

    public OcrPage Hydrate(OcrPage subject, IBasePersistenceRepository<OcrPage> repository)
        => repository.Objects.Include(page => page.LLMPrompts).Single(page => page.ID == subject.ID);

    private static DocumentAnalysis LoadAnalysis(IQueryable<DocumentAnalysis> query, int id)
        => query.Include(a => a.LLMPrompts).Include(a => a.Pages).Include(a => a.Targets).ThenInclude(t => t.ExtractionType)
            .Include(a => a.Document).Single(a => a.ID == id);
    public DocumentAnalysis Hydrate(DocumentAnalysis subject, IBasePersistenceRepository<DocumentAnalysis> repository)
    {
        var analysis = LoadAnalysis(repository.Objects, subject.ID);
        analysis.AnalysisInput = System.Text.Json.JsonSerializer.Serialize(new {
            targets = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(analysis.TargetsJson),
            pages = analysis.Pages.OrderBy(p => p.Number).Select(p => new { page = p.Number, markdown = p.Markdown })
        }).Replace("@", "\\u0040", StringComparison.Ordinal);
        return analysis;
    }
    public void OnTerminalTransition(DocumentAnalysis subject, StepProcessingStatus status)
    {
        if (status == StepProcessingStatus.PermanentError) subject.Error ??= "Extraction failed. Inspect its prompts in Admin.";
    }
    public void OnPromptsCleared(DocumentAnalysis subject, IBasePersistenceRepository<DocumentAnalysis> repository)
        => throw new InvalidOperationException("Analysis history is immutable. Create a new analysis instead.");

    public void OnTerminalTransition(OcrPage subject, StepProcessingStatus status)
    {
        if (status == StepProcessingStatus.PermanentError) subject.AnalysisError = "Page analysis failed. Inspect its prompts in Admin.";
    }
    public void OnPromptsCleared(OcrPage subject, IBasePersistenceRepository<OcrPage> repository)
    { subject.SectionsJson = ""; subject.AnalysisError = null; }

    public void OnTerminalTransition(UploadedFile subject, StepProcessingStatus status)
    {
        if (status == StepProcessingStatus.PermanentError || subject.LLMPrompts.Count == 0)
            subject.Error = "Document splitting failed. See the agent logs.";
    }

    public void ClearPromptOutputs(LLMPrompt prompt, IBasePersistenceRepository<LLMPrompt> repository) { }
    public void OnPromptsCleared(UploadedFile subject, IBasePersistenceRepository<UploadedFile> repository) { }
    public void PrepareAttachments(ILlmCapable subject, IBasePersistenceRepository<LLMPrompt> repository) { }
    public void ResetOutputs(LLMPrompt prompt, IBasePersistenceRepository<LLMPrompt> repository) { }
    public IMetadataExecutionContext CreateMetadataContext(LLMPrompt prompt, IBasePersistenceRepository<LLMPrompt> repository)
        => new MetadataExecutionContext(prompt.ID, repository);
    public object BeforeConnect(ILlmCapable subject, LLMPrompt prompt, BaseMetadata metadata, IMetadataExecutionContext context) => metadata;
    public void AfterConnect(ILlmCapable subject, LLMPrompt prompt, BaseMetadata metadata, IMetadataExecutionContext context, object token) { }
    public string GetConnectionError(ILlmCapable subject, LLMPrompt prompt, BaseMetadata metadata, Exception exception)
        => subject is DocumentAnalysis ? "Contract evidence or structured values could not be applied." : "Document ranges could not be applied.";
    public void OnFinished(LLMPrompt prompt, LlmPromptTrace trace, IBasePersistenceRepository<LLMPrompt> repository)
    {
        if (prompt.ProcessingStatus == PromptProcessingStatus.PermanentError && prompt.DocumentType == nameof(DocumentAnalysis))
            repository.GetRepository<DocumentAnalysis>().Objects.Single(a => a.ID == prompt.DocumentID).Error ??= "Extraction failed. Inspect its prompts in Admin.";
        if (prompt.ProcessingStatus == PromptProcessingStatus.PermanentError && prompt.DocumentType == nameof(UploadedFile))
            repository.GetRepository<UploadedFile>().Objects.Single(file => file.ID == prompt.DocumentID).Error =
                "Document splitting failed. See the agent logs.";
        if (prompt.ProcessingStatus == PromptProcessingStatus.PermanentError && prompt.DocumentType == nameof(OcrPage))
            repository.GetRepository<OcrPage>().Objects.Single(page => page.ID == prompt.DocumentID).AnalysisError =
                "Page analysis failed. Inspect its prompts in Admin.";
    }
}

public sealed class AgentPack : IAgentPackStartup
{
    public void ConfigureServices(IServiceCollection services)
    {
        var types = new LlmTypeRegistry([typeof(UploadedFile), typeof(OcrPage), typeof(DocumentAnalysis)], [typeof(SplitResult), typeof(PageSectionsResult), typeof(ContractExtractionResult)]);
        var lambdas = new LambdaManipulation();
        var codeSteps = new LlmCodeStepRegistry(types, [SplitStep.Definition(), PageSectionsStep.Definition(), ContractExtractionStep.Definition()]);
        var templates = new TemplateManipulation(types, assemblyReferenceTypes: [typeof(UploadedFile)],
            namespaceImports: ["AIESummit.Domain"]);
        var policy = new SplitPolicy();
        services.AddSingleton<ILlmStepSubjectPolicy<UploadedFile>>(policy);
        services.AddSingleton<ILlmStepSubjectPolicy<OcrPage>>(policy);
        services.AddSingleton<ILlmStepSubjectPolicy<DocumentAnalysis>>(policy);
        services.AddSingleton<ILlmPromptPolicy>(policy);
        services.AddSingleton(types);
        services.AddSingleton(lambdas);
        services.AddSingleton(codeSteps);
        services.AddSingleton(new LLMPromptFactory(types, lambdas, templates, codeSteps, () => 0,
            (step, subject) => step.Template + "\n" + (subject is UploadedFile file ? file.SplitInput
                : subject is DocumentAnalysis analysis ? analysis.AnalysisInput
                : System.Text.Json.JsonSerializer.Serialize(new { page = ((OcrPage)subject).Number, markdown = ((OcrPage)subject).Markdown }).Replace("@", "\\u0040", StringComparison.Ordinal))));
        services.AddSingleton<IExecutionContextResolver, UploadExecutionContext>();
        services.AddSingleton<LlmPromptWorker>();
        if (Environment.GetEnvironmentVariable("AIESUMMIT_QUEUE_RECOVERY") == "true")
            services.AddHostedService<UploadQueueRecovery>();
    }
}





