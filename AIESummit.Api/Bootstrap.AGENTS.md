# Startup bootstrap and persisted configuration

Read this guide when changing startup, initial credentials, model/step seeds, agent configuration, or rotation behavior. [Bootstrap.cs](Bootstrap.cs) inserts missing configuration into `AIESummitDb`. It does not synchronize existing rows with source defaults or environment variables.

The current application uses `WebApplication.CreateBuilder` and `IConfiguration` directly. It does not register Horus's `SharedConfig`, `BaseConfigSection`, `ConfigLogger`, or legacy configuration bridge. Borrow the Horus guide's attention to configuration validation and secret handling without adding those types or assuming their startup checks exist here.

## Startup order

[Program.cs](Program.cs) reads Firebase configuration, registers authentication, the PostgreSQL context and repositories, agent-pack services, and queue integration. After building the app, it creates a service scope and awaits `Bootstrap.InitializeAsync`. Agent activation and HTTP request handling come afterward.

Bootstrap executes in this order.

1. Call `db.Database.EnsureCreatedAsync()`.
2. Read the required Gemini API key from configuration.
3. Begin a database transaction.
4. Acquire PostgreSQL transaction advisory lock `825833001`.
5. Check and add missing APIInfo, model, step, and agent-config rows.
6. Save changes once, then commit the transaction.

An unhandled bootstrap failure prevents normal API startup. The health route is mapped later, and the process does not become healthy merely because PostgreSQL is reachable. In Compose, the worker and Admin API wait for API health, so a bootstrap failure can block downstream startup too.

## Schema creation is outside the seed lock

`EnsureCreatedAsync` runs before the advisory lock and before the explicit transaction. The lock therefore does not serialize schema initialization itself. Do not claim this method makes concurrent first-start database creation race-free.

`EnsureCreatedAsync` is useful for creating the application's initial database schema. It does not apply migrations to an existing schema, reconcile renamed columns, or repair an existing database that no longer matches the model. A successful return is not evidence that a changed persisted property has been deployed correctly.

Use [../AIESummit.Domain/Persistence.AGENTS.md](../AIESummit.Domain/Persistence.AGENTS.md) for schema changes and existing-data handling. Do not delete a populated database or volume to make bootstrap succeed unless that specific destructive reset is the intended task.

## Required values and defaults

| Configuration key | Bootstrap behavior |
| --- | --- |
| `GEMINI_API_KEY` | Required non-null value. Google AI Studio key used for newly inserted credential rows |
| `GEMINI_ENDPOINT` | Optional Gemini API base override; defaults to `https://generativelanguage.googleapis.com/v1beta` |
| `OCR_MODEL` | Defaults to `gemini-3.8-flash` when null |
| `LLM_ENDPOINT` | Optional Responses API endpoint override; defaults to the gateway at `http://gemini-gateway:8080` |
| `LLM_MODEL` | Defaults to `gemini-3.8-flash` when null |

The key is read unconditionally, even if all seed rows already exist. Existing database credentials do not remove the `GEMINI_API_KEY` requirement for API startup.

The C# check uses a null-coalescing throw, not whitespace validation. An empty string can pass that check. The optional defaults also apply to null rather than every empty value. A future validation improvement should explicitly test missing, empty, whitespace, and malformed values rather than assuming the current checks already cover them.

Compose adds a separate boundary. Its `${GEMINI_API_KEY:?...}` interpolation requires a nonempty value before starting the configured service. That Compose behavior does not change the C# contract for someone running the API directly.

Firebase configuration is checked earlier in `Program.cs`. `Firebase:ProjectId` is required. A Firebase API key is required unless the Development-only emulator condition permits the demo fallback. Diagnose failures by their actual startup stage instead of assuming every missing setting is a bootstrap seed failure.

## Exact missing-only seeds

