# Client access to OCR and contract analysis

Updated November 7, 2026. The backend exposes OCR and extraction results, structured target definitions and a lightweight progress/analysis-selection summary. The frontend integration described here is future work. No frontend application changes are included, and a running deployment must load the updated API binaries before using the additions.

## Current exposure

The relevant extraction entities are already exposed as explicit, authenticated result projections. They do not need separate entity CRUD routes for a client to display results. `Program.cs` registers both `MapUploadEndpoints()` and `MapExtractionEndpoints()`.

| Data/entity | Existing endpoint | Client-visible information |
| --- | --- | --- |
| Original upload | `GET /api/v1/uploads/{id}/source` | Original protected PDF/image bytes; PDF supports byte-range reads. |
| `OcrPage` | `GET /api/v1/uploads/{id}` → `pages[]` | Page ID, original page number, OCR Markdown, page-summary analysis status/error and sections with summaries/quotes. |
| `Document` | Same endpoint → `documents[]` | Current logical document ID, title, inclusive original page range, combined Markdown and extracted-image references. |
| `ExtractionType` | `GET /api/v1/extraction-types` | Stable tag, name, instructions, benchmark category, definition version, enabled flag and structured `parameters`, versioned `valueSchema`, `schemaSupported` and `valueSchemaDialect`. The legacy `parametersJson` string is retained. |
| `DocumentAnalysis` | `GET /api/v1/uploads/{id}/extractions` → `analyses[]` | Analysis ID, document ID, current-document flag, request key, input hash, status, completion time, error and pinned target definitions. |
| `AnalysisTargetResult` | Same endpoint → `analyses[].targets[]` | Stable type tag, outcome, explanation and zero or more findings. |
| `Extraction` | Same endpoint → `targets[].extractions[]` | Finding ID, title, factual summary, schema version, review state and structured `values`. |
| `ExtractionEvidence` | Same endpoint → `extractions[].evidence[]` | Original page number, snapshot ID/hash, start/end character offsets, exact quote, role and supported value-field names. |
| `PageTextRevision` | `GET /api/v1/uploads/{id}/analysis-pages/{revisionId}` | Immutable snapshot ID, analysis ID, original page number, Markdown and content hash. |
| Processing summary | `GET /api/v1/uploads/{id}/analysis-summary` | OCR/split/page-summary/extraction progress, polling hints and latest-attempt/last-successful IDs for each current document. |
| Processing instrumentation | `GET /api/v1/uploads/{id}/metrics` | Sanitized prompt/model identifiers, hashes, generation parameters, token counters and persistence timestamps. Optional for a results UI. |

Control endpoints are also implemented:

- `POST /api/v1/uploads`: upload and begin the native OCR/split workflow. With enabled targets, successful splitting creates document analyses automatically. Independent page summaries default to enabled; `?pageSections=false` skips only that branch.
- `POST /api/v1/uploads/{id}/analyze`: request the existing page-section summaries. This is distinct from contract target extraction.
- `POST /api/v1/uploads/{id}/extractions?requestKey=initial`: create missing contract analyses for current documents. Reusing a document/request-key pair reuses the existing analysis; a new request key creates a deliberate new attempt.

Every route above requires a Firebase bearer token. Upload-derived reads/writes are scoped to the upload owner; another owner receives 404 and an anonymous caller receives 401. The global extraction catalog requires authentication but is not specific to one upload. MongoDB is tester instrumentation and is not needed by the product client.

## What is missing today

`web/src/api.ts` currently defines upload, document and page-section types and methods. It has no contract-extraction catalog, results, snapshot or request methods. The reader displays the existing page-section summaries, not the new document-level findings.

The backend exposes sufficient data for an initial results screen. Remaining work is principally client integration. The additive catalog and summary contracts below address the initial read-contract gaps. Existing extraction results, snapshot routes and request bodies retain their previous shapes.

## Proposed client integration

### 1. Add typed API access

Extend the existing authenticated session with methods for:

- Listing extraction types.
- Fetching the processing/analysis-selection summary.
- Fetching extraction results for an upload.
- Fetching an immutable analysis-page snapshot.
- Requesting extraction with an explicit request key.
- Optionally fetching processing metrics separately from business results.

Define TypeScript types for the nested response, outcome/status unions, findings and evidence. Represent `values` as a versioned union discriminated by the enclosing target tag and finding schema version. The ten current target shapes and parameter meanings are documented in [contract-extraction.md](contract-extraction.md). Preserve an explicit unsupported-version display instead of assuming unknown data matches v1.

