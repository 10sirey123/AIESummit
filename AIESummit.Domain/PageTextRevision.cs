using System.ComponentModel.DataAnnotations;
using Blueshift.Domain.Abstract;
using Blueshift.Domain.Infrastructure;

namespace AIESummit.Domain;

[DisplayMetadata("lucide:ScanText", MenuName = "Analysis page snapshots")]
[Microsoft.EntityFrameworkCore.Index(nameof(AnalysisId), nameof(Number), IsUnique = true)]
public sealed class PageTextRevision : BasePersistenceObject
{
    [Display(Name = "Analysis Id", Description = "Parent analysis identifier.")]
    [DisplayMetadata("", IsEditable = false)]
    public int AnalysisId { get; set; }
    [Display(Name = "Analysis", Description = "Parent analysis and its pinned definitions.")]
    [DisplayMetadata("", IsEditable = false)]
    public DocumentAnalysis Analysis { get; set; } = null!;
    [Display(Name = "Ocr Page Id", Description = "Original OCR page identifier.")]
    [DisplayMetadata("", IsEditable = false)]
    public int OcrPageId { get; set; }
    [Display(Name = "Ocr Page", Description = "Original OCR page providing source identity.")]
    [DisplayMetadata("", IsEditable = false)]
    public OcrPage OcrPage { get; set; } = null!;
    [Display(Name = "Number", Description = "One-based page position in the original upload.")]
    [DisplayMetadata("", IsEditable = false, ShowInSnippet = true)]
    public int Number { get; set; }
    [Display(Name = "Markdown", Description = "Immutable exact page text used to resolve evidence offsets.")]
    [DisplayMetadata("", IsEditable = false)]
    public string Markdown { get; set; } = "";
    [Display(Name = "Content Hash", Description = "SHA-256 of the exact page Markdown.")]
    [DisplayMetadata("", IsEditable = false)]
    public string ContentHash { get; set; } = "";
}
