# Persistence and schema evolution

Read [AGENTS.md](AGENTS.md) for entity registration and [Relationships.AGENTS.md](Relationships.AGENTS.md) for relationship design. This guide covers the current EF Core/PostgreSQL persistence path and how to approach future schema changes without losing retained data.

The authoritative context is `AIESummitDb` in [AIESummitDb.cs](AIESummitDb.cs). It derives from the pinned framework's `EFBaseDbContext`. The API registers it through `UseNpgsql` and connection string name `AIESummitDb`. The shared worker and Admin load the same context from the domain pack. SQL Server in Compose belongs to the Service Bus emulator, not the application model.

## Current database lifecycle

`Bootstrap.InitializeAsync` calls `db.Database.EnsureCreatedAsync()` at API startup. This initializes an empty database model. It does not compare an existing schema with changed classes and apply upgrades. There is no application migrations directory, model snapshot, migration command wrapper or established migration baseline in this repository.

Do not claim that restarting the API applies entity changes. A build can pass while the database still has the old column, index or foreign-key shape. Similarly, `EnsureCreatedAsync` returning without an error is not evidence that an existing database matches the new model.

Schema initialization currently occurs before the bootstrap seed transaction and advisory lock. The seed lock does not serialize the earlier `EnsureCreatedAsync` call across competing first-start API instances. If deployment grows beyond the current local arrangement, design schema initialization as an explicit deployment responsibility rather than assuming the seed lock covers it.

Named PostgreSQL data is retained when running `docker compose down`. Dropping the database or removing volumes is not a schema migration. An unrelated Firebase or telemetry volume also contains retained state, so broad stack resets have consequences beyond the application tables.

## Registered model

`AIESummitDb` explicitly exposes the following sets.

| Sets | Purpose |
| --- | --- |
| `Uploads`, `Pages`, `Documents`, `PageImages` | Application input and processing output graph |
| `Images` | Framework image bytes referenced by page/image links |
| `APIInfos`, `LLMModels`, `LLMSteps`, `LLMPrompts` | Provider configuration, processing definitions and durable prompt work |
| `OcrAgentConfigs`, `LLMStepAgentConfigs`, `LLMPromptAgentConfigs` | Enabled agents and their runtime configuration |
| `BackendActivities`, `LogLineStores` | Framework persistence support |

Navigation discovery may include additional framework types. Inspect the finalized EF model rather than treating this table as an exhaustive SQL table inventory. The current OpenSearch execution log store is configured independently of these database sets.

Preserve `base.OnModelCreating(modelBuilder)`. The framework assigns `BasePersistenceObject.ID` as the key on mapped persistence roots and applies its conventions. A derived class must not add a competing identifier because a new property name looks more idiomatic.

Current relationship mappings include two important shadow keys.

```csharp
modelBuilder.Entity<UploadedFile>().HasMany(file => file.LLMPrompts).WithOne()
    .HasForeignKey("UploadedFile_ID").OnDelete(DeleteBehavior.Cascade);
modelBuilder.Entity<LLMPrompt>().HasOne(prompt => prompt.UsedModel).WithMany()
    .HasForeignKey("UsedModelID").IsRequired(false).OnDelete(DeleteBehavior.Restrict);
```

These are existing mappings. `UploadedFile_ID` persists the upload's prompt collection. It is distinct from the prompt's logical `DocumentType` and `DocumentID` dispatch identity. The optional `UsedModelID` lets a prompt exist before a provider run identifies its used model. Its restricted deletion prevents removing referenced models through that relationship.

Do not rename shadow keys casually or replace them with new scalar properties without comparing the resulting model. EF may interpret an accidental rename as a new relationship or column. Check both the model and stored values before preparing a transition.

`UploadedFile.SplitInput` is transient and explicitly `[NotMapped]`. `LLMPrompts` is mapped. Removing that navigation from the model breaks persistence of prompts added by the generic step agent. Admin presentation visibility and public serialization are separate concerns and are not reasons to remove a real relationship from EF.

