# Execution telemetry and upload correlation

Page-section analysis and the page reader are implemented. See [the current page-analysis guide](../docs/page-analysis.md) for the `OcrPage` LLM subject, Gemini APIInfo, additive schema upgrade, queues, evidence validation and Admin checks; the detailed split/OCR guidance below remains applicable to that original workflow.

Framework source links in this guide are optional upstream references at the bundled revision and may require maintainer access. Normal development consumes `dependencies/blueshift`; no framework source checkout is required.

Read this guide when investigating OCR or splitting runs, changing execution correlation, or extending operational diagnostics. Start with [UploadExecutionContext.cs](UploadExecutionContext.cs), [compose.yaml](../compose.yaml), and the shared Admin execution endpoints. Business state, retained execution history, and worker liveness answer different questions.

## Current telemetry path

The application agent pack registers `UploadExecutionContext` as `IExecutionContextResolver`. Framework execution scopes supply per-invocation identity and state. The resolver adds a venture root identifier and, for supported prompt subjects, the step tag. `AgentLogFields` writes those identifiers into log documents.

The local Compose configuration enables the OpenSearch log provider for services using the shared application environment. The shared Admin reads that provider and displays Agent Operations at `/agents`. The browser route is configured in the Operations menu of [deploy/admin.json](../deploy/admin.json), alongside the `LLMPrompt` entity entry.

This path does not configure Azure Monitor Workbooks or a Log Analytics workspace. The Horus observability guide is useful as a reference for disciplined inspection, but its subscription, resource group, KQL tables, and workbook update commands do not describe this deployment.

## Correlation fields

Keep direct execution identity and root upload identity separate.

| Resolver input | Returned root type | Returned root ID | Returned step tag |
| --- | --- | --- | --- |
| `UploadedFile`, upload ID | `UploadedFile` | The supplied upload ID | None from this resolver |
| `LLMPrompt`, supported prompt ID | `UploadedFile` | Prompt's `DocumentID` | Prompt's associated `LLMStep.Tag` |
| `LLMPrompt` with no matching row or another `DocumentType` | None | None | None |
| Any other subject type | None | None | None |

The prompt query explicitly requires `prompt.DocumentType == nameof(UploadedFile)`. It selects only `DocumentID` and the step tag. It does not read OCR Markdown, model response bodies, credentials, or file bytes. Preserve that narrow projection when adding another supported subject.

An upload subject is mapped directly without a database lookup. The resolver does not validate ownership, existence, or lifecycle state for that path. It is diagnostic enrichment, not authorization.

For prompt runs, the direct execution entity remains `LLMPrompt` with the prompt's own ID. `RootEntityType` and `RootEntityId` connect it back to the upload. Do not substitute an upload ID into a query for a prompt's direct entity ID.

The framework enrichment method applies only when an execution scope is active and services are available. It sets root fields from the resolver, and fills `StepTag` only if the scope does not already have one. A selected-step request can therefore have its own step identity before this lookup. `RequestedStepTag` is another distinct field recording requested execution context.

`ExecutionLogScope` carries an execution ID formatted as a GUID without separators. It also contains direct entity type/ID, status field, trigger, agent name, start time, queue/message identifiers, attempt, and retry lineage. It uses `AsyncLocal` to keep concurrent invocation context separate and restores the previous scope on disposal.

## Local provider configuration

| Setting in Compose | Current value |
| --- | --- |
| `AgentLog__Enabled` | `true` |
| `AgentLog__Provider` | `OpenSearch` |
| `AgentLog__Endpoint` | `http://opensearch:9200` |
| `AgentLog__IndexName` | `aiesummit-agent-logs` |
| `Environment__EnvID` | `dev` |

The OpenSearch service uses a persistent `telemetry` volume. It runs as a single node with its security plugin disabled in this local Compose arrangement. The service has no host port mapping. Admin is the normal protected inspection surface through the loopback-bound frontend at `http://localhost:13088`.

Do not expose this local OpenSearch service publicly to make queries easier. Production authentication, transport, network placement, and retention would require an explicit deployment design. The current Compose volume is durable storage, not a configured retention or rollover policy.

OpenSearch queries are scoped to the provider's configured environment. Matching the index name alone is insufficient if writer and reader environments differ. Check the API, worker, and Admin settings together when one service sees no retained records.

Worker startup depends on the OpenSearch service being started, not a demonstrated search health check. A started container does not prove the log provider can initialize an index or execute queries.

## Protected inspection endpoints

