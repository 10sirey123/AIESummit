# Code-defined document splitting

Framework source links in this guide are optional upstream references at the bundled revision and may require maintainer access. Normal development consumes `dependencies/blueshift`; no framework source checkout is required.

Read this guide when changing the split step's definition, registration, prompt composition, subject policy, or persisted runtime configuration. [SplitStep.cs](SplitStep.cs) contains the current step definition, response classes, both policy implementations, and `AgentPack` registration. Read [Metadata.AGENTS.md](Metadata.AGENTS.md) for detailed response validation and application behavior, and [../AIESummit.Domain/IAmLLMCapable.AGENTS.md](../AIESummit.Domain/IAmLLMCapable.AGENTS.md) for the subject contract.

## Three separate runtime inputs

The splitter combines an explicit code registry, persisted step configuration, and persisted prompt records. A source edit does not automatically replace either of the latter two.

| Layer | Current responsibility |
| --- | --- |
| `SplitStep.Definition()` | Supplies the initial `LLMStep` configuration and the code-supported tag/subject/metadata binding |
| `LlmCodeStepRegistry` | Captures immutable support bindings from explicitly supplied definitions |
| Persisted `LLMStep` | Supplies the runtime template, selection lambda, model link, enabled state, and other configured values |
| Persisted `LLMPrompt` | Captures the rendered request and step/subject identity for a particular execution |

`LLMStepAgent<UploadedFile>` reads enabled steps from the repository for the subject name and excludes step IDs already represented in the upload's prompts. It evaluates the stored lambda, then asks `LLMPromptFactory` to create a prompt. A step is not eligible solely because a static definition exists in the loaded assembly.

## Current binding and definition

| Field | Current definition |
| --- | --- |
| Tag | `split-upload` |
| Name | `Identify document page ranges` |
| `DocumentType` | `nameof(UploadedFile)`, stored as `UploadedFile` |
| `DTOResponseType` | `typeof(SplitResult).FullName`, currently `AIESummit.Agents.SplitResult` |
| Enabled | `true` |
| Validation status | `Valid` |
| Selection lambda | `o => true` |
| User template prefix | `Classify these OCR pages:` |
| Response purpose | Ordered inclusive one-based ranges that partition all OCR pages |

The system role prompt asks the model to treat OCR text as source data, identify logical document boundaries, preserve continuation pages, and return ranges without gaps or overlaps. It also asks for short titles. The response application validates these expectations independently of the model's instructions.

The definition initializes several optional string fields to empty strings, including parser, reasoning effort, validation messages, and multimodal expression. Preserve the storage contract when adding optional values. `AIESummitDb.SaveChangesAsync` separately normalizes nullable prompt strings. Do not assume every write path or every framework entity has that normalization.

`ValidationStatus.Valid` in the source is a configured value, not proof that a future lambda, template, or response type was exercised successfully. Test the effective definition and generated prompt after changes.

## What registration guarantees

`AgentPack.ConfigureServices` creates a `LlmTypeRegistry` containing only `UploadedFile` as a subject and `SplitResult` as metadata. It then creates `LlmCodeStepRegistry` with `SplitStep.Definition()`.

Subjects are keyed by simple CLR type name with ordinal comparison. Metadata is keyed by full CLR name with ordinal comparison. Use `nameof(UploadedFile)` and `typeof(SplitResult).FullName` in definitions to make the naming intent explicit. Renaming or moving a metadata class changes its persisted type identity and requires reconciliation of stored steps and pending prompts.

The code-step registry copies only the tag's metadata and subject binding. It does not store a replacement runtime template, model choice, or lambda. Tag lookup uses a case-insensitive dictionary, while subject and metadata binding comparisons are exact ordinal matches. Duplicate tags, including case-only duplicates, cannot be registered as independent steps.

`TryValidateStep` rejects an unregistered tag or a stored step whose subject/metadata differs from the code binding. `TryValidatePrompt` also requires a positive persisted step ID matching the prompt's step reference, a positive target ID, and exact prompt subject/metadata agreement with the step.

Those checks keep editable configuration within supported code contracts. Editing `DTOResponseType` in Admin to an arbitrary class does not register that class. Changing a stored tag to bypass an error does not create a supported workflow.

## Bootstrap seeds missing configuration

[Bootstrap.cs](../AIESummit.Api/Bootstrap.cs) creates missing configuration inside a database transaction with an advisory lock. Its behavior preserves existing rows.

- It inserts credentials with identifiers `mistral-ocr` and `openai` only when those identifiers are absent.
- It creates the model tagged `foundry-split` only if missing. Its initial provider is OpenAI, and its initial name comes from `LLM_MODEL` or the default `gpt-4.1-mini`.
- It inserts `SplitStep.Definition()` only if no step with `SplitStep.Tag` exists, and assigns that newly inserted step's `Model` to the located model.
- It creates each supported agent configuration only if its corresponding table has no rows.