The explicit scalar keys `UploadId`, `PageId` and `ImageId` accompany normal reference navigations. Their nonnullable integer types express required relationships. Review EF's finalized delete behavior for these convention-based relationships before implementing deletion. Existing prompt cascade mapping is not proof that every related image byte or application output has a safe deletion lifecycle.

## Indexes and data invariants

| Entity | Declared index | Meaning |
| --- | --- | --- |
| `UploadedFile` | `OwnerId, ID` | Supports owner-scoped upload lookups and ordering |
| `OcrPage` | Unique `UploadId, Number` | One persisted page number per upload |
| `Document` | Unique `UploadId, FirstPage` | No duplicate document start within an upload |
| `PageImage` | Unique `PageId, Reference` | No duplicate image reference within one page |

The attributes are `Microsoft.EntityFrameworkCore.Index` on the class. Do not use EF6 property-level index syntax. Index column order matters for query access paths and must be reviewed with actual filters.

Uniqueness does not establish all business rules. The document index permits invalid overlaps with different starting pages. `SplitResult.ConnectTo` validates full ordered coverage before replacing results. Likewise, an initialized empty owner string is not a valid authentication decision. Public writes must derive ownership from Firebase.

Before adding a unique index to retained data, inspect duplicates and define how they will be reconciled. Before making a column required, inspect nulls and choose a meaningful backfill. Never rely on a schema tool to infer business-correct replacement values.

## Save path and framework hooks

`AIESummitDb.SaveChangesAsync(CancellationToken)` first normalizes null strings on added or modified `LLMPrompt` entries. The normalized fields are `SystemPrompt`, `Prompt`, `Response`, `MultimodalLambda`, `ExpectedResponseJsonFormat`, `ReasoningEffort`, `DTOResponseType`, `MD5Hash`, `Parser` and `DocumentType`. It then delegates to `base.SaveChangesAsync`.

This accommodates framework-created prompts whose optional values have not been initialized while required string columns expect nonnull values. It does not validate that a step, target or metadata type is supported. Those checks remain in the LLM registry and execution path.

The parameterless synchronous path is also covered. The pinned `EFBaseDbContext.SaveChanges()` calls `SaveChangesAsync().GetAwaiter().GetResult()`. Virtual dispatch reaches the AIESummit override. `EFBasePersistenceRepository.SaveChanges()` invokes that parameterless context method, while its asynchronous method forwards the cancellation token to the asynchronous context method.

Do not generalize this to every EF overload or operation. Direct `SaveChanges(bool)`, `SaveChangesAsync(bool, CancellationToken)`, bulk updates and raw SQL are separate paths unless explicitly routed through these overrides. When introducing one, verify normalization, timestamps and publication behavior rather than assuming it is equivalent.

The framework asynchronous path performs the following work.

1. It enforces applicable captured-execution immutability rules for tracked `LLMExecution` rows.
2. It assigns `DateCreated` for new persistence objects when unset and updates `LastModified` for modified objects.
3. It captures previous values for modification hooks and saves tracked changes through EF.
4. It invokes `BasePersistenceObject.ModifiedPO` for created and modified objects using repositories tied to this context.
5. If hooks leave tracked changes, it performs a further base EF save.

Preserve those hooks when extending the context. Replacing the framework base class with plain `DbContext`, or bypassing its save entrypoints, can leave durable rows without the processing publications expected by the app. Direct SQL used for a data correction needs an explicit plan for timestamps and any processing that must be reconsidered.

Do not claim that the override turns every multi-save workflow into one transaction. If several mutations must succeed or fail together, define an explicit transaction boundary and verify hook behavior within it.

## Transactions and publication

`EFBaseDbContext.PublishAfterCommitAsync` dispatches immediately when there is no explicit current transaction. With a current transaction, it registers the callback against that transaction ID. `CommitPublications` runs registered callbacks after commit and discards them on observed rollback or transaction failure.

Domain modification hooks still run during the save. External work routed through this publication mechanism waits until commit, preventing workers from seeing a message before the committed row is visible. Do not publish an independent queue message halfway through a transaction and assume the context can defer it.

The mechanism is context-local, not a durable transactional outbox. A host can stop after data commit and before publication completes. The framework also reports a failure when committed work could not all be published. At that point, a thrown exception does not mean the database transaction was rolled back. Inspect durable state before retrying a write that could create duplicates.

