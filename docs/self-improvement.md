# Extraction improvement workbench

The local `AIESummit.Improvement` .NET 10 web application proposes, approves, implements and evaluates changes to the extraction LLMStep graph. It uses Codex CLI with Muse, the current native BlueShift prompt worker, existing PostgreSQL data/cache, `BenchmarkEvaluator`, and MongoDB instrumentation. No application database or volume is recreated.

## Start

Start the ordinary stack first (`./start.ps1`), prepare the existing benchmark corpus, and retain the baseline manifest/results under `artifacts/extraction-evaluation`. Then run:

```powershell
./scripts/start-improvement.ps1
```

Open <http://127.0.0.1:18110>. The workbench is a loopback-only operator surface; it does not accept remote access. It reads the existing local `MONGODB_URI`/`MONGODB_DATABASE`; Codex receives the local `META_API_KEY` only through its process environment. The supported setup runs the web UI on Windows and Node.js/Codex CLI plus a .NET 10 read-only history reader inside the existing `Ubuntu-Codex` WSL2 distribution. Set `AIESUMMIT_CODEX_DISTRIBUTION` for another prepared distribution, or `AIESUMMIT_WSL_DOTNET` for a nonstandard Linux runtime path. The history reader uses invariant globalization so it also works in the supplied WSL reference environment without ICU. Unsupported isolation stops the attempt rather than running with unrestricted access. No fallback provider or fabricated proposal is used when Muse fails.

The script builds both projects and captures effective persisted extraction prompts/settings into `improvement/workflow.json` only when the file does not exist. The experimental baseline explicitly selects Muse; it does not change the normal application's configured model. `-ImportBaseline` enriches the original retained 20-file baseline with expected/observed snapshots without new inference. Historical GPT baseline totals and new Muse evaluations remain separate runs.

Run `./scripts/measure-improvement-baseline.ps1` to measure the current Muse graph across all 20 frozen documents and select its report as the workbench baseline. It uses live configured providers only on cache misses; `-CacheOnly` forbids provider requests. Every invocation writes to a fresh retained directory. Do not rebuild the shared runner output while another measurement is active.

## Attempts and approvals

Each attempt has a separate tab and optional initial instructions. Planning reads code, the actual graph, previous proposals/results, and development-benchmark misses. It freezes a Git source snapshot with a separate index, graph, dataset manifest and gold files under ignored `artifacts/improvement/attempts/<id>`. The working checkout and user's Git index are preserved.

The web UI shows the concrete hypothesis, changes, expected benefits and risks. Approval binds to the proposal and frozen source/graph/dataset hashes. Only then does a new `codex/improvement/*` worktree and fresh implementation session start. The implementation may change **only** `improvement/workflow.json`; a diff gate prevents changing scoring, validators, target definitions, source files or gold. The runner independently validates the graph before executing providers.

Follow-up attempts may branch from an evaluated candidate without promoting it. A later descendant can become the baseline directly when the current baseline is in its verified graph ancestry; each parent commit and graph must match. This keeps a weak intermediate result visible without making it the working baseline. An unrelated baseline change still blocks promotion.

The coding session can read its frozen checkout and write within `improvement/`, which the CLI requires for its filesystem isolation. The final diff still permits only `improvement/workflow.json`. Its network access is limited to the separate read-only history reader; the approval UI is inaccessible. An already-approved implementation interrupted before a candidate commit or benchmark run can be retried with the same approval hash. Previous logs and graph edits are backed up; evaluated responses are never reset or resampled by that recovery action.

The graph contract supports 1–20 native workflow nodes. Final `extract` nodes partition all ten targets exactly once; optional `detect-pages` nodes can classify one or several of those targets on each source page. Nodes choose configured Muse models and prompt/settings and form an acyclic dependency graph. Dependencies run in topological order; independent nodes execute sequentially. Document extraction and conditional page routing are implemented; arbitrary retrieval or auxiliary provider loops are not.

The runner uses a local native registry and disabled content-addressed step records. New analyses use the reserved `improvement/` request namespace and are committed terminal; ordinary queue workers cannot see their intermediate state. Public extraction endpoints reject that namespace and exclude experiments from latest results/history/metrics. The running API must include these isolation filters before experiments are used.

## Page-first detection and specialized extraction

An extraction node with no `kind` or selector retains the original full-document behavior and cache identity. To evolve one target at a time, add a `kind: "detect-pages"` node for the selected target, route a singleton final extraction node with `selectedPagesFrom: "<detector-key>"`, and keep the other nine targets in a separate ordinary document node. A detector may later combine several targets into one page scan without changing the specialized extractors' inputs.

