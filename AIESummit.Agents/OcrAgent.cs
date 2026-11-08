using Blueshift.Agents;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Blueshift.Agents.Abstract;
using Blueshift.Agents.Infrastructure.Queue;
using Blueshift.Domain.Abstract;
using Blueshift.Domain.Entities;
using Blueshift.Domain.Infrastructure.APIBrowser;
using AIESummit.Domain;
using Microsoft.Extensions.Options;

namespace AIESummit.Agents;

public sealed class OcrAgent(ILogger logger, IBroadcaster broadcaster, IQueueManager queue,
    IServiceProvider services, IOptions<AgentQueueOptions> options)
    : BaseAPIAgent<UploadedFile, ProcessedStatus, OcrAgentConfig>(logger, broadcaster, queue, services, options.Value)
{
    static OcrAgent()
    {
        IsEnabled = IsAsync = true;
        FieldSelectorExpression = file => file.OcrStatus;
        WhereExpression = _ => true;
        OriginalStates = [ProcessedStatus.New];
        SuccessStates = [ProcessedStatus.Processed];
        PermanentErrorStates = [ProcessedStatus.PermanentError];
    }

    public override bool ExecuteAgent(ref UploadedFile obj, ref IBasePersistenceRepository<UploadedFile> repo,
        ref OcrAgentConfig config) => throw new NotSupportedException();

    public override async Task<(bool, UploadedFile, IBasePersistenceRepository<UploadedFile>)> ExecuteAgentAsync(
        UploadedFile file, IBasePersistenceRepository<UploadedFile> repo, OcrAgentConfig config)
    {
        try
        {
            var browser = GetApiBrowser<GeminiOcrBrowser>() ?? throw new InvalidOperationException("OCR APIInfo unavailable.");
            var pages = await browser.ReadAsync(file);
            if (pages.Length is < 1 or > GeminiOcrBrowser.MaxPages || !pages.Select(p => p.Index).SequenceEqual(Enumerable.Range(0, pages.Length)))
                throw new InvalidDataException("OCR page indexes are incomplete.");

            var pageRepo = repo.GetRepository<OcrPage>();
            var imageRepo = repo.GetRepository<PageImage>();
            var pageRows = new List<OcrPage>();
            var imageRows = new List<PageImage>();
            foreach (var source in pages)
            {
                var page = new OcrPage { UploadId = file.ID, Number = source.Index + 1, Markdown = source.Markdown };
                pageRows.Add(page);
                foreach (var image in source.Images ?? [])
                {
                    if (image.Data is not { } data) continue;
                    var comma = data.IndexOf(',');
                    if (comma < 0 || !data.StartsWith("data:image/", StringComparison.Ordinal) ||
                        !data.AsSpan(0, comma).EndsWith(";base64", StringComparison.Ordinal)) continue;
                    var mime = data[5..(comma - 7)];
                    if (mime is not ("image/png" or "image/jpeg" or "image/webp" or "image/gif")) continue;
                    var bytes = Convert.FromBase64String(data[(comma + 1)..]);
                    imageRows.Add(new PageImage
                    {
                        Page = page, Reference = image.Id,
                        Image = new Image { FileData = bytes, FileMimeType = mime, FileSize = bytes.Length }
                    });
                }
            }
            foreach (var page in pageRows) pageRepo.Add(page);
            foreach (var image in imageRows) imageRepo.Add(image);
            file.PageCount = pages.Length;
            file.OcrStatus = ProcessedStatus.Processed;
            file.LLMStepStatus = StepProcessingStatus.CheckForPrompts;
            Log($"OCR extracted {pages.Length} pages.");
            return (true, file, repo);
        }
        catch (OcrPageLimitException ex)
        {
            file.Error = ex.Message;
            file.OcrStatus = ProcessedStatus.PermanentError;
            Log($"OCR rejected: {ex.Message}", LogLineType.Error);
            return (false, file, repo);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException
            or InvalidOperationException or System.Text.Json.JsonException or FormatException)
        {
            file.Error = "OCR failed. See the agent logs.";
            file.OcrStatus = ProcessedStatus.PermanentError;
            var status = (ex as HttpRequestException)?.StatusCode;
            Log($"OCR failed ({ex.GetType().Name}, HTTP {status}).", LogLineType.Error);
            return (false, file, repo);
        }
    }
}

public sealed class OcrPageLimitException(string message) : Exception(message);

public sealed class GeminiOcrBrowser : BaseAPIBrowser
{
    public const int MaxPages = 100;
    private const int PagesPerChunk = 5;
    private const int MaxParallelChunks = 8;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public override string APIIdentifier => "gemini-ocr";

    private const string Instruction =
        "You are a document OCR engine. Transcribe the requested pages of the attached document into GitHub-flavored Markdown. " +
        "Return exactly one entry per requested page, in page order, where index is the one-based page number minus 1. " +
        "Preserve all text verbatim, including headings, numbering, lists, tables and signature blocks. Do not summarize, " +
        "translate, correct or omit text. Describe non-text images briefly as ![description](). Return an empty markdown string for a blank page.";