AIESummit's worker recovery scans eligible pending OCR, step and prompt work every 45 seconds. See [QueueRecovery.AGENTS.md](../AIESummit.Agents/QueueRecovery.AGENTS.md). Preserve the ability to recover pending subjects. Do not reset successful or permanently failed records merely because transport publication was uncertain.

## Bootstrap seed boundary

After schema initialization and configuration validation, `Bootstrap.InitializeAsync` begins a PostgreSQL transaction and executes `SELECT pg_advisory_xact_lock(825833001)`. It then reads or creates the required connection, model, step and agent-configuration records, saves, and commits.

The transaction-scoped advisory lock serializes cooperating bootstrap callers using the same database and key. It does not lock unrelated application code that ignores this convention. It is also not a unique constraint or a substitute for relationship integrity.

Seed identities are `mistral-ocr` and `openai` for API connections, `foundry-split` for the model, and `SplitStep.Tag` for the step. Agent configuration is seeded only when the relevant configuration set has no rows. Do not describe this last check as matching every agent row by name or class.

Existing records are intentionally preserved. A changed environment key, model name or code template does not overwrite existing persisted operator configuration on restart. Rotate or update those records through the intended operator surface. A tag rename can create an additional record rather than updating the original one.

Keep the advisory lock, reads, seed writes and commit in the same transaction when extending bootstrap. Avoid a save-per-row pattern outside the transaction. Verify restart idempotence and preservation of operator edits, not only successful first initialization.

## Future migration workflow

This is a proposed implementation process, not an existing runnable migration command sequence. Do not copy Horus's EF6 tooling, `temp*` naming rule, SQL Server connection strings, `__MigrationHistory` queries or `voiston` CLI commands into AIESummit.

1. Establish the exact target environment, database, application revision and pinned framework revision without printing secrets. Inspect the actual schema, data constraints and any existing migration history.
2. Classify the change as additive, backfill, rename, constraint change or destructive conversion. Define compatibility with the API, worker and Admin versions that may run during deployment.
3. Choose the EF Core migrations assembly, design-time context construction and a locally reproducible tool version compatible with the project. The current repository does not provide that setup yet.
4. Capture a verified baseline from the existing EF model and database. Distinguish a new empty database from a retained database originally created by `EnsureCreated`.
5. Design an explicit adoption procedure for retained databases. Do not invent history rows, stamp a migration as applied to hide drift, or apply a create-everything migration blindly to existing tables. Reconcile the real schema and review the adoption evidence first.
6. Scaffold and review the migration and model snapshot. Inspect generated SQL for table drops, column replacement, nullability, provider types, foreign-key changes and index creation. A rename should preserve data rather than silently become a drop/add pair.
7. Define how schema deployment replaces the `EnsureCreated` startup responsibility. Do not keep two competing mechanisms and hope they converge.
8. Back up the intended data and restore it into a separate test database. Exercise the migration against that restored copy, including realistic prompts and operator configuration, then verify the application behavior.
9. Establish the rollback or forward-repair plan, service quiescing requirements and deployment order. Validate the plan before changing retained data.
10. Apply through the reviewed deployment mechanism, record what ran, then verify schema, migration history and functional behavior. Keep intended migration files and snapshots under version control.

A future EF Core migration history table differs from Horus's EF6 history. Its exact schema and name should be confirmed from the configured provider and migration options. History entries are evidence of a process, not proof that someone has not changed tables afterward.

No host-side `dotnet ef` command is promised to work today. Do not document a guessed startup project or require a package not yet installed. When implementing migration tooling, provide tested commands and design-time configuration that do not accidentally start paid processing or mutate a live database during scaffolding.

## Backups and retained state

Before a change that can lose or transform retained data, identify what must be recoverable and verify a restore into a separate database. The application database includes original files, extracted text/images, prompts and provider configuration, so a backup contains sensitive content even if it sits in ignored `artifacts/`.

