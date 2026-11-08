using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AIESummit.Domain;

namespace AIESummit.WorkflowRunner;

public sealed class WorkflowDefinition
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "Contract extraction";
    public List<WorkflowNode> Nodes { get; set; } = [];
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) {
        WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    [JsonIgnore] public string Hash => HashText(JsonSerializer.Serialize(new { Version, Name, Nodes }, Json));
    public static string HashText(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public WorkflowNode[] OrderedNodes()
    {
        if (Version != 1 || string.IsNullOrWhiteSpace(Name) || Name.Length > 200 || Nodes.Count is < 1 or > 20)
            throw new InvalidDataException("A workflow requires version 1, a name, and 1–20 workflow nodes.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in Nodes)
        {
            if (!Regex.IsMatch(node.Key, "^[a-z][a-z0-9-]{0,63}$") || !keys.Add(node.Key))
                throw new InvalidDataException("Node keys must be unique lowercase identifiers.");
            if (node.Model is not ("muse-wonder-1.0" or "muse-spark-1.3"))
                throw new InvalidDataException("Only the configured Muse model identifiers are allowed.");
            if (node.Temperature is < 0 or > 200 || node.ReasoningEffort is not ("" or "minimal" or "low" or "medium" or "high" or "xhigh"))
                throw new InvalidDataException("Invalid generation settings.");
            if (string.IsNullOrWhiteSpace(node.SystemPrompt) || string.IsNullOrWhiteSpace(node.Prompt)
                || node.SystemPrompt.Length > 100000 || node.Prompt.Length > 100000 || node.Targets.Count == 0)
                throw new InvalidDataException("Each extraction node needs bounded prompts and at least one target.");
            if (node.NodeKind is not ("extract" or "detect-pages"))
                throw new InvalidDataException("Node kind must be extract or detect-pages.");
            if (node.Targets.Distinct(StringComparer.Ordinal).Count() != node.Targets.Count
                || node.Targets.Any(t => !ExtractionCatalog.Defaults().Any(c => c.Tag == t)))
                throw new InvalidDataException("Node targets must be distinct catalog targets.");
            if (node.NodeKind == "detect-pages" && (node.SelectedPagesFrom != null || node.DependsOn.Count != 0))
                throw new InvalidDataException("A page detector requires catalog targets and no prerequisites.");
        }
        var expected = ExtractionCatalog.Defaults().Select(t => t.Tag).Order(StringComparer.Ordinal).ToArray();
        var assigned = Nodes.Where(n => n.NodeKind == "extract").SelectMany(n => n.Targets).Order(StringComparer.Ordinal).ToArray();
        if (!expected.SequenceEqual(assigned))
            throw new InvalidDataException("Assign every benchmark target to exactly one node; scoring and target definitions are fixed.");
        foreach (var node in Nodes)
            if (node.DependsOn.Distinct().Count() != node.DependsOn.Count || node.EffectiveDependencies.Any(p => !keys.Contains(p) || p == node.Key))
                throw new InvalidDataException("Dependencies must name distinct other nodes.");
        foreach (var node in Nodes)
        {
            if (node.SelectedPagesFrom != null)
            {
                var detector = Nodes.Single(n => n.Key == node.SelectedPagesFrom);
                if (node.NodeKind != "extract" || node.Targets.Count != 1 || detector.NodeKind != "detect-pages"
                    || !detector.Targets.Contains(node.Targets.Single(), StringComparer.Ordinal) || node.DependsOn.Any(p => p != detector.Key))
                    throw new InvalidDataException("Page extraction must route its single target from a matching page detector only.");
            }
            else if (node.DependsOn.Any(p => Nodes.Single(n => n.Key == p).NodeKind == "detect-pages"))
                throw new InvalidDataException("Detector dependencies must use selectedPagesFrom so detection output is never treated as a finding.");
        }
        var ordered = new List<WorkflowNode>();
        while (ordered.Count < Nodes.Count)
        {
            var next = Nodes.FirstOrDefault(n => !ordered.Contains(n) && n.EffectiveDependencies.All(p => ordered.Any(o => o.Key == p)));
            if (next == null) throw new InvalidDataException("The extraction workflow contains a dependency cycle.");
            ordered.Add(next);
        }
        return ordered.ToArray();
    }
}

public sealed class WorkflowNode
{
    public string Key { get; set; } = "extract";
    public List<string> Targets { get; set; } = [];
    public List<string> DependsOn { get; set; } = [];
    public string SystemPrompt { get; set; } = "";
    public string Prompt { get; set; } = "Analyze the supplied document against every target.";
    public string Model { get; set; } = "muse-wonder-1.0";
    public int Temperature { get; set; }
    public string ReasoningEffort { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Kind { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SelectedPagesFrom { get; set; }
    [JsonIgnore] public string NodeKind => Kind ?? "extract";
    [JsonIgnore] public IEnumerable<string> EffectiveDependencies => DependsOn.Concat(SelectedPagesFrom == null ? [] : new[] { SelectedPagesFrom }).Distinct(StringComparer.Ordinal);
    [JsonIgnore] public string DefinitionHash => WorkflowDefinition.HashText(JsonSerializer.Serialize(new {
        Targets = Targets.Order(StringComparer.Ordinal), SystemPrompt, Prompt, Model, Temperature, ReasoningEffort,
        responseContract = NodeKind == "detect-pages" ? "PageTargetPresenceResult/v1" : "ContractExtractionResult/v1",
        validation = NodeKind == "detect-pages" ? "page-target-presence-v1" : "evidence-word-dice-50/v1"
    }));

    public string CacheKey(int documentId, string effectiveInput, string schema)
        => WorkflowDefinition.HashText(JsonSerializer.Serialize(new { documentId, DefinitionHash, effectiveInput, schema }));
}
