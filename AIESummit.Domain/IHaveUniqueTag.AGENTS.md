# Stable tags and configuration identity

Framework source links in this guide are optional upstream references at the bundled revision and may require maintainer access. Normal development consumes `dependencies/blueshift`; no framework source checkout is required.

Use this guide when adding code-defined configuration, changing an LLM step tag, or diagnosing duplicate configuration after startup. Read [Persistence.AGENTS.md](Persistence.AGENTS.md) for schema changes and [Bootstrap.AGENTS.md](../AIESummit.Api/Bootstrap.AGENTS.md) for the complete seeding workflow.

## Interface and database contract

`Blueshift.Domain.Abstract.IHaveUniqueTag` declares one member.

```csharp
string Tag { get; set; }
```

The interface makes tagged objects recognizable to framework tooling. It does not add an index, validate nonblank input, normalize case, synchronize rows or resolve references automatically. Its source explicitly asks implementations to add a unique index.

The current framework `LLMStep` and `LLMModel` both implement this interface and declare EF Core unique indexes on `Tag`. Their index names are `IX_PromptTag` and `IX_LLMModelTag`, respectively. Both tag properties have a 128-character length annotation. Preserve these constraints when extending the model and verify that the deployed database actually contains them.

The following is a proposed new entity pattern, not an existing application type.

```csharp
using System.ComponentModel.DataAnnotations;
using Blueshift.Domain.Abstract;

namespace AIESummit.Domain;

[Microsoft.EntityFrameworkCore.Index(nameof(Tag), IsUnique = true)]
public sealed class ProcessingProfile : BasePersistenceObject, IHaveUniqueTag
{
    [Required, StringLength(128)]
    public string Tag { get; set; } = "";

    public string Name { get; set; } = "";
}
```

This proposed entity still needs context registration, schema deployment, validation at write boundaries and deliberate Admin access. An empty initializer makes construction convenient and does not make an empty tag valid. The unique index belongs on the class and must refer to the mapped property. The application uses fully qualified EF Core index attributes to avoid legacy attribute ambiguity.

## Tags, IDs and display names

Use `ID` for persisted relationships within one database. Use a tag for a stable configuration identity across environments whose numeric IDs differ. Use `Name` for an operator-facing label that can change without redefining identity.

A tag is scoped to its entity type. `TaggedSampleCatalog` identifies samples by type plus tag. A model and step can technically share a string, but descriptive distinct tags make configuration easier to inspect. Do not derive tags from database IDs, user-upload filenames or generated document titles.

Choose canonical lowercase spellings for new application configuration. Keep them stable after deployment. Do not embed credentials, tenant secrets or environment-specific endpoints in tags. Changing which provider deployment a logical model uses usually changes its configuration fields, not its stable identity.

## Current application identities

| Value | Kind | Current use |
| --- | --- | --- |
| `split-upload` | `LLMStep.Tag` | `SplitStep.Tag` constant, initial step seed and registered code-step binding |
| `foundry-split` | `LLMModel.Tag` | Bootstrap lookup and initial model used by a new split step |
| `mistral-ocr` | `APIInfo.Identifier` | OCR credential lookup and initial credential seed |
| `openai` | `APIInfo.Identifier` | LLM credential lookup and initial credential seed |

The credential identifiers are not evidence that `APIInfo` uses this tag interface. They are separate lookup fields. Likewise, agent configuration seeding currently checks whether any row of the relevant configuration type exists. It does not use `IHaveUniqueTag`.

`SplitStep.Definition()` returns a new `LLMStep` with tag `split-upload`, subject `UploadedFile` and response type `AIESummit.Agents.SplitResult`. Bootstrap assigns its model navigation to the tracked `LLMModel` found or created for `foundry-split`. The current factory does not return a fully persisted or model-resolved step by itself.

## Current idempotent startup behavior

`Bootstrap.InitializeAsync` creates a missing schema, opens a transaction and obtains PostgreSQL advisory transaction lock `825833001`. Inside that lock it looks up the model by exact tag, adds it only when missing, checks for the step tag, and adds the step only when missing. It saves and commits once the initial configuration is prepared.

Existing model and step rows are preserved. Startup does not overwrite an operator-edited model name, provider, prompt, template, enabled flag or existing step-to-model association. `LLM_MODEL` supplies the initial model name, defaulting to `gpt-4.1-mini`. Changing that environment variable does not update a model already stored under `foundry-split`.

