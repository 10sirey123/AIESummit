# Contract extraction backend and evaluator

Implemented November 7, 2026. The first release implements B01–B10 from [the target specification](extraction-targets.md), using one deliberately naive extraction LLMStep for all ten targets in each logical document. The remaining benchmark targets, healthcare targets, general indemnification and termination-for-cause coverage are not implemented by this step. Risk scoring and frontend extraction cards are not implemented.

## Persistent model

All application rows remain in PostgreSQL, using the existing BlueShift `BasePersistenceObject` base and inherited `ID`. MongoDB is a separate tester instrumentation store, not the application database.

| Entity | Responsibility and relationships |
| --- | --- |
| `ExtractionType` | Global target catalog. Stable, unique lowercase `Tag`; editable definitions are preserved by missing-only startup seeds. Contains name, CUAD category, instructions, definition version, allowed parameter names and enabled state. |
| `DocumentAnalysis` | `ILlmCapable` execution subject for one logical `Document`. Contains mapped prompts, target-definition JSON snapshot, input hash, page snapshots, per-target results, error and completion time. Unique `(DocumentId, RequestKey)` makes a repeated request idempotent. |
| `PageTextRevision` | Immutable page-local Markdown snapshot for one analysis. References the original `OcrPage`; preserves original upload page number and SHA-256 content hash. Unique `(AnalysisId, Number)`. The context rejects modifications to tracked snapshots through its async save path. |
| `AnalysisTargetResult` | One requested target in one analysis. References `ExtractionType`; records outcome and explanation even if no finding exists. Unique `(AnalysisId, ExtractionTypeId)`. |
| `Extraction` | One finding under one target result; several findings of the same type are allowed. Contains title, factual summary, schema version, review state and validated typed values stored as `jsonb`. |
| `ExtractionEvidence` | One contiguous page-local segment supporting a finding. References a page snapshot, stores exact quote, start/end offsets, role and supported parameter names. Multiple segments may support a finding or individual fields. |

`Document.IsCurrent` distinguishes current split outputs from historical ones. Re-splitting supersedes old rows instead of deleting analyses and their evidence. The unique `(UploadId, FirstPage)` index applies only to current documents. Historical analysis references restrict document deletion. User-facing upload results list current documents; extraction history includes `isCurrent`.

`ILlmCapable` is the actual bundled framework interface, rather than the legacy `IAmLLMCapable` name. It belongs on `DocumentAnalysis`, not on ten separate catalog entities. A type definition describes what to extract; an analysis is the runnable subject that receives prompts and results.

## Ten seeded types and parameters

Catalog version: `cuad-ten-v1`. Value schema version: **1**. Every type also allows optional `party` and `conditions` strings. Every populated parameter must be linked to at least one evidence segment. Unknown values are omitted/null, never invented.

| Reference | Stable tag | CUAD mapping | Additional allowed parameters |
| --- | --- | --- | --- |
| B01 | `agreement-effective-date` | `Effective Date` | `date`, `dateExpression` |
| B02 | `agreement-expiration` | `Expiration Date` | `date`, `dateExpression`, `duration`, `unit` |
| B03 | `renewal-term` | `Renewal Term` | `mechanism`, `duration`, `unit` |
| B04 | `non-renewal-notice` | `Notice Period To Terminate Renewal` | `duration`, `unit` |
| B05 | `non-compete` | `Non-Compete` | `activity`, `geography`, `duration`, `unit` |
| B06 | `termination-convenience` | `Termination For Convenience` | `duration`, `unit`, `mechanism` |
| B07 | `liability-cap` | `Cap On Liability` | `amount`, `currency`, `formula`, `duration`, `unit` |
| B08 | `uncapped-liability` | `Uncapped Liability` | `activity`, `exclusions` |
| B09 | `audit-rights` | `Audit Rights` | `activity`, `duration`, `unit`, `mechanism` |
| B10 | `insurance-obligations` | `Insurance` | `activity`, `amount`, `currency`, `duration`, `unit` |

Parameter definitions:

| Parameter | JSON type | Meaning and validation |
| --- | --- | --- |
| `date` | string | Explicit, unambiguous calendar date, exactly `yyyy-MM-dd`; validated using `DateOnly`. No generated/calculated dates in this baseline. |
| `dateExpression` | string | Original date/event/relative-term expression, including unresolved dates. |
| `duration` | decimal number | Positive contractual interval. Must be paired with `unit`. |
| `unit` | string | `calendar_days`, `business_days`, `days`, `months` or `years`. Do not silently equate business and calendar days. |
| `amount` | decimal number | Nonnegative stated monetary amount; use `formula` for fee-based caps or other expressions. |
| `currency` | string | Explicit three-letter uppercase currency code; an unsupported or ambiguous symbol does not justify inventing a code. |
| `formula` | string | Stated cap formula or claim limitation expression. No numeric calculation is claimed. |
| `party` | string | Party/role affected by this particular finding, supported by evidence. |
| `conditions` | string | Material qualifications, triggers, notice-delivery conditions or exceptions. |
| `mechanism` | string | Renewal/termination/audit mechanism stated by the source. |
| `activity` | string | Restricted activity, audit scope, express uncapped category, or insurance coverage description, according to type. |
| `geography` | string | Territory of a non-compete restriction. |
| `exclusions` | string | Express uncapped categories or cap carveouts. Absence of a cap is not proof of unlimited liability. |

