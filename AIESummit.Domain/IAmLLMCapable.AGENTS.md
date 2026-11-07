# LLM-capable upload subjects

This filename retains the Horus term for discoverability. AIESummit implements `Blueshift.Domain.LLM.ILlmCapable`, not Horus's `IAmLLMCapable`. Read [AGENTS.md](AGENTS.md) before changing the model, then [SplitStep.AGENTS.md](../AIESummit.Agents/SplitStep.AGENTS.md) and [Metadata.AGENTS.md](../AIESummit.Agents/Metadata.AGENTS.md) for processing changes.

The entity contract is deliberately small. Hydration, prompt lifecycle and result connection belong to registered policies and metadata types in `AIESummit.Agents`. Adding the interface to a class does not by itself create a working processing flow.

## Current contract and legacy translation

The complete current interface, reproduced from the pinned framework, is shown below. This is existing framework code, not a proposed application type.

```csharp
public interface ILlmCapable
{
    ICollection<LLMPrompt> LLMPrompts { get; set; }
    StepProcessingStatus LLMStepStatus { get; set; }
    string LLMJsonResult { get; set; }
}
```

`UploadedFile` derives from `BasePersistenceObject` and implements those properties in [UploadedFile.cs](UploadedFile.cs). Its collection starts empty and its result string starts as `""`. `SplitInput` is additional application state populated transiently during hydration. Neither an empty collection nor default strings prove that a persisted subject has been loaded.

| Horus guide concept | AIESummit equivalent |
| --- | --- |
| Entity method `GetHidratedObject` | `ILlmStepSubjectPolicy<UploadedFile>.Hydrate` and `ILlmPromptPolicy.Hydrate` |
| Entity methods connecting measurements, prescriptions and procedures | `SplitResult : BaseMetadata` and `ConnectTo<T>` |
| `Measurements` collection | No such current application collection. Accepted split results create `Document` rows |
| Obsolete `ResolvePromptResult` throwing an exception | Not a member of current `ILlmCapable`. Do not add an unused throwing method |
| `GetFilePartToLLM` | Attachment preparation is a prompt-policy responsibility. Current split input is OCR page text |
| EF6 `addLLMRelationship<T>` helper | Explicit EF Core mapping in `AIESummitDb.OnModelCreating` |
| Custom per-entity agent and definitions projects | Shared generic `LLMStepAgent<UploadedFile>`, registered policies and the existing venture pack |
| Parser tags and `Voiston.Parsers` | Current flow uses registered structured `SplitResult`, not a custom text parser |

Do not copy Horus's `System.Data.Entity` imports, clinical entities or prescribed region formatting into this project. Use EF Core. Metadata and serializer attributes should match the actual consumer. Hiding a field in Admin or JSON does not mean it should be excluded from persistence.

## Persist the prompt graph

The step agent creates prompts by adding them to the hydrated subject's `LLMPrompts` collection. The following is the existing application mapping.

```csharp
modelBuilder.Entity<UploadedFile>().HasMany(file => file.LLMPrompts).WithOne()
    .HasForeignKey("UploadedFile_ID").OnDelete(DeleteBehavior.Cascade);
modelBuilder.Entity<LLMPrompt>().HasOne(prompt => prompt.UsedModel).WithMany()
    .HasForeignKey("UsedModelID").IsRequired(false).OnDelete(DeleteBehavior.Restrict);
```

`UploadedFile_ID` is a shadow foreign key for this mapped collection. Keep its identity stable when evolving the database. `LLMPrompt.DocumentID` and `DocumentType` identify the logical processing target and support framework dispatch. They do not replace the mapped relationship that stores a prompt added through the navigation.

Do not put `[NotMapped]` on `LLMPrompts`. That previously broke the intended prompt persistence path. `[NotMapped]` is correct on `SplitInput`, whose value is reconstructed from pages each time. Test visibility through a fresh context so a populated in-memory collection cannot conceal a missing database row.

`UsedModelID` must permit a new prompt before a provider has completed and established the model actually used. Do not fill it with an arbitrary model merely to satisfy a required foreign key. Keep restricted model deletion distinct from cascading upload/prompt deletion. These mappings do not establish a complete user-facing upload deletion workflow.

`AIESummitDb.SaveChangesAsync` normalizes these nullable framework prompt strings before saving.

