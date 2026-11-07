using System.Security.Claims;
using System.Text.Json;
using AIESummit.Agents;
using AIESummit.Domain;
using Blueshift.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIESummit.Api;

internal static class ExtractionEndpoints
{
    public static void MapExtractionEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization();
        api.MapGet("/extraction-types", async (AIESummitDb db) => Results.Ok(await db.ExtractionTypes.OrderBy(t => t.Tag)
            .Select(t => new { t.Tag, t.Name, t.BenchmarkCategory, t.Instructions, t.ParametersJson, t.DefinitionVersion, t.Enabled }).ToListAsync()));
        api.MapPost("/uploads/{id:int}/extractions", Start);
        api.MapGet("/uploads/{id:int}/extractions", Get);
        api.MapGet("/uploads/{id:int}/metrics", Metrics);
    }

    private static async Task<bool> Owns(int id, ClaimsPrincipal user, AIESummitDb db, CancellationToken ct)
        => await db.Uploads.AnyAsync(u => u.ID == id && u.OwnerId == user.FindFirstValue("sub"), ct);

    private static async Task<IResult> Start(int id, ClaimsPrincipal user, AIESummitDb db, CancellationToken ct, string requestKey = "initial")
    {
        if (!await Owns(id, user, db, ct)) return Results.NotFound();
        if (string.IsNullOrWhiteSpace(requestKey) || requestKey.Length > 100) return Results.BadRequest(new { error = "requestKey must have 1–100 characters." });
        if (!await db.LLMSteps.AnyAsync(s => s.Tag == ContractExtractionStep.Tag && s.Enabled, ct))
            return Results.Problem("Enable the extraction step in Admin first.", statusCode: 409);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(825833002, {id})", ct);
        var documents = await db.Documents.Where(d => d.UploadId == id && d.IsCurrent).ToArrayAsync(ct);
        if (documents.Length == 0) return Results.Problem("Wait for OCR and splitting to complete.", statusCode: 409);
        var pages = await db.Pages.Where(p => p.UploadId == id).ToArrayAsync(ct);
        var types = await db.ExtractionTypes.Where(t => t.Enabled).ToArrayAsync(ct);
        if (types.Length == 0) return Results.Problem("Enable at least one extraction target.", statusCode: 409);
        foreach (var doc in documents)
            if (!await db.DocumentAnalyses.AnyAsync(a => a.DocumentId == doc.ID && a.RequestKey == requestKey, ct))
                db.DocumentAnalyses.Add(AnalysisFactory.Create(doc, pages, types, requestKey));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Results.Accepted($"/api/v1/uploads/{id}/extractions", new { id, requestKey });
    }

    private static async Task<IResult> Get(int id, ClaimsPrincipal user, AIESummitDb db, CancellationToken ct)
    {
        if (!await Owns(id, user, db, ct)) return Results.NotFound();
        var analyses = await db.DocumentAnalyses.AsNoTracking().Where(a => a.Document.UploadId == id)
            .Include(a => a.Document).Include(a => a.LLMPrompts).Include(a => a.Pages).Include(a => a.Targets).ThenInclude(t => t.ExtractionType)
            .Include(a => a.Targets).ThenInclude(t => t.Extractions).ThenInclude(e => e.Evidence).OrderBy(a => a.ID).AsSplitQuery().ToListAsync(ct);
        return Results.Ok(new { analyses = analyses.Select(a => new {
            id = a.ID, a.DocumentId, a.Document.IsCurrent, a.RequestKey, a.InputHash, a.CompletedAt, a.Error,
            status = a.CompletedAt != null ? "ready" : a.Error != null || a.LLMStepStatus == StepProcessingStatus.PermanentError ? "failed" : "analyzing",
            definitions = JsonSerializer.Deserialize<JsonElement>(a.TargetsJson),
            modelReportedOutcomes = ModelOutcomes(a.LLMPrompts),
            targets = a.Targets.OrderBy(t => t.ExtractionType.Tag).Select(t => new {
                tag = t.ExtractionType.Tag, t.Outcome, t.Explanation,
                extractions = t.Extractions.Select(e => new {
                    id = e.ID, e.Title, e.Summary, e.SchemaVersion, e.ReviewState,
                    values = JsonSerializer.Deserialize<JsonElement>(e.ValuesJson),
                    evidence = e.Evidence.Select(v => new {
                        page = a.Pages.Single(p => p.ID == v.PageTextRevisionId).Number, v.PageTextRevisionId,
                        contentHash = a.Pages.Single(p => p.ID == v.PageTextRevisionId).ContentHash,
                        v.StartOffset, v.EndOffset, v.Quote, v.Role,
                        supportedFields = JsonSerializer.Deserialize<string[]>(v.SupportedFieldsJson)
                    })
                })
            })
        }) });
    }

    private sealed record ModelOutcome(string Tag, string Outcome, bool EvidenceValidated);
    private static ModelOutcome[] ModelOutcomes(IEnumerable<LLMPrompt> prompts)
    {
        var response = prompts.OrderByDescending(p => p.ID).FirstOrDefault()?.Response;
        if (string.IsNullOrWhiteSpace(response)) return [];
        try
        {
            using var parsed = JsonDocument.Parse(response);
            return parsed.RootElement.GetProperty("targets").EnumerateArray().Select(t => new ModelOutcome(
                t.GetProperty("tag").GetString() ?? "", t.GetProperty("outcome").GetString() ?? "", false)).ToArray();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { return []; }
    }

    private static async Task<IResult> Metrics(int id, ClaimsPrincipal user, AIESummitDb db, CancellationToken ct)
    {
        if (!await Owns(id, user, db, ct)) return Results.NotFound();
        var pageIds = db.Pages.Where(p => p.UploadId == id).Select(p => p.ID);
        var analysisIds = db.DocumentAnalyses.Where(a => a.Document.UploadId == id).Select(a => a.ID);
        var raw = await db.LLMPrompts.Where(p => p.DocumentType == nameof(UploadedFile) && p.DocumentID == id
            || p.DocumentType == nameof(OcrPage) && pageIds.Contains(p.DocumentID)
            || p.DocumentType == nameof(DocumentAnalysis) && analysisIds.Contains(p.DocumentID))
            .Select(p => new { id = p.ID, p.DocumentType, p.DocumentID, step = p.LLMStep.Tag,
                model = p.UsedModel == null ? null : p.UsedModel.Name,
                modelTag = p.UsedModel == null ? null : p.UsedModel.Tag,
                provider = p.UsedModel == null ? (LLMAPIProvider?)null : p.UsedModel.Provider,
                stepId = p.LLMStep_ID, p.SystemPrompt, p.ExpectedResponseJsonFormat, p.Prompt,
                p.Temperature, p.ReasoningEffort, p.InputTokens, p.OutputTokens, p.Tokens,
                p.AccumulatedTokens, p.ApiAttemps, p.ProcessingStatus, p.DateCreated, p.LastModified }).ToListAsync(ct);
        var prompts = raw.Select(p => new { p.id, p.DocumentType, p.DocumentID, p.step, p.stepId, p.model,
            p.modelTag, provider = p.provider?.ToString(), modelVersion = (string?)null,
            systemPromptHash = AnalysisFactory.Hash(p.SystemPrompt), inputPromptHash = AnalysisFactory.Hash(p.Prompt),
            outputSchemaHash = AnalysisFactory.Hash(p.ExpectedResponseJsonFormat),
            generationParameters = new { p.Temperature, p.ReasoningEffort },
            p.InputTokens, p.OutputTokens, p.Tokens, p.AccumulatedTokens, p.ApiAttemps, p.ProcessingStatus, p.DateCreated, p.LastModified }).ToArray();
        return Results.Ok(new { prompts, reportedInputTokens = prompts.Sum(p => p.InputTokens),
            reportedOutputTokens = prompts.Sum(p => p.OutputTokens), reportedTotalTokens = prompts.Sum(p => p.Tokens),
            accumulatedTokens = prompts.Sum(p => p.AccumulatedTokens),
            ocrTokens = (int?)null, inputOutputBreakdownAvailable = prompts.All(p => p.Tokens == 0 || p.InputTokens + p.OutputTokens > 0),
            note = "Provider-reported LLM counters; OCR tokens and immutable provider model revision are unavailable. Zero input/output counters may mean unreported; check inputOutputBreakdownAvailable. Cached responses/retries may differ from billed usage." });
    }
}
