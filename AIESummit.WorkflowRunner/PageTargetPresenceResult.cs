using System.Text.Json;
using AIESummit.Agents;
using AIESummit.Domain;
using Blueshift.Domain.LLM;
using Newtonsoft.Json;

namespace AIESummit.WorkflowRunner;

/// <summary>A source-anchored routing decision, deliberately not an Extraction or scored finding.</summary>
public sealed class PageTargetPresenceResult(IMetadataExecutionContext context) : BaseMetadata(context)
{
    [JsonProperty("decisions", Required = Required.Always)] public List<PageTargetDecision> Decisions { get; set; } = [];

    public static ValidatedPageDecision[] Validate(DocumentAnalysis analysis, IReadOnlyList<PageTargetDecision> decisions)
    {
        if (analysis.CompletedAt != null || analysis.Pages.Count != 1)
            throw new InvalidDataException("Page detection requires one immutable source page and an unfinished subject.");
        using var definitions = JsonDocument.Parse(analysis.TargetsJson);
        var tags = definitions.RootElement.EnumerateArray().Select(d => d.GetProperty("tag").GetString()!).ToArray();
        if (tags.Length == 0 || decisions.Count != tags.Length
            || !decisions.Select(d => d.Tag).Order(StringComparer.Ordinal).SequenceEqual(tags.Order(StringComparer.Ordinal)))
            throw new InvalidDataException("Page detection must return every requested target exactly once.");
        var page = analysis.Pages.Single();
        return decisions.Select(d => {
            if (d.Outcome is not ("present" or "absent" or "uncertain") || d.Occurrence < -1)
                throw new InvalidDataException("Unknown page detection outcome or occurrence.");
            if (d.Outcome == "present" && string.IsNullOrWhiteSpace(d.Quote))
                throw new InvalidDataException("Present page decisions require source evidence.");
            if (d.Outcome == "absent" && !string.IsNullOrWhiteSpace(d.Quote))
                throw new InvalidDataException("An absent page decision must not claim a positive quote.");
            var match = string.IsNullOrWhiteSpace(d.Quote) ? null : EvidenceMatcher.Match(page.Markdown, d.Quote, d.Occurrence);
            return new ValidatedPageDecision(d.Tag, d.Outcome, match?.SourceQuote, match?.Start, match?.End, match?.Method, match?.Score);
        }).ToArray();
    }

    public override bool ConnectTo<T>(T target, IMetadataExecutionContext context)
    {
        if (target is not DocumentAnalysis analysis) return false;
        try
        {
            var decisions = Validate(analysis, Decisions);
            analysis.LLMJsonResult = System.Text.Json.JsonSerializer.Serialize(new {
                kind = "page-target-presence-v1", pageNumber = analysis.Pages.Single().Number, decisions
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            analysis.CompletedAt = DateTime.UtcNow; analysis.Error = null;
            // Targets remain not_analyzed: a detector never publishes a contractual finding.
            return true;
        }
        catch (InvalidDataException ex) { analysis.Error = ex.Message; return false; }
    }
}

public sealed class PageTargetDecision
{
    [JsonProperty("tag", Required = Required.Always)] public string Tag { get; set; } = "";
    [JsonProperty("outcome", Required = Required.Always)] public string Outcome { get; set; } = "uncertain";
    [JsonProperty("quote")] public string? Quote { get; set; }
    [JsonProperty("occurrence")] public int Occurrence { get; set; } = -1;
}

public sealed record ValidatedPageDecision(string Tag, string Outcome, string? Quote, int? StartOffset,
    int? EndOffset, string? MatchMethod, double? MatchScore);
