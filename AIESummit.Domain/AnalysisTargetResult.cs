using System.ComponentModel.DataAnnotations;
using Blueshift.Domain.Abstract;
using Blueshift.Domain.Infrastructure;

namespace AIESummit.Domain;

[DisplayMetadata("lucide:List", MenuName = "Analysis target results")]
[Microsoft.EntityFrameworkCore.Index(nameof(AnalysisId), nameof(ExtractionTypeId), IsUnique = true)]
public sealed class AnalysisTargetResult : BasePersistenceObject
{
    [Display(Name = "Analysis Id", Description = "Parent analysis identifier.")]
    [DisplayMetadata("", IsEditable = false)]
    public int AnalysisId { get; set; }
    [Display(Name = "Analysis", Description = "Parent analysis and its pinned definitions.")]
    [DisplayMetadata("", IsEditable = false)]
    public DocumentAnalysis Analysis { get; set; } = null!;
    [Display(Name = "Extraction Type Id", Description = "Target definition identifier.")]
    [DisplayMetadata("", IsEditable = false)]
    public int ExtractionTypeId { get; set; }
    [Display(Name = "Extraction Type", Description = "Catalog definition for this result.")]
    [DisplayMetadata("", IsEditable = false)]
    public ExtractionType ExtractionType { get; set; } = null!;
    [Display(Name = "Outcome", Description = "Detected, not found, uncertain, not applicable or not yet analyzed.")]
    [DisplayMetadata("", IsEditable = false, ShowInSnippet = true)]
    public string Outcome { get; set; } = "not_analyzed";
    [Display(Name = "Explanation", Description = "Factual reason for the target outcome.")]
    [DisplayMetadata("", IsEditable = false)]
    public string Explanation { get; set; } = "";
    [Display(Name = "Extractions", Description = "Individual findings for this target.")]
    [DisplayMetadata("", IsEditable = false)]
    public ICollection<Extraction> Extractions { get; set; } = [];
}