| Row kind | Existence check | Inserted identity and defaults |
| --- | --- | --- |
| OCR `APIInfo` | Any row with `Identifier == "gemini-ocr"` | `Identifier=gemini-ocr`, configured/default Gemini endpoint, shared Gemini key, OCR model in `AppSecret`, API-key authorization |
| LLM `APIInfo` | Any row with `Identifier == "openai"` | `Identifier=openai`, configured/default gateway endpoint, shared Gemini key, null `AppSecret`, API-key authorization |
| `LLMModel` | `SingleOrDefault` with `Tag == "foundry-split"` | Tag `foundry-split`, configured/default model name, `LLMAPIProvider.OpenAI` |
| `LLMStep` | Any row with `Tag == SplitStep.Tag` | `SplitStep.Definition()`, tag `split-upload`, assigned to the located model |
| `OcrAgentConfig` | Any row in that type's table | Name `Gemini OCR`, class name `OcrAgent`, enabled |
| `LLMStepAgentConfig` | Any row in that type's table | Name `Plan document split`, class name `LLMStepAgent`, enabled |
| `LLMPromptAgentConfig` | Any row in that type's table | Name `Run document split`, class name `LlmPromptAgent`, enabled |

The agent checks are table-wide `AnyAsync()` calls. They do not look for the expected class name or seed name. An existing disabled or differently configured row prevents bootstrap from adding the default. Bootstrap does not guarantee exactly one row per configuration type, nor does it validate that all existing rows activate the intended agent.

The model lookup uses `SingleOrDefaultAsync`. Multiple matching model tags cause failure rather than silently selecting one. The APIInfo and step checks use `AnyAsync`, so they only establish presence for seeding and do not prove unique or valid configuration. Inspect identity constraints and duplicate data when diagnosing a startup/runtime mismatch.

## Endpoint and credential semantics

For the OCR credential, the default URI is the Gemini API base `https://generativelanguage.googleapis.com/v1beta`, with `GEMINI_ENDPOINT` as its override; the browser appends `/models/{model}:generateContent`. For the `openai` credential, the default is the Compose service `http://gemini-gateway:8080`, with `LLM_ENDPOINT` as its override. The gateway translates the worker's Responses API calls to Gemini; see [../deploy/gemini-gateway/AGENTS.md](../deploy/gemini-gateway/AGENTS.md).

`APIInfo.AppKey` stores the Gemini key for both newly inserted credentials. In this venture's OCR configuration, `AppSecret` stores the OCR model name. [OcrAgent.cs](../AIESummit.Agents/OcrAgent.cs) obtains it through the API browser's `AppKeySecret` property and places it in the request path. It is not a second independent OCR authentication secret in this seed convention.

Keep that convention explicit when editing the configuration UI or changing the browser. A field named `AppSecret` can have framework-specific usage, and an operator must not replace the OCR model name with an unrelated credential expecting the same request behavior.

For the LLM path, the selected model's `Name` and `Provider` live in `LLMModel`. Bootstrap assigns OpenAI as the provider and tags the model `foundry-split`; both are kept for persisted identity. "OpenAI" here means the Responses API wire format the worker speaks to the gateway, not the model vendor.

The newly inserted split step receives `step.Model = model`. An existing step receives no repair or rebinding. If its model link is absent, disabled, or points elsewhere, restarting bootstrap preserves that state. Likewise, an existing `foundry-split` model retains its stored name and provider even when environment defaults change.

## Source defaults versus effective configuration

Missing-only seeding preserves operator changes in Admin. Preserve that behavior unless the requested task explicitly introduces a synchronization policy. Do not turn every startup into a broad overwrite of credentials, templates, model selections, or enabled flags.

The same rule applies to [SplitStep.Definition](../AIESummit.Agents/SplitStep.cs). Changing source templates or lambda defaults only changes newly inserted step rows. The registry supports the code-defined tag/subject/metadata binding, while runtime prompt planning reads the stored step. Rebuilding and restarting can therefore load a new registry with an old stored binding or template.

Follow [../AIESummit.Agents/SplitStep.AGENTS.md](../AIESummit.Agents/SplitStep.AGENTS.md) for source-definition reconciliation and fresh-prompt verification. Do not remove the stored step to force reseeding without assessing prompt references and stable IDs.

## Transaction and concurrency boundaries

The transaction-scoped advisory lock coordinates bootstrap calls that acquire the same lock against the relevant PostgreSQL database. Once acquired, it surrounds the missing-row reads and inserts and remains held until commit or rollback.

It is a cooperative lock. An Admin edit, manual database write, different seeder, or other process that does not acquire this lock is not serialized by it. Keep real uniqueness constraints and safe write design in place. Do not describe the advisory lock as a general replacement for database constraints.

The seed changes are saved within the explicit transaction. A failure before commit should not be presented as a completed seed operation. Schema creation occurred earlier and may remain even if credential validation or seed persistence later fails. Report which phase completed when investigating a partial startup.

