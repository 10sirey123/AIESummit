using System.Text.Json;

namespace AIESummit.Domain;

/// <summary>Versioned, finite vocabulary. Database seeds are inserted only when missing.</summary>
public static class ExtractionCatalog
{
    public const string Version = "cuad-ten-v1";
    public static IReadOnlyList<ExtractionType> Defaults() =>
    [
        Type("agreement-effective-date", "Effective date", "Effective Date", "Extract the operative effective date or event, never merely a signing date.", "date", "dateExpression"),
        Type("agreement-expiration", "Initial expiration", "Expiration Date", "Extract initial term expiration or its formula. Do not confuse a renewal or notice date with expiration.", "date", "dateExpression", "duration", "unit"),
        Type("renewal-term", "Renewal mechanism", "Renewal Term", "Extract automatic or optional extensions, renewal duration, exercising party and conditions.", "mechanism", "duration", "unit"),
        Type("non-renewal-notice", "Non-renewal notice", "Notice Period To Terminate Renewal", "Extract the notice window for stopping renewal, not general breach termination notice.", "duration", "unit"),
        Type("non-compete", "Non-compete", "Non-Compete", "Extract actual restrictions on competing activities/geographies; an explicit statement that no restriction exists is not a positive.", "activity", "geography", "duration", "unit"),
        Type("termination-convenience", "Termination for convenience", "Termination For Convenience", "Extract termination without cause. Breach-only termination is not convenience.", "duration", "unit", "mechanism"),
        Type("liability-cap", "Liability cap", "Cap On Liability", "Extract a maximum liability amount/formula or contractual claim time bar; preserve exclusions.", "amount", "currency", "formula", "duration", "unit"),
        Type("uncapped-liability", "Uncapped liability", "Uncapped Liability", "Require express uncapped liability or carveouts from a cap. An absent cap is not evidence of unlimited liability.", "activity", "exclusions"),
        Type("audit-rights", "Audit rights", "Audit Rights", "Extract contractual rights to inspect books, records or premises and their notice/frequency/cost conditions.", "activity", "duration", "unit", "mechanism"),
        Type("insurance-obligations", "Insurance obligations", "Insurance", "Extract requirements to maintain insurance for the counterparty, including coverage, limits and duration.", "activity", "amount", "currency", "duration", "unit")
    ];

    private static ExtractionType Type(string tag, string name, string category, string instructions, params string[] fields)
        => new() { Tag = tag, Name = name, BenchmarkCategory = category, Instructions = instructions,
            ParametersJson = JsonSerializer.Serialize(fields.Concat(["party", "conditions"]).Distinct().ToArray()) };
}
