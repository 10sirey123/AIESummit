using System.Text.Json;
using System.Text.RegularExpressions;
using AIESummit.Domain;

namespace AIESummit.Evaluation;

public sealed record TargetScore(string Tag, string Category, bool Expected, bool Predicted, string Outcome,
    int GoldSpans, int PredictedSegments, double? EvidenceTokenF1);

public static class BenchmarkEvaluator
{
    public static TargetScore[] Compare(JsonElement gold, JsonElement prediction, bool reportedPresenceOnly = false)
    {
        if (gold.GetProperty("dataset").GetString() != "cuad" || gold.GetProperty("expected_detections").ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("This evaluation requires CUAD gold, never unlabeled EDGAR.");
        var analyses = prediction.GetProperty("analyses").EnumerateArray().Where(a => a.GetProperty("isCurrent").GetBoolean()).ToArray();
        return ExtractionCatalog.Defaults().Select(type => {
            var label = gold.GetProperty("expected_detections").EnumerateArray().Single(x => x.GetProperty("category").GetString() == type.BenchmarkCategory);
            var matches = analyses.SelectMany(a => a.GetProperty(reportedPresenceOnly ? "modelReportedOutcomes" : "targets").EnumerateArray()).Where(t => t.GetProperty("tag").GetString() == type.Tag).ToArray();
            var detected = matches.Any(t => t.GetProperty("outcome").GetString() == "detected");
            var quotes = matches.Where(_ => !reportedPresenceOnly).SelectMany(t => t.GetProperty("extractions").EnumerateArray()).SelectMany(e => e.GetProperty("evidence").EnumerateArray())
                .Select(e => e.GetProperty("quote").GetString()!).ToArray();
            var answers = label.GetProperty("answers").EnumerateArray().Select(a => a.GetProperty("text").GetString()!).ToArray();
            var outcome = detected ? "detected" : matches.Length == 0 ? "not_analyzed" : string.Join(",", matches.Select(t => t.GetProperty("outcome").GetString()).Distinct());
            return new TargetScore(type.Tag, type.BenchmarkCategory, label.GetProperty("expected_present").GetBoolean(), detected,
                outcome, answers.Length, quotes.Length, !reportedPresenceOnly && answers.Length > 0 ? TokenF1(string.Join(" ", answers), string.Join(" ", quotes)) : null);
        }).ToArray();
    }

    // Descriptive OCR/reference-text lexical overlap, NOT CUAD official character-span scoring.
    public static double TokenF1(string expected, string predicted)
    {
        static Dictionary<string, int> Bag(string text) => Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}]+")
            .Select(m => m.Value).GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var a = Bag(expected); var b = Bag(predicted); var n = a.Values.Sum() + b.Values.Sum();
        return n == 0 ? 1 : 2.0 * a.Sum(p => Math.Min(p.Value, b.GetValueOrDefault(p.Key))) / n;
    }

    public static object Summarize(IEnumerable<TargetScore> scores)
    {
        var all = scores.ToArray();
        object Metrics(IEnumerable<TargetScore> rows)
        {
            var r = rows.ToArray(); var tp = r.Count(x => x.Expected && x.Predicted); var fp = r.Count(x => !x.Expected && x.Predicted);
            var fn = r.Count(x => x.Expected && !x.Predicted); var tn = r.Count(x => !x.Expected && !x.Predicted);
            return new { tp, fp, fn, tn, precision = tp + fp == 0 ? (double?)null : (double)tp / (tp + fp),
                recall = tp + fn == 0 ? (double?)null : (double)tp / (tp + fn),
                f1 = 2 * tp + fp + fn == 0 ? (double?)null : 2.0 * tp / (2 * tp + fp + fn),
                accuracy = r.Length == 0 ? (double?)null : (double)(tp + tn) / r.Length,
                abstentions = r.Count(x => !x.Predicted && x.Outcome != "not_found_in_reviewed_text") };
        }
        return new { micro = Metrics(all), byTarget = all.GroupBy(x => x.Tag).Select(g => new { tag = g.Key, category = g.First().Category, metrics = Metrics(g) }) };
    }
}
