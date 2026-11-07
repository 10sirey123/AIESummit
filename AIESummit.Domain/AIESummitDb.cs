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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UploadedFile>().HasMany(file => file.LLMPrompts).WithOne()
            .HasForeignKey("UploadedFile_ID").OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<LLMPrompt>().HasOne(prompt => prompt.UsedModel).WithMany()
            .HasForeignKey("UsedModelID").IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        base.OnModelCreating(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
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
