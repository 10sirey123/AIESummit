# Extraction improvement workbench

The local `AIESummit.Improvement` .NET 10 web application proposes, approves, implements and evaluates changes to the extraction LLMStep graph. It uses Codex CLI with Muse, the current native BlueShift prompt worker, existing PostgreSQL data/cache, `BenchmarkEvaluator`, and MongoDB instrumentation. No application database or volume is recreated.

## Start

Start the ordinary stack first (`./start.ps1`), prepare the existing benchmark corpus, and retain the baseline manifest/results under `artifacts/extraction-evaluation`. Then run:

```powershell
./scripts/start-improvement.ps1
```

Open <http://127.0.0.1:18110>. The workbench is a loopback-only operator surface; it does not accept remote access. It reads the existing local `MONGODB_URI`/`MONGODB_DATABASE`; Codex receives the local `META_API_KEY` only through its process environment. On Windows, Node.js and Codex CLI run inside the existing `Ubuntu-Codex` WSL2 distribution; set the process variable `AIESUMMIT_CODEX_DISTRIBUTION` for another prepared distribution. The Windows host runs the web UI and read-only history tools. `AIESUMMIT_CODEX_HOST=windows` selects a native CLI only on machines whose configured Windows isolation supports the required filesystem rules; `AIESUMMIT_CODEX_JS` can override that launcher path. Unsupported isolation stops the attempt rather than running with unrestricted access. No fallback provider or fabricated proposal is used when Muse fails.

The script builds both projects and captures effective persisted extraction prompts/settings into `improvement/workflow.json` only when the file does not exist. The experimental baseline explicitly selects Muse; it does not change the normal application's configured model. `-ImportBaseline` enriches the original retained 20-file baseline with expected/observed snapshots without new inference. Historical GPT baseline totals and new Muse evaluations remain separate runs.

Run `./scripts/measure-improvement-baseline.ps1` to measure the current Muse graph across all 20 frozen documents and select its report as the workbench baseline. It uses live configured providers only on cache misses; `-CacheOnly` forbids provider requests. Every invocation writes to a fresh retained directory. Do not rebuild the shared runner output while another measurement is active.

## Attempts and approvals

Each attempt has a separate tab and optional initial instructions. Planning reads code, the actual graph, previous proposals/results, and development-benchmark misses. It freezes a Git source snapshot with a separate index, graph, dataset manifest and gold files under ignored `artifacts/improvement/attempts/<id>`. The working checkout and user's Git index are preserved.

The web UI shows the concrete hypothesis, changes, expected benefits and risks. Approval binds to the proposal and frozen source/graph/dataset hashes. Only then does a new `codex/improvement/*` worktree and fresh implementation session start. The implementation may change **only** `improvement/workflow.json`; a diff gate prevents changing scoring, validators, target definitions, source files or gold. The runner independently validates the graph before executing providers.

The first graph contract supports 1–20 extraction nodes. Nodes partition all ten targets exactly once, choose configured Muse models, supply prompt/settings, and name acyclic dependencies. Every node receives immutable document pages, its target definitions and validated prerequisite outputs. Dependencies run in topological order; independent nodes execute sequentially in this first implementation. Auxiliary non-extraction/retrieval nodes and dynamic conditional routing are not implemented.

The runner uses a local native registry and disabled content-addressed step records. New analyses use the reserved `improvement/` request namespace and are committed terminal; ordinary queue workers cannot see their intermediate state. Public extraction endpoints reject that namespace and exclude experiments from latest results/history/metrics. The running API must include these isolation filters before experiments are used.

## Cache, graphs and metrics

Cache identity includes document identity, node definition, effective page/target/prerequisite input, schema and model/settings. The whole graph hash is excluded. Changing an independent branch preserves other nodes' reusable results; changing actual upstream input invalidates affected descendants. Both successful and failed terminal analyses are retained. Identical failures are not silently retried.

Every evaluation retains the graph definition and exact execution nodes/edges, source analyses/prompts, node/cache hashes, and reused/failed/skipped states. MongoDB stores expected labels independently from observations. The document matrix compares the same versioned gold across runs and distinguishes accepted findings, rejected evidence, misses, false positives, correct absence, failures and unknowns. It reports target presence; it does not claim clause-by-clause matching of all gold spans.

Logical tokens include reused responses. Newly consumed counters exclude reused results. A native completion-policy extension preserves provider-reported input/output counters that the pinned worker otherwise omits. Historical cached counters stay unknown; the UI never substitutes zero. Multi-attempt usage is conservative where directional totals are incomplete. Scoring includes all selected documents, including failed ones; raw model classifications never count as accepted findings. Quality and efficiency comparisons use validated TP/FP/FN and logical input/output, not warm-cache cost. Promotion remains a human decision.

`Use as baseline` copies the evaluated graph into the working baseline after checking that its parent graph remains current. It updates the workbench's comparison baseline; it does not merge unrelated changes or alter the normal application's persisted step selection. Worktrees and failed attempts are retained.

## Read-only learning access

Codex sessions can query the loopback endpoint `/api/history?kind=missing` with optional `runId`, `sampleId`, `sourceSha256`, and `limit` (1–200). Supported views are runs, documents, steps, workflows, expectations, observations, missing and events. No arbitrary MongoDB commands, pipelines or writes are exposed.

The workbench also exposes a stdio MCP server with `query_history`, `document_matrix` and `attempt_history`. Codex receives this connection explicitly per session; the tool host loads local settings itself, so the model never receives a MongoDB URI. Shell access to the network is disabled. Planning can read its frozen source archive; implementation can write only the candidate workflow file. User-wide app/plugin tools are excluded from these focused sessions.

The existing evaluator also offers:

```powershell
dotnet run --project AIESummit.Evaluation -- history --kind missing --limit 50
dotnet run --project AIESummit.Evaluation -- history --kind matrix --source DOCUMENT_SHA256
```

Confirmation-partition details are excluded from learning views. The historical 20-file baseline is drawn from CUAD's official test split and has already been inspected; it is engineering feedback, not an untouched generalization test. Establish a separate document-held-out confirmation protocol before claiming unseen-contract improvement. No new labels are inferred.

## Recovery and verification

Attempt files/transcripts, checksummed result artifacts and telemetry outboxes are retained under `artifacts/improvement`. Attempt lifecycle snapshots are also written to MongoDB `improvement_attempts`. If a MongoDB attempt write fails, its local journal remains; execution records continue to use the evaluator's durable replayable outbox. Restart marks unfinished attempts interrupted rather than silently restarting model calls. Starting another attempt can reuse eligible terminal node results.

Run `dotnet build AIESummit.slnx --nologo`, `dotnet test AIESummit.WorkflowRunner.Tests`, `./scripts/test-extractions.ps1`, `node --check AIESummit.Improvement/wwwroot/app.js` and browser desktop/narrow checks. Native verification should include a real Muse execution and a fresh `--cache-only` run demonstrating identical source prompt identities and zero newly consumed tokens. Never delete the database to obtain a passing run.
