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
        await SeedSchema.ApplyInTransactionAsync(db);
        await using (var migration = typeof(Bootstrap).Assembly.GetManifestResourceStream("AIESummit.Api.Migrations.001-page-sections.sql")
            ?? throw new InvalidOperationException("Page sections upgrade is missing."))
        using (var reader = new StreamReader(migration))
            await db.Database.ExecuteSqlRawAsync(await reader.ReadToEndAsync());
        await using (var migration = typeof(Bootstrap).Assembly.GetManifestResourceStream("AIESummit.Api.Migrations.003-contract-extractions.sql")
            ?? throw new InvalidOperationException("Extraction upgrade is missing."))
        using (var reader = new StreamReader(migration))
            await db.Database.ExecuteSqlRawAsync(await reader.ReadToEndAsync());
        await using (var migration = typeof(Bootstrap).Assembly.GetManifestResourceStream("AIESummit.Api.Migrations.002-ocr-cache.sql")
            ?? throw new InvalidOperationException("OCR cache upgrade is missing."))
        using (var reader = new StreamReader(migration))
            await db.Database.ExecuteSqlRawAsync(await reader.ReadToEndAsync());
        await using (var migration = typeof(Bootstrap).Assembly.GetManifestResourceStream("AIESummit.Api.Migrations.004-evidence-word-overlap.sql")
            ?? throw new InvalidOperationException("Evidence matching upgrade is missing."))
        using (var reader = new StreamReader(migration))
            await db.Database.ExecuteSqlRawAsync(await reader.ReadToEndAsync());
        await AddCredential("mistral-ocr", config["OCR_ENDPOINT"] ?? endpoint + "/providers/mistral/azure/ocr",
            config["OCR_MODEL"] ?? "mistral-ocr-4-0");
        await AddCredential("openai", config["LLM_ENDPOINT"] ?? endpoint, null);
        // Provision provider identity only; keys are deliberately imported through Admin.
        if (!await db.APIInfos.AnyAsync(a => a.Identifier == "subq"))
            db.APIInfos.Add(new APIInfo { Identifier = "subq", BaseURI = "https://api.subq.ai/v1",
                AppKey = "", AppSecret = "", Enabled = false, AuthorizationType = ApiAuthorizationType.ApiKey });
        if (!await db.LLMModels.AnyAsync(m => m.Tag == "subq-preview"))
            db.LLMModels.Add(new LLMModel { Tag = "subq-preview", Name = "subq-preview", Provider = LLMAPIProvider.SubQ });
        if (!await db.APIInfos.AnyAsync(a => a.Identifier == "meta"))
            db.APIInfos.Add(new APIInfo { Identifier = "meta", BaseURI = "https://api.meta.ai/v1",
                AppKey = "", AppSecret = "", Enabled = false, AuthorizationType = ApiAuthorizationType.ApiKey });
        if (!await db.LLMModels.AnyAsync(m => m.Tag == "meta-muse-wonder"))
            db.LLMModels.Add(new LLMModel { Tag = "meta-muse-wonder", Name = "muse-wonder-1.0", Provider = LLMAPIProvider.Meta });
        if (!await db.APIInfos.AnyAsync(a => a.Identifier == "geminiapi"))
            db.APIInfos.Add(new APIInfo { Identifier = "geminiapi", BaseURI = "https://generativelanguage.googleapis.com/v1beta/models/",
                AppKey = "", AppSecret = "", Enabled = false, AuthorizationType = ApiAuthorizationType.ApiKey });
        var geminiModel = await db.LLMModels.SingleOrDefaultAsync(m => m.Tag == PageSectionsStep.ModelTag);
        if (geminiModel is null)
        {
            geminiModel = new LLMModel { Tag = PageSectionsStep.ModelTag, Name = "gemini-3.8-flash", Provider = LLMAPIProvider.Gemini };
            db.LLMModels.Add(geminiModel);
        }
        if (!await db.LLMSteps.AnyAsync(s => s.Tag == PageSectionsStep.Tag))
        {
            var pageStep = PageSectionsStep.Definition();
            pageStep.Model = geminiModel;
            pageStep.Enabled = await db.APIInfos.AnyAsync(a => a.Identifier == "geminiapi" && a.Enabled && a.AppKey != "");
            db.LLMSteps.Add(pageStep);
        }
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
        await ExtractionSeeds.EnsureAsync(db, model);
        await SeedAgent<LLMStepAgentConfig>("Plan document split", "LLMStepAgent");
        await SeedPromptAgent();
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

        async Task SeedPromptAgent()
        {
            var agent = await db.LLMPromptAgentConfigs.OrderBy(a => a.ID).FirstOrDefaultAsync();
            if (agent is null)
                db.LLMPromptAgentConfigs.Add(new LLMPromptAgentConfig { Name = "Run document split", AgentClassName = "LlmPromptAgent", Enabled = true, MD5HashCheck = true });
            else if (!agent.MD5HashCheck)
                agent.MD5HashCheck = true;
        }
    }
}