The factory's configured fallback model ID is `0`. Normal execution therefore depends on a valid persisted `Model`/`Model_ID` binding. Do not rely on that fallback to choose an available model.

Changing `LLM_MODEL`, endpoint configuration, credentials, or `SplitStep.Definition()` and restarting the API does not overwrite existing rows. Existing Admin edits are retained. Conversely, deleting a step merely to make bootstrap reseed it can disrupt step IDs referenced by prompts. Use a deliberate configuration update that preserves intended identity and references.

`EnsureCreatedAsync` establishes an empty database schema when appropriate. It is not a schema migration or a general synchronization command for existing definitions.

## Prompt hydration and composition

`SplitPolicy` is registered as the same singleton instance under two roles. It keeps no per-upload mutable state. Retain that property if extending it, since executions can share the instance.

| Role | Current work |
| --- | --- |
| `ILlmStepSubjectPolicy<UploadedFile>` | Reloads the upload with its real prompt collection, prepares transient page JSON, handles terminal status, and supplies prompt-clear hooks |
| `ILlmPromptPolicy` | Resolves an upload for a prompt, creates metadata execution context, supplies connection hooks, and handles terminal prompt failure |

The step-subject hydration method explicitly includes `LLMPrompts`. That collection tells the framework which steps have already generated prompts. Omitting it or converting it to `[NotMapped]` can break prompt planning independently of whether the page JSON looks correct.

Hydration queries OCR pages for the upload, orders them by `Number`, and serializes projections containing `Number` and `Markdown` into `UploadedFile.SplitInput`. That property is `[NotMapped]`. It is transient prompt preparation, not a database snapshot or public API field.

The next line replaces every literal `@` with the JSON Unicode escape `\u0040`. The selected template is subsequently processed by the framework template engine. This prevents source text containing Razor-like `@` sequences from being interpreted as template syntax in this path while keeping equivalent text in the JSON representation. Preserve the ordering of serialize, escape, and render. Do not mutate the stored OCR Markdown to achieve the same effect.

The factory registration selects the user template as follows.

```csharp
(step, subject) => step.Template + "\n" + ((UploadedFile)subject).SplitInput
```

The stored step template is still used. Hydrated OCR JSON is appended for this subject. The factory renders that combined string and copies the stored system template into the prompt. It also generates the expected JSON schema for the registered metadata type and can append generated structure descriptions to the system prompt.

The factory copies parser, reasoning effort, model selection, temperature scaled from the step's percentage value, and enabled multimodal expression into the prompt. Existing prompt rows retain the values captured when they were generated. Editing a step later is not equivalent to regenerating an already queued request.

The prompt-policy hydration overload is intentionally narrower than step hydration. It resolves a type/ID to an `UploadedFile` and rejects other types. It does not rebuild `SplitInput` or include the prompt collection in that method. Execution uses the persisted request, while response application queries the current pages through its metadata context.

## Policy hook boundaries

`CreateMetadataContext` returns `MetadataExecutionContext(prompt.ID, repository)`. This binds response application to the current prompt and repository. The current contract is `IMetadataExecutionContext`, not Horus's older `BaseMetadataConfig` constructor convention.

`BeforeConnect` returns the metadata unchanged. `AfterConnect`, `PrepareAttachments`, `ResetOutputs`, `ClearPromptOutputs`, and `OnPromptsCleared` are currently empty. Do not describe those hooks as deleting documents, assembling attachments, resetting OCR, or rolling back prior output.

`OnTerminalTransition` sets a generic splitting error when the upload reaches permanent error or has no prompts. `OnFinished` also marks the upload with a generic error when one of its prompts reaches permanent error. Connection failure reports `Document ranges could not be applied.`. A successful `SplitResult.ConnectTo` clears the upload error after validating and staging replacement documents.

Keep operational error messages separate from provider responses and source content. Investigate using upload ID, prompt ID, step tag, processing state, and the configured log path. [UploadExecutionContext.cs](UploadExecutionContext.cs) associates prompt execution context with the upload and step tag.

## Response and validation boundary

`SplitResult` derives from `BaseMetadata` and accepts `IMetadataExecutionContext` in its constructor. Its `ConnectTo<T>` only applies to `UploadedFile`. The response JSON uses `documents`, `firstPage`, `lastPage`, and `title`, declared through Newtonsoft `JsonProperty` attributes with `Required.Always`.

Do not substitute the CLR property names or a JSON array at the root without updating the complete contract. The current root is a `SplitResult` object containing a documents list. Horus's measurement-centered array examples are not this response format.

Connection validation checks ordered coverage, inclusive range bounds, nonempty results, and title validity before replacing document rows. It joins existing OCR Markdown with the exact page-break marker. Keep business validation in the application phase even if schema descriptions or prompt instructions are strengthened. Follow [Metadata.AGENTS.md](Metadata.AGENTS.md) for invalid payload handling, application atomicity, and exact range cases.

## Adding another step or subject

