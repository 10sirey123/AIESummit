using System.Text;
using System.Text.RegularExpressions;

namespace AIESummit.Agents;

public sealed record EvidenceMatch(int Start, int End, string SourceQuote, string Method, double Score);

/// <summary>Page-local, exact-first evidence anchoring with an inclusive 50% multiset word Dice fallback.</summary>
public static class EvidenceMatcher
{
    public const double MinimumScore = 0.5;
    public const string Version = "word-dice-50-v1";
    private static readonly Regex Words = new(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant);
    private static string Key(string word) => word.Normalize(NormalizationForm.FormKC).ToUpperInvariant();

    public static EvidenceMatch Match(string text, string quote, int occurrence = -1)
    {
        if (string.IsNullOrWhiteSpace(quote) || quote.Length > 8000 || occurrence < -1)
            throw new InvalidDataException("Empty, oversized or invalid evidence.");
        var exact = new List<(int Start, int End)>();
        for (var next = 0; next <= text.Length - quote.Length;)
        {
            var at = text.IndexOf(quote, next, StringComparison.Ordinal);
            if (at < 0) break;
            exact.Add((at, at + quote.Length)); next = at + 1;
        }
        if (exact.Count > 0) return Select(exact, "exact", 1);

        var query = Words.Matches(quote).Select(m => Key(m.Value)).ToArray();
        var source = Words.Matches(text).ToArray();
        if (query.Length == 0 || source.Length == 0) throw new InvalidDataException("Evidence has no matching words.");
        var wanted = query.GroupBy(w => w).ToDictionary(g => g.Key, g => g.Count());
        var width = Math.Min(query.Length, source.Length);
        var counts = new Dictionary<string, int>(); var shared = 0;
        void Add(string key)
        {
            var old = counts.GetValueOrDefault(key);
            if (old < wanted.GetValueOrDefault(key)) shared++;
            counts[key] = old + 1;
        }
        void Remove(string key)
        {
            var old = counts[key];
            if (old <= wanted.GetValueOrDefault(key)) shared--;
            counts[key] = old - 1;
        }
        for (var i = 0; i < width; i++) Add(Key(source[i].Value));
        var bestShared = -1; var candidates = new List<(int Start, int End)>();
        for (var first = 0; first <= source.Length - width; first++)
        {
            if (shared > bestShared) { bestShared = shared; candidates.Clear(); }
            if (shared == bestShared) candidates.Add((source[first].Index, source[first + width - 1].Index + source[first + width - 1].Length));
            if (first + width < source.Length) { Remove(Key(source[first].Value)); Add(Key(source[first + width].Value)); }
        }
        var score = 2.0 * bestShared / (query.Length + width);
        if (score < MinimumScore) throw new InvalidDataException("Evidence word overlap is below 50%.");
        // Adjacent tied windows at the same location are one candidate, rather than artificial ambiguity.
        var locations = new List<(int Start, int End)>();
        foreach (var candidate in candidates)
            if (locations.Count == 0 || candidate.Start >= locations[^1].End) locations.Add(candidate);
        return Select(locations, Version, score);

        EvidenceMatch Select(List<(int Start, int End)> positions, string method, double score)
        {
            var index = occurrence == -1 && positions.Count == 1 ? 0 : occurrence;
            if (index < 0 || index >= positions.Count) throw new InvalidDataException("Evidence missing or ambiguous.");
            var (start, end) = positions[index];
            if ((start > 0 && char.IsLowSurrogate(text[start])) || (end < text.Length && char.IsLowSurrogate(text[end])))
                throw new InvalidDataException("Evidence splits a Unicode surrogate pair.");
            return new(start, end, text[start..end], method, score);
        }
    }
}