Use the existing token refresh coordination, in-memory authentication and abort signals. Abort requests when the user changes upload, signs out or unmounts the reader; discard stale responses. Do not introduce a second authentication implementation.

### 2. Track each processing branch separately

Upload `status: ready` currently means OCR/splitting finished. It does **not** mean contract extraction or page summaries finished.

Fetch upload detail, the analysis summary and extraction results separately. Use `summary.extraction.canPoll` to decide whether contract-extraction polling should continue, with bounded retries/backoff and cancellation. Page summaries are independent; poll that branch while `summary.pageSections.pending > 0` if it is displayed. An empty analysis list means no analysis is available yet, not that no target clauses were found. The legacy extraction GET retains its original three-state status for compatibility; the new summary additionally identifies blocked work and completion without a result.

Group extraction responses by `documentId` and exclude superseded documents (`isCurrent: false`) from the default view. Several analysis attempts can belong to the same current document. Use the summary's per-document `latestAnalysisId` and `lastSuccessfulAnalysisId` to select rows in `analyses[]`, and clearly display the latest attempt's status. If it fails, retain access to the last successful attempt with its own analysis identity; never silently merge findings across attempts. Expose historical attempts through a separate history view when needed.

Do not automatically create new request keys on every poll. Use `initial` for an idempotent initial request/backfill, and a new key only after an explicit user action to reanalyze. Repeating a failed attempt's existing key does not retry it.

### 3. Display factual findings and structured values

For each document, show the requested targets and distinguish:

| State | Display meaning |
| --- | --- |
| Analysis `analyzing` | Results are pending; no absence conclusion. |
| Analysis `failed` | Extraction was rejected or failed. Show its error; unpublished target results do not mean clauses are absent. |
| Target `detected` | Show each accepted finding, structured fields and evidence. |
| Target `not_found_in_reviewed_text` | No matching clause was found in the reviewed document text. |
| Target `uncertain` | A determination needs review. |
| Target `not_applicable` | The target was assessed as inapplicable, with its explanation. |
| Target `not_analyzed` | No completed determination is available. |

Use the analysis's pinned `definitions` for historical target names/versions and field expectations. The current catalog can change after an analysis was performed.

An expiration finding can provide `values.date = "2027-12-31"`, rather than just a highlighted clause. Display it as a calendar date without converting through a timezone-dependent timestamp. When only `dateExpression` exists, show that expression and the unresolved condition. The current baseline does not calculate missing dates. Preserve units/currencies, formulas and qualifications for other target types; null/missing is unknown, not zero or false.

`modelReportedOutcomes` are explicitly unvalidated diagnostics. Do not use them to create business finding cards or override accepted target outcomes. The presence of a clause also does not imply a risk level or a legal conclusion.

### 4. Connect findings to source evidence

Selecting evidence should move the original viewer to `evidence.page`, which is one-based in the original upload, not relative to the split document.

For character highlighting, load the referenced immutable snapshot, verify its ID/hash and confirm:

```javascript
snapshot.markdown.slice(evidence.startOffset, evidence.endOffset) === evidence.quote
```

Offsets are zero-based UTF-16 code units with an inclusive start and exclusive end. They apply to exact snapshot Markdown, including formatting characters. They do not directly map to rendered Markdown DOM positions or PDF coordinates. Initially, show the highlighted source-text passage alongside original-page navigation. Precise PDF rectangles require additional OCR geometry that is not currently provided.

A single finding may have several segments on different pages, including definitions and qualifications. Keep those segments and their `supportedFields` links visible. Do not substitute current OCR text for a historical snapshot merely because its page number matches.

### 5. Verify the complete client journey

Acceptance checks should cover:

- Upload → OCR ready → extraction pending → accepted findings, without treating split completion as overall completion.
- A structured expiration date and a relative/unresolved date expression.
- Multiple findings of one type, multiple evidence segments and evidence across pages.
- Pending, failed, absent, uncertain and inapplicable outcomes.
- Multiple attempts, a failed latest attempt, retained successful history and superseded documents.
- Correct original-page navigation and exact source-text highlighting, including non-BMP Unicode characters.
- Cancellation/stale-response protection, refreshed authentication, anonymous 401 and other-owner 404 for results and snapshots.

