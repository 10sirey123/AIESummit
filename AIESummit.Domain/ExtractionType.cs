using System.ComponentModel.DataAnnotations;
using Blueshift.Domain.Abstract;
using Blueshift.Domain.Infrastructure;

namespace AIESummit.Domain;

[DisplayMetadata("lucide:Tags", MenuName = "Extraction types")]
[Microsoft.EntityFrameworkCore.Index(nameof(Tag), IsUnique = true)]
public sealed class ExtractionType : BasePersistenceObject, IHaveUniqueTag
{
    [Display(Name = "Tag", Description = "Stable target identity used in code, seeding and results.")]
    [DisplayMetadata("", IsEditable = false, ShowInSnippet = true)]
    [Required, StringLength(128)] public string Tag { get; set; } = "";
    [Display(Name = "Name", Description = "Human-readable target name.")]
    [DisplayMetadata("", IsEditable = false, ShowInSnippet = true)]
    public string Name { get; set; } = "";
    [Display(Name = "Benchmark Category", Description = "Exact upstream CUAD category used for presence evaluation.")]
    [DisplayMetadata("", IsEditable = false)]
    public string BenchmarkCategory { get; set; } = "";
    [Display(Name = "Instructions", Description = "Definition pinned into each analysis before model execution.")]
    [DisplayMetadata("", IsEditable = false)]
    public string Instructions { get; set; } = "";
    [Display(Name = "Definition Version", Description = "Version of the target definition and allowed value contract.")]
    [DisplayMetadata("", IsEditable = false)]
    public int DefinitionVersion { get; set; } = 1;
    [Display(Name = "Parameters Json", Description = "JSON array of allowed structured-value field names.")]
    [DisplayMetadata("", IsEditable = false, ConnectorType = AttributeConnectorType.Json)]
    public string ParametersJson { get; set; } = "[]";
    [Display(Name = "Enabled", Description = "Whether new analyses should request this target.")]
    [DisplayMetadata("", IsEditable = false)]
    public bool Enabled { get; set; } = true;
    public override string GetFriendlyName() => Name;
}
