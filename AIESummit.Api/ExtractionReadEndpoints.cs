using System.Security.Claims;
using AIESummit.Domain;
using Blueshift.Domain.Abstract;
using Blueshift.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIESummit.Api;

public static class ExtractionReadEndpoints
{
    public static async Task<IResult> Catalog(AIESummitDb db, CancellationToken ct)
    {
        var types = await db.ExtractionTypes.AsNoTracking().OrderBy(t => t.Tag).ToListAsync(ct);
        return Results.Ok(types.Select(ExtractionReadModel.Describe));
    }

    public static async Task<IResult> Summary(int id, ClaimsPrincipal user, AIESummitDb db, CancellationToken ct)
    {
        var owner = user.FindFirstValue("sub");
        var upload = await db.Uploads.AsNoTracking().Where(u => u.ID == id && u.OwnerId == owner)
            .Select(u => new { u.OcrStatus, u.LLMStepStatus, u.Error, u.AnalyzePageSections }).SingleOrDefaultAsync(ct);
        if (upload is null) return Results.NotFound();
        var stepEnabled = await db.LLMSteps.AnyAsync(s => s.Tag == "extract-contract-targets" && s.Enabled, ct);
        var enabledTargetCount = await db.ExtractionTypes.CountAsync(t => t.Enabled, ct);
        var available = stepEnabled && enabledTargetCount > 0;
        var documents = await db.Documents.AsNoTracking().Where(d => d.UploadId == id && d.IsCurrent)
            .OrderBy(d => d.FirstPage).Select(d => new { id = d.ID, d.Title, d.FirstPage, d.LastPage }).ToListAsync(ct);
        var attempts = await db.DocumentAnalyses.AsNoTracking().Where(a => a.Document.UploadId == id && a.Document.IsCurrent
                && !a.RequestKey.StartsWith(AnalysisFactory.ImprovementRequestPrefix))
            .Select(a => new AnalysisAttemptState(a.ID, a.DocumentId, a.CompletedAt, a.Error, a.LLMStepStatus, a.LLMPrompts.Any())).ToListAsync(ct);
        var selections = documents.Select(d => ExtractionReadModel.Select(d.id, attempts, available)).ToArray();
        var pages = await db.Pages.AsNoTracking().Where(p => p.UploadId == id)
            .Select(p => new { p.LLMStepStatus, p.AnalysisError, HasSections = p.SectionsJson != "" }).ToListAsync(ct);
        var pageReady = pages.Count(p => p.HasSections && p.AnalysisError == null && p.LLMStepStatus != StepProcessingStatus.PermanentError);
        var pageFailed = pages.Count(p => p.AnalysisError != null || p.LLMStepStatus == StepProcessingStatus.PermanentError);
        var pageNotStarted = pages.Count(p => !p.HasSections && p.AnalysisError == null
            && p.LLMStepStatus is StepProcessingStatus.New or StepProcessingStatus.Processed);
        var pagePending = pages.Count - pageReady - pageFailed - pageNotStarted;
        var intakeFailed = upload.Error != null || upload.OcrStatus == ProcessedStatus.PermanentError || upload.LLMStepStatus == StepProcessingStatus.PermanentError;
        var intakeComplete = upload.LLMStepStatus == StepProcessingStatus.Processed;
        var extractionStatus = ExtractionReadModel.Aggregate(selections, intakeFailed, intakeComplete);
        return Results.Ok(new {
            uploadId = id,
            availability = new { stepEnabled, enabledTargetCount, available,
                note = "Configuration availability only; does not establish provider credentials or worker health." },
            ocrStatus = upload.OcrStatus == ProcessedStatus.Processed ? "ready" : upload.OcrStatus == ProcessedStatus.PermanentError ? "failed"
                : upload.OcrStatus == ProcessedStatus.New ? "queued" : "processing",
            splitStatus = upload.Error != null || upload.LLMStepStatus == StepProcessingStatus.PermanentError ? "failed"
                : intakeComplete ? "ready" : upload.OcrStatus == ProcessedStatus.Processed ? "analyzing" : "waiting_for_ocr",
            pageSections = new { total = pages.Count, ready = pageReady, failed = pageFailed, pending = pagePending, notStarted = pageNotStarted,
                status = pagePending > 0 ? "analyzing" : pageFailed > 0 ? pageReady > 0 ? "partial_failure" : "failed"
                    : pages.Count > 0 && pageReady == pages.Count ? "ready" : !upload.AnalyzePageSections ? "not_requested" : "not_started" },
            extraction = new { status = extractionStatus, canPoll = extractionStatus == "waiting_for_documents" || selections.Any(s => s.CanPoll),
                totalDocuments = documents.Count, readyDocuments = selections.Count(s => s.Status == "ready"),
                failedDocuments = selections.Count(s => s.Status == "failed"), pendingDocuments = selections.Count(s => s.CanPoll),
                reason = documents.Count == 0 && intakeFailed ? "intake_failed" : documents.Count == 0 && intakeComplete ? "no_documents" : (string?)null },
            documents = documents.Select(d => new { d.id, d.Title, d.FirstPage, d.LastPage,
                analysis = selections.Single(s => s.DocumentId == d.id) })
        });
    }
}