These are planned client/API acceptance checks, not claims that this document ran them.

## Structured extraction-type contract

`GET /api/v1/extraction-types` still returns an array. Existing fields are preserved. Each entry now also contains:

| Field | Meaning |
| --- | --- |
| `parameters` | Array of descriptors: `name`, `type`, `description`, nullable `format`, nullable `allowedValues`, nullable `minimum`, `exclusiveMinimum`, nullable `maxLength`. |
| `schemaSupported` | Whether this API understands the configured definition version and all parameter names. |
| `valueSchemaDialect` | Currently `openapi-3.0`; the returned schema uses OpenAPI 3.0 Schema Object semantics, including Boolean `exclusiveMinimum`. |
| `valueSchema` | An object schema with allowed properties and `additionalProperties: false`; null for unknown versions, invalid configuration or unsupported fields. |

All values are optional and omitted when unknown. A value payload can be empty for a clause-only detection. `duration` and `unit` must be supplied together. Every populated value requires evidence; schema shape alone cannot establish semantic support. Unknown schema versions should be presented as unsupported/generic read-only data, not silently interpreted as v1.

The current ten definitions use value schema version **1**. Every type additionally allows optional `party` and `conditions` strings.

| Stable tag | Display name | Additional value parameters | OpenAPI value component |
| --- | --- | --- | --- |
| `agreement-effective-date` | Effective date | `date`, `dateExpression` | `EffectiveDateValuesV1` |
| `agreement-expiration` | Initial expiration | `date`, `dateExpression`, `duration`, `unit` | `ExpirationValuesV1` |
| `renewal-term` | Renewal mechanism | `mechanism`, `duration`, `unit` | `RenewalValuesV1` |
| `non-renewal-notice` | Non-renewal notice | `duration`, `unit` | `NonRenewalNoticeValuesV1` |
| `non-compete` | Non-compete | `activity`, `geography`, `duration`, `unit` | `NonCompeteValuesV1` |
| `termination-convenience` | Termination for convenience | `duration`, `unit`, `mechanism` | `ConvenienceTerminationValuesV1` |
| `liability-cap` | Liability cap | `amount`, `currency`, `formula`, `duration`, `unit` | `LiabilityCapValuesV1` |
| `uncapped-liability` | Uncapped liability | `activity`, `exclusions` | `UncappedLiabilityValuesV1` |
| `audit-rights` | Audit rights | `activity`, `duration`, `unit`, `mechanism` | `AuditRightsValuesV1` |
| `insurance-obligations` | Insurance obligations | `activity`, `amount`, `currency`, `duration`, `unit` | `InsuranceObligationsValuesV1` |

Strings are nonblank and bounded to 4,000 characters unless a narrower constraint applies. `date` is an explicit ISO calendar date; `amount` is a nonnegative number; `duration` is positive. `unit` is one of `calendar_days`, `business_days`, `days`, `months`, `years`. `currency` is an explicit three-letter uppercase code. Formulas and relative date expressions remain strings; no calculated calendar date is promised.

The OpenAPI `ExtractionFinding.values` component includes an `x-target-value-schemas` mapping keyed by target tag and schema version. Use the enclosing target's `tag` and the finding's `schemaVersion` to select its named value schema. Do not apply an exclusive union to all ten schemas: their optional/shared fields intentionally overlap.

For example, accepted expiration values may be:

```json
{
  "date": "2027-12-31",
  "conditions": "Unless extended by written agreement."
}
```

Both fields require corresponding evidence. An unresolved expression instead can be represented by `dateExpression` with no `date`.

## Processing summary contract

`GET /api/v1/uploads/{id}/analysis-summary` returns no full document text, prompts or findings. It is intended for progress polling and selecting result/history records.

| Field | Meaning |
| --- | --- |
| `uploadId` | Owned upload identity. |
| `availability` | `stepEnabled`, `enabledTargetCount`, `available`, explanatory `note`. This checks configuration availability, not credentials or worker health. |
| `ocrStatus` | `queued`, `processing`, `ready`, `failed`. |
| `splitStatus` | `waiting_for_ocr`, `analyzing`, `ready`, `failed`. |
| `pageSections` | `total`, `ready`, `failed`, `pending`, `notStarted`, plus a branch-specific `status`. |
| `extraction` | Aggregate `status`, `canPoll`, counts of current total/ready/failed/pending documents, nullable `reason`. |
| `documents[]` | Current document `id`, `title`, `firstPage`, `lastPage`, and `analysis` selection. |