| Normalized strings | Reason |
| --- | --- |
| `SystemPrompt`, `Prompt`, `Response` | Framework-created work may not yet have all request/result text |
| `MultimodalLambda`, `ExpectedResponseJsonFormat`, `ReasoningEffort` | Optional provider or formatting inputs |
| `DTOResponseType`, `MD5Hash`, `Parser`, `DocumentType` | Framework fields that can be unset during object construction |

The override sets null values to empty strings on added or modified prompts, then calls `base.SaveChangesAsync(cancellationToken)`. Preserve that framework base save path and its publishing behavior. Empty strings satisfy persistence constraints, not business correctness. Registry and worker validation still need valid subject and metadata identifiers.

The override is asynchronous. The pinned framework's parameterless `SaveChanges()` forwards to virtual `SaveChangesAsync()`, so that ordinary synchronous path also reaches this normalization. Do not generalize that result to every overload or a new direct persistence path without inspecting and testing it. See [Persistence.AGENTS.md](Persistence.AGENTS.md) before changing mapping or save behavior. Existing startup uses `EnsureCreatedAsync`, which is not an upgrade mechanism for retained databases.

## Hydrate the real subject at both processing stages

The pinned `LLMStepAgent<T>` sets `ShouldHydrate = false` and delegates application loading to its subject policy. Its input can be an ID-only object with default scalar values. Returning that object after merely assigning `SplitInput` risks overwriting the stored owner, MIME type, status and other fields when the framework saves it.

The current implementation deliberately replaces that input with the persisted entity.

```csharp
subject = repository.Objects
    .Include(file => file.LLMPrompts)
    .Single(file => file.ID == subject.ID);
```

This is an excerpt from existing `SplitPolicy.Hydrate`, not a complete replacement method. The remainder queries `OcrPage` rows for the upload, orders them by `Number`, serializes only page number and Markdown, and assigns `SplitInput`. It replaces `@` with the JSON escape `\u0040` so OCR text remains data when passed through the Razor template path.

Load every navigation actually needed by conditions or templates. Do not fetch an entire graph without a consumer, and do not assume lazy loading. Including persisted prompts is essential because the step agent uses their step identities and states to decide whether more work is needed. An empty unloaded collection can lead to incorrect prompt creation decisions.

The second overload handles prompt execution.

```csharp
public ILlmCapable Hydrate(Type type, int id,
    IBasePersistenceRepository<LLMPrompt> repository)
    => type == typeof(UploadedFile)
        ? repository.GetRepository<UploadedFile>().Objects.Single(file => file.ID == id)
        : throw new InvalidOperationException("Unsupported prompt subject.");
```

This is also current application code. It reloads the target for result connection and rejects unsupported types. It does not prepare `SplitInput` because the prompt has already been created. Result connection loads the relevant OCR pages through the metadata context. Keep the two hydration responsibilities distinct when adding another subject.

Current lookups use `Single` and fail when the subject is missing. Do not invent a blank replacement entity to continue processing. If adding cancellation or deletion, define how pending work becomes obsolete and inspect the framework's `CanExecute` and superseded-work hooks. AIESummit currently relies on the default `CanExecute` implementation and has no application cancellation flow.

## Register the complete flow

`AgentPack.ConfigureServices` in [SplitStep.cs](../AIESummit.Agents/SplitStep.cs) constructs and registers the current support graph.

1. `LlmTypeRegistry` receives `UploadedFile` as its subject type and `SplitResult` as its metadata type.
2. `LlmCodeStepRegistry` receives that registry and `SplitStep.Definition()`.
3. `SplitPolicy` is registered as both `ILlmStepSubjectPolicy<UploadedFile>` and `ILlmPromptPolicy`.
4. `LambdaManipulation`, `TemplateManipulation` and `LLMPromptFactory` provide condition and prompt construction. The custom prompt text combines `step.Template` with the hydrated upload's `SplitInput`.
5. `LlmPromptWorker` and `UploadExecutionContext` provide prompt execution and root-upload correlation.
6. The existing framework worker loads the pack. Startup seeds the necessary configuration rows and step/model relationship. Queue recovery is enabled only by its worker environment flag.