This behavior is intentional idempotent creation. Source changes to `SplitStep.Definition()` do not automatically update an existing database step. Use the implemented [configuration seeding workflow](../docs/configuration-seeding.md) to export the actual definitions, review stable-tag matches and field differences, then apply the exact previewed manifest. Default exports preserve selected models and enabled flags; model/API records use keep policy. Seed registry identities supplement the entities' natural keys and record provenance without changing existing database IDs.

The advisory lock serializes cooperating startup seeders that use the same lock. It does not serialize arbitrary Admin writes or outside tools. Unique indexes remain essential for database integrity. An `AnyAsync` check followed by an insert alone is vulnerable to concurrent writers.

`EnsureCreatedAsync` runs before the seed transaction and does not upgrade existing schemas. Adding a unique index annotation to source is insufficient for an already-created database. Read the persistence guide and inspect existing duplicates before planning a constraint transition.

## Runtime bindings and source changes

`AgentPack` constructs `LlmCodeStepRegistry` from `SplitStep.Definition()` and the registered subject/metadata types. The registry captures support bindings by tag. It validates the database step's subject and metadata identity against those bindings. It is not a copy of every editable step field and does not synchronize prompts or templates into PostgreSQL.

The registry compares tag keys with `StringComparer.OrdinalIgnoreCase`. Subject and metadata strings must match their registered bindings with ordinal equality. Prompt validation also requires a positive persisted step ID and a matching `LLMStep_ID`. A correct tag alone does not make a detached step usable as an existing prompt's reference.

When changing a step definition, rebuild and load the affected worker binaries, inspect the stored step, apply the intended database-field changes through the authorized workflow, and verify the effective configuration before processing an upload. A source-only template change can leave an existing database testing its old template.

Horus documents a mandatory `Voiston.CLI repo LLMStep update-from-code` sequence. That executable and workflow do not exist locally in this application. Do not prescribe those commands or imply that rebuilding invokes them. Preserve the underlying requirement to validate the effective runtime definition, using the current application workflow. See [SplitStep.AGENTS.md](../AIESummit.Agents/SplitStep.AGENTS.md).

## Tagged sample providers

[SplitSamples.cs](../AIESummit.Agents/SplitSamples.cs) defines the initial split model and step through `IUniqueTagSampleProvider.ProvideSamples()`. Bootstrap explicitly loads and validates those samples with `TaggedSampleCatalog`, resolves the model tag to a tracked row, and inserts only missing records. `LLM_MODEL` still overrides the deployment name on first insertion. Existing configuration remains operator-managed. Assembly discovery and CLI synchronization are optional extension paths.

For a future global classification catalog, give the entity a stable `Tag` and a unique EF index. Put its default categories in a dedicated sample provider, validate the definitions together, and register them in startup's missing-only seed path. Keep the human-readable label editable independently of the tag. Store a document's selected category as a relationship to the catalog entry. Define the actual vocabulary and its validation before adding such a catalog.

`TaggedSampleCatalog.Discover` scans only the explicitly supplied assemblies. It deduplicates assembly registrations, finds concrete providers, orders them by full name and instantiates them with `Activator.CreateInstance`. Providers therefore need an accessible parameterless construction path. Merely implementing an interface in an unregistered assembly does not make its definitions discoverable.

`ValidateIntegrity()` checks blank tags, duplicate type/tag pairs, missing tagged dependencies, missing discoverable foreign-key properties, persisted positive IDs on tagged references and dependency cycles. Call it before any future synchronization mutates a database. Discovery alone does not perform these validation checks or write rows.

`GetAll` and successful `TryGet` return property copies. The clone copies writable public properties shallowly. Nested reference objects and collections are not deep-cloned. Do not mutate nested sample graphs under the assumption that every returned object is fully isolated.

The catalog uses case-insensitive type/tag identity and returns a `TryGet` match only when exactly one sample matches. Duplicate samples are an integrity error to fix, not a reason to choose the first one.

## Future tagged references

`TaggedReference.To<T>(tag)` creates `new T { Tag = tag }`. It is a detached tag-only placeholder with the default persistence ID. It performs no database query and does not attach an existing row.

This proposed sample fragment uses the current framework types and FK convention.

