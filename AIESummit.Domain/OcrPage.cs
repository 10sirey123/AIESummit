using System.ComponentModel.DataAnnotations;
using Blueshift.Domain.Abstract;
using Blueshift.Domain.Infrastructure;
using Blueshift.Domain.Entities;
using Blueshift.Domain.LLM;

namespace AIESummit.Domain;

[DisplayMetadata("lucide:ScanText", MenuName = "OCR pages",
    MenuDescription = "Page-by-page text extracted from uploads, preserving the original page order.")]
[ForeignTypes(typeof(PageImage), nameof(PageImage.Page), ForeignConnectionTypes.Direct,
    Title = "Extracted images", Icon = "lucide:Image", Order = 10)]
[Microsoft.EntityFrameworkCore.Index(nameof(UploadId), nameof(Number), IsUnique = true)]
public sealed class OcrPage : BasePersistenceObject, ILlmCapable
{
    [Display(Name = "Upload identifier", Description = "Stored foreign key to the source upload.")]
    [DisplayMetadata("lucide:FileInput", IsHidden = true, IsEditable = false)]
    public int UploadId { get; set; }
    [Display(Name = "Source upload", Description = "Original file containing this page. Its owner controls access to the extracted content.")]
    [DisplayMetadata("lucide:FileInput", IsEditable = false, ConnectorType = AttributeConnectorType.SearchListSingle, Order = 20)]
    public UploadedFile Upload { get; set; } = null!;
    [Display(Name = "Page number", Description = "Position in the original file, starting at 1. Unique within the source upload.")]
    [DisplayMetadata("lucide:Files", ShowInSnippet = true, IsEditable = false, Order = 10)]
    public int Number { get; set; }
    [Display(Name = "OCR Markdown", Description = "Text extracted from this page, retained as Markdown source. Image references refer to this page's extracted images.")]
    [DisplayMetadata("lucide:ScrollText", IsEditable = false, ConnectorType = AttributeConnectorType.Text, Order = 30)]
    public string Markdown { get; set; } = "";
    [Display(Name = "Section analysis status")]
    [DisplayMetadata("lucide:BrainCircuit", ShowInSnippet = true, IsEditable = false)]
    public StepProcessingStatus LLMStepStatus { get; set; }
    [DisplayMetadata("", IsEditable = false, ConnectorType = AttributeConnectorType.Json)]
    public string LLMJsonResult { get; set; } = "";
    [Display(Name = "Section classifications and summaries")]
    [DisplayMetadata("lucide:List", IsEditable = false, ConnectorType = AttributeConnectorType.Json)]
    public string SectionsJson { get; set; } = "";
    [DisplayMetadata("", IsEditable = false)]
    public string? AnalysisError { get; set; }
    [DisplayMetadata("", IsHidden = true, IsEditable = false)]
    public ICollection<LLMPrompt> LLMPrompts { get; set; } = [];
    public override string GetFriendlyName() => $"Page {Number} · Upload {UploadId}";
}