The key is a fixed coordination constant, not a generated identity or a credential. Changing it creates a different lock domain. Do not change it casually while different versions of the API may start concurrently.

## Environment forwarding in Compose

[.env.example](../.env.example) documents the expected user-supplied Gemini key, OCR/LLM model defaults, Firebase values, and API environment selection. It contains no actual provider key. Do not read, print, or copy a real `.env` file merely to document or inspect the code's configuration contract.

[compose.yaml](../compose.yaml) explicitly forwards `GEMINI_API_KEY`, `OCR_MODEL`, and `LLM_MODEL` into the API container. It also forwards Firebase and ASP.NET environment values under the appropriate configuration names.

`GEMINI_ENDPOINT` and `LLM_ENDPOINT` are supported by the C# bootstrap but are not forwarded by the checked-in Compose API environment block. Adding them to a local interpolation file alone does not automatically inject them into the container. A requested override needs an explicit Compose environment mapping or another intentional configuration mechanism.

Worker and Admin services share database, queue, environment, and telemetry configuration. They do not receive the Gemini bootstrap variables through that shared environment block. Their runtime integration relies on the seeded database configuration and their own host wiring. Do not copy secrets into every container simply because the API uses them during initialization.

When diagnosing effective configuration, check whether a setting reached the intended process without dumping all environment values. Presence, source, and safe endpoint/model identifiers are normally sufficient. Never echo the provider key or authentication tokens.

## Rotation and operator changes

Changing the Gemini key in the API's environment and restarting does not rotate existing `APIInfo.AppKey` values. A deliberate rotation must update the persisted credential rows that the workers actually resolve, using an authorized credential-management path.

Identify which credential identifiers need rotation, preserve their endpoint and deployment semantics, apply the replacement securely, and verify a bounded request through each affected integration. Account for already running processes or browser/provider instances that may retain configuration in memory. Inspect the actual caching/reload path before claiming a database edit is immediately active everywhere.

Do not expose keys in a verification query, screenshot, task note, or log. Read back safe identifiers and configuration relationships, then verify functional access. A successful bootstrap transaction does not prove the provider accepted the key, deployment, model name, or endpoint.

For model or step changes, record the exact intended fields and preserve stable tags/IDs. Inspect pending prompt rows before restarting or replaying work because they can contain previously captured model and prompt configuration. Configuration reconciliation and workflow retry are separate operations.

## Verification checklist

1. On an isolated empty development database, verify the expected tables are created and the exact credential identifiers, model tag, step tag/model link, and three configuration types are inserted.
2. Restart against the same database and verify no duplicate seeds appear. Confirm intentional stored template, provider/model, endpoint, and enabled-state edits remain intact.
3. Verify missing required key behavior even when seed rows already exist. Distinguish direct-process null handling from Compose's nonempty interpolation requirements.
4. Test a populated but incorrectly shaped schema through the normal schema workflow. Do not treat `EnsureCreated` as the repair step.
5. If changing concurrency behavior, exercise two bootstrappers with the same database and verify the lock-protected seed section. Separately assess first-schema creation, which occurs before the lock.
6. Verify an existing agent-config row prevents default insertion, including a disabled or differently named row. Confirm the actual activated agent/config identity afterward.
7. For an endpoint override, confirm it is explicitly forwarded to the API process and affects only newly seeded rows unless an intentional update also occurs.
8. Verify a newly generated prompt uses the intended persisted step and model, and a controlled OCR request uses the intended OCR model. Use synthetic content and bounded processing.

For code changes, build the API and affected agent/domain projects and use targeted tests appropriate to changed behavior. The existing [smoke script](../scripts/smoke.ps1) exercises the running upload pipeline but does not comprehensively test seed preservation, concurrent initialization, rotation, or malformed startup settings. State the checks actually performed.

## Reference boundary

The complete Horus configuration guide was reviewed for validation and masking practices. Its required-field annotations, ASCII startup tables, section-registration types, and legacy `ConfigurationManager` bridge are not implemented by this bootstrap. Current behavior is defined by [Bootstrap.cs](Bootstrap.cs), [Program.cs](Program.cs), [compose.yaml](../compose.yaml), and [.env.example](../.env.example).

Keep application-specific seed changes in this repository. Do not modify the shared framework or weaken authentication to compensate for missing or stale startup configuration.
