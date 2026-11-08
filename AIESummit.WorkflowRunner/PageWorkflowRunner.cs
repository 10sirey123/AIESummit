using System.Diagnostics;
using System.Text.Json;
using AIESummit.Domain;
using Microsoft.EntityFrameworkCore;

namespace AIESummit.WorkflowRunner;

/// <summary>Page routing around native BlueShift calls; no provider HTTP or alternate extraction validation.</summary>
internal static class PageWorkflowRunner
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<JsonElement> Evaluate(AIESummitDb db, IServiceProvider services, WorkflowNode[] nodes,
        WorkflowDefinition workflow, string documentRunId, int uploadId, Document[] documents, JsonElement fixture,
        bool cacheOnly, WorkflowRunner.SampleProgress progress, Stopwatch clock)
    {
        var finalAnalyses = new List<object>(); var graphNodes = new List<object>(); var edges = new List<object>();
        var usage = new WorkflowUsage(); var ready = true;
        progress.LastResult = Capture();
        foreach (var document in documents)
        {
            progress.Mark("snapshot_load", "fixture_snapshot_unavailable");
            var baselineId = fixture.GetProperty("prediction").GetProperty("analyses").EnumerateArray()
                .First(a => a.GetProperty("documentId").GetInt32() == document.ID).GetProperty("id").GetInt32();
            var baseline = await db.DocumentAnalyses.Include(a => a.Pages).Include(a => a.Targets).ThenInclude(t => t.ExtractionType)
                .SingleAsync(a => a.ID == baselineId && a.DocumentId == document.ID);
            var pages = baseline.Pages.OrderBy(p => p.Number).ToArray();
            if (pages.Length == 0) throw new InvalidDataException("The frozen page snapshots are unavailable.");
            var families = new Dictionary<string, FamilyResult>(StringComparer.Ordinal);
            foreach (var node in nodes)
            {
                using var definitionDoc = JsonDocument.Parse(baseline.TargetsJson);
                var definitions = definitionDoc.RootElement.EnumerateArray()
                    .Where(d => node.Targets.Contains(d.GetProperty("tag").GetString()!, StringComparer.Ordinal)).Select(d => d.Clone()).ToArray();
                if (definitions.Length != node.Targets.Count) throw new InvalidDataException("Frozen target definitions are incomplete.");
                var family = new FamilyResult(); families[node.Key] = family;
                if (node.NodeKind == "detect-pages")
                {
                    foreach (var page in pages)
                    {
                        var id = NodeId(document.ID, node.Key, page.Number);
                        Prepare(id, node, [], page.Number);
                        var input = PageRouting.PageInput(definitions, page);
                        var call = await NativeWorkflowExecutor.Execute(db, services, document, baseline, node, [page], definitions, input, cacheOnly, progress);
                        var decisions = node.Targets.Select(tag => new PageRoutingDecision(tag, page.Number,
                            call.Succeeded ? PageRouting.ReadOutcome(call.Analysis!, tag, page.Number) : "unknown",
                            call.Succeeded, id, call.Analysis?.ID, call.Prompt?.ID)).ToArray();
                        family.Pages.AddRange(decisions); family.InstanceIds.Add(id); family.Succeeded &= call.Succeeded;
                        var sanitized = decisions.Select(d => new { tag = d.TargetTag, outcome = d.Outcome }).ToArray();
                        Record(call, node, id, page.Number, [], definitions, sanitized);
                        ready &= call.Succeeded;
                    }
                }
                else if (node.SelectedPagesFrom != null)
                {
                    var detector = families[node.SelectedPagesFrom];
                    var decisions = detector.Pages.Where(d => d.TargetTag == node.Targets.Single()).OrderBy(d => d.PageNumber).ToArray();
                    if (decisions.Length != pages.Length) throw new InvalidDataException("Page routing did not cover the frozen source exactly once.");
                    var selected = decisions.Where(PageRouting.ShouldExtract).ToArray();
                    family.Succeeded = detector.Succeeded;
                    foreach (var decision in decisions)
                    {
                        var page = pages.Single(p => p.Number == decision.PageNumber);
                        var id = NodeId(document.ID, node.Key, page.Number);
                        var parents = new[] { decision.NodeId };
                        Prepare(id, node, parents, page.Number);
                        if (!PageRouting.ShouldExtract(decision))
                        {
                            var state = decision.Succeeded ? "skipped_not_present" : "blocked_detection_failed";
                            graphNodes.Add(new { id, key = node.Key, stepTag = WorkflowRunner.StepTag(node), kind = "llm", nodeKind = "extract",
                                scope = "page", pageNumber = page.Number, sourcePage = page.Number, state, parentNodeIds = parents,
                                definitionHash = node.DefinitionHash,
                                targetRouting = new { targets = node.Targets, selectedPagesFrom = node.SelectedPagesFrom, gate = decision.Outcome } });
                            AddEdges(id, parents); family.InstanceIds.Add(id);
                            if (!decision.Succeeded) { ready = false; family.Succeeded = false; }
                            Checkpoint(); Emit(node, document.ID, page.Number, state, null, null);
                            continue;
                        }
                        // Deliberately excludes every other page, the full detector JSON, detector hashes and quote text.
                        var input = PageRouting.PageInput(definitions, page);
                        var call = await NativeWorkflowExecutor.Execute(db, services, document, baseline, node, [page], definitions, input, cacheOnly, progress);
                        if (call.Analysis != null) {
                            var projection = WorkflowRunner.ProjectAnalysis(call.Analysis);
                            finalAnalyses.Add(projection); family.FinalAnalyses.Add(projection);
                        }
                        family.InstanceIds.Add(id); family.Succeeded &= call.Succeeded;
                        Record(call, node, id, page.Number, parents, definitions, new { tag = decision.TargetTag, outcome = decision.Outcome });
                        ready &= call.Succeeded;
                    }
                    if (selected.Length == 0 && PageRouting.CanAssertAbsent(decisions))
                    {
                        var derived = await NativeWorkflowExecutor.DeriveAbsent(db, document, baseline, node, definitions, decisions);
                        var projection = WorkflowRunner.ProjectAnalysis(derived.Analysis);
                        finalAnalyses.Add(projection); family.FinalAnalyses.Add(projection);
                        var id = NodeId(document.ID, node.Key, null) + "/all-absent";
                        var parents = decisions.Select(d => d.NodeId).Distinct().ToArray();
                        graphNodes.Add(new { id, key = node.Key, kind = "derived", nodeKind = "extract-aggregate", scope = "logical-document",
                            state = "not_found_derived", parentNodeIds = parents, sourceAnalysisId = derived.Analysis.ID,
                            analysisReused = derived.Reused, targetRouting = new { targets = node.Targets, selectedPagesFrom = node.SelectedPagesFrom, gate = "all_absent" } });
                        AddEdges(id, parents); family.InstanceIds.Add(id); Checkpoint();
                    }
                }
                else
                {
                    // This preserves the original full-document input shape and independent node cache identity.
                    var parents = node.DependsOn.SelectMany(p => families[p].InstanceIds).Distinct().ToArray();
                    var id = NodeId(document.ID, node.Key, null);
                    Prepare(id, node, parents);
                    if (node.DependsOn.Any(p => !families[p].Succeeded))
                    {
                        ready = false; family.Succeeded = false; family.InstanceIds.Add(id);
                        graphNodes.Add(new { id, key = node.Key, stepTag = WorkflowRunner.StepTag(node), kind = "llm", nodeKind = "extract",
                            scope = "logical-document", state = "skipped", parentNodeIds = parents, definitionHash = node.DefinitionHash });
                        AddEdges(id, parents); Checkpoint();
                        continue;
                    }
                    var input = JsonSerializer.Serialize(new { targets = definitions,
                        pages = pages.Select(p => new { page = p.Number, markdown = p.Markdown }),
                        prerequisites = node.DependsOn.Order(StringComparer.Ordinal).Select(p => new { key = p, result = families[p].Projection() }) });
                    var call = await NativeWorkflowExecutor.Execute(db, services, document, baseline, node, pages, definitions, input, cacheOnly, progress);
                    if (call.Analysis != null) {
                        var projection = WorkflowRunner.ProjectAnalysis(call.Analysis);
                        finalAnalyses.Add(projection); family.FinalAnalyses.Add(projection);
                    }
                    family.InstanceIds.Add(id); family.Succeeded = call.Succeeded;
                    Record(call, node, id, null, parents, definitions, null);
                    ready &= call.Succeeded;
                }
            }
        }
        return Capture();

        string NodeId(int documentId, string key, int? page) => $"{documentRunId}/{documentId}/{key}" + (page == null ? "" : $"/page/{page}");
        void Prepare(string id, WorkflowNode node, string[] parents, int? pageNumber = null)
        {
            progress.NodeId = id; progress.NodeKey = node.Key; progress.ParentNodeIds = parents; progress.Mark("node_input");
            progress.NodeKind = node.NodeKind; progress.Scope = pageNumber == null ? "logical-document" : "page";
            progress.PageNumber = pageNumber; progress.TargetTags = node.Targets.ToArray(); progress.StepTag = WorkflowRunner.StepTag(node);
        }
        void AddEdges(string id, string[] parents) { foreach (var parent in parents) edges.Add(new { from = parent, to = id, relation = "depends_on" }); }
        void Record(NativeWorkflowCall call, WorkflowNode node, string id, int? pageNumber, string[] parents, JsonElement[] definitions, object? decision)
        {
            var state = call.CacheMiss ? "cache_miss" : call.Succeeded ? "succeeded" : "failed";
            var metrics = usage.Add(call, node);
            var common = new Dictionary<string, object?> { ["id"] = id, ["key"] = node.Key, ["stepTag"] = WorkflowRunner.StepTag(node),
                ["kind"] = "llm", ["nodeKind"] = node.NodeKind, ["scope"] = pageNumber == null ? "logical-document" : "page",
                ["pageNumber"] = pageNumber, ["sourcePage"] = pageNumber, ["state"] = state, ["parentNodeIds"] = parents,
                ["definitionHash"] = node.DefinitionHash, ["model"] = node.Model, ["targetDefinitions"] = definitions,
                ["targetRouting"] = new { targets = node.Targets, selectedPagesFrom = node.SelectedPagesFrom }, ["decision"] = decision };
            if (call.Prompt != null) {
                common["cacheHit"] = call.CacheHit; common["sourcePromptId"] = call.Prompt.ID;
                common["sourceAnalysisId"] = call.Analysis!.ID; common["cacheKey"] = call.Analysis.RequestKey;
                common["prompt"] = metrics;
            }
            graphNodes.Add(common); AddEdges(id, parents); Checkpoint();
            Emit(node, documentId: int.Parse(id.Split('/')[1], System.Globalization.CultureInfo.InvariantCulture), pageNumber, state,
                call.Prompt == null ? null : call.CacheHit, call.Prompt?.Tokens);
        }
        void Checkpoint()
        {
            progress.LastResult = Capture(); progress.ProviderStarted = false; progress.NodeId = null;
        }
        JsonElement Capture() => JsonSerializer.SerializeToElement(new { status = ready ? "ready" : "failed", uploadId,
            totalSeconds = clock.Elapsed.TotalSeconds, prediction = new { analyses = finalAnalyses }, metrics = usage.Snapshot(),
            graph = new { workflowHash = workflow.Hash, nodes = graphNodes, edges } }, Json);
    }

    private static void Emit(WorkflowNode node, int documentId, int? pageNumber, string state, bool? cacheHit, int? tokens)
        => Console.WriteLine(JsonSerializer.Serialize(new { @event = "workflow_node", documentId, nodeKey = node.Key,
            nodeKind = node.NodeKind, pageNumber, state, cacheHit, reportedTokens = tokens }));

    private sealed class FamilyResult
    {
        public bool Succeeded { get; set; } = true;
        public List<string> InstanceIds { get; } = [];
        public List<PageRoutingDecision> Pages { get; } = [];
        public List<object> FinalAnalyses { get; } = [];
        public object Projection() => new { targets = FinalAnalyses.SelectMany(a => JsonSerializer.SerializeToElement(a, Json).GetProperty("targets").EnumerateArray()).ToArray() };
    }
}