Use a PostgreSQL-compatible logical backup or an established database backup mechanism. Keep the backup target distinct from the active database and avoid passing secret connection strings in visible command text. A successful dump command is insufficient without a restore check and application-level inspection of representative records.

Database backup alone does not cover Firebase accounts, OpenSearch telemetry or Admin audit files stored in other Compose volumes. Decide which of those are required for the intended recovery. Do not remove their volumes as an incidental consequence of database testing.

Quiesce writers when the planned transition requires a stable view. The API, worker and Admin can all write through the shared model. Stopping only the API does not necessarily stop background processing. Keep source and output ownership relationships intact across backfills and schema changes.

## Verification

From the repository root, build the domain and then the solution when mappings or shared signatures change.

```powershell
dotnet build AIESummit.Domain/AIESummit.Domain.csproj --nologo
dotnet build AIESummit.slnx --nologo
git diff --check
```

Choose further checks based on the changed behavior.

- Inspect the finalized EF model for each new or changed key, shadow foreign key, nullability, index and delete behavior. Compare it with actual PostgreSQL catalogs without dumping row contents.
- Create an isolated fresh database to verify initialization. Separately test a restored retained database through the explicit transition. Success on an empty database is not upgrade evidence.
- Save a newly created prompt before a used model exists, then reload it through a fresh context. Verify normalization and the upload/prompt relationship on both ordinary asynchronous and parameterless synchronous repository save paths if those are affected.
- Verify date fields and modification-triggered processing remain intact. With an explicit transaction, verify a rolled-back operation does not publish external work through the commit mechanism.
- Exercise a committed-publication failure or restart with controlled test infrastructure when changing recovery. Verify durable pending work is recoverable without duplicating already completed output.
- Restart bootstrap after an operator configuration edit and confirm it preserves that edit. Confirm unrelated user records and owner IDs remain unchanged after a schema transition.
- Run the synthetic end-to-end smoke when changing persistence used by OCR and LLM processing. It uses live configured providers, so read [smoke.AGENTS.md](../scripts/smoke.AGENTS.md) first.

There is no dedicated local migration test suite or application CLI to substitute for these checks. State exactly which evidence was gathered and which transition remains unverified. Documentation-only edits do not require a database write or live model call.

## Failure diagnosis

| Symptom | Investigate before changing data |
| --- | --- |
| Missing table or column after restart | Existing schema versus current model. `EnsureCreated` did not apply an upgrade |
| Prompt insert rejects null strings | Actual save entrypoint and prompt normalization override |
| Prompt insert requires a used model too early | `UsedModelID` optional relationship and actual stored constraint |
| Prompts appear in memory but disappear after reload | `LLMPrompts` mapping and shadow foreign-key values |
| Data is saved but processing does not start | Base persistence hooks, commit/publication outcome and pending-work recovery |
| Seed changes appear ignored | Preserved existing records and their stable identifiers |
| Uniqueness change fails | Existing duplicates and the proposed reconciliation rule |
| Commit reports publication failure | Whether database commit already succeeded, before retrying the write |

## Sources and inspiration

Current application evidence is [AIESummitDb.cs](AIESummitDb.cs), [Program.cs](../AIESummit.Api/Program.cs), [Bootstrap.cs](../AIESummit.Api/Bootstrap.cs), [compose.yaml](../compose.yaml) and [UploadQueueRecovery.cs](../AIESummit.Agents/UploadQueueRecovery.cs).

Framework evidence is [EFBaseDbContext.cs](../external/blueshift/Blueshift.Domain/Concrete/EFBaseDbContext.cs), [EFBasePersistenceRepository.cs](../external/blueshift/Blueshift.Domain/Concrete/EFBasePersistenceRepository.cs) and [CommitPublications.cs](../external/blueshift/Blueshift.Domain/Concrete/CommitPublications.cs). Inspect the pinned dependency read-only unless the task explicitly concerns a framework change.

The complete reference `../../horus/Horus.Domain/Entities/migrations.AGENTS.md` motivates the build, inspect, apply and verify discipline. Its EF6 commands, environment details, credential examples, temporary naming convention and history-repair procedures are not portable instructions for this repository. This guide deliberately supplies no copied credential or unverified migration command.
