# AIESummit.Agents

This .NET 10 class library contains the application's OCR adapter, document-splitting policies, pack registration, recovery service and execution correlation. It references `AIESummit.Domain` and the pinned BlueShift agent framework. The long-running receiver is the bundled `Blueshift.Agents.Worker` host deployed by [deploy/Dockerfile](../deploy/Dockerfile). There is no application-specific executable host or local CLI project.

Read this guide before changing background processing. The application sources and pinned framework contracts take precedence over analogous implementations in Horus or Cite8.

## Reading routes

| Work | Read next |
| --- | --- |
| OCR request, response validation and image persistence | [OcrAgent.AGENTS.md](OcrAgent.AGENTS.md) |
| Step definitions, subject hydration and registered policies | [SplitStep.AGENTS.md](SplitStep.AGENTS.md) |
| Structured output validation and document replacement | [Metadata.AGENTS.md](Metadata.AGENTS.md) |
| Queue recovery, eligibility and replay | [QueueRecovery.AGENTS.md](QueueRecovery.AGENTS.md) |
| Upload correlation and Admin processing evidence | [Telemetry.AGENTS.md](Telemetry.AGENTS.md) |
| Persisted subject and prompt relationships | [Domain guide](../AIESummit.Domain/AGENTS.md) and [LLM contract](../AIESummit.Domain/IAmLLMCapable.AGENTS.md) |
| Worker pack loading and queue configuration | [Worker deployment guide](../deploy/Worker.AGENTS.md) |
| Effective configuration and seed behavior | [Bootstrap guide](../AIESummit.Api/Bootstrap.AGENTS.md) |
| End-to-end acceptance | [Smoke guide](../scripts/smoke.AGENTS.md) |

## Source map

| File | Responsibility |
| --- | --- |
| `OcrAgent.cs` | `OcrAgent`, `MistralOcrBrowser` and OCR response records |
| `SplitStep.cs` | `SplitStep`, `SplitResult`, `PageRange`, `SplitPolicy` and `AgentPack` |
| `UploadQueueRecovery.cs` | Optional periodic re-enqueue of eligible upload-processing work |
| `UploadExecutionContext.cs` | Correlation from uploads and prompts to the originating upload |

Keep transport concerns in the API browser, state transitions in the agent or framework policy, and structured document validation in metadata. A new feature may justify splitting these files, but preserve registrations and deployed assembly identities when doing so.

## Publishers and receivers

| Host | Queue role | Application registration |
| --- | --- | --- |
| `AIESummit.Api` | Publishes eligible work, does not register receivers | `AddAgentPackServices`, `AddBlueshiftAgents` and `RootBaseAgent.ActivateAllAgents` |
| Shared BlueShift worker | Receives processing work and supports subsequent publication | Loads the Domain and agent packs through `worker.overlay.json` |

The API's `RegisterPublishers = true` and `RegisterReceivers = false` are explicit in [Program.cs](../AIESummit.Api/Program.cs). Do not add a second background OCR receiver to the API to compensate for an unconfigured worker. Inspect queue enablement, registration, persisted eligibility and receiver health.

The worker overlay loads `/packs/AIESummit.Domain.dll`, `/packs/AIESummit.Agents.dll` and context `AIESummit.Domain.AIESummitDb`. It uses connection-string name `AIESummitDb`, venture prefix `aiesummit` and queue filter `^aiesummit-dev-(uploadedfile|llmprompt)-`. A new subject type can require changes to this filter and the emulator queue configuration even when its code builds successfully.

The Docker entrypoint passes `--enqueue-objects false`. Compose enables `AIESUMMIT_QUEUE_RECOVERY` for the worker. Recovery waits on a 45-second timer and asks the enabled OCR, upload step and prompt agents to enqueue pending objects. This application therefore has an explicit periodic recovery path. Do not copy Cite8's statement that its worker never periodically re-sweeps the database into this project.

The queue flag, static agent enablement, stored configuration enablement and eligible entity state are separate conditions. Diagnose each one. A running container does not prove an eligible record was published or that a receiver consumed its message.

## Current processing flow

1. The authenticated API creates an `UploadedFile` containing its Firebase owner, accepted bytes and initial processing state. Persistence and registered publication hooks make eligible OCR work available to the worker.
2. `OcrAgent` selects uploads in `ProcessedStatus.New` through `OcrStatus`, invokes the configured browser and validates the response.
3. OCR constructs `OcrPage` and `PageImage` rows, updates `PageCount`, marks OCR processed and sets `LLMStepStatus` to `CheckForPrompts`.
4. Framework `LLMStepAgent<UploadedFile>` uses registered policies and persisted step configuration to create prompt work for the upload.
5. Framework `LlmPromptAgent` and `LlmPromptWorker` execute the prompt and deserialize the registered structured response.
6. `SplitResult.ConnectTo` validates ordered, complete, nonoverlapping page ranges before staging replacement `Document` rows.
7. The API projects persisted processing state and documents. Admin shows configuration and correlated processing evidence through the shared framework.

