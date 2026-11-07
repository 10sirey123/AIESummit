using System.Text.Json;
using AIESummit.Agents;
using AIESummit.Domain;
using AIESummit.Evaluation;
using Blueshift.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AIESummit.Extraction.Tests;

public sealed class ExtractionTests
{
    private static DocumentAnalysis Analysis()
    {
        var type = ExtractionCatalog.Defaults()[1]; type.ID = 3;
        var analysis = AnalysisFactory.Create(new Document { UploadId = 1, FirstPage = 1, LastPage = 2 },
            [new OcrPage { ID = 1, UploadId = 1, Number = 1, Markdown = "😀 Expiration: 2027-12-31." },
             new OcrPage { ID = 2, UploadId = 1, Number = 2, Markdown = "Except if renewed." }], [type]);
        analysis.Targets.Single().ExtractionType = type; analysis.Targets.Single().ID = 1;
        var n = 0; foreach (var p in analysis.Pages) p.ID = ++n;
        return analysis;
    }
    private static TargetOutput Output() => new() { Tag = "agreement-expiration", Outcome = "detected", Explanation = "Explicit expiration date.", Findings = [new() {
        Title = "Initial expiration", Summary = "Expires unless renewed.", Values = new() { Date = "2027-12-31", Conditions = "Except if renewed." },
        Evidence = [new() { Page = 1, Quote = "2027-12-31", Fields = ["date"] },
                    new() { Page = 2, Quote = "Except if renewed.", Role = "qualification", Fields = ["conditions"] }] }] };

    [Fact] public void MultipleEvidenceSegmentsUseExactUtf16OffsetsAndFieldLinks()
    {
        var staged = Assert.Single(ContractExtractionResult.Validate(Analysis(), [Output()]));
        var finding = Assert.Single(staged.Extractions); Assert.Equal(2, finding.Evidence.Count);
        Assert.Equal(15, finding.Evidence.First().StartOffset);
        Assert.Contains("2027-12-31", finding.ValuesJson);
    }
    [Fact] public void RepeatedQuotesRequireAnOccurrenceAndQuotesAreCaseSensitive()
    {
        Assert.Throws<InvalidDataException>(() => ContractExtractionResult.ResolveQuote("notice notice", "notice"));
        Assert.Equal(7, ContractExtractionResult.ResolveQuote("notice notice", "notice", 1));
        Assert.Throws<InvalidDataException>(() => ContractExtractionResult.ResolveQuote("Notice", "notice"));
        Assert.Throws<InvalidDataException>(() => ContractExtractionResult.ResolveQuote("abc", ""));
    }
    [Fact] public void EvidenceOutsideDocumentOrAbsentFromPageIsRejectedBeforeMutation()
    {
        var a = Analysis(); var o = Output(); o.Findings[0].Evidence[1].Page = 3;
        Assert.Throws<InvalidDataException>(() => ContractExtractionResult.Validate(a, [o]));
        Assert.Equal("not_analyzed", a.Targets.Single().Outcome); Assert.Empty(a.Targets.Single().Extractions);
        o = Output(); o.Findings[0].Evidence[0].Quote = "2028-12-31";
        Assert.Throws<InvalidDataException>(() => ContractExtractionResult.Validate(a, [o]));
    }
    [Fact] public void InvalidDatesUnsupportedFieldsAndUnanchoredValuesAreRejected()
    {
        var o = Output(); o.Findings[0].Values.Date = "2027-02-30";
        Assert.Throws<InvalidDataException>(() => ContractExtractionResult.Validate(Analysis(), [o]));
        o = Output(); o.Findings[0].Values.Amount = 10;
        Assert.Throws<InvalidDataException>(() => ContractExtractionResult.Validate(Analysis(), [o]));
        o = Output(); o.Findings[0].Evidence[0].Fields.Clear();
        Assert.Throws<InvalidDataException>(() => ContractExtractionResult.Validate(Analysis(), [o]));
    }
    [Fact] public void DuplicateUnknownAndMissingTargetsAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => ContractExtractionResult.Validate(Analysis(), []));
        Assert.Throws<InvalidDataException>(() => ContractExtractionResult.Validate(Analysis(), [Output(), Output()]));
        var o = Output(); o.Tag = "invented";
        Assert.Throws<InvalidDataException>(() => ContractExtractionResult.Validate(Analysis(), [o]));
    }
    [Fact] public void NegativeOutcomeHasNoFabricatedFindingAndCompletedRunsCannotBeReplaced()
    {
        var o = Output(); o.Outcome = "not_found_in_reviewed_text";
        Assert.Throws<InvalidDataException>(() => ContractExtractionResult.Validate(Analysis(), [o]));
        o.Findings.Clear(); Assert.Empty(Assert.Single(ContractExtractionResult.Validate(Analysis(), [o])).Extractions);
        var a = Analysis(); a.CompletedAt = DateTime.UtcNow;
        Assert.Throws<InvalidDataException>(() => ContractExtractionResult.Validate(a, [o]));
    }
    [Fact] public void SnapshotCannotBorrowPagesFromAnotherUploadOrSkipPages()
    {
        Assert.Throws<InvalidOperationException>(() => AnalysisFactory.Create(new Document { UploadId = 1, FirstPage = 1, LastPage = 2 },
            [new OcrPage { UploadId = 2, Number = 1 }], ExtractionCatalog.Defaults()));
        var a = Analysis(); var before = a.InputHash; a.Pages.First().Markdown = "changed";
        Assert.NotEqual(a.Pages.First().ContentHash, AnalysisFactory.Hash(a.Pages.First().Markdown)); Assert.Equal(before, a.InputHash);
    }
    [Fact] public void RelationalModelKeepsPromptAndEvidenceForeignKeysAndCurrentDocumentUniqueness()
    {
        using var db = new AIESummitDb(new DbContextOptionsBuilder<AIESummitDb>().UseNpgsql("Host=localhost;Database=model").Options);
        Assert.Equal("DocumentAnalysis_ID", db.Model.FindEntityType(typeof(DocumentAnalysis))!.FindNavigation("LLMPrompts")!.ForeignKey.Properties.Single().Name);
        Assert.Contains("IsCurrent", db.Model.FindEntityType(typeof(Document))!.GetIndexes().Single(i => i.IsUnique).GetFilter());
        Assert.Equal("jsonb", db.Model.FindEntityType(typeof(Domain.Extraction))!.FindProperty("ValuesJson")!.GetColumnType());
    }
    [Fact] public void CatalogHasTenUniqueTargetsAndTheStepUsesTheRealLlmSubject()
    {
        var types = ExtractionCatalog.Defaults(); Assert.Equal(10, types.Count); Assert.Equal(10, types.Select(t => t.Tag).Distinct().Count());
        Assert.Equal(nameof(DocumentAnalysis), ContractExtractionStep.Definition().DocumentType);
        Assert.Contains("all", ContractExtractionStep.Definition().SystemRolePromptTemplate.ToLowerInvariant());
    }
    [Fact] public void BenchmarkScoringSeparatesLexicalOverlapFromPresenceAndRejectsUnlabeledGold()
    {
        Assert.Equal(1, BenchmarkEvaluator.TokenF1("90 days", "90 DAYS"));
        Assert.Equal(0, BenchmarkEvaluator.TokenF1("90 days", ""));
        using var gold = JsonDocument.Parse("{\"dataset\":\"edgar-corpus\",\"expected_detections\":null}");
        Assert.Throws<InvalidDataException>(() => BenchmarkEvaluator.Compare(gold.RootElement, default));
    }
}
