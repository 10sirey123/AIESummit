using System.Text.Json;
using AIESummit.Agents;
using AIESummit.Domain;
using Blueshift.Agents.LLM;
using Blueshift.Domain.Abstract;
using Blueshift.Domain.Entities;
using Blueshift.Domain.LLM;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AIESummit.WorkflowRunner;

internal sealed record NativeWorkflowCall(DocumentAnalysis? Analysis, LLMPrompt? Prompt, bool CacheHit, bool CacheMiss)
{
    public bool Succeeded => Analysis?.CompletedAt != null;
}

/// <summary>Executes a captured, native BlueShift prompt. Candidate code never handles credentials or provider HTTP.</summary>
internal static class NativeWorkflowExecutor
{
    public static async Task<NativeWorkflowCall> Execute(AIESummitDb db, IServiceProvider services, Document document,
        DocumentAnalysis baseline, WorkflowNode node, IReadOnlyList<PageTextRevision> pages, JsonElement[] definitions,
        string input, bool cacheOnly, WorkflowRunner.SampleProgress progress)
    {
        var responseType = node.NodeKind == "detect-pages" ? typeof(PageTargetPresenceResult) : typeof(ContractExtractionResult);
        var types = new LlmTypeRegistry([typeof(DocumentAnalysis)], [responseType]);
        var templates = new TemplateManipulation(types, [typeof(DocumentAnalysis)], ["AIESummit.Domain"]);
        var schema = templates.GetJsonSchema(responseType.FullName!)
            ?? throw new InvalidOperationException("The native response schema could not be generated.");
        var key = AnalysisFactory.ImprovementRequestPrefix + node.CacheKey(document.ID, input, schema);
        progress.Mark("node_cache_lookup");
        var existing = await WorkflowRunner.LoadAnalysis(db).SingleOrDefaultAsync(a => a.DocumentId == document.ID && a.RequestKey == key);
        if (existing != null)
        {
            var retained = existing.LLMPrompts.Single();
            progress.Mark("cache_identity", "cache_identity_mismatch");
            if (existing.InputHash != WorkflowDefinition.HashText(input) || retained.Prompt != node.Prompt + "\n" + input
                || retained.SystemPrompt != node.SystemPrompt || retained.ExpectedResponseJsonFormat != schema
                || retained.DTOResponseType != responseType.FullName
                || retained.Temperature != node.Temperature / 100.0 || (retained.ReasoningEffort ?? "") != node.ReasoningEffort
                || retained.UsedModel?.Name != node.Model || retained.UsedModel.Provider != LLMAPIProvider.Meta)
                throw new InvalidDataException("Retained node content no longer matches its experiment identity.");
            return new(existing, retained, true, false);
        }
        if (cacheOnly) return new(null, null, false, true);

        await using var transaction = await db.Database.BeginTransactionAsync();
        progress.Mark("step_configuration");
        var model = await db.LLMModels.SingleOrDefaultAsync(m => m.Provider == LLMAPIProvider.Meta && m.Name == node.Model)
            ?? throw new InvalidOperationException("The selected Muse model is not configured.");
        var tag = WorkflowRunner.StepTag(node);
        var step = await db.LLMSteps.SingleOrDefaultAsync(s => s.Tag == tag);
        if (step == null)
        {
            step = ContractExtractionStep.Definition(); step.Tag = tag; step.Name = "Improvement · " + node.Key;
            step.Enabled = false; step.Lambda = "o => false"; step.Model = model; step.DTOResponseType = responseType.FullName!;
            step.Template = node.Prompt; step.SystemRolePromptTemplate = node.SystemPrompt;
            step.Temperature = node.Temperature; step.ReasoningEffort = node.ReasoningEffort;
            db.LLMSteps.Add(step); await db.SaveChangesAsync();
        }
        if (step.DTOResponseType != responseType.FullName)
            throw new InvalidDataException("The retained step response contract has changed.");
        var registry = new LlmCodeStepRegistry(types, [step]);
        var analysis = new DocumentAnalysis { Document = document, RequestKey = key,
            InputHash = WorkflowDefinition.HashText(input), TargetsJson = JsonSerializer.Serialize(definitions),
            LLMStepStatus = StepProcessingStatus.New,
            Pages = pages.Select(CopyPage).ToList(),
            Targets = baseline.Targets.Where(t => node.Targets.Contains(t.ExtractionType.Tag)).Select(t => new AnalysisTargetResult {
                ExtractionTypeId = t.ExtractionTypeId }).ToList() };
        db.DocumentAnalyses.Add(analysis); await db.SaveChangesAsync();
        var prompt = new LLMPrompt { LLMStep = step, LLMStep_ID = step.ID, DocumentType = nameof(DocumentAnalysis),
            DocumentID = analysis.ID, DTOResponseType = responseType.FullName!, Prompt = node.Prompt + "\n" + input,
            SystemPrompt = node.SystemPrompt, ExpectedResponseJsonFormat = schema, Temperature = node.Temperature / 100.0,
            PreferedModel = model, PreferedModel_ID = model.ID, ReasoningEffort = node.ReasoningEffort,
            ProcessingStatus = PromptProcessingStatus.New };
        analysis.LLMPrompts.Add(prompt); await db.SaveChangesAsync();
        var worker = new LlmPromptWorker(null!, new InstrumentedPromptPolicy(),
            new LlmProviderFactory(services.GetRequiredService<IHttpClientFactory>(), services.GetRequiredService<IConfiguration>()),
            types, new LambdaManipulation(), registry, services.GetRequiredService<IMemoryCache>());
        progress.Mark("native_execution"); progress.ProviderStarted = true;
        await worker.ExecuteAsync(prompt, services.GetRequiredService<IBasePersistenceRepository<LLMPrompt>>(),
            new LlmPromptExecutionOptions { MD5HashCheck = false });
        analysis.LLMStepStatus = analysis.CompletedAt != null ? StepProcessingStatus.Processed : StepProcessingStatus.PermanentError;
        if (analysis.CompletedAt == null) analysis.Error ??= "Native page workflow failed; inspect its retained prompt.";
        await db.SaveChangesAsync(); await transaction.CommitAsync();
        progress.Mark("node_result");
        analysis = await WorkflowRunner.LoadAnalysis(db).SingleAsync(a => a.ID == analysis.ID);
        return new(analysis, prompt, false, false);
    }

