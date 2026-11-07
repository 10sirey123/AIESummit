# Evaluation instrumentation: MongoDB consumer contract

Schema version **1**. Writer: `AIESummit.Evaluation/MongoInstrumentation.cs`. Application data stays in PostgreSQL. This MongoDB repository describes tests and execution graphs; it is not the source of truth for user uploads or contractual findings.

## Configuration and retention

The CLI reads `MONGODB_URI` and `MONGODB_DATABASE` from local `.env`, overridden by process variables. The complete `MONGODB_URI` is supplied unchanged to the official driver; credentials/options are not reconstructed from other settings. Use a complete URI without unresolved placeholders. Optional `MONGODB_TLS12=true` pins TLS 1.2 for compatibility without disabling certificate validation. SRV/TLS defaults otherwise remain driver-managed. Atlas project/organization IDs are not used as database credentials.

Never place secrets in CLI arguments, report files, telemetry or Git. Connection failures report sanitized exception types, not the connection string. The official MongoDB .NET driver is used only by the tester. The writer creates only the `evaluation_*` collections below and their indexes in the configured database; it does not delete existing data or change Atlas network settings.

Normal `run`/`batch` initializes MongoDB with an admin ping using primary read preference before provider work. `--mongo-offline` records the same writes to disk without connecting; `--no-mongo` disables telemetry entirely. Preserve each run's full artifact directory. Every MongoDB mutation is first appended to `telemetry-outbox.ndjson` in canonical Extended JSON, then acknowledged by MongoDB. A failed write stops that execution path and leaves local artifacts/outbox for diagnosis. A crash can leave a run or document `running`; absence of a terminal event is meaningful, not implicit success.

`replay <outbox-file>` executes stored collection/ID/operation commands in order. Replaying a complete outbox does not create duplicate run, document, step or event records because its stored `_id` values are reused. Run replay after the original writer has stopped; replaying an old partial prefix can temporarily restore an earlier state. Replay is not an exactly-once transaction across collections.

`import-results` creates a new instrumentation run for existing result JSON files. Its events reflect import time. Original measured wall times, application IDs, prompt timestamps and token counters remain in the result. Do not interpret import event times as live execution times.

No source PDF bytes, OCR text, prompt text or full prediction/evidence content is copied into telemetry. The local result JSON retains detailed findings; MongoDB records its location and checksum. File paths and sample filenames are recorded intentionally. MongoDB is not an artifact file server, so another machine needs access to the retained artifact directory to open those files. Source inputs stay in their original folder and are linked by content hash.

## Common conventions

- All records have numeric `schemaVersion: 1`.
- `_id`, `runId`, `documentRunId` and `workflowId` are strings. Run/document IDs are random 32-character GUID strings; workflow IDs are lowercase SHA-256 hashes of the serialized workflow definition.
- Source and artifact SHA-256 hashes identify exact bytes. They are not a substitute for a document-run ID: the same PDF can be tested repeatedly with different workflows/models.
- Writer-created timestamps and recognized nested prompt timestamps are BSON dates in UTC. Timing measurements are numeric seconds. Outbox dates use canonical Extended JSON `$date` representations.
- Unknown measurements are null or explicitly marked unavailable. They are never silently estimated as zero. Native input/output counters can be zero when unreported; use `inputOutputBreakdownAvailable`.
- Graph edges go **from prerequisite to dependent**. `parentNodeIds` contains prerequisites. The current edge relation string is `depends_on`, using this documented direction.
- IDs from PostgreSQL are meaningful only within the application environment identified by the run configuration. Combine them with `runId`/`documentRunId`; do not treat prompt 42 as globally unique.

## Collections

### `evaluation_workflows`

`_id = workflowId`. A reusable workflow definition with `name`, numeric `version`, `targetCatalog`, `nodes`, `edges` and explanatory `note`.

Each node describes a family, with `key`, `kind`, `stepTag` and `scope`. The current families are:

| Key | Kind / scope | Dependencies |
| --- | --- | --- |
| `intake` | HTTP / upload | None |
| `ocr` | OCR / upload | Intake |
| `split` | LLM / upload | OCR |
| `sections` | LLM / page, optional | OCR |
| `extract` | LLM / logical document | Split |

The page-summary branch is independent from splitting. Extraction consumes page snapshots from the split document and does not depend on the summaries. The evaluator skips page summaries by default. A workflow definition is a possible graph; its instantiated document graph records the nodes that actually have retained execution data.

When adding new workflow stages, update the version and node/edge definition so its hash changes. Do not infer ordering from array position or timestamps. An additional extraction stage should explicitly depend on its prerequisites, rather than being appended to an assumed linear sequence.

