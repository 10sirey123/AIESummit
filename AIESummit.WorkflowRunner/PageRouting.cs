using System.Text.Json;
using AIESummit.Domain;

namespace AIESummit.WorkflowRunner;

public sealed record PageRoutingDecision(string TargetTag, int PageNumber, string Outcome, bool Succeeded, string NodeId,
    int? AnalysisId, int? PromptId);

/// <summary>Pure routing rules. Source quotes and detector output are never injected into extraction inputs.</summary>
public static class PageRouting
{
    public static bool ShouldExtract(PageRoutingDecision decision)
        => decision.Succeeded && decision.Outcome is "present" or "uncertain";

    public static bool CanAssertAbsent(IReadOnlyCollection<PageRoutingDecision> decisions)
        => decisions.Count > 0 && decisions.All(d => d.Succeeded && d.Outcome == "absent");

    public static string PageInput(IEnumerable<JsonElement> definitions, PageTextRevision page)
        => JsonSerializer.Serialize(new { targets = definitions,
            pages = new[] { new { page = page.Number, markdown = page.Markdown } },
            prerequisites = Array.Empty<object>() });

    public static string ReadOutcome(DocumentAnalysis analysis, string target, int pageNumber)
    {
        if (analysis.CompletedAt == null) throw new InvalidDataException("An incomplete detector cannot route pages.");
        using var stored = JsonDocument.Parse(analysis.LLMJsonResult);
        if (stored.RootElement.GetProperty("kind").GetString() != "page-target-presence-v1"
            || stored.RootElement.GetProperty("pageNumber").GetInt32() != pageNumber)
            throw new InvalidDataException("Retained detector scope no longer matches its page.");
        var decision = stored.RootElement.GetProperty("decisions").EnumerateArray()
            .Single(d => d.GetProperty("tag").GetString() == target);
        var outcome = decision.GetProperty("outcome").GetString();
        if (decision.GetProperty("tag").GetString() != target || outcome is not ("present" or "absent" or "uncertain"))
            throw new InvalidDataException("Retained detector target or outcome is invalid.");
        return outcome;
    }
}