For another step using the existing upload subject, choose a stable unique tag, register its definition explicitly, register its metadata type, seed or update a persisted step with the right model, and ensure its output application has a defined lifecycle. The current template selector appends page JSON to every selected step using this factory, so decide whether that input is appropriate for the new task.

For a different subject, registration alone is insufficient. The existing selector casts every subject to `UploadedFile`. It will fail for another subject, including a captured `LLMExecution` if such a path is added. The prompt policy also rejects other subject types.

A new subject needs its own hydration policy, a compatible prompt-policy dispatch design, and a template selector that handles the actual subject types explicitly. It also needs the correct persistence contract, agent activation, response application, model binding, and operational context resolution. Review all those integration points before broadening `LlmTypeRegistry`.

Avoid adding a second singleton `ILlmPromptPolicy` registration and assuming both policies will automatically dispatch. Inspect how the worker resolves that service and compose the intended behavior deliberately.

## Translate the Horus synchronization workflow

The complete Horus LLMSteps guide correctly emphasizes checking stored runtime configuration after source changes. Its commands target a Horus-specific `Voiston.CLI`, `net9.0` binaries, and that venture's `update-from-code` and forced-run workflow. This repository has no local `AIESummit.CLI` or `Voiston.CLI` project. Do not present those commands as runnable AIESummit instructions.

The corresponding local workflow is concrete but depends on the configured environment.

1. Inspect the stored `split-upload` row and model association before changing it. Record which settings were intentionally customized.
2. Change the application definition, registration, or policy as required and rebuild the API and agent pack that will run it.
3. Reconcile only the intended stored step fields using a verified Admin/configuration path or an explicitly scoped repository operation. Startup seeding only inserts missing rows.
4. Read back the effective row and check tag, subject name, full metadata name, enabled state, lambda, templates, parser, reasoning effort, and model reference against the intended result.
5. Verify the running API and worker use the new agent assembly and expected database. A rebuilt local DLL does not replace an already running worker container.
6. Generate a fresh representative prompt and inspect its captured request/schema/model identity. Existing prompt rows can still represent the old definition.
7. Execute a controlled test subject only after resolving stale or concurrently running work. Use the application's supported recovery workflow rather than assuming an old Horus `--force-entity` command applies.

The shared framework contains CLI functionality, but that alone does not establish a configured venture command line, domain pack, connection, or safe invocation. Inspect the actual tool's current help and deployment wiring before documenting a specific command.

## Verification and test scope

This checkout has no dedicated venture unit-test project. [scripts/smoke.ps1](../scripts/smoke.ps1) is an existing integration entry point, not proof that every registry, lambda, or malformed response case is covered. Read its prerequisites and fixtures before using it. Do not claim tests were executed merely because the framework has tests for the underlying components.

For C# changes, build the affected application projects from the repository root.

```powershell
dotnet build AIESummit.Agents/AIESummit.Agents.csproj
dotnet build AIESummit.Api/AIESummit.Api.csproj
```

For meaningful behavior changes, verify registration accepts the intended binding and rejects mismatched subject/metadata. Check duplicate tags and missing persisted step/model references. Exercise lambda parsing through `LambdaManipulation` or the factory rather than assuming a C#-looking expression will parse. If introducing nullable booleans, verify the selected Dynamic LINQ syntax against the current parser.

Verify hydration with a fresh context, multiple ordered OCR pages, an existing prompt, and source text containing `@` and Razor-like sequences. Confirm the rendered user prompt contains escaped source data and the intended stored prefix, while persisted OCR text remains unchanged.

Inspect a newly created prompt's `LLMStep_ID`, `DocumentType`, `DocumentID`, `DTOResponseType`, expected schema, system prompt, user prompt, and preferred model reference. Use synthetic text and avoid copying sensitive prompt content into logs or documentation.

Finally, cover valid and invalid structured responses, document replacement, generic failure propagation, and restart behavior with an existing customized step. The latter check should demonstrate that bootstrap preserves the stored row rather than silently syncing source defaults.

## Source anchors

- [SplitStep.cs](SplitStep.cs) and [Bootstrap.cs](../AIESummit.Api/Bootstrap.cs) define the venture behavior and seed preservation.
- [LlmTypeRegistry.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Domain/LLM/LlmTypeRegistry.cs) defines explicit supported types and naming.
- [LlmCodeStepRegistry.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Domain/LLM/LlmCodeStepRegistry.cs) defines binding and persisted-prompt validation.
- [LLMPromptFactory.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Domain/LLM/LLMPromptFactory.cs) defines template selection, schema generation, lambda execution, and captured prompt fields.
- [LLMStepAgent.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Agents/LLM/LLMStepAgent.cs) reads persisted steps and determines prompt eligibility.
- [ILlmStepSubjectPolicy.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Agents/LLM/ILlmStepSubjectPolicy.cs) and [LlmPromptContracts.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Agents/LLM/LlmPromptContracts.cs) define the two policy roles.

Use framework sources as references. Keep application prompt composition, hydration, seed reconciliation, and response behavior in the venture rather than changing the shared framework for this one step.
