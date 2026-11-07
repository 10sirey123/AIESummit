using Blueshift.Domain.Concrete;
using Blueshift.Domain.Entities;
using Blueshift.Domain.Entities.AgentConfigs;
using Microsoft.EntityFrameworkCore;

namespace AIESummit.Domain;

public sealed class AIESummitDb(DbContextOptions options) : EFBaseDbContext(options)
{
    public DbSet<UploadedFile> Uploads => Set<UploadedFile>();
    public DbSet<OcrPage> Pages => Set<OcrPage>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<PageImage> PageImages => Set<PageImage>();
    public DbSet<Image> Images => Set<Image>();
    public DbSet<APIInfo> APIInfos => Set<APIInfo>();
    public DbSet<LLMModel> LLMModels => Set<LLMModel>();
    public DbSet<LLMStep> LLMSteps => Set<LLMStep>();
    public DbSet<LLMPrompt> LLMPrompts => Set<LLMPrompt>();
    public DbSet<LLMStepAgentConfig> LLMStepAgentConfigs => Set<LLMStepAgentConfig>();
    public DbSet<LLMPromptAgentConfig> LLMPromptAgentConfigs => Set<LLMPromptAgentConfig>();
    public DbSet<OcrAgentConfig> OcrAgentConfigs => Set<OcrAgentConfig>();
    public DbSet<BackendActivity> BackendActivities => Set<BackendActivity>();
    public DbSet<LogLineStore> LogLineStores => Set<LogLineStore>();
    public DbSet<SeedIdentity> SeedIdentities => Set<SeedIdentity>();
    public DbSet<ExtractionType> ExtractionTypes => Set<ExtractionType>();
    public DbSet<DocumentAnalysis> DocumentAnalyses => Set<DocumentAnalysis>();
    public DbSet<PageTextRevision> PageTextRevisions => Set<PageTextRevision>();
    public DbSet<AnalysisTargetResult> AnalysisTargetResults => Set<AnalysisTargetResult>();
    public DbSet<Extraction> Extractions => Set<Extraction>();
    public DbSet<ExtractionEvidence> ExtractionEvidence => Set<ExtractionEvidence>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Document>().HasIndex(d => new { d.UploadId, d.FirstPage }).IsUnique().HasFilter("\"IsCurrent\" = TRUE");
        modelBuilder.Entity<DocumentAnalysis>().HasMany(a => a.LLMPrompts).WithOne()
            .HasForeignKey("DocumentAnalysis_ID").OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<DocumentAnalysis>().HasOne(a => a.Document).WithMany().OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<PageTextRevision>().HasOne(p => p.OcrPage).WithMany().OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<AnalysisTargetResult>().HasOne(r => r.ExtractionType).WithMany().OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ExtractionEvidence>().HasOne(e => e.PageTextRevision).WithMany().OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Extraction>().Property(e => e.ValuesJson).HasColumnType("jsonb");
        modelBuilder.Entity<ExtractionType>().Property(e => e.ParametersJson).HasColumnType("jsonb");
        modelBuilder.Entity<DocumentAnalysis>().Property(e => e.TargetsJson).HasColumnType("jsonb");
        modelBuilder.Entity<ExtractionEvidence>().Property(e => e.SupportedFieldsJson).HasColumnType("jsonb");
        modelBuilder.Entity<UploadedFile>().HasMany(file => file.LLMPrompts).WithOne()
            .HasForeignKey("UploadedFile_ID").OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<OcrPage>().HasMany(page => page.LLMPrompts).WithOne()
            .HasForeignKey("OcrPage_ID").OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<LLMPrompt>().HasOne(prompt => prompt.UsedModel).WithMany()
            .HasForeignKey("UsedModelID").IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        // Admin's generic relation projector cannot infer this legacy shadow FK by CLR naming.
        modelBuilder.Entity<LLMPrompt>().Navigation(prompt => prompt.UsedModel).AutoInclude();
        base.OnModelCreating(modelBuilder);
    }

    private void ValidateSnapshots()
    {
        if (ChangeTracker.Entries<PageTextRevision>().Any(e => e.State == EntityState.Modified))
            throw new InvalidOperationException("Analysis text snapshots are immutable; create a new analysis.");
    }

    public override int SaveChanges()
    {
        ValidateSnapshots();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ValidateSnapshots();
        // SeedImporter constructs detached records; initialize legacy nonnullable diagnostics
        // without managing or overwriting existing operator values.
        foreach (var entry in ChangeTracker.Entries<LLMStep>().Where(e => e.State == EntityState.Added))
        {
            var step = entry.Entity;
            step.Description ??= ""; step.LambdaValidation ??= ""; step.DTOResponseTypeValidation ??= "";
            step.LatestError ??= ""; step.SystemRoleTemplateValidation ??= ""; step.TemplateValidation ??= "";
            step.Parser ??= ""; step.ReasoningEffort ??= ""; step.MultimodalLambda ??= "";
        }
        foreach (var entry in ChangeTracker.Entries<LLMPrompt>()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            var prompt = entry.Entity;
            prompt.SystemPrompt ??= "";
            prompt.Prompt ??= "";
            prompt.Response ??= "";
            prompt.MultimodalLambda ??= "";
            prompt.ExpectedResponseJsonFormat ??= "";
            prompt.ReasoningEffort ??= "";
            prompt.DTOResponseType ??= "";
            prompt.MD5Hash ??= "";
            prompt.Parser ??= "";
            prompt.DocumentType ??= "";
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
