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
            throw new InvalidDataException("A workflow requires version 1, a name, and 1–20 extraction nodes.");
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
        }
        var expected = ExtractionCatalog.Defaults().Select(t => t.Tag).Order(StringComparer.Ordinal).ToArray();
        var assigned = Nodes.SelectMany(n => n.Targets).Order(StringComparer.Ordinal).ToArray();
        if (!expected.SequenceEqual(assigned))
            throw new InvalidDataException("Assign every benchmark target to exactly one node; scoring and target definitions are fixed.");
        foreach (var node in Nodes)
            if (node.DependsOn.Distinct().Count() != node.DependsOn.Count || node.DependsOn.Any(p => !keys.Contains(p) || p == node.Key))
                throw new InvalidDataException("Dependencies must name distinct other nodes.");
        var ordered = new List<WorkflowNode>();
        while (ordered.Count < Nodes.Count)
        {
            var next = Nodes.FirstOrDefault(n => !ordered.Contains(n) && n.DependsOn.All(p => ordered.Any(o => o.Key == p)));
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
    [JsonIgnore] public string DefinitionHash => WorkflowDefinition.HashText(JsonSerializer.Serialize(new {
        Targets = Targets.Order(StringComparer.Ordinal), SystemPrompt, Prompt, Model, Temperature, ReasoningEffort,
        responseContract = "ContractExtractionResult/v1", validation = "evidence-word-dice-50/v1"
    }));

    public string CacheKey(int documentId, string effectiveInput, string schema)
        => WorkflowDefinition.HashText(JsonSerializer.Serialize(new { documentId, DefinitionHash, effectiveInput, schema }));
}
