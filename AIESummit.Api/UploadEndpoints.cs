using System.Security.Claims;
using System.Security.Cryptography;
using Blueshift.Domain.Abstract;
using Blueshift.Domain.Entities;
using AIESummit.Domain;
using Microsoft.EntityFrameworkCore;

namespace AIESummit.Api;

internal static class UploadEndpoints
{
    public const int MaxBytes = 20 * 1024 * 1024;

    public static void MapUploadEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization();
        api.MapPost("/uploads", Upload).DisableAntiforgery();
        api.MapGet("/uploads", List);
        api.MapGet("/uploads/{id:int}", Get);
        api.MapGet("/uploads/{id:int}/images/{imageId:int}", GetImage);
        api.MapGet("/uploads/{id:int}/source", GetSource);
        api.MapPost("/uploads/{id:int}/analyze", AnalyzePages);
    }

    private static string Owner(ClaimsPrincipal user) => user.FindFirstValue("sub")!;

    private static async Task<IResult> Upload(IFormFile file, ClaimsPrincipal user, AIESummitDb db, CancellationToken token)
    {
        if (file.Length is <= 0 or > MaxBytes) return Results.Problem("Upload a file up to 20 MiB.", statusCode: 400);
        await using var source = file.OpenReadStream();
        using var buffer = new MemoryStream((int)file.Length);
        await source.CopyToAsync(buffer, token);
        var bytes = buffer.ToArray();
        var mime = DetectType(bytes);
        if (mime is null) return Results.Problem("Upload a PDF, PNG or JPEG.", statusCode: 415);
        var filename = Path.GetFileName(file.FileName.Replace('\\', '/'));
        var upload = new UploadedFile { OwnerId = Owner(user), FileName = filename[..Math.Min(filename.Length, 255)],
            ContentType = mime, Source = bytes, OcrCacheKey = Convert.ToHexString(SHA256.HashData(bytes)) };
        db.Uploads.Add(upload);
        await db.SaveChangesAsync(token);
        return Results.Accepted($"/api/v1/uploads/{upload.ID}", new { id = upload.ID });
    }

    private static string? DetectType(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith("%PDF-"u8)) return "application/pdf";
        if (data.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        if (data.StartsWith(new byte[] { 255, 216, 255 })) return "image/jpeg";
        return null;
    }

    private static IQueryable<UploadSummary> Summaries(IQueryable<UploadedFile> uploads)
        => uploads.Select(u => new UploadSummary(
            u.ID, u.FileName, u.DateCreated, u.PageCount,
            u.Error != null || u.LLMStepStatus == StepProcessingStatus.PermanentError ? "failed"
                : u.LLMStepStatus == StepProcessingStatus.Processed ? "ready"
                : u.OcrStatus == ProcessedStatus.Processed ? "splitting" : "queued",
            u.Error));

    private static async Task<IResult> List(ClaimsPrincipal user, AIESummitDb db, CancellationToken token, int before = int.MaxValue)
        => Results.Ok(await Summaries(db.Uploads.Where(u => u.OwnerId == Owner(user) && u.ID < before).OrderByDescending(u => u.ID).Take(50)).ToListAsync(token));

    private static async Task<IResult> Get(int id, ClaimsPrincipal user, AIESummitDb db, CancellationToken token)
    {
        var upload = await Summaries(db.Uploads.Where(u => u.OwnerId == Owner(user) && u.ID == id)).SingleOrDefaultAsync(token);
        if (upload is null) return Results.NotFound();
        var images = await db.PageImages.Where(i => i.Page.UploadId == id)
            .Select(i => new ImageReference(i.ImageId, i.Page.Number, i.Reference)).ToListAsync(token);
        var documents = await db.Documents.Where(d => d.UploadId == id).OrderBy(d => d.FirstPage)
            .Select(d => new { id = d.ID, d.Title, d.FirstPage, d.LastPage, d.Markdown }).ToListAsync(token);
        var pages = await db.Pages.Where(p => p.UploadId == id).OrderBy(p => p.Number)
            .Select(p => new { id = p.ID, p.Number, p.Markdown, p.SectionsJson, p.AnalysisError, p.LLMStepStatus }).ToListAsync(token);
        return Results.Ok(new { upload, pages = pages.Select(p => new {
            p.id, p.Number, p.Markdown, error = p.AnalysisError,
            analysisStatus = p.AnalysisError != null || p.LLMStepStatus == StepProcessingStatus.PermanentError ? "failed"
                : p.SectionsJson != "" ? "ready" : p.LLMStepStatus is StepProcessingStatus.New or StepProcessingStatus.Processed ? "not_started" : "analyzing",
            sections = p.SectionsJson == "" ? Array.Empty<System.Text.Json.JsonElement>()
                : System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement[]>(p.SectionsJson) ?? []
        }), documents = documents.Select(d => new {
            d.id, d.Title, d.FirstPage, d.LastPage, d.Markdown,
            images = images.Where(i => i.Page >= d.FirstPage && i.Page <= d.LastPage)
        }) });
    }

    private static async Task<IResult> GetSource(int id, ClaimsPrincipal user, AIESummitDb db, HttpContext context, CancellationToken token)
    {
        var file = await db.Uploads.Where(u => u.ID == id && u.OwnerId == Owner(user))
            .Select(u => new { u.Source, u.ContentType }).SingleOrDefaultAsync(token);
        if (file is null) return Results.NotFound();
        context.Response.Headers.CacheControl = "private, no-store";
        return Results.File(file.Source, file.ContentType, enableRangeProcessing: true);
    }

    private static async Task<IResult> AnalyzePages(int id, ClaimsPrincipal user, AIESummitDb db, CancellationToken token)
    {
        if (!await db.Uploads.AnyAsync(u => u.ID == id && u.OwnerId == Owner(user), token)) return Results.NotFound();
        var step = await db.LLMSteps.Include(s => s.Model)
            .SingleOrDefaultAsync(s => s.Tag == "classify-page-sections" && s.Enabled, token);
        var identifier = step?.Model?.Provider switch
        {
            LLMAPIProvider.Gemini => "geminiapi",
            LLMAPIProvider.Meta => "meta",
            LLMAPIProvider.OpenAI => "openai",
            _ => null
        };
        if (identifier is null || !await db.APIInfos.AnyAsync(a => a.Identifier == identifier && a.Enabled && a.AppKey != "", token))
            return Results.Problem("Configure and enable the page classification step, its model and the matching APIInfo in Admin first.", statusCode: 409);
        var pages = await db.Pages.Include(p => p.LLMPrompts).Where(p => p.UploadId == id).ToListAsync(token);
        foreach (var page in pages.Where(p => p.SectionsJson == "" && p.LLMPrompts.Count == 0))
            page.LLMStepStatus = StepProcessingStatus.CheckForPrompts;
        await db.SaveChangesAsync(token);
        return Results.Accepted($"/api/v1/uploads/{id}", new { id });
    }

    private static async Task<IResult> GetImage(int id, int imageId, ClaimsPrincipal user, AIESummitDb db, CancellationToken token)
    {
        var owner = Owner(user);
        var image = await db.PageImages.Where(i => i.ImageId == imageId && i.Page.UploadId == id && i.Page.Upload.OwnerId == owner)
            .Select(i => new { i.Image.FileData, i.Image.FileMimeType }).FirstOrDefaultAsync(token);
        return image?.FileData is null ? Results.NotFound() : Results.File(image.FileData, image.FileMimeType);
    }

    private sealed record UploadSummary(int Id, string FileName, DateTime Created, int PageCount, string Status, string? Error);
    private sealed record ImageReference(int Id, int Page, string Reference);
}

