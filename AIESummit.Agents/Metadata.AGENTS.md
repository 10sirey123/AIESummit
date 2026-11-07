# Structured split metadata

This guide covers the JSON returned by the document-split prompt and its conversion into persisted documents. The implementation is `SplitResult`, `PageRange` and `SplitPolicy` in [SplitStep.cs](SplitStep.cs). Read [SplitStep.AGENTS.md](SplitStep.AGENTS.md) for prompt generation and [Domain persistence guidance](../AIESummit.Domain/Persistence.AGENTS.md) for database boundaries.

## Current response contract

The response is one JSON object with a required `documents` array. Each element requires `firstPage`, `lastPage` and `title`. Pages are inclusive and one-based.

```json
{
  "documents": [
    { "firstPage": 1, "lastPage": 2, "title": "Invoice" },
    { "firstPage": 3, "lastPage": 3, "title": "Delivery receipt" }
  ]
}
```

That example is valid only for a persisted upload containing three contiguous pages. The response supplies document boundaries and labels. It does not supply upload IDs, owner IDs, source Markdown, image storage locations or arbitrary instructions to execute.

`SplitResult.Documents` uses Newtonsoft `JsonProperty("documents", Required = Required.Always)`. `PageRange` declares the same required setting for all three element fields. Keep serializer names aligned with the prompt and registered metadata type. A C# property rename must preserve or intentionally migrate the JSON contract.

Required-property deserialization is only the first validation layer. A schema-shaped response can still contain overlapping pages, out-of-range indexes or meaningless titles. `ConnectTo` enforces the business invariants before changing repositories.

## Constructor, registry and execution context

The concrete declaration is `SplitResult(IMetadataExecutionContext context) : BaseMetadata(context)`, using `Blueshift.Domain.LLM.BaseMetadata`. The framework contains other similarly named legacy types. Use the namespace already imported by the application.

The current base exposes `ConnectTo<T>(T target, IMetadataExecutionContext context)`, constrained to persistence objects implementing `ILlmCapable`. It also supplies JSON rendering helpers and a `StringValue` property ignored by Newtonsoft serialization. There is no application-specific measurement factory in this flow.

`AgentPack` registers `SplitResult` in `LlmTypeRegistry` and registers `SplitStep.Definition()` in `LlmCodeStepRegistry`. The step's `DTOResponseType` is `typeof(SplitResult).FullName`. The code-step registry binds this metadata type to tag `split-upload` and subject `UploadedFile`. Adding a class without registering it does not make it an accepted response type.

`SplitPolicy.CreateMetadataContext` creates `MetadataExecutionContext(prompt.ID, repository)`. Its repositories come from the active prompt repository. `PromptId` supplies execution provenance, while `GetRepository<TEntity>()` gives access to the application's persistence layer. This application supplies no structured-result renderer, so calling `RenderStructuredResult` through this context throws.

The worker resolves the registered type, normalizes response text and uses Newtonsoft deserialization with `BaseMetadataJsonConverter`. The converter first uses its configured factory or a constructor accepting `IMetadataExecutionContext`. Its fallback permits parameterless construction when that constructor is absent. Keep the existing context constructor for repository-backed metadata rather than depending on that fallback.

The converter populates the constructed object's fields. It does not authorize repository writes or validate page coverage. A context permits persistence operations and must therefore be supplied by trusted application execution, not by response JSON.

## Exact validation sequence

`SplitResult.ConnectTo` currently performs the following steps.

1. Reject a target that is not `UploadedFile` by returning false.
2. Query only `OcrPage` rows whose `UploadId` equals the target's ID, order them by `Number` and materialize the array.
3. Initialize the next expected page to 1.
4. For each returned range, reject a null element, a first page unequal to the next expected page, a last page below that first page, a last page beyond the persisted page count, a whitespace-only title or a title longer than 250 characters.
5. Advance the expected page to one beyond the accepted last page.
6. Reject an empty result or a final expected page that does not equal the page count plus one.
7. Only after every range passes, remove existing documents for that upload and stage the replacements.
8. Clear the upload's error and return true.

This enforces ordered complete coverage with neither gaps nor overlaps. It also excludes zero and negative starting pages. There is no sorting or silent repair of model output. Out-of-order valid-looking ranges remain invalid so a malformed response does not unexpectedly change meaning.

