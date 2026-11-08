using System.Net;
using System.Text;
using System.Text.Json;
using Blueshift.Agents.LLM.SubQ;
using Blueshift.Domain.Entities;
using Blueshift.Domain.Infrastructure.APIBrowser;
using Xunit;

namespace AIESummit.Tests;

public sealed class SubQProviderTests
{
    [Theory]
    [InlineData("http://api.subq.ai/v1")]
    [InlineData("https://api.subq.ai.attacker.test/v1")]
    [InlineData("https://api.subq.ai:8443/v1")]
    [InlineData("https://api.subq.ai/v1?redirect=elsewhere")]
    [InlineData("https://api.subq.ai/other")]
    public async Task RejectsUnexpectedEndpointBeforeSendingCredentials(string endpoint)
    {
        using var handler = new ResponseHandler("completed");
        using var client = new HttpClient(handler);
        var api = new SubQAPI(client) { Model = "subq-preview" };
        api.SetContext(new ApiBrowserContext { BaseUri = endpoint, AppKey = "synthetic-subq-key" });
        Assert.Null(await api.GetResponse(new LLMPrompt { Prompt = "test" }));
        Assert.Null(handler.Url);
    }

    [Theory]
    [InlineData("completed", true)]
    [InlineData("incomplete", false)]
    public async Task PreviewUsesSubQBearerAndStrictSchemaAndRejectsPartialOutput(string status, bool accepted)
    {
        using var handler = new ResponseHandler(status);
        using var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Add("api-key", "stale-azure-key");
        var api = new SubQAPI(client) { Model = "subq-preview" };
        api.SetContext(new ApiBrowserContext { BaseUri = "https://api.subq.ai/v1", AppKey = "synthetic-subq-key" });
        var result = await api.GetResponse(new LLMPrompt {
            Prompt = "Synthetic clause: terminate on 30 days notice.",
            SystemPrompt = "Extract only source evidence.",
            ExpectedResponseJsonFormat = """{"type":"object","properties":{"section":{"type":"string"}}}"""
        });
        Assert.Equal("https://api.subq.ai/v1/responses", handler.Url);
        Assert.Equal("Bearer synthetic-subq-key", handler.Authorization);
        Assert.False(handler.AzureHeader);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("subq-preview", body.RootElement.GetProperty("model").GetString());
        Assert.True(body.RootElement.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
        Assert.Equal(accepted, result is not null);
        if (accepted) {
            Assert.Equal(LLMAPIProvider.SubQ, result!.ModelUsed.Provider);
            Assert.Equal("subq-preview", result.ModelUsed.Name);
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
