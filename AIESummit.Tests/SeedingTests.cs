using System.Text.Json;
using AIESummit.Agents;
using AIESummit.Configuration;
using AIESummit.Domain;
using Blueshift.Domain.Abstract;
using Blueshift.Domain.Concrete;
using Blueshift.Domain.Entities;
using Blueshift.Domain.Infrastructure;
using Blueshift.Domain.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace AIESummit.Tests;

public sealed class SeedingTests
{
    [Fact]
    public void ExportUsesActualDefinitionsAndExcludesSecretsAndRuntimeBindings()
    {
        var manifest = WorkflowSeeds.Create();
        var text = JsonSerializer.Serialize(manifest);
        Assert.DoesNotContain("AppKey", text);
        Assert.DoesNotContain("AppSecret", text);
        Assert.DoesNotContain("AppAuthToken", text);
        Assert.DoesNotContain("LLMPrompt", text);
        foreach (var step in new[] { SplitStep.Definition(), PageSectionsStep.Definition() })
        {
            var row = Assert.Single(manifest.Records, r => r.Entity == nameof(LLMStep) && r.Tag == step.Tag);
            Assert.Equal(step.SystemRolePromptTemplate, row.Values[nameof(LLMStep.SystemRolePromptTemplate)].GetString());
            Assert.Equal(step.DTOResponseType, row.Values[nameof(LLMStep.DTOResponseType)].GetString());
            Assert.False(row.Values.ContainsKey(nameof(LLMStep.Enabled)));
            Assert.Empty(row.References);
        }
        var bound = WorkflowSeeds.Create(true).Records.Where(r => r.Entity == nameof(LLMStep));
        Assert.All(bound, row => Assert.True(row.References.ContainsKey(nameof(LLMStep.Model))));
    }