Titles are trimmed when constructing documents, after validating their original length. A title of more than 250 characters is rejected even if trimming would shorten it. Preserve this behavior unless the contract change explicitly chooses a different normalization rule.

## Page-array assumption

The implementation converts a range into `pages[(FirstPage - 1)..LastPage]`. It uses array positions rather than looking up every declared page number. This is correct under the existing OCR invariant that stored pages are numbered consecutively from 1 through the page count.

OCR validates provider indexes against `0..N-1` before persisting `Number = Index + 1`, and the database uniquely constrains `UploadId, Number`. The unique index prevents duplicate numbers but does not enforce contiguity or a starting number of 1. `ConnectTo` does not revalidate those numeric properties. It also uses the queried array length rather than `UploadedFile.PageCount` as the upper bound.

If future code permits page deletion, insertion, manual renumbering or partial OCR imports, revisit this assumption before reusing the current slice. A robust extension would validate the persisted sequence or deliberately map ranges by page number. Label that as new work rather than claiming it is already implemented.

## Document construction and save boundaries

For each valid range, the implementation stages a `Document` with the target upload ID, trimmed title, inclusive endpoints and Markdown joined from the selected stored pages. The separator is `\n\n<!-- page break -->\n\n`. Source text comes from persisted OCR pages, not from a model-generated Markdown field.

Existing documents are selected only by the same target upload ID. The method calls repository `Remove` and `Add`, which stage EF changes. It does not call `SaveChanges`, start a transaction or commit independently.

Validation before mutation means a rejected range leaves the method's document replacement untouched. It does not prove that every possible failure after mutation rolls back the whole workflow. Exceptions while staging changes can leave tracked changes in the shared context. The worker catches connection errors, so extensions must inspect the outer error and save path before assuming those tracked changes are discarded.

The current worker saves processed prompt/application changes, then can save again when it moves the subject back to `CheckForPrompts`. EF transaction behavior applies to actual saves and any surrounding transaction. The complete provider-call, parse, replacement, status and queue lifecycle is not one automatic transaction simply because `ConnectTo` returned true.

For changes that add complex writes, precompute and validate the entire proposed output before removing old rows. Define the intended transaction and rollback behavior at the owning execution layer. Test failure during persistence as well as validation failure. Do not introduce independent contexts or intermediate saves casually, since those can expose partially replaced results.

## Policy hooks and errors

The current `SplitPolicy` has intentionally empty `ClearPromptOutputs`, `OnPromptsCleared`, `PrepareAttachments` and `ResetOutputs` methods. `BeforeConnect` returns the metadata object, and `AfterConnect` does nothing. Returning that object is not a snapshot or rollback implementation.

Clearing prompts or resetting a prompt therefore does not itself clear existing documents through these hooks. Successful `ConnectTo` replaces documents. Invalid output is rejected before the replacement stage, so old documents can remain while the upload reports a new processing error. Any rerun feature must define this user-visible retention behavior explicitly.

The worker treats a false connection result as a connection failure and marks the prompt `PermanentError`. Deserialization failures also become prompt errors. The subject's step state is propagated to permanent error through the worker's failure handling.

`GetConnectionError` returns the safe message `Document ranges could not be applied.`. `OnFinished` assigns `Document splitting failed. See the agent logs.` to the related upload when an upload prompt ends in permanent error. `OnTerminalTransition` also sets that error when the step reaches permanent error or has no prompts. Successful metadata application clears `file.Error`.

Keep public errors concise. Use prompt and upload IDs to investigate diagnostics. Avoid embedding full OCR text or provider responses in newly added user-facing messages. A provider response containing valid JSON still needs the same application validation as a fixture.

## Ownership and trust boundaries

The worker chooses the target through trusted persisted prompt identity and the registered subject type. `SplitPolicy.Hydrate` loads the upload by that ID. The JSON cannot choose another upload because no such target field exists in the current response DTO.

Repository reads and replacements inside `ConnectTo` scope to `file.ID`. Preserve that restriction for every new entity written from metadata. If a future response introduces identifiers for existing objects, resolve and authorize them in trusted code. Do not let a model-provided ID bypass upload ownership or assign another user's document.

