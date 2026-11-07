# Worker pack and queue configuration

Page-section analysis and the page reader are implemented. See [the current page-analysis guide](../docs/page-analysis.md) for the `OcrPage` LLM subject, Gemini APIInfo, additive schema upgrade, queues, evidence validation and Admin checks; the detailed split/OCR guidance below remains applicable to that original workflow.

Framework source links in this guide are optional upstream references at the bundled revision and may require maintainer access. Normal development consumes `dependencies/blueshift`; no framework source checkout is required.

The application runs its agents in the shared `Blueshift.Agents.Worker` executable. The Docker worker image contains the application packs and [worker.overlay.json](worker.overlay.json). There is no local AIESummit worker executable or CLI project.

Read [deployment guidance](AGENTS.md) for the whole stack, [agent guidance](../AIESummit.Agents/AGENTS.md) for processing contracts and [QueueRecovery.AGENTS.md](../AIESummit.Agents/QueueRecovery.AGENTS.md) for recovery and execution coordination details.

## Packed files and entrypoint

[Dockerfile](Dockerfile) publishes `AIESummit.Agents` into `/out/packs`, copies it to `/packs` and extracts the bundled `dependencies/blueshift/worker-linux-x64.tar.gz` into `/app` in the worker image. The Domain assembly is included with the agent pack's published dependencies.

The entrypoint is equivalent to the following inside the container.

```text
dotnet Blueshift.Agents.Worker.dll --config /config/worker.overlay.json --enqueue-objects false
```

The overlay is copied into the image during build. It is not mounted from the host in current Compose configuration. Changing it requires rebuilding and recreating the worker container. The emulator configuration is mounted separately and has a different deployment lifecycle.

| Overlay key | Current value | Purpose |
| --- | --- | --- |
| `domainPack` | `/packs/AIESummit.Domain.dll` | Assembly containing the persistent model |
| `agentPack` | `/packs/AIESummit.Agents.dll` | Assembly containing application agents and pack startup |
| `dbContext` | `AIESummit.Domain.AIESummitDb` | Fully qualified application EF context |
| `connectionStringName` | `AIESummitDb` | Configuration lookup under `ConnectionStrings` |
| `venturePrefix` | `aiesummit` | Queue naming namespace for the application |
| `queueRegexGroup` | `^aiesummit-dev-(uploadedfile\|llmprompt)-` | Receiver filter for the two current queued entity types |

The actual JSON regex uses `(uploadedfile|llmprompt)` with ordinary alternation. The escaped table separator above is Markdown formatting. Do not introduce a literal backslash before the pipe when copying the expression into configuration.

## Configuration resolution

The pinned `WorkerConfig` loader accepts overlay keys `domainPack`, `agentPack`, `dbContext`, `connectionStringName`, `venturePrefix`, `connectionFallbackEnv`, `appsettingsPaths` and `queueRegexGroup`. Current AIESummit configuration uses only the six keys listed above.

Explicit CLI options override corresponding overlay values. Relative pack and configured appsettings paths resolve relative to the overlay directory. The current absolute container paths avoid ambiguity. The loader also discovers optional appsettings files beside the overlay according to the host environment.

Worker program configuration loads resolved appsettings files, then environment variables, and installs the resolved connection string under the selected name. ASP.NET-style double underscores in Compose become configuration section separators. Do not assume the worker automatically reads the API's project-local appsettings files.

`Environment__EnvID=dev` controls the queue environment segment. It is distinct from `DOTNET_ENVIRONMENT` or `ASPNETCORE_ENVIRONMENT`, which participate in host/appsettings selection. The API currently hardcodes its publisher environment to `dev`, so changing only the worker environment would separate receivers from published queues.

Listening startup validates the application connection string, queue enablement, queue connection string and regex syntax. Missing pack files or an incorrect context type fail during pack loading. This is a different system from Horus's centralized `Config`, `AgentHostSection` and hierarchical masked settings table.

## Current connections and services

