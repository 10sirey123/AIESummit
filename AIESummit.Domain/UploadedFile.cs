using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Blueshift.Domain.Abstract;
using Blueshift.Domain.Entities;
using Blueshift.Domain.Infrastructure;
using Blueshift.Domain.LLM;

namespace AIESummit.Domain;

[DisplayMetadata("lucide:FileInput", MenuName = "Uploads",
    MenuDescription = "Original files submitted by users, with OCR and document-splitting progress.")]
[ForeignTypes(typeof(OcrPage), nameof(OcrPage.Upload), ForeignConnectionTypes.Direct,
    Title = "OCR pages", Icon = "lucide:ScanText", Order = 10)]
[ForeignTypes(typeof(Document), nameof(Document.Upload), ForeignConnectionTypes.Direct,
    Title = "Documents", Icon = "lucide:FileText", Order = 20)]
[ForeignTypes(typeof(LLMPrompt), nameof(LLMPrompts), ForeignConnectionTypes.MultipleIndirect,
    Title = "Processing prompts", Icon = "lucide:MessageSquareText", Order = 30)]
[Microsoft.EntityFrameworkCore.Index(nameof(OwnerId), nameof(ID))]
/// <summary>
/// Subject of the split-upload step. SplitPolicy reloads this row and its persisted prompts,
/// then builds SplitInput from the upload's OCR pages in page-number order.
/// </summary>
public sealed class UploadedFile : BasePersistenceObject, ILlmCapable
{
    [Display(Name = "Owner identifier", Description = "Firebase account subject that owns this upload. Assigned at upload time and used to authorize access to its results.")]
    [DisplayMetadata("lucide:UserRound", IsEditable = false, Order = 60)]
    public string OwnerId { get; set; } = "";
    [Display(Name = "File name", Description = "Original upload name with directory components removed. Retained as a display label, not a storage path.")]
    [DisplayMetadata("lucide:FileInput", ShowInSnippet = true, IsEditable = false, ConnectorType = AttributeConnectorType.Text, Order = 10)]
    public string FileName { get; set; } = "";
    [Display(Name = "File format", Description = "MIME type detected from the uploaded bytes: PDF, PNG, or JPEG.")]
    [DisplayMetadata("lucide:FileBadge", IsEditable = false, Order = 50)]
    public string ContentType { get; set; } = "";
    [Display(Name = "Original file bytes", Description = "Retained source file sent to OCR. Binary content is hidden from the Admin form.")]
    [DisplayMetadata("lucide:Package", IsHidden = true, IsEditable = false)]
    public byte[] Source { get; set; } = [];
    [Display(Name = "OCR source fingerprint")]
    [DisplayMetadata("lucide:Fingerprint", IsHidden = true, IsEditable = false)]
    public string OcrCacheKey { get; set; } = "";
    public bool AnalyzePageSections { get; set; } = true;
    [Display(Name = "OCR status", Description = "Progress of extracting text and images from the original file. Managed by the OCR agent.")]
    [DisplayMetadata("lucide:ScanText", ShowInSnippet = true, IsEditable = false, Order = 20)]
    public ProcessedStatus OcrStatus { get; set; }
    [Display(Name = "Document splitting status", Description = "Progress of identifying logical documents from the OCR pages. Managed by the LLM step and prompt agents.")]
    [DisplayMetadata("lucide:Scissors", ShowInSnippet = true, IsEditable = false, Order = 30)]
    public StepProcessingStatus LLMStepStatus { get; set; }
    [Display(Name = "Page count", Description = "Number of accepted OCR pages. Zero until OCR completes; successful extraction contains 1 to 30 pages.")]
    [DisplayMetadata("lucide:Files", ShowInSnippet = true, IsEditable = false, Order = 40)]
    public int PageCount { get; set; }
    [Display(Name = "Processing error", Description = "User-facing failure summary. Inspect Agent Operations for execution details before attempting recovery.")]
    [DisplayMetadata("lucide:Activity", IsEditable = false, ConnectorType = AttributeConnectorType.Text, Order = 70)]
    public string? Error { get; set; }
    [Display(Name = "LLM result JSON", Description = "Structured result retained by the LLM workflow. Generated output; document records contain the applied page ranges and text.")]
    [DisplayMetadata("lucide:BrainCircuit", IsEditable = false, ConnectorType = AttributeConnectorType.Json, Order = 80)]
    public string LLMJsonResult { get; set; } = "";
    [Display(Name = "Prepared split input", Description = "Transient ordered OCR-page JSON prepared for prompt generation. Rebuilt during hydration and never stored.")]
    [DisplayMetadata("lucide:ScanText", IsHidden = true, IsEditable = false)]
    [NotMapped]
    public string SplitInput { get; set; } = "";
    [Display(Name = "Processing prompts", Description = "Persisted prompts belonging to this upload. Inspect them in the Processing prompts related section.")]
    [DisplayMetadata("lucide:MessageSquareText", IsHidden = true, IsEditable = false)]
    public ICollection<LLMPrompt> LLMPrompts { get; set; } = [];
    public override string GetFriendlyName() => FileName;
}