    public static PageTextRevision CopyPage(PageTextRevision page) => new() {
        OcrPageId = page.OcrPageId, Number = page.Number, Markdown = page.Markdown, ContentHash = page.ContentHash
    };

    public static async Task<(DocumentAnalysis Analysis, bool Reused)> DeriveAbsent(AIESummitDb db, Document document,
        DocumentAnalysis baseline, WorkflowNode node, JsonElement[] definitions, IReadOnlyList<PageRoutingDecision> decisions)
    {
        if (!PageRouting.CanAssertAbsent(decisions)) throw new InvalidDataException("Incomplete routing cannot assert target absence.");
        var provenance = JsonSerializer.Serialize(new { kind = "page-routing-negative-v1", target = node.Targets.Single(),
            detectorNodeKey = node.SelectedPagesFrom, pages = decisions.OrderBy(d => d.PageNumber).Select(d => new {
                pageNumber = d.PageNumber, outcome = d.Outcome, sourceAnalysisId = d.AnalysisId, sourcePromptId = d.PromptId }) });
        var key = AnalysisFactory.ImprovementRequestPrefix + "route/" + WorkflowDefinition.HashText(JsonSerializer.Serialize(new {
            documentId = document.ID, node.DefinitionHash, definitions, provenance }));
        var existing = await WorkflowRunner.LoadAnalysis(db).SingleOrDefaultAsync(a => a.DocumentId == document.ID && a.RequestKey == key);
        if (existing != null) return (existing, true);
        await using var tx = await db.Database.BeginTransactionAsync();
        var analysis = new DocumentAnalysis { Document = document, RequestKey = key,
            InputHash = WorkflowDefinition.HashText(provenance), TargetsJson = JsonSerializer.Serialize(definitions),
            Pages = baseline.Pages.Select(CopyPage).ToList(), LLMStepStatus = StepProcessingStatus.New,
            Targets = baseline.Targets.Where(t => node.Targets.Contains(t.ExtractionType.Tag)).Select(t => new AnalysisTargetResult {
                ExtractionTypeId = t.ExtractionTypeId }).ToList() };
        db.DocumentAnalyses.Add(analysis); await db.SaveChangesAsync();
        var validated = ContractExtractionResult.Validate(analysis, [new TargetOutput {
            Tag = node.Targets.Single(), Outcome = "not_found_in_reviewed_text",
            Explanation = "Every frozen source page completed target detection and was classified absent; no extraction call was needed." }]);
        analysis.Targets.Single().Outcome = validated.Single().Outcome;
        analysis.Targets.Single().Explanation = validated.Single().Explanation;
        analysis.LLMJsonResult = provenance; analysis.CompletedAt = DateTime.UtcNow; analysis.LLMStepStatus = StepProcessingStatus.Processed;
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return (await WorkflowRunner.LoadAnalysis(db).SingleAsync(a => a.ID == analysis.ID), false);
    }
}