Strings are bounded to 4,000 characters during validation. The fixed C# `ExtractionValues` contract supplies typed numeric/string members; per-target field allowlists further restrict its shape. These are deliberately modest v1 payloads, not a complete model of every clause. For example, multiple insurance policies or party-specific caps should produce separate findings when necessary. Formula normalization and computed deadlines require a later, versioned extension with its own gold labels.

## Evidence and result validation

- Page numbers are inclusive one-based positions in the original upload.
- Offsets are zero-based UTF-16 code-unit indexes into **the exact snapshot Markdown**. `StartOffset` is inclusive and `EndOffset` exclusive. C# slicing and JavaScript `slice(start, end)` agree. These are not CUAD Unicode-code-point offsets, PDF byte offsets, screen coordinates, or rendered-text offsets.
- The model supplies a page, exact quote and occurrence (`-1` for a unique match; otherwise a zero-based occurrence index). The server resolves offsets by ordinal substring matching. Repeated or absent quotes without a valid occurrence are rejected. Surrogate pairs must not be split.
- Evidence roles are `primary`, `qualification` and `definition`. `fields` lists supported parameter names. A cross-page clause needs separate evidence segments on each page.
- The server validates every requested tag exactly once, outcome consistency, bounded titles/summaries, target-specific parameter types/values and all evidence before publishing any result for that analysis.
- A detected target requires findings and evidence. Other outcomes (`not_found_in_reviewed_text`, `uncertain`, `not_applicable`) must not contain findings. Pending results use `not_analyzed`.
- Validation failure rejects the entire logical-document analysis and leaves its targets unpublished. Other logical documents or earlier analyses remain intact. The failed prompt and error are retained; there is no silent retry/reset or acceptance of approximate quotes.
- Field/evidence links prove source anchoring and declared support, not semantic correctness. An anchored quote can still be misinterpreted; the model and human review remain responsible for the meaning.

Exact-source evidence enables Markdown highlighting and original-page navigation. PDF-coordinate highlighting is not implemented. The `ReviewState` initially is `unreviewed`; no human-review editing workflow is introduced here.

## Native workflow and configuration

1. Authenticated upload stores original PDF bytes and publishes normal OCR work.
2. OCR persists ordered pages; the upload split LLMStep determines logical document ranges.
3. Applying a valid split creates a `DocumentAnalysis` for each new current document when extraction targets are enabled. It copies full page text and the enabled target definitions into that analysis.
4. Native `LLMStepAgent<DocumentAnalysis>` hydrates its full persisted state, snapshots and prompt collection. One code-defined step, `extract-contract-targets`, prepares all requested targets and all pages together.
5. Native prompt/provider workers produce `ContractExtractionResult`; its connector validates and stages results for the framework save. `DocumentAnalysis_ID` is the separate persisted prompt foreign key.
6. Queue recovery includes `DocumentAnalysis`, and execution correlation maps its prompts to the original upload. Worker queue filters and emulator queues include the new subject.

There is one extraction step per logical document, not ten parallel target calls. A PDF split into several agreements therefore causes several extraction prompts. Existing per-page section summaries are independent. Uploads default to including those summaries; the evaluator explicitly sets `pageSections=false` to measure OCR → splitting → contract extraction without unrelated summarization calls.

Startup applies `003-contract-extractions.sql` under the existing advisory transaction lock. The additive upgrade preserves uploaded files, pages, prompts, existing document IDs and database volumes. The targeted SQL upgrade is repeatable; it is not a general EF migration framework.

`ExtractionSeeds.EnsureAsync` inserts missing target tags and the missing extraction step only. It does not overwrite existing names, instructions, enabled flags, templates or model selection. The initial extraction step uses the existing `foundry-split` model. Framework sample/export support includes the ten catalog records with **Keep** policy and the actual code-defined step. Intentional later prompt updates use the reviewed export/plan/apply workflow in [configuration seeding](configuration-seeding.md); startup does not synchronize templates automatically. Each analysis pins its definitions even if catalog configuration later changes.

Existing uploads are not automatically backfilled. The owner can explicitly request analysis for their current documents. A repeated document/request-key pair reuses history; a new key creates a deliberate new analysis. Disabling a step does not erase already completed work.

## API

All routes require Firebase authentication. Upload-derived reads and writes check `UploadedFile.OwnerId` before accessing any analysis or prompt metrics. Other owners receive 404; anonymous callers receive 401. EF graphs, prompt text and credentials are not returned.