    [SeedDatabaseFact]
    public async Task PostgreSqlPlanApplyPreservesOperatorDataAndRejectsStalePlans()
    {
        var connection = Environment.GetEnvironmentVariable("AIESUMMIT_SEED_TEST_CONNECTION")!;
        Assert.Equal("aiesummit_seed_test", new NpgsqlConnectionStringBuilder(connection).Database);
        AIESummitDb Open() => new(new DbContextOptionsBuilder<AIESummitDb>().UseNpgsql(connection).Options);
        await using (var db = Open())
        {
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync("DROP TABLE \"SeedIdentities\"");
            await SeedSchema.ApplyAsync(db);
            await SeedSchema.ApplyAsync(db);
            var model = new LLMModel { Tag = "operator-meta", Name = "operator-selected-model", Provider = LLMAPIProvider.Meta };
            var step = PageSectionsStep.Definition(); step.Model = model; step.Enabled = true; step.Template = "old template";
            db.Add(step);
            db.APIInfos.Add(new APIInfo { Identifier = "meta", BaseURI = "https://api.meta.ai/v1", AppKey = "synthetic-secret", Enabled = true });
            db.Uploads.Add(new UploadedFile { OwnerId = "synthetic", FileName = "retained.pdf", Source = [1,2,3] });
            await db.SaveChangesAsync();
        }
        var manifest = WorkflowSeeds.Create();
        await using (var db = Open())
        {
            var plan = new SeedPlanner(db).Plan(manifest, SeedPolicy.Update);
            Assert.Empty(db.ChangeTracker.Entries());
            Assert.Empty(await db.SeedIdentities.ToListAsync());
            using var services = Services(db);
            new SeedImporter(db, new RepositoryWriteGateway(services)).Apply(manifest, SeedPolicy.Update, plan.PreviewToken);
        }
        DateTime? modified;
        await using (var db = Open())
        {
            var step = await db.LLMSteps.Include(s => s.Model).SingleAsync(s => s.Tag == PageSectionsStep.Tag);
            Assert.Equal(PageSectionsStep.Definition().Template, step.Template);
            Assert.Equal("operator-selected-model", step.Model.Name); Assert.True(step.Enabled);
            var api = await db.APIInfos.SingleAsync(a => a.Identifier == "meta");
            Assert.Equal("synthetic-secret", api.AppKey); Assert.True(api.Enabled);
            Assert.Equal(new byte[] {1,2,3}, (await db.Uploads.SingleAsync()).Source);
            var split = await db.LLMSteps.SingleAsync(s => s.Tag == SplitStep.Tag);
            Assert.False(split.Enabled); Assert.Null(split.Model_ID);
            modified = step.LastModified;
        }
        await using (var db = Open())
        {
            var plan = new SeedPlanner(db).Plan(manifest, SeedPolicy.Update);
            Assert.All(plan.Items, item => Assert.Equal("keep", item.Action));
            using var services = Services(db);
            new SeedImporter(db, new RepositoryWriteGateway(services)).Apply(manifest, SeedPolicy.Update, plan.PreviewToken);
        }
        await using (var db = Open())
        {
            Assert.Equal(modified, (await db.LLMSteps.SingleAsync(s => s.Tag == PageSectionsStep.Tag)).LastModified);
            var stale = new SeedPlanner(db).Plan(manifest, SeedPolicy.Update);
            var step = await db.LLMSteps.SingleAsync(s => s.Tag == PageSectionsStep.Tag);
            step.Template = "operator changed after preview"; await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            using var services = Services(db);
            Assert.Throws<SeedManifestException>(() => new SeedImporter(db, new RepositoryWriteGateway(services)).Apply(manifest, SeedPolicy.Update, stale.PreviewToken));
            Assert.Equal("operator changed after preview", (await db.LLMSteps.AsNoTracking().SingleAsync(s => s.Tag == PageSectionsStep.Tag)).Template);
        }
        await using (var db = Open())
        {
            var failure = SeedManifest.Parse("""
                {"version":1,"venture":"aiesummit","set":"rollback-test","records":[
                  {"entity":"APIInfo","tag":"rollback-first","match":{"Identifier":"rollback-first"},"values":{"Enabled":false}},
                  {"entity":"LLMModel","tag":"rollback-second","match":{"Tag":"rollback-second"},"values":{"Tag":"operator-meta","Name":"duplicate","Provider":"Meta"}}
                ]}
                """);
            var plan = new SeedPlanner(db).Plan(failure, SeedPolicy.Update);
            using var services = Services(db);
            // The second save violates the unique model tag; the first save and registry entry must roll back.
            var error = Assert.Throws<DbUpdateException>(() => new SeedImporter(db, new RepositoryWriteGateway(services)).Apply(failure, SeedPolicy.Update, plan.PreviewToken));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
            Assert.False(await db.APIInfos.AnyAsync(a => a.Identifier == "rollback-first"));
            Assert.False(await db.SeedIdentities.AnyAsync(i => i.SeedSet == "rollback-test"));
        }
        await using (var db = Open())
        {
            var bound = WorkflowSeeds.Create(true);
            var plan = new SeedPlanner(db).Plan(bound, SeedPolicy.Update);
            using var services = Services(db);
            new SeedImporter(db, new RepositoryWriteGateway(services)).Apply(bound, SeedPolicy.Update, plan.PreviewToken);
            var split = await db.LLMSteps.Include(s => s.Model).SingleAsync(s => s.Tag == SplitStep.Tag);
            Assert.True(split.Enabled); Assert.Equal(SplitSamples.ModelTag, split.Model.Tag);
            var page = await db.LLMSteps.Include(s => s.Model).SingleAsync(s => s.Tag == PageSectionsStep.Tag);
            Assert.Equal(PageSectionsStep.ModelTag, page.Model.Tag);
            Assert.Equal("synthetic-secret", (await db.APIInfos.SingleAsync(a => a.Identifier == "meta")).AppKey);
        }
    }

    private static ServiceProvider Services(AIESummitDb db) => new ServiceCollection().AddSingleton<EFBaseDbContext>(db)
        .AddScoped(typeof(IBasePersistenceRepository<>), typeof(EFBasePersistenceRepository<>)).BuildServiceProvider();
}

public sealed class SeedDatabaseFactAttribute : FactAttribute
{
    public SeedDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AIESUMMIT_SEED_TEST_CONNECTION")))
            Skip = "Run scripts/test-seeding.ps1 for isolated PostgreSQL acceptance.";
    }
}