Preserve the distinction between OCR status, subject step status and each prompt's processing status. A successful OCR call does not mean the document split completed. A user-facing status should continue to derive from the established persisted workflow rather than a new in-memory flag.

## API-backed agent pattern

`OcrAgent` inherits `BaseAPIAgent<UploadedFile, ProcessedStatus, OcrAgentConfig>`. Its static constructor sets async execution, the status selector, eligibility and terminal states. It implements the current asynchronous signature returning success, subject and repository. The synchronous override deliberately throws because this agent is async.

Use the checked-in implementation as the signature reference when introducing another agent. Horus's illustrative `BaseAgent` snippets are conceptual and are not copy-ready method declarations for this pinned version.

`OcrAgentConfig` lives in the Domain project and derives from `BaseAgentConfig`. Bootstrap initially seeds its `AgentClassName` as `OcrAgent`. Framework LLM configuration rows use class names `LLMStepAgent` and `LlmPromptAgent`. Keep these names aligned with the activated implementations.

`GetApiBrowser<MistralOcrBrowser>()` resolves browser configuration using `APIIdentifier = "mistral-ocr"`. `MistralOcrBrowser` uses `APIInfo`-supplied base URI, app key and app secret. The current app secret holds the OCR model/deployment value. Provider calls belong behind this configuration boundary, with no embedded credentials.

The browser sends a data URL using `document_url` for PDFs and `image_url` for images, requests embedded image base64 and uses the `api-key` header. It uses a shared `HttpClient`. Its token-repair methods return false and there is no custom OCR retry loop. Do not promise automatic recovery for authentication or provider failures based on unrelated Horus integrations.

## OCR validation and persistence

OCR accepts 1–30 returned pages and requires zero-based response indexes to exactly match the ordered range. Persisted page numbers are one-based. Supported embedded image data URLs are decoded into framework `Image` rows and linked through `PageImage`. Unsupported or missing image data is skipped, while malformed accepted base64 can fail the operation.

The implementation constructs page and image lists before adding them to repositories. It then returns the updated tracked subject for framework persistence. Keep save ownership coherent when extending this path. Inserting intermediate saves can publish downstream work before the complete page graph is available.

Caught provider, cancellation, parsing and validation failures set a generic user-facing error and mark OCR `PermanentError`. Logs record exception type and HTTP status without dumping upload content or credentials. New failure handling should preserve useful operational evidence without exposing sensitive request bodies.

OCR does not currently remove existing pages before adding replacements. The database uniquely constrains page numbers per upload and references per page. Resetting a processed upload to `New` is not a complete replay strategy. Define cleanup and document invalidation before introducing rerun controls.

## LLM pack registration

`AgentPack` implements `IAgentPackStartup`, allowing the shared hosts to register application services without embedding AIESummit-specific behavior in BlueShift. It registers the following cooperating pieces.

- `LlmTypeRegistry` with subject `UploadedFile` and metadata `SplitResult`.
- `LlmCodeStepRegistry` with `SplitStep.Definition()` and stable tag `split-upload`.
- `LambdaManipulation` and `TemplateManipulation` with the application Domain assembly and namespace.
- `SplitPolicy` as both `ILlmStepSubjectPolicy<UploadedFile>` and `ILlmPromptPolicy`.
- `LLMPromptFactory`, whose input callback appends the hydrated `SplitInput` to the persisted step template.
- `LlmPromptWorker` and `UploadExecutionContext`.
- `UploadQueueRecovery` only when `AIESUMMIT_QUEUE_RECOVERY` equals the exact string `true`.

Keep the singleton policy stateless across requests. Resolve per-execution data through the supplied repository or metadata context. Do not retain a scoped context, tracked upload or mutable prompt in a singleton field.

The code-step registry establishes supported tag, subject and metadata bindings. It does not overwrite every stored step setting. Bootstrap creates missing configuration and preserves existing rows. Editing `SplitStep.Definition()` and rebuilding does not update an existing stored prompt template automatically. Inspect effective configuration when validating changes.

## Hydration and metadata application

The step policy reloads the upload from its repository with `LLMPrompts` included. It builds transient JSON from ordered pages and replaces `@` with a JSON unicode escape for the template path. Keep `SplitInput` transient and keep `LLMPrompts` mapped.

An ID-oriented subject is not a fully hydrated upload. Returning it unchanged from the policy can lose stored fields or processing state when saved. A new subject policy must deliberately reload its persisted fields and needed relationships before building input or changing status.

`SplitResult` derives from current `BaseMetadata` and receives `IMetadataExecutionContext`. Its `ConnectTo` method rejects unsupported targets, gaps, overlaps, reversed ranges, out-of-range pages, empty results and blank or oversized titles before removing existing documents. The resulting Markdown joins the selected inclusive page range.

