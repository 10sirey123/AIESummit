using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using AIESummit.Evaluation;

namespace AIESummit.Improvement;

public sealed class ImprovementService(WorkbenchOptions options, AttemptStore store, ProcessRunner processes) : BackgroundService
{
    private readonly Channel<(string Id, string Action)> jobs = Channel.CreateUnbounded<(string, string)>();
    private readonly SemaphoreSlim transitions = new(1);
    private readonly TaskCompletionSource initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private const string AllowedFile = "improvement/workflow.json";

    public async Task<Attempt> Create(CreateAttempt request)
    {
        await initialized.Task;
        var instructions = request.Instructions ?? "";
        if (instructions.Length > 12000) throw new InvalidDataException("Keep instructions under 12,000 characters.");
        var parent = request.ParentAttemptId == null ? null : await store.Get(request.ParentAttemptId);
        if (parent != null && parent.Status is not ("evaluated" or "promoted")) throw new InvalidDataException("Choose a completed parent attempt.");
        var item = new Attempt { Instructions = instructions.Trim(), ParentAttemptId = parent?.Id };
        await store.Save(item); jobs.Writer.TryWrite((item.Id, "plan")); return item;
    }

    public async Task<Attempt> Approve(string id, string hash)
    {
        await initialized.Task;
        await transitions.WaitAsync();
        try
        {
            var item = await store.Get(id);
            ValidateApproval(item, hash);
            item.ApprovedProposalHash = hash; item.ApprovedAt = DateTime.UtcNow; item.Status = "approved";
            await store.Save(item); jobs.Writer.TryWrite((id, "implement")); return item;
        }
        finally { transitions.Release(); }
    }

    public static void ValidateApproval(Attempt item, string hash)
    {
        if (item.Status != "awaiting_approval" || item.ProposalHash != hash || item.Proposal == null
            || item.CalculateApprovalHash() != hash)
            throw new InvalidDataException("This proposal has changed or is no longer awaiting approval. Refresh and review it again.");
    }

    public async Task<Attempt> Reject(string id)
    {
        await initialized.Task;
        await transitions.WaitAsync();
        try
        {
            var item = await store.Get(id);
            if (item.Status is not ("awaiting_approval" or "evaluated" or "failed")) throw new InvalidDataException("Wait for the current operation to finish.");
            item.Status = "rejected"; await store.Save(item); return item;
        }
        finally { transitions.Release(); }
    }

