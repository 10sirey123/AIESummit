using System.Text.Json;
using AIESummit.Agents;
using AIESummit.Domain;
using Blueshift.Domain.Entities;
using Blueshift.Domain.Seeding;

namespace AIESummit.Configuration;

/// <summary>Explicit configuration fields only; never exports credentials or processing records.</summary>
public static class WorkflowSeeds
{
    public static SeedManifest Create(bool includeBindings = false)
    {
        var records = new List<SeedRecord>
        {
            Model(SplitSamples.ModelTag, "gpt-4.1-mini", LLMAPIProvider.OpenAI),
            Model(PageSectionsStep.ModelTag, "gemini-3.8-flash", LLMAPIProvider.Gemini),
            Model("meta-muse-wonder", "muse-wonder-1.0", LLMAPIProvider.Meta),
            Api("geminiapi", "https://generativelanguage.googleapis.com/v1beta/models/"),
            Api("meta", "https://api.meta.ai/v1"),
            Step(SplitStep.Definition(), SplitSamples.ModelTag, includeBindings),
            Step(PageSectionsStep.Definition(), PageSectionsStep.ModelTag, includeBindings),
            Step(ContractExtractionStep.Definition(), SplitSamples.ModelTag, includeBindings)
        };
        records.AddRange(ExtractionCatalog.Defaults().Select(t => new SeedRecord {
            Entity = nameof(ExtractionType), Tag = t.Tag, Policy = SeedPolicy.Keep,
            Match = Fields(new { t.Tag }), Values = Fields(new { t.Name, t.BenchmarkCategory, t.Instructions,
                t.DefinitionVersion, t.ParametersJson, t.Enabled }) }));
        var manifest = new SeedManifest { Venture = "aiesummit", Set = "workflow", Records = records };
        manifest.Validate();
        return manifest;
    }

    private static SeedRecord Model(string tag, string name, LLMAPIProvider provider) => new()
    {
        Entity = nameof(LLMModel), Tag = tag, Policy = SeedPolicy.Keep,
        Match = Fields(new { Tag = tag }), Values = Fields(new { Name = name, Provider = provider })
    };

    private static SeedRecord Api(string identifier, string uri) => new()
    {
        Entity = nameof(APIInfo), Tag = identifier, Policy = SeedPolicy.Keep,
        Match = Fields(new { Identifier = identifier }),
        Values = Fields(new { BaseURI = uri, AuthorizationType = ApiAuthorizationType.ApiKey, Enabled = false })
    };

    private static SeedRecord Step(LLMStep step, string modelTag, bool includeBindings)
    {
        var values = Fields(new
        {
            step.Name, step.Description, step.DocumentType, step.DTOResponseType,
            step.Template, step.SystemRolePromptTemplate, step.Lambda, step.ValidationStatus,
            step.Temperature, step.ReasoningEffort, step.Multimodal, step.MultimodalLambda, step.Parser
        });
        // Omitted fields are preserved by update policy. New unbound steps remain disabled.
        if (includeBindings) values[nameof(LLMStep.Enabled)] = JsonSerializer.SerializeToElement(step.Enabled);
        return new SeedRecord
        {
            Entity = nameof(LLMStep), Tag = step.Tag, Match = Fields(new { step.Tag }), Values = values,
            References = includeBindings ? new() { [nameof(LLMStep.Model)] = new(nameof(LLMModel), modelTag) } : []
        };
    }

    private static Dictionary<string, JsonElement> Fields(object source)
        => JsonSerializer.SerializeToElement(source).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
}
