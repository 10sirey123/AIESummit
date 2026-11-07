using AIESummit.Agents;
using AIESummit.Domain;
using Blueshift.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Blueshift.Domain.Abstract;
using Blueshift.Domain.Concrete;
using Blueshift.Domain.LLM;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AIESummit.Extraction.Tests;

public sealed class ExtractionDatabaseFact : FactAttribute
{
    public ExtractionDatabaseFact() { if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AIESUMMIT_EXTRACTION_TEST_CONNECTION"))) Skip = "Run scripts/test-extractions.ps1 for disposable PostgreSQL coverage."; }
}

public sealed class PersistenceTests
{
    [ExtractionDatabaseFact]
    public async Task AdditiveUpgradeIsRepeatableAndSeedsPreserveEditsAndResultsAreDurable()
    {
        var connection = Environment.GetEnvironmentVariable("AIESUMMIT_EXTRACTION_TEST_CONNECTION")!;
        Assert.Equal("aiesummit_extraction_test", new NpgsqlConnectionStringBuilder(connection).Database);
        AIESummitDb Open() => new(new DbContextOptionsBuilder<AIESummitDb>().UseNpgsql(connection).Options);
        await using var db = Open(); await db.Database.EnsureCreatedAsync();
        var upload = new UploadedFile { OwnerId = "synthetic-owner", FileName = "synthetic.pdf", Source = [1, 2, 3] };
        var page = new OcrPage { Upload = upload, Number = 1, Markdown = "Agreement expires on 2027-12-31." };
        var document = new Document { Upload = upload, FirstPage = 1, LastPage = 1, Title = "Synthetic" };
        db.AddRange(upload, page, document); await db.SaveChangesAsync();
        // Simulate the retained pre-extraction schema, without deleting original upload/page/document rows.
        await db.Database.ExecuteSqlRawAsync("""
            DROP TABLE "ExtractionEvidence", "Extractions", "AnalysisTargetResults", "PageTextRevisions", "DocumentAnalyses", "ExtractionTypes" CASCADE;
            ALTER TABLE "LLMPrompts" DROP COLUMN "DocumentAnalysis_ID";
            ALTER TABLE "Documents" DROP COLUMN "IsCurrent" CASCADE;
            ALTER TABLE "Uploads" DROP COLUMN "AnalyzePageSections";
            CREATE UNIQUE INDEX "IX_Documents_UploadId_FirstPage" ON "Documents" ("UploadId", "FirstPage");
            """);
        using var migration = typeof(PersistenceTests).Assembly.GetManifestResourceStream("AIESummit.Extraction.Tests.Upgrade.sql")!;
        using var reader = new StreamReader(migration); var sql = await reader.ReadToEndAsync();
        await db.Database.ExecuteSqlRawAsync(sql); await db.Database.ExecuteSqlRawAsync(sql);
        Assert.Equal(new byte[] { 1, 2, 3 }, (await db.Uploads.AsNoTracking().SingleAsync()).Source);
        var model = new LLMModel { Tag = "synthetic-extraction", Name = "synthetic", Provider = LLMAPIProvider.OpenAI };
        db.LLMModels.Add(model); await ExtractionSeeds.EnsureAsync(db, model); await db.SaveChangesAsync();
        var type = await db.ExtractionTypes.SingleAsync(t => t.Tag == "agreement-expiration");
        type.Name = "Operator label"; type.Instructions = "Operator instructions";
        var step = await db.LLMSteps.SingleAsync(s => s.Tag == ContractExtractionStep.Tag);
        step.Enabled = false; step.Template = "Operator template";
        await db.SaveChangesAsync();
        await ExtractionSeeds.EnsureAsync(db, model); await db.SaveChangesAsync();
        Assert.Equal(10, await db.ExtractionTypes.CountAsync()); Assert.Equal("Operator label", type.Name);
        Assert.Equal("Operator instructions", type.Instructions); Assert.False(step.Enabled); Assert.Equal("Operator template", step.Template);

        var analysis = AnalysisFactory.Create(document, [page], [type]); db.DocumentAnalyses.Add(analysis); await db.SaveChangesAsync();
        var result = analysis.Targets.Single(); result.Outcome = "detected";
        result.Extractions.Add(new Domain.Extraction { Title = "Expiration", Summary = "Explicit date", ValuesJson = "{\"date\":\"2027-12-31\"}",
            Evidence = [new ExtractionEvidence { PageTextRevisionId = analysis.Pages.Single().ID, Quote = "2027-12-31", StartOffset = 21, EndOffset = 31, SupportedFieldsJson = "[\"date\"]" }] });
        analysis.CompletedAt = DateTime.UtcNow; await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var evidence = await db.ExtractionEvidence.Include(e => e.PageTextRevision).SingleAsync();
        Assert.Equal(evidence.Quote, evidence.PageTextRevision.Markdown[evidence.StartOffset..evidence.EndOffset]);
        evidence.PageTextRevision.Markdown = "changed";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"PageTextRevisions\" SET \"Markdown\" = 'changed' WHERE \"ID\" = {evidence.PageTextRevisionId}"));

        // Historical documents coexist with a current replacement, and analysis keeps its original source identity.
        var old = await db.Documents.SingleAsync(); old.IsCurrent = false;
        db.Documents.Add(new Document { UploadId = upload.ID, FirstPage = 1, LastPage = 1, Title = "Replacement" }); await db.SaveChangesAsync();
        Assert.Equal(2, await db.Documents.CountAsync()); Assert.Single(await db.Documents.Where(d => d.IsCurrent).ToListAsync());
        db.ChangeTracker.Clear(); db.Documents.Remove(await db.Documents.SingleAsync(d => !d.IsCurrent));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        using var services = new ServiceCollection().AddSingleton<EFBaseDbContext>(db)
            .AddScoped(typeof(IBasePersistenceRepository<>), typeof(EFBasePersistenceRepository<>)).BuildServiceProvider();
        var context = new MetadataExecutionContext(0, services.GetRequiredService<IBasePersistenceRepository<LLMPrompt>>());
        var split = new SplitResult(context) { Documents = [new PageRange { FirstPage = 1, LastPage = 1, Title = "Re-split" }] };
        Assert.True(split.ConnectTo(await db.Uploads.SingleAsync(), context)); await db.SaveChangesAsync();
        Assert.Equal(3, await db.Documents.CountAsync());
        Assert.Single(await db.Documents.Where(d => d.IsCurrent).ToListAsync());
    }
}
