using System.Text.Json;
using System.Text.Json.Nodes;
using AIESummit.Domain;
using Blueshift.Domain.Abstract;
using Blueshift.Domain.Entities;

namespace AIESummit.Api;

public sealed record ExtractionParameter(string Name, string Type, string Description, string? Format = null,
    string[]? AllowedValues = null, decimal? Minimum = null, bool ExclusiveMinimum = false, int? MaxLength = null);

public sealed record ExtractionTypeContract(string Tag, string Name, string BenchmarkCategory, string Instructions,
    string ParametersJson, int DefinitionVersion, bool Enabled, ExtractionParameter[] Parameters,
    bool SchemaSupported, string ValueSchemaDialect, JsonObject? ValueSchema);

public sealed record AnalysisAttemptState(int Id, int DocumentId, DateTime? CompletedAt, string? Error,
    StepProcessingStatus NativeStatus, bool HasPrompts);

public sealed record DocumentAnalysisSelection(int DocumentId, int? LatestAnalysisId, int? LastSuccessfulAnalysisId,
    string Status, string? Reason, bool CanPoll);

/// <summary>Public read-contract semantics; independent from native worker state transitions.</summary>
public static class ExtractionReadModel
{
    public static readonly IReadOnlyList<ExtractionParameter> ParameterDefinitions = new ExtractionParameter[]
    {
        new("date", "string", "Explicit calendar date. Do not convert to a timezone-dependent timestamp.", "date", MaxLength: 10),
        new("dateExpression", "string", "Original date, event or unresolved relative-date expression.", MaxLength: 4000),
        new("duration", "number", "Positive interval, paired with unit.", Minimum: 0, ExclusiveMinimum: true),
        new("unit", "string", "Unit of duration; business and calendar days are distinct.",
            AllowedValues: ["calendar_days", "business_days", "days", "months", "years"], MaxLength: 4000),
        new("amount", "number", "Nonnegative stated monetary amount; no calculation is implied.", Minimum: 0),
        new("currency", "string", "Explicit three-letter uppercase currency code.", MaxLength: 3),
        new("formula", "string", "Stated cap formula or claim limitation expression.", MaxLength: 4000),
        new("party", "string", "Party or role affected by this finding.", MaxLength: 4000),
        new("conditions", "string", "Material qualifications, triggers, conditions or exceptions.", MaxLength: 4000),
        new("mechanism", "string", "Contractual renewal, termination or audit mechanism.", MaxLength: 4000),
        new("activity", "string", "Restricted activity, audit scope, uncapped category or insurance coverage.", MaxLength: 4000),
        new("geography", "string", "Territory of a non-compete restriction.", MaxLength: 4000),
        new("exclusions", "string", "Express uncapped categories or cap carveouts.", MaxLength: 4000)
    };

    public static ExtractionTypeContract Describe(ExtractionType type)
    {
        string[] names;
        try { names = JsonSerializer.Deserialize<string[]>(type.ParametersJson) ?? []; }
        catch (JsonException) { names = []; }
        var known = ParameterDefinitions.ToDictionary(p => p.Name, StringComparer.Ordinal);
        var supported = type.DefinitionVersion == 1 && names.Length > 0
            && names.Distinct(StringComparer.Ordinal).Count() == names.Length
            && names.All(n => n is not null && known.ContainsKey(n));
        var parameters = names.Where(n => n is not null).Select(n => known.GetValueOrDefault(n)
            ?? new ExtractionParameter(n, "unknown", "Not supported by the current value-schema contract.")).ToArray();
        return new(type.Tag, type.Name, type.BenchmarkCategory, type.Instructions, type.ParametersJson,
            type.DefinitionVersion, type.Enabled, parameters, supported, "openapi-3.0", supported ? ValueSchema(parameters) : null);
    }

    private static JsonObject ValueSchema(IEnumerable<ExtractionParameter> parameters)
    {
        var properties = new JsonObject();
        foreach (var p in parameters)
        {
            var schema = new JsonObject { ["type"] = p.Type, ["description"] = p.Description };
            if (p.Format != null) schema["format"] = p.Format;
            if (p.AllowedValues != null) schema["enum"] = new JsonArray(p.AllowedValues.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());
            if (p.Minimum != null) schema["minimum"] = p.Minimum.Value;
            if (p.ExclusiveMinimum) schema["exclusiveMinimum"] = true;
            if (p.MaxLength != null) schema["maxLength"] = p.MaxLength.Value;
            if (p.Type == "string") schema["minLength"] = 1;
            if (p.Name == "currency") schema["pattern"] = "^[A-Z]{3}$";
            properties[p.Name] = schema;
        }
        var result = new JsonObject { ["type"] = "object", ["properties"] = properties, ["additionalProperties"] = false };
        if (properties.ContainsKey("duration"))
            result["oneOf"] = JsonNode.Parse("""
                [{"required":["duration","unit"]},
                 {"not":{"anyOf":[{"required":["duration"]},{"required":["unit"]}]}}]
                """);
        return result;
    }

    public static DocumentAnalysisSelection Select(int documentId, IEnumerable<AnalysisAttemptState> attempts, bool available)
    {
        var matching = attempts.Where(a => a.DocumentId == documentId).OrderByDescending(a => a.Id).ToArray();
        var latest = matching.FirstOrDefault();
        var successful = matching.FirstOrDefault(a => a.CompletedAt != null);
        if (latest is null) return new(documentId, null, null, available ? "not_started" : "blocked",
            available ? "analysis_not_requested" : "extraction_unavailable", false);
        if (latest.CompletedAt != null) return new(documentId, latest.Id, successful?.Id, "ready", null, false);
        if (latest.Error != null || latest.NativeStatus == StepProcessingStatus.PermanentError)
            return new(documentId, latest.Id, successful?.Id, "failed", "analysis_failed", false);
        if (latest.NativeStatus == StepProcessingStatus.Processed)
            return new(documentId, latest.Id, successful?.Id, "failed", "completed_without_result", false);
        if (!available && !latest.HasPrompts)
            return new(documentId, latest.Id, successful?.Id, "blocked", "extraction_unavailable", false);
        return new(documentId, latest.Id, successful?.Id, "analyzing", null, true);
    }

    public static string Aggregate(IReadOnlyList<DocumentAnalysisSelection> documents, bool intakeFailed, bool intakeComplete)
    {
        if (documents.Count == 0) return intakeFailed || intakeComplete ? "failed" : "waiting_for_documents";
        if (documents.Any(d => d.CanPoll)) return "analyzing";
        if (documents.All(d => d.Status == "ready")) return "ready";
        if (documents.Any(d => d.Status == "ready"))
            return documents.Any(d => d.Status == "failed") ? "partial_failure" : "incomplete";
        if (documents.Any(d => d.Status == "failed")) return "failed";
        if (documents.Any(d => d.Status == "blocked")) return "blocked";
        return "not_started";
    }
}
