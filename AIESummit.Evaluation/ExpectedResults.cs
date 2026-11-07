using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIESummit.Domain;

namespace AIESummit.Evaluation;

public sealed record ExpectedTarget(string Tag, string Category, bool? Expected, string Expectation, int GoldFindings);
public sealed record ExpectedResultsSnapshot(string Id, string SampleId, string Dataset, string SourceSha256,
    string LabelSha256, string Partition, JsonElement LabelVersion, JsonElement? Provenance,
    JsonElement? ExpectedDetections, ExpectedTarget[] Targets);
public sealed record ObservedTarget(string Tag, string Category, bool? Expected, bool Found, bool ModelReported,
    string Outcome, string State, int FindingCount, object[] Extractions);

/// <summary>Benchmark-only label snapshots. Unknown labels never become negative gold.</summary>
public static class ExpectedResults
{
    public static ExpectedResultsSnapshot Create(ReadOnlySpan<byte> labelBytes, string sourceSha256, string partition = "development")
    {
        if (partition is not ("development" or "confirmation")) throw new InvalidDataException("Unknown benchmark partition.");
        using var document = JsonDocument.Parse(labelBytes.ToArray()); var gold = document.RootElement;
        var hash = gold.GetProperty("document").GetProperty("sha256").GetString()!;
        if (!hash.Equals(sourceSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Gold label and document checksums differ.");
        var dataset = gold.GetProperty("dataset").GetString()!;
        var labelHash = Convert.ToHexString(SHA256.HashData(labelBytes)).ToLowerInvariant();
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{dataset}/{sourceSha256.ToLowerInvariant()}/{labelHash}/{partition}"))).ToLowerInvariant();
        JsonElement? detections = gold.TryGetProperty("expected_detections", out var labels) && labels.ValueKind == JsonValueKind.Array ? labels.Clone() : null;
        var targets = ExtractionCatalog.Defaults().Select(type => {
            var matches = detections?.EnumerateArray().Where(x => x.TryGetProperty("category", out var c) && c.GetString() == type.BenchmarkCategory).ToArray() ?? [];
            bool? expected = matches.Length == 1 && matches[0].TryGetProperty("expected_present", out var present)
                && present.ValueKind is JsonValueKind.True or JsonValueKind.False ? present.GetBoolean() : null;
            var findings = matches.Sum(m => m.TryGetProperty("answers", out var answers) && answers.ValueKind == JsonValueKind.Array ? answers.GetArrayLength() : 0);
            return new ExpectedTarget(type.Tag, type.BenchmarkCategory, expected, expected is true ? "present" : expected is false ? "absent" : "unknown", findings);
        }).ToArray();
        return new(id, gold.GetProperty("sample_id").GetString()!, dataset, sourceSha256.ToLowerInvariant(), labelHash, partition,
            gold.GetProperty("schema_version").Clone(), gold.TryGetProperty("source", out var source) ? source.Clone() : null, detections, targets);
    }

    public static ObservedTarget[] Observe(ExpectedResultsSnapshot expected, JsonElement result)
    {
        var analyses = result.TryGetProperty("prediction", out var prediction) && prediction.ValueKind == JsonValueKind.Object
            && prediction.TryGetProperty("analyses", out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Where(x => !x.TryGetProperty("isCurrent", out var current) || current.ValueKind == JsonValueKind.True).ToArray() : [];
        var failed = result.TryGetProperty("status", out var status) && status.GetString() == "failed";
        return expected.Targets.Select(target => {
            bool HasTag(JsonElement row) => row.TryGetProperty("tag", out var t) && t.GetString() == target.Tag;
            bool AnalysisFailed(JsonElement analysis) => analysis.TryGetProperty("status", out var state) && state.ValueKind == JsonValueKind.String
                ? state.GetString() == "failed" : failed;
            var relevant = analyses.Where(x => Rows(x, "targets").Any(HasTag) || Rows(x, "definitions").Any(HasTag)
                || Rows(x, "modelReportedOutcomes").Any(HasTag)).ToArray();
            var matches = relevant.SelectMany(x => Rows(x, "targets")).Where(HasTag).ToArray();
            var targetFailed = relevant.Length == 0 ? failed : relevant.Any(AnalysisFailed);
            var raw = relevant.Where(AnalysisFailed).SelectMany(x => Rows(x, "modelReportedOutcomes")).Any(x => HasTag(x) && Outcome(x) == "detected");
            var found = matches.Any(x => Outcome(x) == "detected");
            var extractions = matches.Where(x => Outcome(x) == "detected").SelectMany(x => Rows(x, "extractions")).Select(e => (object)new {
                id = Value(e, "id"), title = Value(e, "title"), values = Value(e, "values"),
                evidence = Rows(e, "evidence").Select(v => new { id = Value(v, "id"), page = Value(v, "page"), pageTextRevisionId = Value(v, "pageTextRevisionId"),
                    contentHash = Value(v, "contentHash"), startOffset = Value(v, "startOffset"), endOffset = Value(v, "endOffset"),
                    role = Value(v, "role"), supportedFields = Value(v, "supportedFields") }).ToArray()
            }).ToArray();
            var outcome = found ? "detected" : matches.Length == 0 ? "not_analyzed" : string.Join(",", matches.Select(Outcome).Distinct());
            var state = found ? target.Expected is false ? "false_positive" : "found"
                : raw ? "rejected_evidence" : targetFailed ? "failed" : outcome == "not_analyzed" ? "not_analyzed"
                : target.Expected is true ? "missed" : target.Expected is false ? "correct_absence" : "unknown";
            return new ObservedTarget(target.Tag, target.Category, target.Expected, found, raw, outcome, state, extractions.Length, extractions);
        }).ToArray();
    }

    private static JsonElement[] Rows(JsonElement value, string name) => value.TryGetProperty(name, out var rows) && rows.ValueKind == JsonValueKind.Array ? rows.EnumerateArray().ToArray() : [];
    private static string Outcome(JsonElement value) => value.TryGetProperty("outcome", out var o) ? o.GetString() ?? "not_analyzed" : "not_analyzed";
    private static JsonElement? Value(JsonElement value, string name) => value.TryGetProperty(name, out var item) ? item.Clone() : null;
}