The route group `/admin/v1` applies the shared Admin read policy to GET requests. Application Firebase credentials for upload endpoints are a separate authentication context. Use an authorized Admin session or the local Admin token setup. Never paste the token into a URL, log, issue, or source file.

| Route | Use |
| --- | --- |
| `GET /admin/v1/agents/executions` | Distinct retained execution summaries |
| `GET /admin/v1/agents/events` | Individual recorded state events |
| `GET /admin/v1/agents/runs/{jobId}/logs` | Retained lines for one execution ID |

The summary endpoint accepts `entityType`, `entityId`, `agentName`, `statusField`, `state`, `fromUtc`, `toUtc`, and `take`. Its default take is 25. The event endpoint also accepts `stepTag` and `executionId`, and defaults to 100. Do not assume extra query parameters filter summaries merely because the underlying query model has those fields.

Common query validation requires `take` from 1 through 200, a positive entity ID, an entity type when an ID is supplied, and a UTC range whose start is not after its end. Identity filter strings have a 256-character limit. An event execution ID must be a GUID without separators.

Recognized state values are `Queued`, `Running`, `Succeeded`, `Failed`, `Cancelled`, `TimedOut`, `Busy`, `Deferred`, `Skipped`, and `Ineligible`. Use the exact values rather than the user-facing upload labels such as `ready` or `splitting`.

The run-log route validates the same GUID format and requests up to 500 lines through `AgentLogService`. Unsupported history queries can return 503. Invalid filters return 400. An authentication failure is not evidence that the underlying execution or log provider failed.

## How to read summaries and events

The execution summary describes retained evidence for an invocation. It does not assert that a process is currently alive. A retained `Running` event may be the last known state of a process that later stopped without recording a terminal event. Conversely, an upload may finish before a particular reader sees its completion history.

The reducer prefers the newest retained event whose state is neither `Queued` nor `Running`, falling back to the newest event when no such event exists. `LastRecordedUtc` records the newest retained activity. A later nonterminal line does not automatically erase terminal evidence.

The OpenSearch summary implementation inspects at most 201 execution buckets, derives summaries from latest and terminal evidence, and then applies summary state/time filters. The API returns `hasMore`, `isBounded`, and `inspectedExecutions` alongside availability and items. These are limits on inspected evidence, not a complete historical count.

Event filtering answers which recorded events match a state and time. Summary filtering answers which reduced execution histories match. Use events to inspect transitions and summaries to locate distinct invocations. Do not equate the count of state events with the number of executions.

Direct `entityType`/`entityId` filtering does not search `rootEntityType`/`rootEntityId`. To locate prompt work for an upload, inspect an appropriately bounded prompt-run result and compare its root fields, or query the known prompt ID directly. Increasing `take` cannot exceed the inspection boundary or guarantee discovery in a busy environment.

## Availability and timing

An empty summary result can mean there are no matching retained runs, the provider is unavailable or lacks history support, the chosen environment is different, or bounded inspection omitted older work. Inspect the response's availability and boundary fields before drawing a conclusion.

`AgentLogService` returns unavailable summary metadata when history is unsupported or logging is unavailable. Endpoint query failures can instead return HTTP 503. Keep these cases separate from an ordinary successful query with no matches.

Allow a short bounded wait for execution, emission, and search visibility before declaring telemetry missing. The current OpenSearch writer explicitly sends `refresh=true` when indexing documents and supports explicit refresh on flush. Do not describe every delay as an unbounded eventual-indexing guarantee. A consistent absence after the operation completes warrants checking write failures, environment/index mismatch, and correlation fields.

The database upload state remains the business result. A successful OCR run can coexist with a later split failure. A successful invocation can also finish without creating a new prompt because no additional eligible step exists. Inspect both the captured invocation and the resulting domain state.

## Bounded investigation workflow

1. Start from the upload ID, approximate UTC time, visible upload status, and intended operation. Preserve those identifiers without copying source contents.
2. Read the upload and prompt state through authorized application/Admin paths. Determine whether the issue is OCR, step planning, prompt execution, connection, or only telemetry visibility.
3. Query execution summaries for `entityType=UploadedFile` and that upload ID. Inspect `OcrAgent` and the run whose status field is `LLMStepStatus`.
4. Locate relevant prompt rows or bounded `LlmPromptAgent` summaries. Confirm both `rootEntityType == UploadedFile` and the expected root ID when performing manual correlation.
5. Select one execution ID and read its event sequence or run logs. Preserve queue message ID, attempt, retry lineage, state, and timestamps when comparing repeated runs.
6. If the API reports unavailable history, check Admin provider configuration and service health. If only correlation is missing, check resolver registration, prompt `DocumentType`, the step association, and active execution scope.
7. Broaden the time range or result scope only as needed. Report whether the result was bounded and what evidence remains missing.

