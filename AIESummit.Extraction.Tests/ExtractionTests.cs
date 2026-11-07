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
    [Fact] public void RepeatedQuotesRequireAnOccurrenceAndWordMatchingIgnoresCase()
    {
        Assert.Throws<InvalidDataException>(() => ContractExtractionResult.ResolveQuote("notice notice", "notice"));
        Assert.Equal(7, ContractExtractionResult.ResolveQuote("notice notice", "notice", 1));
        Assert.Equal(0, ContractExtractionResult.ResolveQuote("Notice", "notice"));
        Assert.Throws<InvalidDataException>(() => ContractExtractionResult.ResolveQuote("abc", ""));
    }
    [Fact] public void EvidenceOutsideDocumentOrAbsentFromPageIsDroppedWithItsUnsupportedValues()
    {
        var a = Analysis(); var o = Output(); o.Findings[0].Evidence[1].Page = 3;
        var target = Assert.Single(ContractExtractionResult.Validate(a, [o]));
        Assert.Equal("not_analyzed", a.Targets.Single().Outcome); Assert.Empty(a.Targets.Single().Extractions);
        var finding = Assert.Single(target.Extractions); Assert.Single(finding.Evidence);
        Assert.Contains("2027-12-31", finding.ValuesJson); Assert.DoesNotContain("conditions", finding.ValuesJson);
        Assert.Contains("Validation removed 1 unsupported value, 1 evidence segment", target.Explanation);
        o = Output(); o.Findings[0].Evidence[0].Quote = "unrelated missing source words";
        finding = Assert.Single(Assert.Single(ContractExtractionResult.Validate(a, [o])).Extractions);
        Assert.DoesNotContain("date", finding.ValuesJson); Assert.Contains("conditions", finding.ValuesJson);
        o = Output(); o.Findings[0].Evidence.ForEach(e => e.Page = 3);
        target = Assert.Single(ContractExtractionResult.Validate(a, [o]));
        Assert.Empty(target.Extractions); Assert.Equal("uncertain", target.Outcome);
    }
    [Fact] public void FiftyPercentWordOverlapIsInclusiveAndOffsetsStillSelectActualSource()
    {
        var match = EvidenceMatcher.Match("😀 Alpha beta gamma delta.", "alpha beta other words");
        Assert.Equal(0.5, match.Score); Assert.Equal(EvidenceMatcher.Version, match.Method);
        Assert.Equal("Alpha beta gamma delta", match.SourceQuote);
        Assert.Equal(match.SourceQuote, "😀 Alpha beta gamma delta."[match.Start..match.End]);
        Assert.Throws<InvalidDataException>(() => EvidenceMatcher.Match("alpha beta gamma delta epsilon", "alpha beta other missing words"));
    }
    [Fact] public void WordCountsCannotBeInflatedAndSeparateLocationsRemainAmbiguous()
    {
        Assert.Throws<InvalidDataException>(() => EvidenceMatcher.Match("alpha beta gamma delta", "alpha alpha alpha alpha"));
        Assert.Throws<InvalidDataException>(() => EvidenceMatcher.Match("alpha beta gamma delta", "!!!"));
        var text = "alpha beta gamma delta separator extra tokens between alpha beta gamma delta";
        Assert.Throws<InvalidDataException>(() => EvidenceMatcher.Match(text, "ALPHA BETA GAMMA DELTA"));
        Assert.True(EvidenceMatcher.Match(text, "ALPHA BETA GAMMA DELTA", 1).Start > 0);
    }
    [Fact] public void InvalidDatesUnsupportedFieldsAndUnanchoredValuesAreRemovedNotPublished()
    {
        static string Values(TargetOutput o) => Assert.Single(Assert.Single(ContractExtractionResult.Validate(Analysis(), [o])).Extractions).ValuesJson;
        var o = Output(); o.Findings[0].Values.Date = "2027-02-30";
        Assert.DoesNotContain("date", Values(o));
        o = Output(); o.Findings[0].Values.Amount = 10;
        var json = Values(o); Assert.DoesNotContain("amount", json); Assert.Contains("2027-12-31", json);
        o = Output(); o.Findings[0].Evidence[0].Fields.Clear();
        Assert.DoesNotContain("date", Values(o));
        o = Output(); o.Findings[0].Values.Duration = 3;
        Assert.DoesNotContain("duration", Values(o));
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
        var negative = Assert.Single(ContractExtractionResult.Validate(Analysis(), [o]));
        Assert.Empty(negative.Extractions); Assert.Equal("not_found_in_reviewed_text", negative.Outcome);
        o.Findings.Clear(); Assert.Empty(Assert.Single(ContractExtractionResult.Validate(Analysis(), [o])).Extractions);
        o.Outcome = "invented"; Assert.Throws<InvalidDataException>(() => ContractExtractionResult.Validate(Analysis(), [o]));
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
