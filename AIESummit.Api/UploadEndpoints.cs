using System.Security.Claims;
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
            ContentType = mime, Source = bytes };
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
        return Results.Ok(new { upload, documents = documents.Select(d => new {
            d.id, d.Title, d.FirstPage, d.LastPage, d.Markdown,
            images = images.Where(i => i.Page >= d.FirstPage && i.Page <= d.LastPage)
        }) });
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

