# Pending-work queue recovery

Page-section analysis and the page reader are implemented. See [the current page-analysis guide](../docs/page-analysis.md) for the `OcrPage` LLM subject, Gemini APIInfo, additive schema upgrade, queues, evidence validation and Admin checks; the detailed split/OCR guidance below remains applicable to that original workflow.

Framework source links in this guide are optional upstream references at the bundled revision and may require maintainer access. Normal development consumes `dependencies/blueshift`; no framework source checkout is required.

`UploadQueueRecovery` restores queue stimuli for durable work that is still eligible for processing. It exists because the local Service Bus emulator recreates its queues when it restarts while PostgreSQL retains uploads and prompts. It also helps recover pending work after a commit/publication interruption.

Recovery does not rerun every upload, reset errors, rebuild missing application rows or guarantee exactly-once external calls. Read [OcrAgent.AGENTS.md](OcrAgent.AGENTS.md), [SplitStep.AGENTS.md](SplitStep.AGENTS.md) and [Telemetry.AGENTS.md](Telemetry.AGENTS.md) alongside this guide when diagnosing a stalled upload.

## Registration and schedule

`AgentPack.ConfigureServices` in [SplitStep.cs](SplitStep.cs) registers the hosted service only when this expression is true.

```csharp
Environment.GetEnvironmentVariable("AIESUMMIT_QUEUE_RECOVERY") == "true"
```

The comparison is case-sensitive. `True`, `TRUE`, `1`, an empty value and an unset variable do not enable the service. This reads the process environment directly, not an arbitrary configuration key supplied in an overlay.

`compose.yaml` sets `AIESUMMIT_QUEUE_RECOVERY: "true"` on `worker` only. API and Admin also use pack code for their own purposes, so do not move this setting into the shared environment anchor without evaluating duplicate recovery hosts. Registration happens when services are configured. Changing the environment afterward requires recreating or restarting the appropriately configured process.

The implementation creates a `PeriodicTimer` with a 45-second period and waits for its first tick before scanning. It does not perform an immediate scan on service startup. Each tick enumerates the current `RootBaseAgent.AgentInfos` and awaits each selected agent in sequence.

There is one loop per hosted service instance, not one task per agent or one overlapping scan per timer tick. A slow scan can delay subsequent work. The period is not a guaranteed 45-second maximum recovery time because startup, scan duration, queue readiness and receiver processing add latency.

## Exact scope

The service selects enabled agent metadata whose instance is one of the following types.

- `OcrAgent`
- `LLMStepAgent<UploadedFile>`
- `LlmPromptAgent`

For each selected instance it calls `EnqueuePendingObjects(queues)`. It does not invoke agent execution directly, inspect uploaded content or write a processing status itself.

The closed generic type is intentional. A future `LLMStepAgent<AnotherSubject>` is not included merely because it implements the same generic pattern. A new subject needs an explicit recovery-scope review as well as pack, queue and registry changes.

The prompt agent operates on the mapped `LLMPrompt` set. Recovery does not add a separate `DocumentType == UploadedFile` predicate. In the current application pack the registered business subject is `UploadedFile`, but do not assume that remains a recovery isolation boundary if more prompt subjects are introduced later.

## Why global startup catch-up is disabled

The worker target in `deploy/Dockerfile` runs the shared host with the following arguments.

```text
dotnet Blueshift.Agents.Worker.dll --config /config/worker.overlay.json --enqueue-objects false
```

This disables the shared host's initial general enqueue sweep. It does not disable queue consumption, normal persistence-triggered publication or this application's periodic hosted service.

The local model registers the configuration tables required for the upload flow. A broad framework sweep can visit unrelated built-in agents whose configuration tables are not included in `AIESummitDb`. The narrow service avoids assuming those unrelated agent models exist. Do not remove `--enqueue-objects false` as a generic fix for a stalled upload without checking which agents and tables the shared host will inspect.

The worker overlay loads `AIESummit.Domain.dll` and `AIESummit.Agents.dll`, selects `AIESummit.Domain.AIESummitDb`, uses connection string `AIESummitDb`, and limits receiver queue names with `^aiesummit-dev-(uploadedfile|llmprompt)-`. Compose supplies venture prefix `aiesummit` and environment `dev`.

Keep the overlay pattern, publisher naming and `deploy/servicebus.json` queue definitions aligned. A database row can be pending while its constructed queue name is excluded by the receiver filter. Adding an entity type to the recovery service alone does not make its queue routable.