### `evaluation_runs`

`_id = runId`.

| Field | Meaning |
| --- | --- |
| `runId`, `workflowId` | Identity and workflow definition reference. |
| `status` | `running`, `completed`, or `completed_with_failures`. |
| `startedAt`, `completedAt` | Instrumentation run lifecycle; completion absent until finalized. |
| `artifactRoot` | Absolute directory path in the writer's filesystem. |
| `configuration` | Command, input/manifest, origin and run options where applicable; retrospective imports identify their timing source. No credentials. |
| `runnerVersion`, `runnerInformationalVersion`, `runtime` | Executable assembly and runtime provenance; informational version can contain the source revision. |
| `documentsAttempted`, `documentsFailed` | Final execution counts. |
| `summary` | Comparison aggregates or diagnostic/import summary. |

Zero-document `mongo-check` and replay diagnostic runs are legitimate; filter by `configuration.command` when selecting benchmark runs.

### `evaluation_documents`

`_id = documentRunId`. One attempted source-file test within a run; logical split documents remain identifiable through their analyses and prompt subjects.

| Field | Meaning |
| --- | --- |
| `documentRunId`, `runId`, `workflowId` | Join keys. |
| `sampleId` | Benchmark identifier or single-file name stem. |
| `sourceSha256`, `sourcePath`, `filename` | Exact input identity and retained input location. |
| `resultPath` | Absolute path to the local detailed result JSON. |
| `status`, `startedAt`, `completedAt` | Instrumentation lifecycle. Final document status is `ready` or `failed`. |
| `result` | Measurements, application upload ID, token/prompt metrics, benchmark scores and errors. The full `prediction` property is removed. |
| `graph.nodesCollection` | Currently `evaluation_steps`. Select nodes by `documentRunId`. |
| `graph.edges[]` | Instantiated `{from, to, relation}` dependencies using full node IDs. |

Measurements in a successful result include `intakeSeconds` (upload through OCR/split ready), `extractionReadySeconds` (upload through extraction ready) and `totalSeconds` (through requested terminal workflow state). Those phases can overlap: **do not sum them**. A failed result retains total elapsed time and available metrics; it does not imply that all upstream provider calls failed or consumed no tokens.

`modelReportedScores` are diagnostic predictions without validated evidence. Validated scores only represent published findings. The two must never be combined into one “accuracy” number. `scores.json` also reports all-document validated findings, so failed analyses are not hidden by successful-only scoring.

### `evaluation_steps`

`_id` identifies one node instance, not a target category. There is no per-target LLM call in this baseline.

| Node ID | Description |
| --- | --- |
| `<documentRunId>/intake` | HTTP intake family instance. |
| `<documentRunId>/ocr` | OCR family instance; token usage unavailable. |
| `<documentRunId>/prompt/<promptId>` | A retained native BlueShift prompt instance. |

LLM nodes optionally include `state` (`succeeded`, `failed`, `queued`, `unknown`) and `observedLifecycleSeconds` derived from persistence timestamps. These are lifecycle observations, not provider latency. Earlier retained v1 records can omit these optional fields.

LLM nodes contain `runId`, `documentRunId`, `workflowId`, `kind`, `stepTag`, `parentNodeIds`, `model`, nullable `modelVersion`, `modelVersionStatus`, `timingBasis`, and the sanitized `prompt` metrics object. Extraction nodes additionally retain the pinned `targetDefinitions` from their matching analysis when available.

The `prompt` object carries:

- `id`, `documentType`, `documentID`, `step`, `stepId`: application identities and dispatch context.
- `model`, `modelTag`, `provider`, nullable `modelVersion`: configured model identity actually associated with the prompt. The framework does not expose an immutable provider revision; a deployment name is not falsely labeled as one.
- `systemPromptHash`, `inputPromptHash`, `outputSchemaHash`: SHA-256 fingerprints, never the underlying prompt text.
- `generationParameters`: temperature and reasoning effort, when exposed by the captured metrics version.
- `inputTokens`, `outputTokens`, `tokens`, `accumulatedTokens`: native counters. Input/output breakdown can be unavailable even when total tokens are populated.
- `apiAttemps`: native framework spelling, representing its API-attempt count. Retain this spelling for v1 consumers. A cache hit can reuse response counters with no new provider attempt.
- `processingStatus`: native numeric enum; completed is 200 and permanent failure is 500 in the current pin. Preserve the native value and consult the bundled contract when extending status interpretation.
- `dateCreated`, `lastModified`: persistence timestamps. Their difference includes queueing and processing; **it is not measured provider latency**. Exact per-attempt network latency is unavailable.