Compose supplies `ConnectionStrings__AIESummitDb` pointing to service `postgres`, database `aiesummit`. This is the application database for uploads, pages, documents, prompts, configuration and related framework rows.

The Service Bus emulator uses service `sql`, a separate SQL Server instance, for emulator storage. Do not point `AIESummitDb` at SQL Server to repair an emulator problem. The volumes are separate as well, with application data in `database` and emulator backing data in `queue-db`.

The queue connection points to Compose service `emulator` with `UseDevelopmentEmulator=true`. The emulator's configured namespace is `sbemulatorns`. That namespace name is different from application venture prefix `aiesummit` and queue environment `dev`. Preserve their separate meanings during troubleshooting.

Current emulator credentials are local development configuration. Do not reuse them as a production credential strategy or print a real queue connection string in diagnostics. Foundry credentials are initially persisted through API bootstrap and read by processing through configured APIInfo records. The worker does not require copying those secret values into its overlay.

## Declared queues and eligible states

[servicebus.json](servicebus.json) declares eight queues under `UserConfig.Namespaces`.

| Queue | Subject field/state |
| --- | --- |
| `aiesummit-dev-uploadedfile-ocrstatus-new` | Upload OCR `New` |
| `aiesummit-dev-uploadedfile-llmstepstatus-clearandcheckforprompts` | Upload step `ClearAndCheckForPrompts` |
| `aiesummit-dev-uploadedfile-llmstepstatus-checkforprompts` | Upload step `CheckForPrompts` |
| `aiesummit-dev-uploadedfile-llmstepstatus-temporaryerror` | Upload step `TemporaryError` |
| `aiesummit-dev-llmprompt-processingstatus-new` | Prompt `New` |
| `aiesummit-dev-llmprompt-processingstatus-tobereprocessed` | Prompt `ToBeReprocessed` |
| `aiesummit-dev-llmprompt-processingstatus-temporaryerror` | Prompt `TemporaryError` |
| `aiesummit-dev-llmprompt-processingstatus-readytoparse` | Prompt `ReadyToParse` |

These names encode venture, environment, entity, status property and state in lowercase. The regex accepts both current entity prefixes and their status/state suffixes. It does not create missing queues or make an otherwise ineligible database row eligible.

The namespace has an empty Topics list. Each declared queue uses the same settings.

- `DefaultMessageTimeToLive` is `PT1H`, one hour.
- `DeadLetteringOnMessageExpiration` is true.
- `LockDuration` is `PT1M`, one minute.
- `MaxDeliveryCount` is 10.
- `RequiresDuplicateDetection` is false, despite a configured `PT20S` history window.
- `RequiresSession` is false and forwarding destinations are empty.

Use the configuration's actual ISO-8601 duration values. Do not confuse the one-hour message TTL with the one-minute message lock or the application's recovery interval. Expiration and redelivery settings do not implement application-level retry policy on their own.

The emulator file currently uses `UserConfig.Logging` with `Type` set to `Console`. Keep this exact schema for the pinned emulator image `2.0.1`. Do not rename it to `LoggingConfig` by copying another emulator-version example without validating the selected image's contract.

## Publication, receivers and recovery

The API registers publishers without receivers. In normal listening mode, the worker registers both publishers and receivers so processing can trigger subsequent work. The shared host defers receiver startup while constructing and activating agents, avoiding callbacks during agent construction.

`--enqueue-objects false` disables the host's initial pending-object enqueue pass. It does not disable ordinary save-triggered publication or the application's separate periodic recovery service.

`AgentPack` registers `UploadQueueRecovery` only when `AIESUMMIT_QUEUE_RECOVERY` equals `true`. Compose sets that value for the worker. After each 45-second timer tick, recovery asks enabled `OcrAgent`, `LLMStepAgent<UploadedFile>` and `LlmPromptAgent` instances to enqueue pending objects. It does not run immediately on service construction.