## Framework eligibility checks

The pinned `BaseAgent.EnqueuePendingObjectsCore` opens a service scope and resolves the subject repository. It reads the corresponding configuration set and requires exactly one row. Zero rows or multiple rows cause an agent log entry and an early return. A single disabled configuration row also prevents enqueueing.

The outer service's `agent.Enabled` check is therefore only the first gate. The configuration check happens again inside the framework. An inspection path that shows the latest configuration row can look healthy while this enqueue method rejects the total row count. When investigating duplicates, inspect count and identities rather than deleting rows blindly.

Eligible rows satisfy both the agent's `WhereExpression` and the union of its `OriginalStates` and `TemporaryErrorStates`. Each candidate's current state determines its queue name. The framework applies the receiver queue filter before calling `IQueueManager.AddToQueue`.

| Agent | Eligible states in the current definition | Excluded terminal states |
| --- | --- | --- |
| OCR | `New` | `Processed`, `PermanentError` |
| Upload step | `ClearAndCheckForPrompts`, `CheckForPrompts`, `TemporaryError` | `Processed`, `PermanentError` |
| Prompt | `New`, `TemporaryError`, `ToBeReprocessed`, `ReadyToParse` | `Processed`, `Superseded`, `PermanentError` |

The step's waiting-for-prompt statuses are not a reason to reset it to `CheckForPrompts`. At that stage the pending prompt is the work item that may need recovery. Diagnose the current stage rather than treating every non-ready upload as an OCR problem.

The pending scan does not page or cap the result set in application code and does not compare each row against queue contents before adding it. Its cost grows with eligible backlog. It also does not implement an age threshold or backoff scheduler of its own. Temporary-error scheduling details belong to the framework queue/execution path, while this scan can offer eligible rows again each tick.

The framework awaits `AddToQueue` but this scan does not inspect its returned boolean before issuing its trace log. Absence of an exception or a “sending” trace is not proof of successful broker acceptance or eventual processing. Verify queue and execution evidence.

## Cancellation and failures

The timer wait receives `stoppingToken`. Cancellation during that wait terminates the background service through the normal cancellation path.

The per-agent catch handles exceptions only while `!stoppingToken.IsCancellationRequested`. It logs the agent type and the base exception type, then allows later agents in that scan to proceed. It does not log exception messages, connection strings, source documents or provider payloads.

The `EnqueuePendingObjects` call does not receive the service's cancellation token. An in-flight database enumeration or enqueue may therefore delay shutdown. If it throws after cancellation is requested, the filter declines to catch it. Do not describe shutdown as immediate or every cancellation as a warning-and-continue event.

Only the per-agent enqueue call is inside that try/catch. Failures in timer handling or outside that block are not covered by its warning policy. There is no explicit success summary or recovery counter emitted by this application service.

## Duplicate stimuli and execution leases

Repeated scans can generate duplicate stimuli for a row that remains eligible, including while a previous delivery is executing. Recovery does not acquire an execution lease before enqueueing. Coordination occurs when the framework dispatches work.

The queue callback obtains `SubjectExecutionLease` keyed by entity type, ID and status-field name. With PostgreSQL, it uses `pg_try_advisory_lock` on a dedicated nonpooled connection. Contention returns a busy metric instead of dispatching a second callback immediately. Lock acquisition failures do not fall back to unprotected relational execution.

Once it owns the lease, the callback reads current configuration and durable subject status. The current status must match the queue's target status. Even agents that hydrate their own graph get a durable status check, so an old message's default enum value is not trusted. A stale message can therefore be recognized after an earlier execution has moved the row forward.

The lease coordinates the same entity and status field across the supported database path. It is not an upload-wide lock across every related prompt or every status field. It also is not the queue's message lock, a durable completion record or a guarantee that an external provider call occurs only once.

A process can fail after a provider accepts a request but before its result is persisted. The eventual retry may call the provider again. Database uniqueness and status checks help constrain stored output, but OCR currently does not implement a general cleanup/upsert path for arbitrary manual reprocessing. Preserve these limitations in operational advice.

## Safe recovery diagnosis

Start with the upload ID and current durable state, not a bulk update. The app's user-facing `queued` status is a simplified projection and does not identify the exact broker or agent fault.

