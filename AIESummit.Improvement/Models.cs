using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIESummit.Improvement;

public static class ImprovementJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}

public sealed class Attempt
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Analyzing the current workflow";
    public string Status { get; set; } = "planning";
    public string Instructions { get; set; } = "";
    public string BaseCommit { get; set; } = "";
    public string SourceTree { get; set; } = "";
    public string ParentGraphHash { get; set; } = "";
    public string ManifestHash { get; set; } = "";
    public string? ParentAttemptId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public JsonNode? Proposal { get; set; }
    public string? ProposalHash { get; set; }
    public string? ApprovedProposalHash { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? Error { get; set; }
    public string? Worktree { get; set; }
    public string? CandidateCommit { get; set; }
    public string? RunId { get; set; }
    public string Model { get; set; } = "muse-wonder-1.0";
    public JsonNode? Graph { get; set; }
    public JsonNode? ParentGraph { get; set; }
    public JsonNode? Results { get; set; }
    public JsonNode? BaselineResults { get; set; }
    public JsonNode? Comparison { get; set; }
    public List<string> Logs { get; set; } = [];
    public string CalculateApprovalHash() => ImprovementJson.Hash(Proposal!.ToJsonString() + "|" + BaseCommit + "|" + ParentGraphHash + "|" + ManifestHash);
}

public sealed record CreateAttempt(string Instructions, string? ParentAttemptId = null);
public sealed record ApproveAttempt(string ProposalHash);

public sealed class WorkbenchOptions(string root)
{
    public string Root { get; } = Path.GetFullPath(root);
    public string State => Path.Combine(Root, "artifacts", "improvement");
    public string EnvFile => Path.Combine(Root, ".env");
    public string Manifest => Path.Combine(Root, "artifacts", "extraction-evaluation", "manifest-final.json");
    public string Fixtures => Path.Combine(Root, "artifacts", "extraction-evaluation", "final");
    public string Workflow => Path.Combine(Root, "improvement", "workflow.json");
    public string AttemptFolder(string id) => Path.Combine(State, "attempts", id);
}