The type registry resolves subject names using the class's simple name and metadata using the full type name, with ordinal comparisons. Subject types must be concrete persistence classes implementing `ILlmCapable`. Metadata types must be concrete `BaseMetadata` subclasses. Avoid subject simple-name collisions even across namespaces.

The code-step registry binds a stable tag to a registered subject and metadata type. It compares tag keys case-insensitively but requires exact bound subject and metadata strings. Prompt validation also checks the persisted step ID and positive target identity. Renaming a class, namespace or tag can invalidate existing step and prompt records. Treat those changes as persistence transitions rather than cosmetic refactoring.

`SplitStep.Definition()` uses tag `split-upload`, `DocumentType = nameof(UploadedFile)`, and `DTOResponseType = typeof(SplitResult).FullName`. The condition is `o => true`. It does not mean every uploaded file bypasses OCR eligibility. Inspect the OCR transition and agent state machine before changing when split processing begins.

`Bootstrap.InitializeAsync` creates a missing `foundry-split` model and a missing split step, linking them on first creation. Existing records are preserved. The code registry supplies supported bindings, while persisted configuration supplies the operational step and model. Editing the code definition is not a general promise to overwrite existing database templates on restart.

## Structured output and result application

The current response is a `SplitResult` object containing a required `documents` list. Each `PageRange` contains required `firstPage`, `lastPage` and `title` properties. Do not follow the legacy assumption that the worker necessarily deserializes a top-level `List<MyDto>`.

The framework resolves the registered metadata type, deserializes with `BaseMetadataJsonConverter` and a policy-created `IMetadataExecutionContext`, then calls metadata connection. Unknown types, malformed JSON or failed connection become prompt failures. Provider schema support is useful but does not prove the result is semantically valid.

`SplitResult.ConnectTo` checks the actual persisted page set. It requires a nonempty set of ranges beginning at page 1, no gap or overlap, increasing inclusive endpoints, endpoints within the page count, and nonblank titles no longer than 250 characters. The next expected page after the last range must equal the number of pages plus one.

Only after validating every range does it remove existing `Document` rows and add replacements for the target upload. Each replacement joins the selected ordered Markdown pages using `<!-- page break -->`. It trims the title and clears `UploadedFile.Error`. The method stages changes through repositories rather than saving each individual document. Preserve that validate-before-mutate boundary and the outer save behavior.

The unique database index on `UploadId, FirstPage` prevents duplicate starts. It cannot prove complete coverage or prevent all overlaps. Keep application validation even if provider output is schema-constrained.

`SplitPolicy.OnTerminalTransition` supplies a safe upload error for permanent failures or no prompts. `OnFinished` also sets the upload error when a prompt permanently fails. Current output-clearing and attachment hooks are no-ops, while successful metadata application replaces documents. A new subject with other output rows needs deliberate cleanup behavior rather than copying those no-ops blindly.

## Ownership and data boundaries

The subject's persisted `OwnerId` remains authoritative. Background workers operate with service-level database access, so their repositories do not automatically enforce Firebase ownership. Scope every page/document query to the hydrated subject ID and do not accept target IDs or owners from model output.

`PageRange` contains page positions and titles, not entity IDs or owner changes. Keep it that way unless a new feature supplies explicit authorization and validation. Treat OCR content as untrusted source text. The current system prompt tells the model to treat page text as data, and deterministic validation limits what its response can apply.

Public API responses are explicit projections and must remain owner-scoped. Do not expose prompt bodies, source bytes, credential records or the complete EF graph merely because `ILlmCapable` has public properties. Admin presentation metadata is not an end-user authorization mechanism.

Avoid writing real document text, prompt bodies or provider keys into fixtures, guides or shared diagnostics. Use synthetic pages for tests. Telemetry can correlate IDs and execution states without copying the document body into a report.

## Proposed new-subject checklist

`Document` is currently an output of the upload step. If a document-level step is introduced, reload the persisted `Document` by ID with its mapped prompt collection in the step policy. Use its title, Markdown, and inclusive page range as input. Load the source upload and range-scoped page or image references only when the step needs them. Register a document-specific prompt hydration branch and metadata type, and define the transition that makes a newly created document eligible. Coordinate that transition with replacement of documents during an upload rerun so pending work cannot apply to an obsolete document.