| Node | Native prompt input | Output and publication |
| --- | --- | --- |
| `extract`, no selector | Frozen document pages, assigned definitions and declared prerequisite results | Existing validated extraction result contract |
| `detect-pages` | One original OCR page and one or several pinned target definitions | Exactly one present/absent/uncertain decision per target; no published findings |
| `extract` with `selectedPagesFrom` | Only a selected page's OCR, original page number and singleton target definition | Existing validated extraction result contract, independently per selected page |

The detector response is `decisions: [{tag, outcome, quote, occurrence}]`. A present decision requires evidence anchored to that page with the existing matcher. An uncertain decision can omit evidence and still routes to extraction, preventing uncertainty from silently becoming a negative. An absent decision skips extraction on that page. Detector decisions and source quotes remain retained in PostgreSQL native analysis/prompt history; only sanitized routing decisions, identities and metrics enter MongoDB execution graphs. Detectors never enter `prediction.analyses` or count as benchmark true positives.

When every page's detector execution succeeded and every decision for a target is absent, the runner derives an explicit, persisted `not_found_in_reviewed_text` result without a specialized provider call. Its provenance identifies every detector page analysis/prompt, and its final result is checked by the existing extraction validator. Missing/failed detector pages cannot justify absence. Valid findings on other selected pages remain published even when the source has a failed branch. Frozen failures such as a missing baseline split stay failures; the page pipeline does not invent source documents or change OCR/splitting.

The selector is an implicit dependency. A routed extractor must assign one target included by its detector; its explicit `dependsOn`, when supplied, may name only that detector. A detector has no prerequisites. Final extraction assignments still cover all ten targets exactly once across node families; detectors may overlap them. Optional `kind`/`selectedPagesFrom` fields are omitted when null, preserving existing baseline graph and native cache hashes.

Specialized extraction inputs exclude the full document, detector JSON, detector quotes and detector hashes. Their cache identity depends on the selected page's actual OCR/definition and extractor model/settings/schema. Revising or merging a detector can therefore reuse a specialized extraction if the same page is selected again. An extractor-only experiment reuses unchanged detector and other-target calls. No failed native response is reset for a favorable retry.

## Cache, graphs and metrics

Cache identity includes document identity, node definition, effective page/target/prerequisite input, schema and model/settings. The whole graph hash is excluded. Changing an independent branch preserves other nodes' reusable results; changing actual upstream input invalidates affected descendants. Both successful and failed terminal analyses are retained. Identical failures are not silently retried.

Every evaluation retains the graph definition and exact execution nodes/edges, source analyses/prompts, node/cache hashes, and reused/failed/skipped states. Page instances additionally retain `nodeKind`, scope, one-based `pageNumber`/`sourcePage`, sanitized routing decisions and explicit detector-to-extractor edges. Derived negatives and blocked/absent page skips are visible without inventing provider calls. MongoDB stores expected labels independently from observations. The document matrix compares the same versioned gold across runs and distinguishes accepted findings, rejected evidence, misses, false positives, correct absence, failures and unknowns. It reports target presence; it does not claim clause-by-clause matching of all gold spans.

Logical tokens include reused responses. Newly consumed counters exclude reused results. A native completion-policy extension preserves provider-reported input/output counters that the pinned worker otherwise omits. Historical cached counters stay unknown; the UI never substitutes zero. Multi-attempt usage is conservative where directional totals are incomplete. Scoring includes all selected documents, including failed ones; raw model classifications never count as accepted findings. Quality and efficiency comparisons use validated TP/FP/FN and logical input/output, not warm-cache cost. Promotion remains a human decision.

Page pipeline usage includes every detector and selected-page extraction call, including failures. Skipped and derived nodes consume no new provider tokens. Sanitized `workflow_node` JSON progress records report node kind, page, state, reuse and reported tokens after each page/native node, before the source document finishes. A page-routing proposal must be evaluated through the same frozen dataset and scorer; more detector positives alone are not an improvement.

Codex planning and implementation usage is retained separately in attempt records and the execution-log disclosure. These counters include the CLI's reported cached-input usage; they are not added to extraction benchmark usage or treated as invoice verification.

`Use as baseline` copies the evaluated graph into the working baseline after checking that its parent graph remains current. It updates the workbench's comparison baseline; it does not merge unrelated changes or alter the normal application's persisted step selection. Worktrees and failed attempts are retained.

## Read-only learning access

Codex sessions can query the loopback endpoint `/api/history?kind=missing` with optional `runId`, `sampleId`, `sourceSha256`, and `limit` (1–200). Supported views are runs, documents, steps, workflows, expectations, observations, missing and events. No arbitrary MongoDB commands, pipelines or writes are exposed.