Read-only investigation does not require starting a forced run, retrying a prompt, resetting processing state, deleting an index, or clearing a volume. Those actions change the evidence or workflow and require a concrete task reason. Inspect before initiating recovery.

For container diagnostics, use bounded log tails and a time window for the relevant service. Avoid dumping the entire Compose environment or unrestricted application logs, which can contain connection details or request data. Report exception type and HTTP/provider status where available rather than exposing document content.

## Existing smoke coverage

[scripts/smoke.ps1](../scripts/smoke.ps1) reads the local Admin token from `artifacts/admin.token.local.txt` and sends it as a bearer header. The token file is local credential material, not a documentation fixture.

After its upload and document checks, the script verifies that an anonymous request to the execution summary endpoint returns 401. It then waits up to 15 seconds, polling once per second for retained successful runs.

The script queries upload summaries by direct `UploadedFile` ID and queries `LlmPromptAgent` summaries with `take=50`. It checks for a successful OCR invocation, a successful run with status field `LLMStepStatus`, and a successful prompt invocation whose `rootEntityId` matches the upload. The prompt assertion does not independently check root type or step tag.

That is evidence of API-level successful-run visibility and one root-ID correlation path. It is not browser interaction coverage for the Agent Operations page, a complete role/authorization matrix, every failure state, retention behavior, high-volume history discovery, or end-to-end cancellation/retry correctness. Do not describe it as those tests.

The 15-second polling window accommodates asynchronous completion and observation. A smoke failure should retain enough non-sensitive identifiers to distinguish a failed business workflow from missing telemetry. Under heavy activity, its recent 50-prompt selection may also miss the relevant prompt even when older retained evidence exists.

## Extending correlation or telemetry

Keep `IExecutionContextResolver` read-only and identifier-only. Add an explicit branch for a newly supported subject, select only the necessary IDs/tag, and return empty context for unsupported types. Do not serialize entity graphs or retrieve file bytes inside enrichment.

For a new prompt subject, validate the stored `DocumentType` before using `DocumentID` as a root ID. Update the subject registry and policy in their own appropriate implementation work. Correlation alone does not make that subject executable.

Preserve direct entity identity and root identity as separate fields. Do not reuse `executionId` for a database entity ID, queue message ID, or retry parent ID. Avoid identifying executions by user-visible labels that can change.

Verify a changed resolver against an upload subject, a valid prompt, an unsupported prompt subject, a missing prompt, and concurrent execution scopes. Check that the preexisting step tag is retained when enrichment supplies another value and that source text/secrets never appear in resolver output.

Verify configuration changes with a real retained event, an environment-scoped read, and an authenticated Admin query. Exercise unavailable-provider handling and bounded-result indicators if changing operational UI behavior. Keep browser checks explicit when a UI change is involved.

## Source anchors

- [UploadExecutionContext.cs](UploadExecutionContext.cs) defines the venture root correlation. [SplitStep.cs](SplitStep.cs) registers it.
- [compose.yaml](../compose.yaml), [admin.json](../deploy/admin.json), and [smoke.ps1](../scripts/smoke.ps1) define local configuration, navigation, and current assertions.
- [IExecutionContextResolver.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Agents/Infrastructure/Log/IExecutionContextResolver.cs), [ExecutionLogScope.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Agents/Infrastructure/Log/ExecutionLogScope.cs), and [AgentLogFields.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Agents/Infrastructure/Log/AgentLogFields.cs) define enrichment and emitted identifiers.
- [ExecutionEventEndpoints.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Admin.Api/Infrastructure/Endpoints/ExecutionEventEndpoints.cs), [AdminEndpointExtensions.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Admin.Api/Infrastructure/Endpoints/AdminEndpointExtensions.cs), and [AdminSecurity.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Admin.Api/Hosting/AdminSecurity.cs) define routes and protection.
- [AgentLogService.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Admin.Api/Services/AgentLogService.cs), [ExecutionEventQuery.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Agents/Infrastructure/Log/ExecutionEventQuery.cs), [ExecutionHistory.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Agents/Infrastructure/Log/ExecutionHistory.cs), and [OpenSearchLogManager.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Agents/Infrastructure/Log/OpenSearchLogManager.cs) define query bounds, reduction, availability, and provider behavior.

The full Horus observability guide was reviewed for its fetch-before-change workflow and operational depth. Do not copy its Azure resource identities or workbook mutation commands into AIESummit instructions. Framework code remains reference material for venture-specific diagnostics.