The initial graph preserves stage dependencies and native prompt identity, including multiple extraction prompts for a PDF split into multiple documents. Retries within one native prompt are represented by attempt/accumulated counters; individual retry spans are not exposed. A future workflow runner should add distinct attempt records/events if it obtains real per-attempt timing. Do not manufacture those spans from aggregate counts.

### `evaluation_events`

Append-style lifecycle snapshots: `_id` is a generated event ID; fields are `runId`, `documentRunId`, `workflowId`, `nodeKey`, `state`, `occurredAt`, `details`.

Live runs write intake started/accepted, split completion and document terminal events. Full prompt nodes are materialized from retained metrics at document finalization. These are **not** a complete streaming trace of every provider event. Imported results write retrospective started/terminal events only. A future runner can add events without removing the stage graph.

### `evaluation_artifacts`

`_id = <documentRunId>/result`. Fields: `runId`, `documentRunId`, `kind` (`result-json`), absolute `path`, path relative to the run artifact root, lowercase `sha256`, byte count and `contentStoredInMongo: false`.

Verify the checksum before trusting a retrieved artifact. Copying the artifact folder to another machine does not change its content hash; use `relativePath` with the new local root rather than relying on the original absolute path. Manifests and aggregate reports remain alongside the per-document files in the retained folder.

## Indexes and example queries

The writer creates indexes on documents `(runId, sampleId)` and `sourceSha256`, steps `(documentRunId, stepTag)`, and events `(runId, occurredAt)`. MongoDB's default `_id` indexes provide identity uniqueness. There is no TTL index or automatic cleanup.

```javascript
// Select a completed benchmark/import run.
db.evaluation_runs.find({ documentsAttempted: 20 }).sort({ startedAt: -1 })

// Files and failures for one run.
db.evaluation_documents.find(
  { runId: "RUN_ID", status: "failed" },
  { sampleId: 1, sourceSha256: 1, "result.uploadId": 1,
    "result.totalSeconds": 1, "result.error": 1, resultPath: 1 }
)

// Retrieve all graph nodes; use parentNodeIds/graph.edges to draw dependencies.
db.evaluation_steps.find({ documentRunId: "DOCUMENT_RUN_ID" })

// LLM tokens by model and step for one run, including rejected responses.
db.evaluation_steps.aggregate([
  { $match: { runId: "RUN_ID", kind: "llm" } },
  { $group: { _id: { model: "$model", step: "$stepTag" },
      reportedTokens: { $sum: "$prompt.tokens" },
      apiAttempts: { $sum: "$prompt.apiAttemps" }, prompts: { $sum: 1 } } }
])

// Repeated experiments on the exact same source bytes.
db.evaluation_documents.find({ sourceSha256: "PDF_SHA256" })
```

Consumers should read `schemaVersion`, join by explicit IDs, distinguish definitions from graph instances, preserve unknown/null measurements, and label configured model names separately from immutable model revisions. Do not equate successful classification with accepted evidence, native token counters with invoiced usage, or an imported event timestamp with the original processing time.

## Connectivity checks and the resolved TLS failure

Resolved on November 7, 2026. A missing Atlas IP access-list entry caused the primary endpoint to return TLS alert 80 (`TLSV1_ALERT_INTERNAL_ERROR`), while secondary connections authenticated successfully. This machine uses destination-dependent outbound NAT: browser/secondary traffic and primary-node traffic did not always present the same public IP. A browser's “current IP” or a single HTTPS IP-check endpoint therefore did not establish the actual primary connection's source.

The owner approved adding only the additional observed `/32` address. After it became active, official mongosh 2.14.1 and the .NET tester connected to the primary. MongoDB's read-only `whatsmyuri` command confirmed that the primary saw the newly allowed address. The URI, authentication database, credentials and certificate validation were preserved. No system DNS override or application connection workaround was required.

`mongo-check` verified an acknowledged write, and the pending outbox was replayed successfully. Primary read-back confirmed the original 20-file baseline and its graph. This is a retrospective import, not a new inference run. Baseline details and the stored run ID are in [the measured report](extraction-baseline-results.md).

Use the read-only CLI probe from the repository root:

```powershell
# Use mongosh on PATH, or the portable official tool downloaded for this session.
$mongosh = (Get-ChildItem artifacts/mongodb-tools -Filter mongosh.exe -Recurse | Select-Object -First 1).FullName
& $mongosh --nodb --quiet --file scripts/mongodb-probe.js

# Verify the tester's actual write path, then replay retained commands if needed.
dotnet run --project AIESummit.Evaluation -- mongo-check
dotnet run --project AIESummit.Evaluation -- replay artifacts/extraction-evaluation/final/telemetry-outbox.ndjson
```