    private static readonly object PagesSchema = new
    {
        type = "object",
        properties = new
        {
            pages = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new { index = new { type = "integer" }, markdown = new { type = "string" } },
                    required = new[] { "index", "markdown" },
                    additionalProperties = false
                }
            }
        },
        required = new[] { "pages" },
        additionalProperties = false
    };

    private static readonly object CountSchema = new
    {
        type = "object",
        properties = new { pageCount = new { type = "integer" } },
        required = new[] { "pageCount" },
        additionalProperties = false
    };

    /// <summary>Counts pages first (rejecting oversize documents quickly), then transcribes fixed page chunks in parallel.</summary>
    public async Task<OcrPageResponse[]> ReadAsync(UploadedFile file)
    {
        var data = Convert.ToBase64String(file.Source);
        var pageCount = file.ContentType == "application/pdf"
            ? (await GenerateAsync<PageCountResponse>(file.ContentType, data, null,
                "How many pages does this document have? Count every page, including blank ones.", CountSchema, 256)).PageCount
            : 1;
        if (pageCount < 1) throw new InvalidDataException("Gemini reported no pages.");
        if (pageCount > MaxPages)
            throw new OcrPageLimitException($"This document has {pageCount} pages; the limit is {MaxPages}.");

        using var gate = new SemaphoreSlim(MaxParallelChunks);
        var chunks = Enumerable.Range(0, (pageCount + PagesPerChunk - 1) / PagesPerChunk).Select(async c =>
        {
            var first = c * PagesPerChunk;
            var last = Math.Min(first + PagesPerChunk, pageCount) - 1;
            await gate.WaitAsync();
            try { return await ReadChunkAsync(file.ContentType, data, first, last, pageCount); }
            finally { gate.Release(); }
        });
        return (await Task.WhenAll(chunks)).SelectMany(p => p).ToArray();
    }

    private async Task<OcrPageResponse[]> ReadChunkAsync(string mime, string data, int first, int last, int pageCount)
    {
        var expected = Enumerable.Range(first, last - first + 1).ToArray();
        var request = pageCount == 1
            ? "Transcribe this single-page document."
            : $"Transcribe ONLY pages {first + 1} to {last + 1} (one-based) of this {pageCount}-page document.";
        for (var attempt = 1; ; attempt++)
        {
            var pages = (await GenerateAsync<OcrResponse>(mime, data, Instruction, request, PagesSchema, 32768)).Pages ?? [];
            pages = pages.OrderBy(p => p.Index).ToArray();
            if (pages.Select(p => p.Index).SequenceEqual(expected)) return pages;
            if (attempt >= 2)
                throw new InvalidDataException($"Gemini returned the wrong pages for {first + 1}-{last + 1}.");
        }
    }

    private async Task<T> GenerateAsync<T>(string mime, string data, string? system, string prompt, object schema, int maxTokens)
    {
        var model = string.IsNullOrWhiteSpace(AppKeySecret) ? "gemini-3.8-flash" : AppKeySecret;
        var payload = new Dictionary<string, object>
        {
            ["contents"] = new[]
            {
                new
                {
                    role = "user",
                    parts = new object[] { new { inline_data = new { mime_type = mime, data } }, new { text = prompt } }
                }
            },
            ["generationConfig"] = new
            {
                responseMimeType = "application/json",
                responseJsonSchema = schema,
                thinkingConfig = new { thinkingLevel = "low" },
                maxOutputTokens = maxTokens
            }
        };
        if (system is not null) payload["systemInstruction"] = new { parts = new[] { new { text = system } } };
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{ApiBaseUri.ToString().TrimEnd('/')}/models/{Uri.EscapeDataString(model)}:generateContent");
        request.Headers.TryAddWithoutValidation("x-goog-api-key", AppKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<GeminiResponse>(Json);
        var candidate = result?.Candidates?.FirstOrDefault() ?? throw new InvalidDataException("Gemini returned no candidates.");
        if (candidate.FinishReason is not (null or "STOP"))
            throw new InvalidDataException($"Gemini OCR stopped early ({candidate.FinishReason}).");
        var text = string.Concat((candidate.Content?.Parts ?? []).Where(p => p.Thought != true).Select(p => p.Text));
        return JsonSerializer.Deserialize<T>(text, Json) ?? throw new InvalidDataException("Gemini returned empty JSON.");
    }

    protected override bool CheckIfAccessTokenExpired(string response) => false;
    protected override bool CheckIfAccessTokenInvalid(string response) => false;
    protected override bool CheckIfPermissionInvalid(string response) => false;
    protected override Task<bool> FixExpiredToken() => Task.FromResult(false);
    protected override Task<bool> FixInvalidToken() => Task.FromResult(false);
    protected override Task<bool> FixInsuficientPermission() => Task.FromResult(false);
}

public sealed record PageCountResponse(int PageCount);
public sealed record GeminiResponse(GeminiCandidate[]? Candidates);
public sealed record GeminiCandidate(GeminiContent? Content, string? FinishReason);
public sealed record GeminiContent(GeminiPart[]? Parts);
public sealed record GeminiPart(string? Text, bool? Thought);

public sealed record OcrResponse(OcrPageResponse[] Pages);
public sealed record OcrPageResponse(int Index, string Markdown, OcrImageResponse[]? Images);
public sealed record OcrImageResponse(string Id, [property: JsonPropertyName("image_base64")] string? Data);







