using System.Net;
using System.Text;
using System.Text.Json;
using Blueshift.Agents.LLM.Meta;
using Blueshift.Domain.Entities;
using Blueshift.Domain.Infrastructure.APIBrowser;
using Xunit;

namespace AIESummit.Tests;

public sealed class MetaProviderTests
{
    [Fact]
    public async Task ExplicitReasoningEffortIsForwardedToMuse()
    {
        using var handler = new ResponseHandler("completed");
        using var client = new HttpClient(handler);
        var api = new MetaAPI(client) { Model = "muse-wonder-1.0" };
        api.SetContext(new ApiBrowserContext { BaseUri = "https://api.meta.ai/v1", AppKey = "synthetic-meta-key" });
        await api.GetResponse(new LLMPrompt {
            Prompt = "Inspect the synthetic page.", SystemPrompt = "Return source facts.",
            ReasoningEffort = "low"
        });
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("low", body.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());
    }

    [Theory]
    [InlineData("completed", true)]
    [InlineData("incomplete", false)]
    public async Task MuseWonderUsesMetaBearerAndStrictSchemaAndRejectsPartialOutput(string status, bool accepted)
    {
        using var handler = new ResponseHandler(status);
        using var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Add("api-key", "stale-azure-key");
        var api = new MetaAPI(client) { Model = "muse-wonder-1.0" };
        api.SetContext(new ApiBrowserContext { BaseUri = "https://api.meta.ai/v1", AppKey = "synthetic-meta-key" });
        var result = await api.GetResponse(new LLMPrompt {
            Prompt = "Synthetic clause: terminate on 30 days notice.",
            SystemPrompt = "Extract only source evidence.",
            ExpectedResponseJsonFormat = """{"type":"object","properties":{"section":{"type":"string"}}}"""
        });
        Assert.Equal("https://api.meta.ai/v1/responses", handler.Url);
        Assert.Equal("Bearer synthetic-meta-key", handler.Authorization);
        Assert.False(handler.AzureHeader);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("muse-wonder-1.0", body.RootElement.GetProperty("model").GetString());
        Assert.True(body.RootElement.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
        Assert.Equal(accepted, result is not null);
        if (accepted) {
            Assert.Equal(LLMAPIProvider.Meta, result!.ModelUsed.Provider);
            Assert.Equal("muse-wonder-1.0", result.ModelUsed.Name);
            Assert.Equal(12, result.InputTokens);
            Assert.Equal(8, result.OutputTokens);
        }
    }

    private sealed class ResponseHandler(string status) : HttpMessageHandler
    {
        public string? Url, Authorization, Body;
        public bool AzureHeader;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Url = request.RequestUri!.ToString(); Authorization = request.Headers.Authorization?.ToString();
            AzureHeader = request.Headers.Contains("api-key"); Body = await request.Content!.ReadAsStringAsync(token);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new {
                status, output = new[] { new { type = "message", content = new[] { new { type = "output_text", text = "{\"section\":\"termination\"}" } } } },
                usage = new { input_tokens = 12, output_tokens = 8, total_tokens = 20 }
            }), Encoding.UTF8, "application/json") };
        }
    }
}
