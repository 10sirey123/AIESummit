using System.ComponentModel.DataAnnotations;
using Blueshift.Domain.Abstract;
using Blueshift.Domain.Infrastructure;

namespace AIESummit.Domain;

[DisplayMetadata("lucide:ScanText", MenuName = "Extraction evidence")]
public sealed class ExtractionEvidence : BasePersistenceObject
{
    [Display(Name = "Model quote", Description = "Original model-supplied evidence before source anchoring.")]
    [DisplayMetadata("", IsEditable = false)]
    public string ModelQuote { get; set; } = "";
    [Display(Name = "Match method", Description = "Exact match or versioned word-overlap matching rule.")]
    [DisplayMetadata("", IsEditable = false)]
    public string MatchMethod { get; set; } = "exact";
    [Display(Name = "Match score", Description = "Word Dice overlap from zero to one; exact matches score one.")]
    [DisplayMetadata("", IsEditable = false)]
    public double MatchScore { get; set; } = 1;
    [Display(Name = "Extraction Id", Description = "Finding supported by this segment.")]
    [DisplayMetadata("", IsEditable = false)]
    public int ExtractionId { get; set; }
    [Display(Name = "Extraction", Description = "Finding supported by this segment.")]
    [DisplayMetadata("", IsEditable = false)]
    public Extraction Extraction { get; set; } = null!;
    [Display(Name = "Page Text Revision Id", Description = "Immutable page snapshot containing this evidence.")]
    [DisplayMetadata("", IsEditable = false)]
    public int PageTextRevisionId { get; set; }
    [Display(Name = "Page Text Revision", Description = "Immutable page snapshot containing this evidence.")]
    [DisplayMetadata("", IsEditable = false)]
    public PageTextRevision PageTextRevision { get; set; } = null!;
    [Display(Name = "Start Offset", Description = "Inclusive zero-based UTF-16 offset into snapshot Markdown.")]
    [DisplayMetadata("", IsEditable = false)]
    public int StartOffset { get; set; }
    [Display(Name = "End Offset", Description = "Exclusive UTF-16 offset into snapshot Markdown.")]
    [DisplayMetadata("", IsEditable = false)]
    public int EndOffset { get; set; }
    [Display(Name = "Quote", Description = "Exact source substring between the stored offsets.")]
    [DisplayMetadata("", IsEditable = false)]
    public string Quote { get; set; } = "";
    [Display(Name = "Role", Description = "Primary evidence, qualification or definition.")]
    [DisplayMetadata("", IsEditable = false)]
    public string Role { get; set; } = "primary";
    [Display(Name = "Supported Fields Json", Description = "JSON array of structured fields supported by this segment.")]
    [DisplayMetadata("", IsEditable = false, ConnectorType = AttributeConnectorType.Json)]
    public string SupportedFieldsJson { get; set; } = "[]";
}