```csharp
var step = SplitStep.Definition();
step.Model = TaggedReference.To<LLMModel>("foundry-split");
```

`SplitSamples` uses this pattern. Bootstrap replaces the detached reference with the tracked model before calling `db.Add(step)`. Passing the placeholder directly to EF can cause a duplicate model insert.

A future synchronizer must resolve the model tag against the target database and assign its local ID/navigation before saving. A standalone sample catalog containing this step also needs a corresponding model sample, otherwise dependency validation reports a missing target definition.

`TaggedReferenceInspector` checks a navigation's `ForeignKey` attribute, then attributes on scalar properties, then conventional key names. Supported conventions include navigation name plus `_ID`, `ID` or `Id`, followed by the same suffixes on the target type name. `LLMStep.Model_ID` is explicitly attributed with `ForeignKey(nameof(Model))`, so this reference is discoverable.

Do not copy positive IDs from development into code-defined references. They are not portable and catalog validation rejects positive IDs on tagged reference targets. A future resolver must also define duplicate, missing-target and concurrent-write behavior before deployment.

## Case sensitivity and renames

Framework catalogs and the code-step registry use case-insensitive tag matching. Current Bootstrap uses EF equality predicates such as `m.Tag == "foundry-split"`, whose database semantics depend on the column type and collation. Ordinary PostgreSQL varchar equality is case-sensitive, and this application does not declare an explicit case-insensitive tag normalization policy.

Do not infer database behavior from the in-memory comparer. `foundry-split` and `Foundry-Split` may behave as distinct database keys while being the same framework catalog identity. Check the actual deployed collation and constraints. Keep the exact current canonical spelling across source, stored rows and tools. If normalization is introduced, audit collisions before changing constraints or data.

Renaming a tag is a configuration identity change. With current seed behavior, removing or renaming the stored `foundry-split` model causes startup to create a replacement row under the expected tag. An existing `split-upload` step is not automatically repointed to it. Changing the source step constant can similarly seed another step while historical prompts remain attached to the old persisted ID.

Plan a rename deliberately.

1. Find every code reference, stored row, model association and existing prompt reference affected by the old tag.
2. Decide whether the intent is an in-place identity rename or a new version that should coexist. Record treatment of in-flight work and old prompts.
3. Check collisions under both database equality and framework case-insensitive matching.
4. Coordinate source bindings and persisted configuration so the supported step remains executable throughout the transition.
5. Preserve existing numeric relationships when appropriate. Do not delete configuration rows merely to force startup to seed replacements.
6. Restart the relevant hosts and verify the expected number of rows, selected model, supported type binding and prompt references.

## Verification and failure diagnosis

- Confirm one intended database row exists for each canonical tag, and inspect differently cased variants when duplicate behavior is suspected.
- Confirm the actual unique index exists. A declaration in source does not demonstrate that a retained database was upgraded.
- Run startup again against the same database and verify configuration count and operator-edited fields remain unchanged.
- For concurrency changes, test cooperating startup instances and a conflicting outside writer separately. A seed lock and a unique index protect different boundaries.
- For a step edit, inspect effective stored template, subject, metadata type, model association and enabled state before evaluating output quality.
- For a future sample workflow, require an empty integrity-error list and verify dependency resolution across databases with different IDs.
- Treat a registry support error as a tag/type-binding investigation. Treat a duplicate-index error as a database identity or concurrency investigation. Avoid retrying either indefinitely without correcting the underlying state.
- Keep credentials out of sample providers, diagnostics and tag reports. A model identity check does not require logging API keys.

## Source references

Current behavior is defined by [Bootstrap.cs](../AIESummit.Api/Bootstrap.cs), [SplitStep.cs](../AIESummit.Agents/SplitStep.cs) and [AIESummitDb.cs](AIESummitDb.cs). Framework evidence is in [IHaveUniqueTag.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Domain/Abstract/IHaveUniqueTag.cs), [LLMStep.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Domain/Entities/LLMStep.cs), [LLMModel.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Domain/Entities/LLMModel.cs) and [LlmCodeStepRegistry.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Domain/LLM/LlmCodeStepRegistry.cs).

The full historical reference `../../horus/Horus.Domain/LLMSteps/AGENTS.md` supplies the effective-runtime verification principle. Its CLI commands, measurement metadata lifecycle and legacy metadata constructor APIs are not current AIESummit contracts.