This recovery addresses persisted eligible work after emulator queue recreation or missed publication. It does not automatically reset permanent errors, clear old OCR pages or repair invalid provider settings. Review eligibility and output cleanup before forcing a record back into a processing state.

Message locks, framework execution coordination and database state checks protect different boundaries. Consult the recovery companion and pinned coordination implementation before changing lease behavior or adding multiple worker instances. Do not claim exactly-once provider calls or exactly-once document application from queue settings alone. Duplicate publication, redelivery and process failure remain relevant test cases.

## Build and startup verification

From the repository root, `docker compose build worker` rebuilds the worker image and copied packs/overlay. `docker compose up -d worker` creates or recreates the selected service and its declared dependencies as needed. Read [Startup.AGENTS.md](../Startup.AGENTS.md) for the supported whole-stack startup workflow and required `.env` values.

The worker waits for the API to be healthy and for emulator/OpenSearch services to be started. The API health check only proves its HTTP process is responding after startup. `service_started` does not prove the broker or OpenSearch is ready to accept requests. The worker has `restart: on-failure`, so startup races can appear as repeated exits rather than a permanent configuration change.

Use `docker compose ps` and bounded logs such as `docker compose logs --tail 100 worker emulator` to identify the failing stage. Avoid dumping resolved environment configuration when it contains provider or queue secrets.

Check startup in this order.

1. Confirm the expected worker image was rebuilt after pack or overlay changes.
2. Confirm the overlay exists at the configured path and both assemblies/context names match published output.
3. Confirm the API completed schema/bootstrap startup and the worker uses the same application database.
4. Confirm SQL Server and the emulator started and accepted the mounted queue configuration.
5. Confirm queue enablement, venture prefix, environment and regex agree with API publishers and declared queue names.
6. Confirm stored agent configurations are enabled and the relevant subject has an eligible persisted status.
7. Confirm one known upload progresses through OCR, prompt creation and document output, with correlated logs visible in Admin.

A running process is not processing readiness. A matching queue name is not proof that a provider call succeeded. Inspect database output and telemetry for the same upload ID before declaring the workflow healthy.

## Extending the worker configuration

When adding an agent or queued subject, update the application pack registration, persisted config seed, receiver filter and emulator queue inventory together. A new subject excluded by `(uploadedfile|llmprompt)` will not be received under the current filter.

Preserve state-name parity with the pinned framework rather than manually guessing queue suffixes. Verify initial and retry states, review whether recovery should include the agent, and define rerun cleanup. Keep API publication activation in scope when the API creates the new subject.

For a new environment, coordinate API and worker environment values, venture prefix, broker namespace and queue provisioning. Changing `ASPNETCORE_ENVIRONMENT` alone does not rename the application queues. Avoid broadening the regex to unrelated ventures just to make a message visible.

Validate a fresh startup, retained eligible work after restart and duplicate/redelivered work. Use the [smoke guide](../scripts/smoke.AGENTS.md) for the live provider path and [Telemetry.AGENTS.md](../AIESummit.Agents/Telemetry.AGENTS.md) for evidence. Do not delete retained application or emulator volumes as a routine diagnostic step.

## Sources and legacy translation

Current evidence is [worker.overlay.json](worker.overlay.json), [servicebus.json](servicebus.json), [compose.yaml](../compose.yaml), [Dockerfile](Dockerfile), [SplitStep.cs](../AIESummit.Agents/SplitStep.cs) and [UploadQueueRecovery.cs](../AIESummit.Agents/UploadQueueRecovery.cs). Configuration contracts are in the pinned worker's [WorkerConfig.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Agents.Worker/WorkerConfig.cs), [WorkerHostOptions.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Agents.Worker/WorkerHostOptions.cs), [StartupValidator.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Agents.Worker/StartupValidator.cs) and [Program.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Agents.Worker/Program.cs).

The full historical `../../horus/Voiston.AgentHostCore/Configuration/AGENTS.md` supplies the principle of validating configuration before execution. Its `Config` inheritance, settings sections and diagnostic-table guarantees are not the current worker loader.