OCR text is untrusted source material. The system prompt asks the model to treat it as source data, and hydration serializes it for input. The response application must continue to enforce allowed structure and effects even when source text contains instructions. Prompt wording alone is not an authorization mechanism.

This internal processing path does not accept a Firebase caller. End-user authorization remains in API queries that check the upload's owner. Do not infer that exposing a new generated field or relationship through an endpoint is automatically safe because its metadata application succeeded.

## Extending the response

Treat response-shape changes as coordinated changes across the DTO, code registry, effective persisted step, validation, domain storage and public API where applicable.

For example, adding a proposed `category` field requires a deliberate allowed-value set, missing/unknown handling and storage decision. Adding `confidence` requires an explicit finite range and a product meaning. Neither field currently exists. Do not store or expose them solely because the provider can generate them.

Use this sequence for an extension.

1. Specify the JSON names, required versus optional fields and valid business values.
2. Define constructor/context behavior and update registered metadata identity if a new type is introduced.
3. Update the effective prompt definition. Bootstrap preserves existing rows, so a source edit does not synchronize an already-seeded template.
4. Validate every new value before staging replacement entities. Keep target identity and ownership outside model control.
5. Decide compatibility with responses already persisted on prompts that may be parsed after deployment.
6. Add the necessary mapped fields and explicit database transition if persistence changes.
7. Update API projections and schemas only for values intended for end users.
8. Verify success, malformed output, rerun behavior and persistence failure before running a live acceptance scenario.

Do not carry Horus's legacy measurement factory switch or `BaseMetadataConfig` constructor into this path. Current execution uses registered metadata, `IMetadataExecutionContext` and `ConnectTo` through framework policy. Provider structured-output settings help shape JSON but do not replace application invariants.

## Focused verification cases

| Case | Expected evidence |
| --- | --- |
| Three pages split into 1–2 and 3–3 | Two documents with matching titles, endpoints and source-page Markdown |
| One range covering all pages | One document with every page exactly once |
| Gap, overlap or reordered ranges | Connection rejected before document replacement |
| Zero, negative, reversed or oversized endpoints | Connection rejected |
| Empty array, null range or blank title | Connection rejected |
| Title length 250 versus 251 | Boundary accepted or rejected according to current untrimmed length rule |
| Missing required field or wrong root shape | Deserialization failure through the actual converter |
| Another upload with existing documents | Its rows remain unaffected |
| Old documents and valid replacement | Old rows replaced on the actual save path |
| Old documents and invalid response | Old rows retained, with processing failure propagated |
| Gapped persisted page numbers | Test exposes the current array-position assumption before any extension relies on it |
| Database failure during replacement | Verify actual transaction outcome and tracked-state cleanup rather than assuming rollback |

For deserialization tests, use `BaseMetadataJsonConverter` with a controlled `IMetadataExecutionContext`, matching the worker. Directly constructing a DTO cannot verify required JSON fields or constructor injection. For repository tests, inspect state before and after the outer save to distinguish staging from persistence.

Use a live smoke run only when the full provider-to-persistence path needs verification. Deterministic fixtures are more precise for individual validation branches. The live result must still be checked against persisted documents, prompt status and upload correlation.

## Sources

Application behavior is in [SplitStep.cs](SplitStep.cs), [OcrAgent.cs](OcrAgent.cs) and [Document.cs](../AIESummit.Domain/Document.cs). Framework contracts are [BaseMetadata.cs](../external/blueshift/Blueshift.Domain/LLM/BaseMetadata.cs), [IMetadataExecutionContext.cs](../external/blueshift/Blueshift.Domain/LLM/IMetadataExecutionContext.cs), [BaseMetadataJsonConverter.cs](../external/blueshift/Blueshift.Domain/LLM/BaseMetadataJsonConverter.cs), [LlmPromptWorker.cs](../external/blueshift/Blueshift.Agents/LLM/LlmPromptWorker.cs) and [EFBasePersistenceRepository.cs](../external/blueshift/Blueshift.Domain/Concrete/EFBasePersistenceRepository.cs).

The complete historical `../../horus/Voiston.Agents/Concrete/LLM/AGENTS.md` was used for pipeline context. Its legacy factories, separate agent-definition library and provider schema guarantees must not be treated as current application contracts.
