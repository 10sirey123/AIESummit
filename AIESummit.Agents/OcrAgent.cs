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
            var browser = GetApiBrowser<MistralOcrBrowser>() ?? throw new InvalidOperationException("OCR APIInfo unavailable.");
            var pages = await browser.ReadAsync(file);
            if (pages.Length is < 1 or > 30 || !pages.Select(p => p.Index).SequenceEqual(Enumerable.Range(0, pages.Length)))
                throw new InvalidDataException("OCR page indexes are incomplete.");

            var pageRepo = repo.GetRepository<OcrPage>();
            var imageRepo = repo.GetRepository<PageImage>();
            var pageRows = new List<OcrPage>();
            var imageRows = new List<PageImage>();
            foreach (var source in pages)
            {
                var page = new OcrPage { UploadId = file.ID, Number = source.Index + 1, Markdown = source.Markdown,
                    LLMStepStatus = StepProcessingStatus.CheckForPrompts };
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

public sealed class MistralOcrBrowser : BaseAPIBrowser
{
    private static readonly HttpClient Http = new();
    public override string APIIdentifier => "mistral-ocr";

    public async Task<OcrPageResponse[]> ReadAsync(UploadedFile file)
    {
        var value = $"data:{file.ContentType};base64,{Convert.ToBase64String(file.Source)}";
        var document = file.ContentType == "application/pdf"
            ? (object)new { type = "document_url", document_url = value }
            : new { type = "image_url", image_url = value };
        using var request = new HttpRequestMessage(HttpMethod.Post, ApiBaseUri);
        request.Headers.TryAddWithoutValidation("api-key", AppKey);
        var payload = JsonSerializer.Serialize(new { model = AppKeySecret, document, include_image_base64 = true });
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OcrResponse>())?.Pages ?? [];
    }

    protected override bool CheckIfAccessTokenExpired(string response) => false;
    protected override bool CheckIfAccessTokenInvalid(string response) => false;
    protected override bool CheckIfPermissionInvalid(string response) => false;
    protected override Task<bool> FixExpiredToken() => Task.FromResult(false);
    protected override Task<bool> FixInvalidToken() => Task.FromResult(false);
    protected override Task<bool> FixInsuficientPermission() => Task.FromResult(false);
}

public sealed record OcrResponse(OcrPageResponse[] Pages);
public sealed record OcrPageResponse(int Index, string Markdown, OcrImageResponse[]? Images);
public sealed record OcrImageResponse(string Id, [property: JsonPropertyName("image_base64")] string? Data);