1. Confirm the local API, worker, PostgreSQL and emulator are running. Compose waits for API health but only for emulator service start, so initial connection failures can reflect broker readiness.
2. Inspect the upload's OCR state, LLM step state and relevant prompt states through the operator surface. Determine which agent owns the next transition.
3. Check that the agent is active and has exactly one enabled configuration row. Verify the required configuration table exists before interpreting an empty queue as lost work.
4. Confirm the worker process received the lowercase recovery flag and the intended overlay. Allow for the first 45-second timer tick and actual scan duration.
5. Check the queue name derived from subject and status against the configured receiver pattern and emulator definitions.
6. Inspect bounded worker logs and correlated Admin executions. A pending count, an enqueue trace and a successful execution are different observations.
7. If the row is permanently failed, inspect its cause. Recovery intentionally leaves it alone. Fixing provider credentials does not itself define a safe state/output reset.
8. Prefer a scoped, reviewed enqueue of an already eligible ID through an available Admin operation over resetting all statuses. The framework has `TryEnqueueByIdAsync`, but this repository does not expose a standalone application CLI command for it.

Useful read-only commands from the repository root are shown below.

```powershell
docker compose ps
docker compose logs --tail 100 worker emulator
```

Do not print a fully resolved Compose configuration or environment to diagnose one flag because those outputs can expose credentials. `docker compose config -q` validates configuration without dumping resolved values.

Avoid repeated worker restarts as a substitute for diagnosis. Restarting resets the recovery timer, and restarting the emulator can recreate the very queues being investigated. Do not delete volumes, mass-reset status columns or force provider calls merely to make a queue nonempty.

## Verification scenarios

For implementation changes, build the agent project and solution from the repository root.

```powershell
dotnet build AIESummit.Agents/AIESummit.Agents.csproj --nologo
dotnet build AIESummit.slnx --nologo
docker compose config -q
```

The following are proposed targeted acceptance checks, not existing named unit tests.

- With the flag unset or differently cased, the pack does not register this service. With exact `true`, it registers once in the intended worker process.
- No recovery scan occurs immediately at service startup. The first scan begins after the timer tick, with a tolerance suitable for scheduling and startup.
- Enabled OCR, upload step and prompt agents are selected. Disabled metadata and unrelated agent types are skipped.
- Missing, duplicate and disabled configuration rows stop that agent's enqueue path without being treated as successful recovery.
- Eligible rows are offered to matching queues. Terminal rows and names excluded by the receiver filter are not offered.
- One enqueue exception logs safe type information and does not prevent later selected agents from being considered while the service is running.
- Cancellation stops timer waiting and does not start another ordinary scan. Verify shutdown behavior for an already in-flight enqueue separately.
- Duplicate deliveries contend on the same subject/status lease and recheck durable state. Do not assert an exactly-once external call from this test alone.

For a local restart experiment, use a disposable or explicitly selected development stack with synthetic inputs. Preserve PostgreSQL while exercising emulator restart, then observe eligible work being republished and completing. Record the original ID and state, observed enqueue/dispatch evidence and final persisted output. Do not rely solely on a timer wait or queue count.

Such an experiment can call configured paid OCR/LLM deployments. Read [smoke.AGENTS.md](../scripts/smoke.AGENTS.md) before running live integration. The existing smoke verifies a fresh successful processing chain and telemetry, but it does not restart the emulator or prove the recovery path by itself.

## Sources and Horus translation

Application evidence is [UploadQueueRecovery.cs](UploadQueueRecovery.cs), [SplitStep.cs](SplitStep.cs), [worker.overlay.json](../deploy/worker.overlay.json), [Dockerfile](../deploy/Dockerfile) and [compose.yaml](../compose.yaml).

Framework evidence is [BaseAgent.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Agents/Abstract/BaseAgent.cs), [SubjectExecutionLease.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Agents/Coordination/SubjectExecutionLease.cs), [LLMStepAgent.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Agents/LLM/LLMStepAgent.cs), [LlmPromptAgent.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Agents/LLM/LlmPromptAgent.cs) and [AgentHostService.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Agents.Worker/AgentHostService.cs).

The complete historical guide `../../horus/Voiston.AgentHostCore/AGENTS.md` motivates reading host configuration and distinguishing normal queue processing from scoped testing. Its .NET 6 private host, legacy configuration bridge, `voiston` alias, `--force-entity` syntax and split-host temp workflow are not current AIESummit commands. This application uses the shared .NET 10 BlueShift worker plus a venture pack and a narrow hosted recovery service.