| Route | Behavior |
| --- | --- |
| `GET /api/v1/extraction-types` | Catalog definitions and allowed parameter JSON. |
| `POST /api/v1/uploads/{id}/extractions?requestKey=initial` | Idempotently creates missing analyses for current logical documents. Returns 202; 409 if prerequisites/step are unavailable. |
| `GET /api/v1/uploads/{id}/extractions` | Analysis history, pinned definitions, per-target outcomes, findings, typed values and evidence. Also exposes explicitly **unvalidated** `modelReportedOutcomes` for diagnostic classification comparison; these are never accepted findings. |
| `GET /api/v1/uploads/{id}/metrics` | Prompt IDs and subjects, step/model/provider identifiers, definition/input/schema hashes, token counters, attempts and persistence timestamps. No prompt or response text. |
| `POST /api/v1/uploads?pageSections=false` | Normal multipart upload with independent page summaries skipped; OCR/splitting/extraction still run. |

Token counters are the framework/provider counters. Some providers populate `Tokens` but leave input/output counters zero; check `inputOutputBreakdownAvailable`. OCR token usage and immutable provider model revision are not exposed by this framework and remain null/unknown. Model tag, configured deployment name, provider and prompt/schema hashes are retained instead. No dollar-cost or billing-accurate token estimate is claimed.

## Tester CLI

`AIESummit.Evaluation` is a separate .NET 10 executable. It uses the authenticated application API and native workers, rather than a second OCR/provider implementation. The only new library dependency is the official `MongoDB.Driver`, confined to the tester for instrumentation.

Start the normal local stack first. The CLI signs up a synthetic test account only when `/firebase-config` confirms the local emulator on a loopback origin. Otherwise supply a Firebase ID token in a private file using `--token-file`; token contents are not printed. Automatic sessions are test accounts, not production sign-in automation.

```powershell
# One original PDF, with optional CUAD gold sidecar.
dotnet run --project AIESummit.Evaluation -- run corporea/benchmarks/cuad/0001.pdf --gold corporea/benchmarks/cuad/0001.json

# Freeze a 20-document selection, then run it.
dotnet run --project AIESummit.Evaluation -- select corporea/benchmarks artifacts/extraction-evaluation/manifest.json
dotnet run --project AIESummit.Evaluation -- batch artifacts/extraction-evaluation/manifest.json --output artifacts/extraction-evaluation/new-run

# Preserve local telemetry without contacting MongoDB; replay it later.
dotnet run --project AIESummit.Evaluation -- run document.pdf --mongo-offline
dotnet run --project AIESummit.Evaluation -- replay artifacts/extraction-evaluation/new-run/telemetry-outbox.ndjson
```

MongoDB settings are read from the local `.env` (or `--env-file`), with process variables taking precedence. See [the instrumentation schema](evaluation-instrumentation.md) for connection settings, collections, graph conventions and replay. Normal runs require MongoDB to initialize; `--mongo-offline` explicitly writes its replayable outbox locally. `--no-mongo` explicitly disables instrumentation, useful for isolated backend diagnostics.

Other options: `--origin`, `--timeout-seconds` (default 900), `--include-page-sections`, `--output`. Default output folders contain a UTC timestamp and random suffix. Existing result files are refused rather than overwritten. Preserve the entire output folder, its manifests, JSON results and outbox; the tester does not delete its generated files. Original input PDFs remain in their source folder and are identified by SHA-256.

`score <manifest> <results-folder>` regenerates comparisons from retained diagnostics without inference. `import-results <manifest> <results-folder> [--mongo-offline]` instruments retained results, explicitly identifying its events as retrospective imports. `mongo-check` verifies connection and writes a zero-document diagnostic run. The internal `schema <file>` command emits the EF model DDL for inspection without connecting to a database.

## Evaluation and checks

The selector uses original CUAD PDFs from the official test split, with 1–10 pages, ordered by SHA-256 of `aiesummit-extraction-v1/<sample_id>`. PDF checksums are frozen in the manifest. This favors shorter contracts and does not establish performance on long agreements or healthcare contracts. Gold labels never enter extraction prompts. Preserve upstream splits and keep prompt development outside the reported evaluation documents.

Presence comparisons use exact CUAD category mappings. Report two separate views: diagnostic model-reported classification and findings that passed source/value validation. Failed analyses remain in the full-set validated score as missing findings; never hide failure coverage. Raw model outcomes are not evidence-validated predictions. Descriptive evidence token-bag overlap is not official CUAD character-span scoring. Normalized structured fields lack independent gold in these sidecars and receive no accuracy claim.

```powershell
dotnet build AIESummit.slnx --nologo
dotnet test AIESummit.slnx
./scripts/test-extractions.ps1
node AIESummit.Api/ApiSpec/build.mjs
git diff --check
```

The extraction test project is separate from the earlier application tests. Its disposable PostgreSQL checks exercise retained-schema upgrade, repeated seeds, operator edits, evidence persistence, snapshot mutation rejection and historical document references. Its disposable MongoDB checks exercise writes, graph structure, idempotent replay and exclusion of prediction content from telemetry. Pure checks cover Unicode offsets, ambiguous quotes, out-of-scope pages, malformed values, outcomes and benchmark boundaries. Docker test containers are uniquely named and removed; diagnostic artifact folders remain.

See [the recorded baseline results](extraction-baseline-results.md) for actual measurements and limitations.