Each document's `analysis` contains `documentId`, nullable `latestAnalysisId`, nullable `lastSuccessfulAnalysisId`, `status`, nullable `reason` and `canPoll`. Latest means highest analysis ID for that document; last successful means the highest analysis ID with a published completion. Re-splitting excludes superseded documents from this summary without deleting their extraction history.

Document analysis statuses:

- `ready`: latest attempt has published results; polling stops.
- `failed`: a processing/validation error exists, or the native workflow completed without publishing a result; polling stops.
- `analyzing`: work is pending, including existing prompt work when step configuration later becomes unavailable; polling may continue with a client deadline.
- `not_started`: no attempt exists and extraction is configured; no running work is implied and polling stops.
- `blocked`: extraction is unavailable and there is no active prompt work for this attempt; polling stops. Older successful results remain addressable.

Aggregate extraction statuses also include `waiting_for_documents`, `partial_failure` and `incomplete`. `partial_failure` means some latest document attempts succeeded and others failed. `incomplete` means some succeeded while others are missing or blocked, without an active attempt. Active work takes precedence and yields `analyzing`. An intake failure or a completed split with no documents yields `failed`; neither causes endless waiting.

Machine reasons currently include `analysis_not_requested`, `extraction_unavailable`, `analysis_failed`, `completed_without_result`, `intake_failed`, and `no_documents`. User-facing error details still come from the corresponding upload/analysis result. A missing target finding is not a failure code or proof of absence.

An abbreviated synthetic example demonstrates a failed latest attempt with retained successful history:

```json
{
  "uploadId": 8,
  "ocrStatus": "ready",
  "splitStatus": "ready",
  "extraction": {
    "status": "failed",
    "canPoll": false,
    "totalDocuments": 1,
    "readyDocuments": 0,
    "failedDocuments": 1,
    "pendingDocuments": 0,
    "reason": null
  },
  "documents": [
    {
      "id": 12,
      "title": "Services agreement",
      "firstPage": 1,
      "lastPage": 4,
      "analysis": {
        "documentId": 12,
        "latestAnalysisId": 42,
        "lastSuccessfulAnalysisId": 37,
        "status": "failed",
        "reason": "analysis_failed",
        "canPoll": false
      }
    }
  ]
}
```

Fetch the existing extraction response and select analysis 42 for its failure details or explicitly offer analysis 37 as an earlier successful result. Do not label analysis 37 as the current attempt.

## Compatibility and remaining work

These additions require no database migration, seed overwrite, new provider calls or frontend edits. Existing extraction result bodies, snapshot reads, metrics and POST request-key semantics remain compatible. The legacy catalog string is retained alongside structured metadata. The upload's existing `ready` status still means OCR/splitting completion, not overall analysis completion.

History pagination/filtering, a single-analysis detail endpoint and opt-in diagnostic responses remain potential future improvements. The extraction history endpoint still returns all attempts. The summary avoids loading those full results merely to poll progress, but it is not a historical-results pagination API.

The API additions are covered by pure contract/state tests and an isolated PostgreSQL endpoint test covering owner scoping, SQL projection, latest/fallback selection, blocked state, catalog serialization and exclusion of document/prompt text. These tests do not run OCR or external models. Runtime deployment and the frontend journey still need verification in their respective sessions.

## Source references

- [Extraction endpoint implementation](../AIESummit.Api/ExtractionEndpoints.cs).
- [Upload/OCR endpoint implementation](../AIESummit.Api/UploadEndpoints.cs).
- [Route registration](../AIESummit.Api/Program.cs).
- [Extraction OpenAPI paths](../AIESummit.Api/ApiSpec/Paths/extractions.json) and [schemas](../AIESummit.Api/ApiSpec/Components/extractions.json).
- [Existing client API/session](../web/src/api.ts) and [reader](../web/src/document-viewer.tsx).
- [Backend entities, parameters and validation](contract-extraction.md).

## Evidence matching policy

Source highlighting remains exact: `quote` is the actual snapshot substring between the returned offsets. `modelQuote`, `matchMethod` and `matchScore` identify whether anchoring used an exact quote or the 50% word-overlap fallback. See [evidence matching](evidence-matching.md). Display approximate matches as such; matching overlap does not establish semantic correctness.