Muse's Codex sessions use a dedicated read-only reader on WSL loopback `127.0.0.2:18112`, exposing only GET `/history`, `/matrix/{sha}` and `/attempts`. A per-session network proxy permits only that literal address, keeping the approval UI on `127.0.0.1:18110` outside the model's allowed network destinations. The reader loads local settings itself; the model never receives a MongoDB URI. Its lifetime is limited to the session and cleanup targets its verified process identity. Planning can read its frozen source archive; implementation can write only the candidate workflow file. User-wide app/plugin tools are excluded from these focused sessions.

There is also an optional stdio MCP server (`--mcp <repository-root>`) with `query_history`, `document_matrix` and `attempt_history`. Its flat strict schemas accept explicit nulls for unused filters. Direct protocol reads are verified; Muse/Codex currently misroutes namespaced MCP calls, so the workbench uses the isolated HTTP reader rather than relying on that integration.

The existing evaluator also offers:

```powershell
dotnet run --project AIESummit.Evaluation -- history --kind missing --limit 50
dotnet run --project AIESummit.Evaluation -- history --kind matrix --source DOCUMENT_SHA256
```

Confirmation-partition details are excluded from learning views. The historical 20-file baseline is drawn from CUAD's official test split and has already been inspected; it is engineering feedback, not an untouched generalization test. Establish a separate document-held-out confirmation protocol before claiming unseen-contract improvement. No new labels are inferred.

## MongoDB history report: what to expect

The report is assembled from the evaluator's `evaluation_*` collections and the workbench's `improvement_attempts` collection. Evaluation records describe what actually ran; an improvement-attempt record describes why a candidate was proposed, whether it was approved, and which evaluation run tested it. Join those records by `improvement_attempts.runId` → `evaluation_runs._id`. A planning-only or approval-rejected attempt has no evaluation run and must not be drawn as a measured chart point.

| Collection | Report role | Important fields |
| --- | --- | --- |
| `improvement_attempts` | Proposal and approval timeline | `_id`, `title`, `status`, `createdAt`, `updatedAt`, `approvedAt`, `parentAttemptId`, `proposal`, `comparison`, `runId`, `error` |
| `evaluation_runs` | One benchmark execution and its totals | `_id`/`runId`, `workflowId`, `status`, `startedAt`, `completedAt`, `documentsAttempted`, `documentsFailed`, `summary`, `configuration` |
| `evaluation_workflows` | Reusable LLMStep graph definition | `_id`/`workflowId`, `nodes`, `edges`, `targetCatalog`, `version`, `note` |
| `evaluation_documents` | One source PDF in one run | `_id`/`documentRunId`, `runId`, `sampleId`, `sourceSha256`, `expectationId`, `status`, `result`, `graph` |
| `evaluation_expectations` | Immutable gold snapshot for one exact source and label version | `_id`/`expectationId`, `sourceSha256`, `labelSha256`, `partition`, `expected_detections`, `targets` |
| `evaluation_observations` | Expected-versus-validated outcome for every target in a document/run | `_id`, `runId`, `documentRunId`, `expectationId`, `targets[]`, `status` |
| `evaluation_steps` | Actual instantiated graph nodes and prompt metrics | `_id`, `runId`, `documentRunId`, `stepTag`, `kind`, `parentNodeIds`, `state`, `model`, `prompt`, `targetDefinitions` |

Expectations are registered before inference. Thus a failed document still has its gold snapshot and can show every expected target. Snapshots are content-addressed by dataset, source PDF checksum, label checksum and partition; they are not rewritten when labels change. The observation links to that exact snapshot with `expectationId`. All supplied benchmark detections, including categories outside the implemented ten targets and their answer spans, remain in `expected_detections`; the mapped `targets` array contains the ten reportable target types. `present`, `absent` and `unknown` are distinct: unknown or unlabeled data is never a negative example. Confirmation-partition labels and results must not appear in a development report.

For an **attempt history**, sort measured runs by `startedAt`, then show the linked attempt title/status and the run's graph, benchmark size, quality scores and token metrics. Resolve the definition through `evaluation_runs.workflowId` → `evaluation_workflows._id`. For an exact instantiated graph, use the document's `graph.nodeIds` membership and `graph.edges`; retrieve those IDs from `evaluation_steps` and draw arrows in the documented prerequisite-to-dependent direction. Do not draw every step with the same `runId`: earlier exact-graph records may also contain inferred legacy nodes. The bounded `steps` history view already applies the exact membership filter. A graph definition shows the candidate structure; instantiated nodes show what was actually executed, reused, skipped or failed on each document.

