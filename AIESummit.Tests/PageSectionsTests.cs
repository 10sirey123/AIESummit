using AIESummit.Agents;
using AIESummit.Domain;
using Blueshift.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AIESummit.Tests;

public sealed class PageSectionsTests
{
    private static PageSection Section(string quote = "30 days") => new()
    { Heading = "Termination", Classification = "termination", Summary = "Written notice is required.", Quote = quote };

    [Fact]
    public void EvidenceMustBeAnExactSubstringOfThisPage()
    {
        Assert.True(PageSectionsResult.IsValid([Section()], "Give **30 days** written notice."));
        Assert.False(PageSectionsResult.IsValid([Section("60 days")], "Give 30 days written notice."));
        Assert.False(PageSectionsResult.IsValid([Section("30 Days")], "Give 30 days written notice."));
    }

    [Fact]
    public void EmptyPagesAreAllowedAndMalformedOrUnboundedResultsAreRejected()
    {
        Assert.True(PageSectionsResult.IsValid([], ""));
        Assert.False(PageSectionsResult.IsValid(null, ""));
        Assert.False(PageSectionsResult.IsValid(Enumerable.Repeat(Section(), 31).ToList(), "30 days"));
        Assert.False(PageSectionsResult.IsValid([new PageSection()], "30 days"));
        var item = Section(); item.Summary = new string('x', 1201);
        Assert.False(PageSectionsResult.IsValid([item], "30 days"));
    }

    [Fact]
    public void PageAndUploadPromptsHaveSeparatePersistedRelationships()
    {
        using var db = new AIESummitDb(new DbContextOptionsBuilder<AIESummitDb>().UseNpgsql("Host=localhost;Database=model-test").Options);
        var page = db.Model.FindEntityType(typeof(OcrPage))!;
        var upload = db.Model.FindEntityType(typeof(UploadedFile))!;
        Assert.Equal("OcrPage_ID", page.FindNavigation(nameof(OcrPage.LLMPrompts))!.ForeignKey.Properties.Single().Name);
        Assert.Equal("UploadedFile_ID", upload.FindNavigation(nameof(UploadedFile.LLMPrompts))!.ForeignKey.Properties.Single().Name);
        Assert.True(db.Model.FindEntityType(typeof(LLMPrompt))!.FindProperty("UsedModelID")!.IsNullable);
        Assert.True(db.Model.FindEntityType(typeof(LLMPrompt))!.FindNavigation(nameof(LLMPrompt.UsedModel))!.IsEagerLoaded);
    }
}
