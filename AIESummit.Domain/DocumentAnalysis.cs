using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Blueshift.Domain.Abstract;
using Blueshift.Domain.Entities;
using Blueshift.Domain.Infrastructure;
using Blueshift.Domain.LLM;

namespace AIESummit.Domain;

[DisplayMetadata("lucide:BrainCircuit", MenuName = "Document analyses")]
[Microsoft.EntityFrameworkCore.Index(nameof(DocumentId), nameof(RequestKey), IsUnique = true)]
public sealed class DocumentAnalysis : BasePersistenceObject, ILlmCapable
{
    [Display(Name = "Document Id", Description = "Source logical document identifier.")]
    [DisplayMetadata("", IsEditable = false)]
    public int DocumentId { get; set; }
    [Display(Name = "Document", Description = "Source logical document; its upload controls owner access.")]
    [DisplayMetadata("", IsEditable = false)]
    public Document Document { get; set; } = null!;
    [Display(Name = "Request Key", Description = "Idempotency key within this logical document.")]
    [DisplayMetadata("", IsEditable = false)]
    public string RequestKey { get; set; } = "initial";
    [Display(Name = "Input Hash", Description = "SHA-256 of the ordered page input snapshot.")]
    [DisplayMetadata("", IsEditable = false)]
    public string InputHash { get; set; } = "";
    [Display(Name = "Targets Json", Description = "Pinned target definitions for this execution.")]
    [DisplayMetadata("", IsEditable = false, ConnectorType = AttributeConnectorType.Json)]
    public string TargetsJson { get; set; } = "[]";
    [Display(Name = "LLMStep Status", Description = "Native framework analysis processing state.")]
    [DisplayMetadata("", IsEditable = false, ShowInSnippet = true)]
    public StepProcessingStatus LLMStepStatus { get; set; } = StepProcessingStatus.CheckForPrompts;
    [Display(Name = "LLMJson Result", Description = "Retained native structured response metadata.")]
    [DisplayMetadata("", IsEditable = false)]
    public string LLMJsonResult { get; set; } = "";
    [Display(Name = "LLMPrompts", Description = "Persisted native prompts belonging to this analysis.")]
    [DisplayMetadata("", IsEditable = false)]
    public ICollection<LLMPrompt> LLMPrompts { get; set; } = [];
    [Display(Name = "Pages", Description = "Immutable source page snapshots for this analysis.")]
    [DisplayMetadata("", IsEditable = false)]
    public ICollection<PageTextRevision> Pages { get; set; } = [];
    [Display(Name = "Targets", Description = "Per-target outcomes and findings.")]
    [DisplayMetadata("", IsEditable = false)]
    public ICollection<AnalysisTargetResult> Targets { get; set; } = [];
    [Display(Name = "Completed At", Description = "UTC completion time after output validation.")]
    [DisplayMetadata("", IsEditable = false)]
    public DateTime? CompletedAt { get; set; }
    [Display(Name = "Error", Description = "Validation or processing failure; successful earlier analyses remain intact.")]
    [DisplayMetadata("", IsEditable = false)]
    public string? Error { get; set; }
    [Display(Name = "Analysis Input", Description = "Transient prompt input constructed during hydration.")]
    [DisplayMetadata("", IsEditable = false, IsHidden = true)]
    [NotMapped] public string AnalysisInput { get; set; } = "";
    public override string GetFriendlyName() => $"Document {DocumentId} · analysis {ID}";
}
