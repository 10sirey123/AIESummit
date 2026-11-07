using Blueshift.Domain.Abstract;
using Blueshift.Domain.Entities;
using Blueshift.Domain.Entities.AgentConfigs;
using AIESummit.Agents;
using AIESummit.Domain;
using Microsoft.EntityFrameworkCore;

namespace AIESummit.Api;

internal static class Bootstrap
{
    public static async Task InitializeAsync(AIESummitDb db, IConfiguration config)
    {
        await db.Database.EnsureCreatedAsync();
        var endpoint = config["AZURE_FOUNDRY_ENDPOINT"]?.TrimEnd('/') ?? throw new InvalidOperationException("Foundry endpoint is required.");
        var key = config["AZURE_FOUNDRY_KEY"] ?? throw new InvalidOperationException("Foundry key is required.");
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(825833001)");
        await AddCredential("mistral-ocr", config["OCR_ENDPOINT"] ?? endpoint + "/providers/mistral/azure/ocr",
            config["OCR_MODEL"] ?? "mistral-ocr-4-0");
        await AddCredential("openai", config["LLM_ENDPOINT"] ?? endpoint, null);
        var samples = new TaggedSampleCatalog(new SplitSamples().ProvideSamples());
        var issues = samples.ValidateIntegrity();
        if (issues.Count != 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, issues));
        var step = samples.GetAll(typeof(LLMStep)).Cast<LLMStep>().Single();
        var modelSample = samples.GetAll(typeof(LLMModel)).Cast<LLMModel>().Single(m => m.Tag == step.Model.Tag);
        var model = await db.LLMModels.SingleOrDefaultAsync(m => m.Tag == modelSample.Tag);
        if (model is null)
        {
            model = modelSample;
            model.Name = config["LLM_MODEL"] ?? model.Name;
            db.LLMModels.Add(model);
        }
        if (!await db.LLMSteps.AnyAsync(s => s.Tag == step.Tag))
        {
            step.Model = model;
            db.LLMSteps.Add(step);
        }
        await SeedAgent<OcrAgentConfig>("Mistral OCR", nameof(OcrAgent));
        await SeedAgent<LLMStepAgentConfig>("Plan document split", "LLMStepAgent");
        await SeedAgent<LLMPromptAgentConfig>("Run document split", "LlmPromptAgent");
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        async Task AddCredential(string identifier, string uri, string? deployment)
        {
            if (!await db.APIInfos.AnyAsync(a => a.Identifier == identifier))
                db.APIInfos.Add(new APIInfo { Identifier = identifier, BaseURI = uri,
                    AppKey = key, AppSecret = deployment, AuthorizationType = ApiAuthorizationType.ApiKey });
        }

        async Task SeedAgent<T>(string name, string className) where T : BaseAgentConfig, new()
        {
            if (!await db.Set<T>().AnyAsync())
                db.Add(new T { Name = name, AgentClassName = className, Enabled = true });
        }
    }
}
