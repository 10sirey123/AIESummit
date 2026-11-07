using System.ComponentModel.DataAnnotations;
using Blueshift.Domain.Abstract;
using Blueshift.Domain.Entities;
using Blueshift.Domain.Infrastructure;

namespace AIESummit.Domain;

[DisplayMetadata("lucide:Image", MenuName = "Extracted images",
    MenuDescription = "Links between OCR pages and retained image files, identified by the OCR image reference.")]
[Microsoft.EntityFrameworkCore.Index(nameof(PageId), nameof(Reference), IsUnique = true)]
public sealed class PageImage : BasePersistenceObject
{
    [Display(Name = "Page identifier", Description = "Stored foreign key to the OCR page containing this image reference.")]
    [DisplayMetadata("lucide:ScanText", IsHidden = true, IsEditable = false)]
    public int PageId { get; set; }
    [Display(Name = "Source page", Description = "OCR page on which this image occurs. The page's upload determines ownership.")]
    [DisplayMetadata("lucide:ScanText", IsEditable = false, ConnectorType = AttributeConnectorType.SearchListSingle, Order = 20)]
    public OcrPage Page { get; set; } = null!;
    [Display(Name = "Image identifier", Description = "Stored foreign key to the retained image bytes.")]
    [DisplayMetadata("lucide:Image", IsHidden = true, IsEditable = false)]
    public int ImageId { get; set; }
    [Display(Name = "Image file", Description = "Retained binary image extracted by OCR. Documents include this image when their page range contains its source page.")]
    [DisplayMetadata("lucide:Image", IsEditable = false, ConnectorType = AttributeConnectorType.SearchListSingle, Order = 30)]
    public Image Image { get; set; } = null!;
    [Display(Name = "OCR image reference", Description = "Image identifier supplied by OCR and used in the page Markdown. Unique within this page.")]
    [DisplayMetadata("lucide:Tag", ShowInSnippet = true, IsEditable = false, ConnectorType = AttributeConnectorType.Text, Order = 10)]
    public string Reference { get; set; } = "";
    public override string GetFriendlyName() => $"{Reference} · Page {PageId}";
}