This checklist describes future work. AIESummit currently has only `UploadedFile` registered as its application LLM subject.

1. Add a concrete `BasePersistenceObject` implementing the three current interface properties and register it in `AIESummitDb`.
2. Map its prompt navigation explicitly and plan schema evolution. Do not reuse another subject's shadow foreign key without a deliberate model design.
3. Implement subject hydration that loads persisted scalar state and required relationships, including prompts. Implement prompt-stage hydration that rejects unsupported types.
4. Define registered `BaseMetadata` and validate all output before mutating target rows.
5. Add a stable code-defined step with correct subject and full metadata names, then register it in the existing pack's registries.
6. Extend the prompt-policy dispatch, prompt factory behavior and metadata context only as required. The current factory casts subjects to `UploadedFile`, so merely adding a registry entry is insufficient.
7. Seed missing runtime configuration idempotently and verify the generic agent activates for the new subject. Review worker queue filters and emulator queue definitions.
8. Extend recovery selection and execution correlation when required. Current recovery explicitly selects the upload step agent, not every future closed generic type.
9. Check ownership, public projections and cleanup rules independently of the LLM interface.
10. Validate fresh persistence, retained-data migration, prompt creation, provider/result handling and observable completion.

## Verification and regression cases

For documentation-only changes, check source names and links. For implementation changes, build from the repository root.

```powershell
dotnet build AIESummit.slnx --nologo
```

Meaningful regression cases include the following.

- Hydrate an ID-only upload and verify that owner, content type, original bytes, OCR status and existing prompts survive processing unchanged where the step has no reason to modify them.
- Create a prompt without `UsedModel` and with the framework's nullable string fields, save through the actual production path, then reload through a fresh context and verify the upload relationship.
- Validate ranges covering pages 1–2 and 3 for a three-page upload. Reject an empty list, missing page, overlap, reversed range, out-of-bounds endpoint, blank title and title longer than 250 characters. Existing documents should remain unmodified on validation rejection.
- Verify unknown subject names, unregistered metadata and mismatched step/prompt identities fail before applying output to unrelated rows.
- Check that restart preserves operator configuration and that pending prompts remain discoverable after an emulator restart.
- Read the results as the owner and another Firebase user. The second account must not gain access through an upload or image identifier.

For a full local integration change, use `./start.ps1` and `./scripts/smoke.ps1` after reading [smoke.AGENTS.md](../scripts/smoke.AGENTS.md). The smoke script uses configured live OCR and LLM deployments and may incur charges. It checks successful synthetic processing and isolation, but it does not cover every invalid metadata case or prove all hydration invariants. Report that distinction rather than treating one successful run as exhaustive coverage.

## Source references

- [UploadedFile.cs](UploadedFile.cs), [SplitStep.cs](../AIESummit.Agents/SplitStep.cs), [Bootstrap.cs](../AIESummit.Api/Bootstrap.cs) and [UploadEndpoints.cs](../AIESummit.Api/UploadEndpoints.cs) define the current application behavior.
- [ILlmCapable.cs](../external/blueshift/Blueshift.Domain/LLM/ILlmCapable.cs) defines the actual entity contract.
- [ILlmStepSubjectPolicy.cs](../external/blueshift/Blueshift.Agents/LLM/ILlmStepSubjectPolicy.cs), [LlmPromptContracts.cs](../external/blueshift/Blueshift.Agents/LLM/LlmPromptContracts.cs), [LLMStepAgent.cs](../external/blueshift/Blueshift.Agents/LLM/LLMStepAgent.cs) and [LlmPromptWorker.cs](../external/blueshift/Blueshift.Agents/LLM/LlmPromptWorker.cs) define hydration, prompt creation and connection behavior.
- [LlmTypeRegistry.cs](../external/blueshift/Blueshift.Domain/LLM/LlmTypeRegistry.cs) and [LlmCodeStepRegistry.cs](../external/blueshift/Blueshift.Domain/LLM/LlmCodeStepRegistry.cs) define supported type and step identities.
- The complete historical reference is `../../horus/Horus.Domain/Entities/IAmLLMCapable.agents.md`. Its flow-oriented explanation inspired this guide, while the translation table separates its legacy API from the checked-out framework.

Inspect framework sources read-only for application work. Do not change the submodule to make an unverified legacy example compile.