The probe loads `.env` (or the file named by `MONGODB_ENV_FILE`), lets process settings override the URI/database, and prints primary status, the client address observed by MongoDB, and metadata/counts for the latest document run. It performs no writes and does not print credentials. Download mongosh from [MongoDB's official shell download](https://www.mongodb.com/try/download/shell) when it is absent.

When diagnosing asymmetric node failures, compare all SRV nodes, use primary read preference for the write-path check, and compare source addresses rather than assuming a secondary read proves primary access. Any additional access-list entry requires the owner's explicit authorization; do not broaden the list automatically.

## Improvement-cycle expectations and document matrices

The evaluator now writes two additional benchmark-only collections. Existing v1 runs remain readable and are not rewritten automatically. Re-import retained results to attach versioned labels and observations without model inference.

To enrich an existing run while preserving its original IDs, measurements, graph, and event timestamps:

```powershell
dotnet run --project AIESummit.Evaluation -- enrich-results artifacts/extraction-evaluation/manifest-final.json artifacts/extraction-evaluation/final --run ead9272caae140fcaf21df00b749b74c
```

This was executed for the retained 20-document baseline on November 7, 2026. It added expectation/observation records and document links, without creating a duplicate inference run. Its replayable enrichment outbox is under `artifacts/improvement-enrichment/<runId>/`.

- `evaluation_expectations`: content-addressed snapshots keyed by dataset, source PDF SHA-256, exact label-file SHA-256, and development/confirmation partition. Stores sample identity, label schema version, upstream provenance, **all** supplied `expected_detections` (including answer spans and categories beyond the ten implemented targets), and the ten mapped target expectations. Each mapped expectation is `present`, `absent`, or `unknown`. Missing/ambiguous categories and unlabeled EDGAR remain unknown, never negative gold.
- `evaluation_observations`: one document-run record referring to its immutable expectation ID and workflow ID. Contains every mapped target's expected state, validated presence, diagnostic model presence, outcome, matrix state, and accepted finding IDs/titles/typed values with source page/offset references. Source quotes and complete predictions remain in checksummed local result artifacts. Public benchmark gold answer text is intentionally retained in expectation snapshots; this is an explicit exception to the older metadata-only telemetry scope, not permission to copy arbitrary private documents into MongoDB.

Matrix states distinguish `found`, `missed`, `false_positive`, `correct_absence`, `rejected_evidence`, `failed`, `not_run`, and `unknown`. Accepted findings survive in the matrix even if another analysis for the same source failed. “Found” is validated document/target presence; a target's `goldFindings` count is not proof of individual-clause matching. The scorer remains the existing presence evaluator.

Failure state is derived from the analyses responsible for each target, rather than copied from the overall document. A successful independent branch returning no finding remains `missed` or `correct_absence` when another branch fails. Only failed relevant analyses contribute rejected model-reported detections. Read-only views also join through each run's top-level and configured partition, so old rows without their own partition do not expose confirmation outcomes. Label-bearing records require explicit development classification and visible development usage; missing partition metadata never grants access to gold.

`RegisterExpectedAsync` runs before inference/finalization so a failed document still retains its expectations. A run cannot mix development and confirmation partitions. Read-only history excludes confirmation labels, observations, and run summaries. The actual instantiated workflow can be supplied to `OpenAsync(..., workflowDefinition: graph)`; `RecordGraphAsync` records the exact node membership and checks every edge endpoint. Consumers should prefer `graph.nodeIds` for exact graphs rather than assuming every retained stage-family node belongs to that graph.

Codex and the web UI use `MongoHistory`, a bounded reader with no mutation, arbitrary command, or arbitrary pipeline surface. The CLI exposes the same views:

```powershell
dotnet run --project AIESummit.Evaluation -- history --kind runs --limit 20
dotnet run --project AIESummit.Evaluation -- history --kind missing --sample 0201
dotnet run --project AIESummit.Evaluation -- history --kind matrix --source PDF_SHA256
dotnet run --project AIESummit.Evaluation -- history --kind steps --run RUN_ID
```

Views are `runs`, `documents`, `steps`, `workflows`, `expectations`, `observations`, `missing`, `events`, and `matrix`. Results are capped at 200 records; a matrix requires an exact source checksum and, when several label versions exist, `--expectation EXPECTATION_ID`. Queries read the existing URI without printing it. Give an external Codex environment access through this bounded service or a genuinely read-only database account, never by copying writer credentials into prompts or transcripts.