For the **improvement chart**, use comparable Muse `workflow-run` evaluations over the same document set and benchmark version. The quality series is validated true positives (`summary.validatedFindings.micro.tp`), with false positives (`fp`) shown alongside it; `tp + fn` is the count of known positive document/target cases. Token series come from `summary.metrics`: `reportedTotalTokens`, `reportedInputTokens` and `reportedOutputTokens`. Logical totals include cached/reused responses; newly consumed tokens measure provider work and should be a separate efficiency view. Directional input/output totals may be unavailable for older prompts; display a gap or partial known subtotal, never zero. The workbench chart includes measured candidate evaluations (including regressions) so the trajectory is honest; promotion is shown separately as the selected champion. A proposal, failed planning session or run with no usable benchmark scores is not a data point. Do not combine diagnostic model-reported scores with validated findings.

For a **single-attempt coverage table**, select a `runId`, then join its `evaluation_observations` to `evaluation_documents` by `documentRunId` and to `evaluation_expectations` by `expectationId`. Pivot the observation `targets[]` so rows are documents (`sampleId`) and columns are target types (`tag`/`category`). Each cell comes from the matching target's `state`; use the expectation snapshot for its expected label. Green means expected-present and validated found (`found`). Red means expected-present but not found (`missed`). Keep `failed`, `rejected_evidence`, `not_run`, `unknown`, `false_positive` and `correct_absence` visually distinct from those two cases; in particular, a failed analysis is not a miss, and an unexpected finding is not a success. The current per-document matrix is the complementary pivot: target rows, with one column per chronological observation/attempt, making it easy to follow one document as attempts change. Its columns include stable run/workflow IDs and status. A report with previous/next controls should order only comparable measured runs and update the selected `runId`; the same gold snapshot is used across those columns when its `expectationId`/label checksum is unchanged.

Useful bounded read-only views are available through the CLI or `/api/history`: `runs`, `documents`, `workflows`, `steps`, `expectations`, `observations`, `missing`, `events` and `matrix`. Use `history --kind matrix --source <PDF_SHA256>` to compare one document across runs; if multiple gold versions exist, supply `--expectation <expectationId>`. The dashboard's `/api/documents/{id}/matrix` returns the same per-document comparison. For an attempt's run, `/api/runs/{runId}` returns its run, up to 200 documents and steps. These read paths are bounded and exclude confirmation records. Avoid an unfiltered join of all observations to all expectations: expectations have no `runId`, and the explicit `expectationId` link is the version-safe join.

## Recovery and verification

Attempt files/transcripts, checksummed result artifacts and telemetry outboxes are retained under `artifacts/improvement`. Attempt lifecycle snapshots are also written to MongoDB `improvement_attempts`. If a MongoDB attempt write fails, its local journal remains; execution records continue to use the evaluator's durable replayable outbox. Restart marks unfinished attempts interrupted rather than silently restarting model calls. Starting another attempt can reuse eligible terminal node results.

Run `dotnet build AIESummit.slnx --nologo`, `dotnet test AIESummit.WorkflowRunner.Tests`, `./scripts/test-extractions.ps1`, `node --check AIESummit.Improvement/wwwroot/app.js` and browser desktop/narrow checks. Native verification should include a real Muse execution and a fresh `--cache-only` run demonstrating identical source prompt identities and zero newly consumed tokens. Never delete the database to obtain a passing run.

## Extraction Observatory

The separate read-only dashboard starts with `./scripts/start-observatory.ps1` at http://127.0.0.1:18115 (API 18114). It reads MongoDB directly through the existing bounded history reader, with no provider calls or attempt mutations. It groups scored workflow runs by manifest hash and displays validated TP/FP/FN, logical and newly consumed tokens, and exact selected-document execution graphs. Unknown token measurements stay unknown. The displayed executed LLM node count is document-specific. Views are bounded to the latest 200 records; older or inferred graphs are explicitly unavailable. See `AIESummit.Observatory/AGENTS.md`.

The Observatory opens on a global overview with per-benchmark bar charts of validated detection totals across measured attempts. Click a bar to open the benchmark detail and that run; use All benchmarks to return. Totals sum TP across documents/targets within a run, never across repeated attempts or different manifests.

Attempt detail includes Extraction coverage, a document-by-target matrix joined to exact expectation snapshots. Expected/found cells are green; expected/missed or rejected cells are pink. Failures and unexpected findings have separate colors and labels. Click document identifiers to inspect their workflow graph.