    public async Task<Attempt> RetryApprovedImplementation(string id)
    {
        await initialized.Task; await transitions.WaitAsync();
        try
        {
            var item = await store.Get(id);
            if (item.Status is not ("failed" or "interrupted") || item.ApprovedProposalHash == null
                || item.ApprovedProposalHash != item.CalculateApprovalHash() || item.CandidateCommit != null || item.RunId != null)
                throw new InvalidDataException("Only an already approved implementation that stopped before a candidate commit can be resumed.");
            var priorLog = Path.Combine(options.AttemptFolder(id), "implement.log");
            if (File.Exists(priorLog)) File.Copy(priorLog, Path.Combine(options.AttemptFolder(id), "implement-stopped-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + ".log"));
            item.Error = null; item.Status = "approved";
            await Log(item, "Retrying the same approved proposal after an environment fix. No benchmark responses were cleared or retried.");
            jobs.Writer.TryWrite((id, "implement")); return item;
        }
        finally { transitions.Release(); }
    }

    public async Task<Attempt> Promote(string id)
    {
        await initialized.Task;
        await transitions.WaitAsync();
        try
        {
            var item = await store.Get(id);
            if (item.Status != "evaluated" || item.Worktree == null) throw new InvalidDataException("Evaluate this attempt before using it as the baseline.");
            ValidatePromotionBaseline(item, JsonNode.Parse(await File.ReadAllTextAsync(options.Workflow)),
                (await store.All()).ToDictionary(a => a.Id, StringComparer.Ordinal));
            var head = (await Git(item.Worktree, ["rev-parse", "HEAD"])).Output.Trim();
            if (head != item.CandidateCommit || (await Git(item.Worktree, ["status", "--porcelain"])).Output.Trim().Length != 0)
                throw new InvalidDataException("The evaluated worktree changed. Promotion requires its unchanged evaluated commit.");
            var names = await Git(item.Worktree, ["diff", "--name-only", item.BaseCommit, item.CandidateCommit!]);
            ValidateScope(names.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            var candidateGraph = (await Git(item.Worktree, ["show", item.CandidateCommit + ":" + AllowedFile])).Output;
            ValidateEvaluatedGraph(item, candidateGraph);
            await File.WriteAllTextAsync(options.Workflow + ".new", candidateGraph);
            File.Move(options.Workflow + ".new", options.Workflow, true);
            var baselinePath = Path.Combine(options.State, "baseline.json");
            var chartBaselinePath = Path.Combine(options.State, "chart-baseline.json");
            if (!File.Exists(chartBaselinePath) && File.Exists(baselinePath)) File.Copy(baselinePath, chartBaselinePath);
            await File.WriteAllTextAsync(Path.Combine(options.State, "baseline.json"), JsonSerializer.Serialize(new {
                runId = item.RunId, workflowHash = item.Results?["workflowHash"], summary = item.Results }, ImprovementJson.Options));
            item.Status = "promoted"; await Log(item, "Promoted the candidate graph to the working baseline. Application step configuration and database history were preserved.");
            return item;
        }
        finally { transitions.Release(); }
    }

    public static void ValidateScope(IEnumerable<string> paths)
    {
        if (paths.Any(p => p.Trim().Replace('\\', '/') != AllowedFile))
            throw new InvalidDataException("Candidate changed files outside improvement/workflow.json. The evaluation was blocked.");
    }

    public static void ValidatePromotionBaseline(Attempt item, JsonNode? baseline, IReadOnlyDictionary<string, Attempt> history)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = item;
        while (visited.Add(current.Id))
        {
            if (current.ParentGraph != null && JsonNode.DeepEquals(baseline, current.ParentGraph)) return;
            if (current.ParentAttemptId == null || !history.TryGetValue(current.ParentAttemptId, out var parent)
                || current.BaseCommit != parent.CandidateCommit || parent.Graph == null
                || !JsonNode.DeepEquals(current.ParentGraph, parent.Graph)) break;
            current = parent;
        }
        throw new InvalidDataException("The baseline is outside this attempt's verified ancestry. Compare against the current baseline before promotion.");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Directory.CreateDirectory(options.State);
        try { await store.ReplayJournal(); } catch { /* Keep the durable journal; history availability is reported by the UI. */ }
        foreach (var stored in await store.All())
        {
            var item = await store.Get(stored.Id);
            var changed = stored.PlanningUsage == null && item.PlanningUsage != null || stored.ImplementationUsage == null && item.ImplementationUsage != null;
            if (item.Status is "planning" or "approved" or "implementing" or "evaluating")
            { item.Status = "interrupted"; item.Error = "The workbench restarted. Retained files and database records were preserved; start a new attempt to continue."; changed = true; }
            if (changed) await store.Save(item);
        }
        initialized.TrySetResult();
        await foreach (var job in jobs.Reader.ReadAllAsync(stoppingToken))
        {
            var item = await store.Get(job.Id);
            try
            {
                // OS lease also prevents another local workbench from evaluating concurrently.
                await using var lease = new FileStream(Path.Combine(options.State, "execution.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                if (job.Action == "plan") await Plan(item, stoppingToken); else await Implement(item, stoppingToken);
            }
            catch (Exception error)
            {
                item.Status = stoppingToken.IsCancellationRequested ? "interrupted" : "failed";
                item.Error = error is InvalidDataException ? error.Message : $"Operation stopped ({error.GetType().Name}). Inspect the retained execution log; existing data was preserved.";
                await Log(item, item.Error);
            }
        }
    }

    private async Task Plan(Attempt item, CancellationToken token)
    {
        await Log(item, "Freezing source code, workflow, dataset and prior-attempt context.");
        string graphText;
        await transitions.WaitAsync(token);
        try
        {
            var parent = item.ParentAttemptId == null ? null : await store.Get(item.ParentAttemptId);
            graphText = parent?.Worktree == null ? await File.ReadAllTextAsync(options.Workflow)
                : (await Git(parent.Worktree, ["show", parent.CandidateCommit + ":" + AllowedFile])).Output;
            item.ParentGraphHash = ImprovementJson.Hash(graphText); item.ParentGraph = JsonNode.Parse(graphText);
            var baselineFile = Path.Combine(options.State, "baseline.json");
            item.BaselineResults = parent?.Results?.DeepClone() ?? (File.Exists(baselineFile) ? JsonNode.Parse(await File.ReadAllTextAsync(baselineFile))?["summary"]?.DeepClone() : null);
            var snapshot = await Snapshot(item.Id);
            item.BaseCommit = parent?.CandidateCommit ?? snapshot.Commit; item.SourceTree = snapshot.Tree;
            var committedGraph = (await Git(options.Root, ["show", item.BaseCommit + ":" + AllowedFile])).Output;
            if (!JsonNode.DeepEquals(JsonNode.Parse(committedGraph), item.ParentGraph)) throw new InvalidDataException("The workflow changed while freezing the proposal. Start a fresh attempt.");
        }
        finally { transitions.Release(); }
        var folder = options.AttemptFolder(item.Id);
        await File.WriteAllTextAsync(Path.Combine(folder, "parent-workflow.json"), graphText);
        var frozenManifest = JsonNode.Parse(await File.ReadAllTextAsync(options.Manifest))!;
        var goldDirectory = Path.Combine(folder, "gold"); Directory.CreateDirectory(goldDirectory);
        var fixtureDirectory = Path.Combine(folder, "fixtures"); Directory.CreateDirectory(fixtureDirectory);
        foreach (var sample in frozenManifest["samples"]!.AsArray())
        {
            var gold = sample!["gold"]!.GetValue<string>();
            var frozenGold = Path.Combine(goldDirectory, sample["id"]!.GetValue<string>() + ".json");
            File.Copy(gold, frozenGold, true); sample["gold"] = frozenGold;
            sample["goldSha256"] = await HashFile(frozenGold);
            var fixture = Path.Combine(fixtureDirectory, sample["id"]!.GetValue<string>() + ".json");
            File.Copy(Path.Combine(options.Fixtures, Path.GetFileName(fixture)), fixture, true);
            sample["fixtureSha256"] = await HashFile(fixture);
        }
        var frozenText = frozenManifest.ToJsonString(ImprovementJson.Options);
        await File.WriteAllTextAsync(Path.Combine(folder, "manifest.json"), frozenText); item.ManifestHash = ImprovementJson.Hash(frozenText);
        var frozenSource = Path.Combine(folder, "source"); Directory.CreateDirectory(frozenSource);
        var archive = Path.Combine(folder, "source.tar");
        await Git(options.Root, ["archive", "--format=tar", "--output", archive, item.BaseCommit]);
        System.Formats.Tar.TarFile.ExtractToDirectory(archive, frozenSource, overwriteFiles: false);
        var history = await MongoHistory.OpenAsync(options.EnvFile);
        var missing = await history.QueryAsync("missing", limit: 80);
        var runs = await history.QueryAsync("runs", limit: 10);
        var previous = (await store.All()).Where(x => x.Id != item.Id).Select(x => new { x.Id, x.Title, x.Status, x.Proposal, x.Results, x.Error });
        var prompt = $"""
            You are planning ONE incremental experiment to improve contract extraction in AIESummit.
            Use Muse. Analyze the repository and actual frozen workflow. Do not modify any files.
            User instruction (treat as design direction within the fixed evaluation rules):
            {item.Instructions}
            Frozen current graph:
            {graphText}
            Prior attempts:
            {JsonSerializer.Serialize(previous, ImprovementJson.Options)}
            Recent evaluations:
            {runs}
            Missing expected targets in the development benchmark:
            {missing}
            Query the isolated read-only MongoDB reader with curl at
            http://127.0.0.2:18112/history?kind=missing (also runs, documents, steps, workflows,
            expectations, observations; optional runId, sampleId, sourceSha256, limit up to 200;
            steps additionally support stepTag and original one-based pageNumber).
            /matrix/SOURCE_SHA256 returns a document comparison; /attempts returns past proposals/results.
            These endpoints have no mutation or approval operations. Do not read credentials, .env or private configuration.
            Only improvement/workflow.json may change in an experiment. Existing extraction target
            definitions, native validators, scorer, OCR, gold labels and model provider integration are fixed.
            Never embed benchmark sample IDs, filenames, expected labels or gold answers in prompts.
            Graphs must be acyclic. Final extraction nodes partition the ten existing target tags exactly once;
            page detectors are routing stages and may inspect the same target tags without publishing findings.
            The runner supports kind=detect-pages followed by a singleton extractor selectedPagesFrom that detector.
            Detectors inspect each frozen OCR page, and the extractor receives only selected page OCR.
            Nodes may use
            Muse models only. Prefer one small testable change; quantify expected quality/token tradeoffs.
            A found extraction means accepted evidence, not just a raw model prediction. Preserve unknowns.
            Return a concrete proposal in the required JSON schema. Do not execute inference or implement.
            """;
        var proposal = await Codex(item, prompt, frozenSource, "plan", readOnly: true, token);
        item.Proposal = JsonNode.Parse(proposal); item.Title = item.Proposal?["title"]?.GetValue<string>() ?? "Workflow improvement";
        item.ProposalHash = item.CalculateApprovalHash(); item.Status = "awaiting_approval";
        await Log(item, "Proposal ready. Implementation will begin only after approval of this exact proposal.");
    }

    private async Task Implement(Attempt item, CancellationToken token)
    {
        if (item.ApprovedProposalHash != item.ProposalHash) throw new InvalidDataException("Proposal approval is missing.");
        if (item.CalculateApprovalHash() != item.ApprovedProposalHash) throw new InvalidDataException("Frozen proposal inputs no longer match the approval.");
        if (ImprovementJson.Hash(await File.ReadAllTextAsync(Path.Combine(options.AttemptFolder(item.Id), "manifest.json"))) != item.ManifestHash)
            throw new InvalidDataException("The frozen benchmark manifest changed after proposal approval.");
        await VerifyFrozenArtifacts(options.AttemptFolder(item.Id));
        item.Status = "implementing";
        item.Worktree = Path.Combine(options.State, "worktrees", item.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(item.Worktree)!);
        if (!Directory.Exists(item.Worktree))
            await Git(options.Root, ["worktree", "add", "-b", "codex/improvement/" + item.Id[..12], item.Worktree, item.BaseCommit]);
        else
        {
            if ((await Git(item.Worktree, ["rev-parse", "HEAD"])).Output.Trim() != item.BaseCommit)
                throw new InvalidDataException("The retained worktree no longer matches the approved baseline commit.");
            var retainedChanges = await Git(item.Worktree, ["diff", "--name-only", item.BaseCommit]);
            var retainedFiles = await Git(item.Worktree, ["ls-files", "--others", "--exclude-standard"]);
            ValidateScope((retainedChanges.Output + "\n" + retainedFiles.Output).Split('\n', StringSplitOptions.RemoveEmptyEntries));
            var retainedGraph = Path.Combine(item.Worktree, AllowedFile);
            if (File.Exists(retainedGraph)) File.Copy(retainedGraph, Path.Combine(options.AttemptFolder(item.Id), "workflow-before-resume.json"), true);
        }
        // Explicit parent graph lets independent tabs start from a retained evaluated attempt.
        var graphPath = Path.Combine(item.Worktree, AllowedFile);
        Directory.CreateDirectory(Path.GetDirectoryName(graphPath)!);
        await File.WriteAllTextAsync(graphPath, item.ParentGraph!.ToJsonString(ImprovementJson.Options));
        await Log(item, "Created an isolated worktree. Implementing the approved step-graph change.");
        await Codex(item, $"""
            Implement exactly this approved AIESummit extraction experiment:
            {item.Proposal}
            Change ONLY improvement/workflow.json. Do not modify any other file, commit, install dependencies,
            contact models, run provider evaluation, alter databases, validators, scorer or gold labels.
            Read AIESummit.WorkflowRunner source for the graph contract. All model names must be Muse.
            Preserve final extraction coverage of all ten targets and the DAG; detector targets may overlap.
            Keep unchanged nodes byte-semantically identical
            for cache reuse. User instruction: {item.Instructions}
            Do not hardcode benchmark cases, filenames, expected labels or gold answers into the workflow.
            Use curl with the isolated read-only MongoDB reader at
            http://127.0.0.2:18112/history?kind=missing
            (also runs/documents/steps/workflows/expectations/observations with runId/sampleId/sourceSha256/limit).
            Never read .env or secrets. Summarize the implemented change in your final response.
            """, item.Worktree, "implement", readOnly: false, token);
        var changed = await Git(item.Worktree, ["diff", "--name-only", item.BaseCommit]);
        var untracked = await Git(item.Worktree, ["ls-files", "--others", "--exclude-standard"]);
        ValidateScope((changed.Output + "\n" + untracked.Output).Split('\n', StringSplitOptions.RemoveEmptyEntries));
        var text = await File.ReadAllTextAsync(graphPath);
        if (JsonNode.DeepEquals(JsonNode.Parse(text), item.ParentGraph)) throw new InvalidDataException("The implementation did not change the workflow.");
        item.Graph = JsonNode.Parse(text);
        await Git(item.Worktree, ["add", "--", AllowedFile]);
        await Git(item.Worktree, ["-c", "user.name=AIESummit Improvement", "-c", "user.email=improvement@localhost", "commit", "-m", item.Title]);
        item.CandidateCommit = (await Git(item.Worktree, ["rev-parse", "HEAD"])).Output.Trim();
        item.Status = "evaluating"; await Log(item, "Building the native workflow runner, then evaluating frozen benchmark fixtures.");
        var publish = Path.Combine(options.AttemptFolder(item.Id), "publish");
        var built = await processes.Run("dotnet", ["publish", "AIESummit.WorkflowRunner/AIESummit.WorkflowRunner.csproj", "-c", "Release", "--nologo", "-o", publish], item.Worktree, cancellationToken: token);
        await WriteLog(item, "build", built.Output);
        if (built.ExitCode != 0) throw new InvalidDataException("Candidate build failed. See the retained build log.");
        File.Copy(graphPath, Path.Combine(publish, "workflow.json"), true);
        var resultFolder = Path.Combine(options.AttemptFolder(item.Id), "results"); Directory.CreateDirectory(resultFolder);
        await VerifyFrozenArtifacts(options.AttemptFolder(item.Id));
        var result = await RunNative(publish, resultFolder, ["run", "--workflow", "/app/workflow.json", "--manifest", "/results/manifest.json", "--fixtures", "/results/fixtures", "--output", "/results", "--env-file", "/repo/.env", "--attempt-id", item.Id], item, token);
        var identity = Path.Combine(resultFolder, "run-identity.json");
        if (File.Exists(identity)) item.RunId = JsonNode.Parse(await File.ReadAllTextAsync(identity))?["runId"]?.GetValue<string>();
        var summary = Path.Combine(resultFolder, "summary.json");
        if (File.Exists(summary)) item.Results = JsonNode.Parse(await File.ReadAllTextAsync(summary));
        if (result.ExitCode != 0 && !File.Exists(summary)) throw new InvalidDataException("Evaluation stopped before producing a report. Retained work is available in the attempt log.");
        item.Comparison = Comparison.Compare(item.BaselineResults, item.Results); item.Status = "evaluated";
        await Log(item, "Evaluation complete. Review accepted detections, false positives and logical token usage before promotion.");
    }

    public async Task<ProcessResult> RunNative(string publish, string output, string[] command, Attempt? item, CancellationToken token)
    {
        Directory.CreateDirectory(output);
        if (item != null)
        {
            File.Copy(Path.Combine(options.AttemptFolder(item.Id), "manifest.json"), Path.Combine(output, "manifest.json"), true);
            Directory.CreateDirectory(Path.Combine(output, "fixtures"));
            foreach (var file in Directory.EnumerateFiles(Path.Combine(options.AttemptFolder(item.Id), "fixtures"), "*.json"))
                File.Copy(file, Path.Combine(output, "fixtures", Path.GetFileName(file)), true);
        }
        var connection = await processes.Run("docker", ["compose", "exec", "-T", "api", "printenv", "ConnectionStrings__AIESummitDb"], options.Root);
        if (connection.ExitCode != 0) throw new InvalidDataException("Start the local application stack before running an experiment.");
        var args = new List<string> { "run", "--rm", "--network", "aiesummit_default", "--mount", $"type=bind,source={options.Root},target=/repo,readonly",
            "--mount", $"type=bind,source={publish},target=/app,readonly", "--mount", $"type=bind,source={output},target=/results",
            "-e", "ConnectionStrings__AIESummitDb", "mcr.microsoft.com/dotnet/aspnet:10.0", "dotnet", "/app/AIESummit.WorkflowRunner.dll" };
        args.AddRange(command);
        var result = await processes.Run("docker", args, options.Root, environment: new() { ["ConnectionStrings__AIESummitDb"] = connection.Output.Trim() },
            onLine: line => { if (item != null) { lock (item.Logs) File.AppendAllText(Path.Combine(options.AttemptFolder(item.Id), "live.log"), Sanitize(line) + "\n"); } }, timeoutSeconds: 7200, cancellationToken: token);
        if (item != null) await WriteLog(item, "evaluation", result.Output);
        return result;
    }

    private async Task<string> Codex(Attempt item, string prompt, string cwd, string phase, bool readOnly, CancellationToken token)
    {
        var settings = ReadEnvironment(options.EnvFile);
        var key = Environment.GetEnvironmentVariable("META_API_KEY") ?? settings.GetValueOrDefault("META_API_KEY");
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidDataException("Configure META_API_KEY locally to enable Muse planning and implementation.");
        var script = Environment.GetEnvironmentVariable("AIESUMMIT_CODEX_JS") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "@openai", "codex", "bin", "codex.js");
        var final = Path.Combine(options.AttemptFolder(item.Id), phase + "-response.json");
        var useWsl = OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("AIESUMMIT_CODEX_HOST") != "windows";
        string RuntimePath(string path) => useWsl ? ToWslPath(path) : path;
        // One inline TOML value preserves quoted filesystem keys; dotted CLI overrides split path keys.
        var workspacePermissions = readOnly ? "{\".\"=\"read\"}" : "{\".\"=\"read\",\"improvement\"=\"write\"}";
        var permissions = "permissions={improvement={extends=\":read-only\",filesystem={\":root\"=\"deny\",\":minimal\"=\"read\",\":workspace_roots\"=" + workspacePermissions + "},network={enabled=true,domains={\"127.0.0.2\"=\"allow\"}}}}";
        var args = new List<string> { script, "exec", "--json", "--ignore-user-config", "--disable", "apps", "--disable", "plugins", "--disable", "multi_agent", "-m", item.Model, "-C", RuntimePath(cwd),
            "-c", "approval_policy=\"never\"", "-c", "web_search=\"disabled\"",
            "-c", "shell_environment_policy={inherit=\"core\",ignore_default_excludes=false,filters={\"MONGODB_*\"=\"exclude\",\"ConnectionStrings*\"=\"exclude\"}}",
            "-c", "default_permissions=\"improvement\"", "-c", permissions, "-c", "features.network_proxy=true",
            "-c", "model_provider=\"meta\"", "-c", "model_providers.meta.name=\"Meta\"", "-c", "model_providers.meta.base_url=\"https://api.meta.ai/v1\"",
            "-c", "model_providers.meta.env_key=\"MODEL_API_KEY\"", "-c", "model_providers.meta.wire_api=\"responses\"", "-o", RuntimePath(final) };
        if (readOnly)
        {
            var schema = Path.Combine(options.AttemptFolder(item.Id), "proposal-schema.json");
            await File.WriteAllTextAsync(schema, """{"type":"object","additionalProperties":false,"properties":{"title":{"type":"string"},"summary":{"type":"string"},"hypothesis":{"type":"string"},"changes":{"type":"array","items":{"type":"string"}},"expectedBenefit":{"type":"string"},"risks":{"type":"array","items":{"type":"string"}}},"required":["title","summary","hypothesis","changes","expectedBenefit","risks"]}""");
            args.AddRange(["--output-schema", RuntimePath(schema)]);
        }
        args.Add("-");
        var live = Path.Combine(options.AttemptFolder(item.Id), "live.log");
        var executable = "node";
        var environment = new Dictionary<string, string> { ["MODEL_API_KEY"] = key };
        if (useWsl)
        {
            executable = "wsl.exe";
            args = ["-d", Environment.GetEnvironmentVariable("AIESUMMIT_CODEX_DISTRIBUTION") ?? "Ubuntu-Codex", "--cd", RuntimePath(cwd), "--exec", "/usr/bin/codex", ..args.Skip(1)];
            environment["WSLENV"] = (Environment.GetEnvironmentVariable("WSLENV") is { Length: > 0 } existing ? existing + ":" : "") + "MODEL_API_KEY/u";
        }
        if (!useWsl) throw new InvalidDataException("This workbench currently requires the configured WSL Codex environment for isolated source and history access.");
        await using var historyReader = await HistoryBridgeLease.Start(processes, options, token);
        var result = await processes.Run(executable, args, cwd, prompt, environment,
            onLine: line => { lock (item.Logs) File.AppendAllText(live, Sanitize(line) + "\n"); }, timeoutSeconds: 1200, cancellationToken: token);
        await WriteLog(item, phase, result.Output);
        var retained = await store.Get(item.Id);
        item.PlanningUsage = retained.PlanningUsage; item.ImplementationUsage = retained.ImplementationUsage;
        if (result.ExitCode != 0 || !File.Exists(final)) throw new InvalidDataException($"Muse {phase} session failed. See the retained {phase} log; no fallback model was used.");
        if (result.Output.Contains("rejected: blocked by policy", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Muse {phase} could not access the approved source environment. Its output was retained but will not be executed.");
        return await File.ReadAllTextAsync(final);
    }

    private async Task<(string Commit, string Tree)> Snapshot(string id)
    {
        var index = Path.Combine(options.AttemptFolder(id), "snapshot-index");
        var env = new Dictionary<string, string> { ["GIT_INDEX_FILE"] = index };
        async Task<ProcessResult> Run(params string[] args)
        {
            var result = await processes.Run("git", args, options.Root, environment: env);
            if (result.ExitCode != 0) throw new InvalidDataException("Could not freeze the repository snapshot."); return result;
        }
        await Run("read-tree", "HEAD"); await Run("add", "-A", "--", ".");
        var tree = (await Run("write-tree")).Output.Trim();
        var head = (await Run("rev-parse", "HEAD")).Output.Trim();
        var commit = await processes.Run("git", ["-c", "user.name=AIESummit Improvement", "-c", "user.email=improvement@localhost", "commit-tree", tree, "-p", head], options.Root,
            input: "Freeze improvement baseline " + id, environment: env);
        if (commit.ExitCode != 0) throw new InvalidDataException("Could not freeze the baseline commit.");
        await Run("update-ref", "refs/improvement/snapshots/" + id, commit.Output.Trim());
        File.Delete(index); return (commit.Output.Trim(), tree);
    }
    private async Task<ProcessResult> Git(string cwd, string[] args)
    {
        var result = await processes.Run("git", args, cwd);
        if (result.ExitCode != 0) throw new InvalidDataException("Git could not complete the isolated worktree operation. Existing changes were preserved.");
        return result;
    }
    private async Task Log(Attempt item, string message)
    {
        lock (item.Logs) item.Logs.Add(DateTime.UtcNow.ToString("HH:mm:ss") + " " + message);
        await store.Save(item);
    }
    private Task WriteLog(Attempt item, string phase, string text) => File.WriteAllTextAsync(Path.Combine(options.AttemptFolder(item.Id), phase + ".log"), Sanitize(text));
    private string Sanitize(string text)
    {
        foreach (var value in ReadEnvironment(options.EnvFile).Where(x => x.Key.Contains("KEY") || x.Key.Contains("TOKEN") || x.Key.Contains("URI") || x.Key.Contains("PASSWORD")).Select(x => x.Value).Where(x => x.Length > 8))
            text = text.Replace(value, "[redacted]", StringComparison.Ordinal);
        return text;
    }
    public static Dictionary<string, string> ReadEnvironment(string file)
    {
        var data = new Dictionary<string, string>();
        if (File.Exists(file)) foreach (var line in File.ReadLines(file))
        {
            var s = line.Trim(); var at = s.IndexOf('=');
            if (s.StartsWith('#') || at <= 0) continue;
            data[s[..at].Trim()] = s[(at + 1)..].Trim().Trim('"', '\'');
        }
        return data;
    }
    public static void ValidateEvaluatedGraph(Attempt item, string graph)
    {
        if (item.Graph == null || !JsonNode.DeepEquals(item.Graph, JsonNode.Parse(graph)))
            throw new InvalidDataException("The candidate graph no longer matches the evaluated graph.");
    }
    private static async Task<string> HashFile(string file) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(file))).ToLowerInvariant();
    public static async Task VerifyFrozenArtifacts(string directory)
    {
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "manifest.json")))!;
        foreach (var sample in manifest["samples"]!.AsArray())
        {
            var id = sample!["id"]!.GetValue<string>();
            if (await HashFile(Path.Combine(directory, "gold", id + ".json")) != sample["goldSha256"]!.GetValue<string>()
                || await HashFile(Path.Combine(directory, "fixtures", id + ".json")) != sample["fixtureSha256"]!.GetValue<string>())
                throw new InvalidDataException("Frozen benchmark labels or fixture artifacts changed after approval.");
        }
    }
    public static string ToWslPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (normalized.Length >= 3 && char.IsAsciiLetter(normalized[0]) && normalized[1] == ':' && normalized[2] == '/')
            return "/mnt/" + char.ToLowerInvariant(normalized[0]) + normalized[2..];
        throw new InvalidDataException("The WSL Codex host requires a local drive path.");
    }
}

internal static class FileOperations
{
    public static Task CopyAsyncCompat(string from, string to) { File.Copy(from, to, true); return Task.CompletedTask; }
}
