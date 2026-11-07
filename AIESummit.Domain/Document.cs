using System.ComponentModel.DataAnnotations;
using Blueshift.Domain.Abstract;
using Blueshift.Domain.Infrastructure;

namespace AIESummit.Domain;

[DisplayMetadata("lucide:FileText", MenuName = "Documents",
    MenuDescription = "Logical documents identified within uploads, with inclusive page ranges and extracted text.")]
[ForeignTypes(typeof(DocumentAnalysis), nameof(DocumentAnalysis.Document), ForeignConnectionTypes.Direct,
    Title = "Contract analyses", Icon = "lucide:BrainCircuit", Order = 10)]
/// <summary>A logical document produced by applying the source upload's split result.</summary>
public sealed class Document : BasePersistenceObject
{
    [Display(Name = "Current split version", Description = "False when a later split supersedes this document; retained analyses keep their source identity.")]
    [DisplayMetadata("", IsEditable = false)]
    public bool IsCurrent { get; set; } = true;
    [Display(Name = "Upload identifier", Description = "Stored foreign key to the source upload.")]
    [DisplayMetadata("lucide:FileInput", IsHidden = true, IsEditable = false)]
    public int UploadId { get; set; }
    [Display(Name = "Source upload", Description = "Original file from which this document's pages were extracted.")]
    [DisplayMetadata("lucide:FileInput", IsEditable = false, ConnectorType = AttributeConnectorType.SearchListSingle, Order = 40)]
    public UploadedFile Upload { get; set; } = null!;
    [Display(Name = "Document title", Description = "Short title generated for this logical document from its OCR pages.")]
    [DisplayMetadata("lucide:FileText", ShowInSnippet = true, IsEditable = false, ConnectorType = AttributeConnectorType.Text, Order = 10)]
    public string Title { get; set; } = "";
    [Display(Name = "First page", Description = "First included page in the original upload, numbered from 1.")]
    [DisplayMetadata("lucide:Files", ShowInSnippet = true, IsEditable = false, Order = 20)]
    public int FirstPage { get; set; }
    [Display(Name = "Last page", Description = "Last included page in the original upload. Equal to the first page for a single-page document.")]
    [DisplayMetadata("lucide:Files", ShowInSnippet = true, IsEditable = false, Order = 30)]
    public int LastPage { get; set; }
    [Display(Name = "Document Markdown", Description = "OCR text for the inclusive page range, joined with page-break comments. This record does not contain a separate PDF.")]
    [DisplayMetadata("lucide:ScrollText", IsEditable = false, ConnectorType = AttributeConnectorType.Text, Order = 50)]
    public string Markdown { get; set; } = "";
    public override string GetFriendlyName() => Title;
}
