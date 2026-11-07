using System.ComponentModel.DataAnnotations;
using Blueshift.Domain.Abstract;
using Blueshift.Domain.Infrastructure;

namespace AIESummit.Domain;

[DisplayMetadata("lucide:FileText", MenuName = "Extractions")]
[ForeignTypes(typeof(ExtractionEvidence), nameof(ExtractionEvidence.Extraction), ForeignConnectionTypes.Direct, Title = "Source evidence", Order = 10)]
public sealed class Extraction : BasePersistenceObject
{
    [Display(Name = "Target Result Id", Description = "Parent target result identifier.")]
    [DisplayMetadata("", IsEditable = false)]
    public int TargetResultId { get; set; }
    [Display(Name = "Target Result", Description = "Parent target outcome, analysis and document.")]
    [DisplayMetadata("", IsEditable = false)]
    public AnalysisTargetResult TargetResult { get; set; } = null!;
    [Display(Name = "Title", Description = "Short factual finding title.")]
    [DisplayMetadata("", IsEditable = false, ShowInSnippet = true)]
    public string Title { get; set; } = "";
    [Display(Name = "Summary", Description = "Factual summary; not a legal risk assessment.")]
    [DisplayMetadata("", IsEditable = false)]
    public string Summary { get; set; } = "";
    [Display(Name = "Schema Version", Description = "Version of the validated structured-value payload.")]
    [DisplayMetadata("", IsEditable = false)]
    public int SchemaVersion { get; set; } = 1;
    [Display(Name = "Values Json", Description = "Typed extracted facts validated against the target field allowlist.")]
    [DisplayMetadata("", IsEditable = false, ConnectorType = AttributeConnectorType.Json)]
    public string ValuesJson { get; set; } = "{}";
    [Display(Name = "Review State", Description = "Human-review state; initially unreviewed.")]
    [DisplayMetadata("", IsEditable = false, ShowInSnippet = true)]
    public string ReviewState { get; set; } = "unreviewed";
    [Display(Name = "Evidence", Description = "Exact source passages and field-level support for this finding.")]
    [DisplayMetadata("", IsEditable = false)]
    public ICollection<ExtractionEvidence> Evidence { get; set; } = [];
    public override string GetFriendlyName() => Title;
}
