using System.Text.Json.Nodes;

namespace AIESummit.Improvement;

public static class Comparison
{
    public static JsonNode Compare(JsonNode? baseline, JsonNode? candidate)
    {
        static long? N(JsonNode? node) => node != null && long.TryParse(node.ToJsonString(), out var value) ? value : null;
        var b = baseline?["validatedFindings"]?["micro"]; var c = candidate?["validatedFindings"]?["micro"];
        var bp = N(b?["tp"]); var cp = N(c?["tp"]); var bf = N(b?["fp"]); var cf = N(c?["fp"]);
        var bn = N(b?["fn"]); var cn = N(c?["fn"]);
        if (bp == null || cp == null || bf == null || cf == null || bn == null || cn == null)
            return JsonValue.Create("No comparable Muse baseline is available yet. Review this run independently.")!;
        var bm = baseline?["metrics"]; var cm = candidate?["metrics"];
        var bi = N(bm?["reportedInputTokens"]); var ci = N(cm?["reportedInputTokens"]);
        var bo = N(bm?["reportedOutputTokens"]); var co = N(cm?["reportedOutputTokens"]);
        var sameQuality = bp == cp && bf == cf && bn == cn;
        var tokenImprovement = bi != null && ci != null && bo != null && co != null
            && ci <= bi && co <= bo && (ci < bi || co < bo);
        var qualityImprovement = cp >= bp && cf <= bf && cn <= bn && (cp > bp || cf < bf || cn < bn);
        var classification = qualityImprovement ? "quality_improvement" : sameQuality && tokenImprovement ? "efficiency_improvement"
            : sameQuality && (bi == null || ci == null || bo == null || co == null) ? "token_comparison_unavailable"
            : sameQuality && bi == ci && bo == co ? "equivalent" : cp < bp || cf > bf || cn > bn ? "regression_or_tradeoff" : "tradeoff";
        var summary = classification switch {
            "quality_improvement" => "Validated detection quality improved. Review input and output token tradeoffs before promotion.",
            "efficiency_improvement" => "Detection quality is unchanged and logical input/output usage improved without increasing either counter.",
            "equivalent" => "No measured improvement over the baseline.",
            "token_comparison_unavailable" => "Validated quality is unchanged. Token comparison is unavailable because directional usage was not retained.",
            _ => "This attempt has a regression or a tradeoff. Review missed targets, false positives and token usage." };
        return new JsonObject { ["classification"] = classification, ["summary"] = summary, ["truePositiveDelta"] = cp - bp,
            ["falsePositiveDelta"] = cf - bf, ["inputTokenDelta"] = ci - bi, ["outputTokenDelta"] = co - bo };
    }
}