Metadata methods stage repository changes for the framework's save path. Do not add provider calls, unrelated orchestration or independent commits to the document-application method. See the metadata companion for transaction boundaries and failure assertions.

`AIESummitDb` explicitly maps prompt ownership, permits a new prompt's `UsedModel` to be absent, normalizes required prompt strings and delegates saving to `EFBaseDbContext`. Preserve these protections and the framework's modification/publication hooks when changing repositories or save behavior. Database commit visibility and queue delivery are separate concerns, which is why restart recovery matters.

## Correlation and diagnostics

`UploadExecutionContext` returns the upload identity directly for upload subjects. For `LLMPrompt`, it looks up a prompt whose `DocumentType` is `UploadedFile`, then returns its originating document ID and step tag. Unknown subjects return an empty context.

Extend this resolver deliberately when adding another subject. Confirm that Admin links and traces resolve to the correct business object rather than merely recording a prompt ID. Compose configures OpenSearch logging through the shared `AgentLog` settings. Local code also persists framework activity/log entities through the Domain context.

Start investigations from one upload ID. Check its OCR and step states, persisted pages, prompt records, effective step/model configuration, receiver logs and correlated Admin activity. A health endpoint or successful upload request alone is not processing evidence. Use [Telemetry.AGENTS.md](Telemetry.AGENTS.md) for the detailed route.

## Adding or changing an agent

1. Identify the durable subject, status field, eligible states and completion/error behavior. Document whether reruns are safe and what output cleanup they require.
2. Add or reuse a Domain configuration type and map any new persisted entities. Review ownership and required relationships before defining execution logic.
3. Implement the verified current framework contract. Configure hydration and static eligibility once, with asynchronous external calls behind the appropriate browser/provider abstraction.
4. Register application policies and supporting types through `AgentPack`. For an LLM subject, add both subject and metadata bindings, prompt policy handling and effective step configuration.
5. Seed missing configuration idempotently without overwriting operator values. Keep provider credentials in `APIInfo` configuration and deployment secrets.
6. Review API publication activation, worker discovery, queue names, overlay filter and emulator queue definitions. Decide whether the recovery service should include the new agent.
7. Preserve commit-aware publication and required prompt mappings. Verify downstream work cannot observe an incomplete graph.
8. Extend execution correlation and diagnostics with identifiers and safe summaries. Avoid logging source documents, credential values or full provider requests.
9. Validate failure, replay and owner-isolation behavior at the appropriate boundary, then run the relevant end-to-end acceptance scenario.
10. Update the focused companion guides when contracts or operational behavior change.

## Build and verification boundaries

From the repository root, build the library with `dotnet build AIESummit.Agents/AIESummit.Agents.csproj`. Build the solution for changes that alter contracts shared with the API or Domain. Do not copy Horus's broad warning-suppression command as the normal application build procedure.

Use focused checks for deterministic boundaries such as OCR response validation, supported image formats, split range validation, hydration and repository application. Mock provider results can verify those behaviors but cannot prove a live endpoint's credentials, model selection or output quality.

Use the repository smoke workflow for the complete upload-to-document path. It exercises the configured stack, real OCR/LLM processing, persisted output and account isolation. Read its prerequisites before running it because provider calls consume real service resources. Verify database results and Admin evidence, not only a successful HTTP response.

Recovery changes need a restart or queue-loss scenario with persisted eligible work. Status changes need checks that completed work is not repeatedly selected. A new metadata contract needs both valid output and rejection cases that leave no partially applied document set. Run tests proportionate to the change. Documentation-only edits do not require live model calls.

## Reference projects and differences

The full Horus `Voiston.Agents/AGENTS.md` informs the agent/config/browser separation and explicit status lifecycle. Its clinical widgets, measurement factories, legacy API signatures, specialized page-container retries and build commands are not implemented AIESummit features.

Erick's Cite8 agent guide was read from commit `903626a9bb4ce471bccc28490b857886d6aa5c78` at `src/Cite8.Agents/AGENTS.md`. It provides the closer architectural reference for a venture class library loaded by the shared worker, API publishers without receivers, and pack-owned LLM registries. Its catalog seed tools, case subjects, scripts, no-periodic-sweep behavior and credential-seeding policy must not be copied as descriptions of this application.

Current evidence is [OcrAgent.cs](OcrAgent.cs), [SplitStep.cs](SplitStep.cs), [UploadQueueRecovery.cs](UploadQueueRecovery.cs), [UploadExecutionContext.cs](UploadExecutionContext.cs), [Program.cs](../AIESummit.Api/Program.cs), [Bootstrap.cs](../AIESummit.Api/Bootstrap.cs), [worker.overlay.json](../deploy/worker.overlay.json), [compose.yaml](../compose.yaml) and [deploy/Dockerfile](../deploy/Dockerfile). Use the bundled contracts and optional upstream citations when a framework contract needs clarification; source inspection requires a separate authorized maintainer checkout.
