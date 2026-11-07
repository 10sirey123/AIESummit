using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AIESummit.Domain;

public static class AnalysisFactory
{
    public static DocumentAnalysis Create(Document document, IEnumerable<OcrPage> pages,
        IEnumerable<ExtractionType> types, string requestKey = "initial")
    {
        var selected = types.Where(t => t.Enabled).OrderBy(t => t.Tag, StringComparer.Ordinal).ToArray();
        if (selected.Length == 0) throw new InvalidOperationException("No extraction targets are enabled.");
        var source = pages.Where(p => p.UploadId == document.UploadId && p.Number >= document.FirstPage && p.Number <= document.LastPage)
            .OrderBy(p => p.Number).ToArray();
        if (!source.Select(p => p.Number).SequenceEqual(Enumerable.Range(document.FirstPage, document.LastPage - document.FirstPage + 1)))
            throw new InvalidOperationException("Document pages must be complete and ordered.");
        return new DocumentAnalysis
        {
            Document = document, RequestKey = requestKey,
            InputHash = Hash(JsonSerializer.Serialize(source.Select(p => new { p.Number, p.Markdown }))),
            TargetsJson = JsonSerializer.Serialize(selected.Select(t => new { tag = t.Tag, name = t.Name,
                version = t.DefinitionVersion, instructions = t.Instructions,
                fields = JsonSerializer.Deserialize<string[]>(t.ParametersJson), category = t.BenchmarkCategory })),
            Pages = source.Select(p => new PageTextRevision { OcrPageId = p.ID, Number = p.Number,
                Markdown = p.Markdown, ContentHash = Hash(p.Markdown) }).ToList(),
            Targets = selected.Select(t => new AnalysisTargetResult { ExtractionTypeId = t.ID }).ToList()
        };
    }
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
