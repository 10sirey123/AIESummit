# Hierarchy traversal and deletion

Framework source links in this guide are optional upstream references at the bundled revision and may require maintainer access. Normal development consumes `dependencies/blueshift`; no framework source checkout is required.

Read this guide before introducing `IHaveChildren` or changing deletion of an entity with dependents. Read [Relationships.AGENTS.md](Relationships.AGENTS.md) for EF mapping and Admin related lists. The hierarchy contract describes application traversal and cleanup. EF relationships, database delete behavior, authorization, and file storage remain separate concerns.

## Current venture boundary

None of the local entities in [AGENTS.md](AGENTS.md) implements `IHaveChildren` or derives from a hierarchy base. `UploadedFile`, `OcrPage`, `Document`, and `PageImage` derive directly from `BasePersistenceObject`. `OcrAgentConfig` derives from `BaseAgentConfig`.

`UploadedFile.LLMPrompts` is a real mapped EF collection. `AIESummitDb` explicitly gives its shadow FK `UploadedFile_ID` cascade delete behavior. The prompt's optional `UsedModelID` relationship uses `Restrict`. These mappings do not declare a hierarchy configurator. Do not change them merely to adopt the examples below.

The current Admin overlay disables deleting uploads, pages, documents, page-image links, images, and prompts. Adding an interface must not silently enable an operator deletion workflow. Any future delete operation must deliberately define authorization, ownership, processing coordination, and the save boundary.

Every example in this guide is a proposed pattern. No example introduces an existing venture entity or implies that local delete endpoints already use hierarchy traversal.

## Decide whether a relationship is owned

An owned child has a lifecycle that belongs to one parent. Deleting the parent is intended to delete that child. A reusable model, shared image, category, follower, or other independently meaningful record is not automatically a child just because a navigation reaches it.

| Relationship intent | Hierarchy declaration | Other work still required |
| --- | --- | --- |
| Delete an owned record with its parent | `Add` | Mapped navigation, child hierarchy implementation, explicit traversal call, valid delete ordering |
| Preserve a dependent but clear its optional parent reference | `AddConnecting(collection, nullableFk)` | Nullable integer FK and compatible EF/database constraints |
| Preserve both records and remove collection links | `AddConnecting(collection)` | A mapped mutable collection and correct join mapping |
| Display related records in Admin | Usually `ForeignTypes` rather than hierarchy | Follow the relationship guide |
| Cascade entirely through database relationships | EF delete behavior | Verify loaded/unloaded behavior and all inbound constraints |

Write down the intended lifetime before selecting a method. Never put the same relationship in both `Add` and `AddConnecting` as a workaround for a constraint error. A many-to-many link collection is normally a connection, while an explicitly modeled join entity may itself be owned if its lifecycle warrants that choice.

## Interfaces and imports

The current definitions live in `Blueshift.Domain.Entities.Base`. Use the following namespaces for the patterns below.

```csharp
using System.Linq.Expressions;
using Blueshift.Domain.Abstract;
using Blueshift.Domain.Entities.Base;
```

`IHaveChildren<T>` requires `T : BasePersistenceObject`. Its two members configure traversal and delete descendants.

```csharp
Expression<Func<ChildrenConfigurator<TConfigurator, T>,
    ChildrenConfigurator<TConfigurator, T>>>
    GetChildrenConfigurator<TConfigurator>();

int DeleteChildren<TRepo>(IBasePersistenceRepository<TRepo> repo,
    IDictionary<Type, object> cache_repo = null)
    where TRepo : BasePersistenceObject;
```

The separate nongeneric `IHaveChildren` exposes traversal and deleting one node through a type-independent reference.

```csharp
IEnumerable<ChildNode> MyChildren<TRepo>(
    IBasePersistenceRepository<TRepo> originalRepo,
    IDictionary<Type, object> repo_cache = null,
    int recursion_level = 0)
    where TRepo : BasePersistenceObject;

void DeleteMe<TRepo>(IBasePersistenceRepository<TRepo> original_repo,
    IDictionary<Type, object> cache_repo)
    where TRepo : BasePersistenceObject;
```

These signatures mirror the framework's existing nullable-default syntax. In nullable-enabled application code, a manually implemented optional cache can be annotated `IDictionary<Type, object>?` and normalized before calling the helper. Notice that the nongeneric interface requires a cache argument for `DeleteMe`, even though `BaseWithChildren<T>` provides a convenience default on its public method.

## Choose a base class deliberately

`BaseWithChildren<T>` derives from `BasePersistenceObject` and implements both interfaces. Its default configurator returns its argument unchanged, so a leaf class can use the base without declaring any children. Its type parameter must be the implementing entity type, not the type of its children.

`BaseUserWithChildren<T>` derives from `BaseUserObject`. `FileWithChildren<T>` derives from `PersistenceFileWithUpload`. Both supply the hierarchy methods, but also change the entity's base-domain contract. Do not replace `UploadedFile` with a file base simply because it contains bytes. Its current `Source`, owner string, upload API, and processing interfaces are independent design choices.

The old Horus guide also lists `DataPartnerObject<T>`. Do not import that venture-specific base into AIESummit or assume it is part of this framework checkout. Choose from types that actually exist in the referenced project.

### Proposed owned collection

This minimal example demonstrates the checked generic signatures. It is a modeling sketch, not a complete new-entity rollout or a proposed replacement for current uploads.

```csharp
public sealed class ReviewBatch : BaseWithChildren<ReviewBatch>
{
    public ICollection<ReviewItem> Items { get; set; } = [];

    public override Expression<Func<
        ChildrenConfigurator<TConfigurator, ReviewBatch>,
        ChildrenConfigurator<TConfigurator, ReviewBatch>>>
        GetChildrenConfigurator<TConfigurator>()
        => config => config.Add<ReviewItem>(batch => batch.Items);
}

public sealed class ReviewItem : BaseWithChildren<ReviewItem>
{
    public int ReviewBatchId { get; set; }
    public ReviewBatch ReviewBatch { get; set; } = null!;
}
```

Both `Add` overloads require the selected child type to derive from `BasePersistenceObject` and implement nongeneric `IHaveChildren`. A plain `BasePersistenceObject` child does not satisfy that constraint. The leaf above inherits the empty implementation. Existing `OcrPage` and `Document` therefore cannot simply be added to a new configurator without addressing their hierarchy contracts too.

One `Add` overload selects a single child. The other selects `IEnumerable<TChild>`, which accepts a suitable mapped collection such as `ICollection<TChild>`. Keep collections initialized and ensure selectors refer to real navigation properties. The helper uses EF `Include` for configured owned collections. `[NotMapped]` is not an appropriate way to hide such a collection from Admin. Use display metadata and the appropriate serialization/projection boundary instead.

The helper's load configuration does not install a single-reference include callback. A single-child selector can therefore depend on an already loaded reference. Verify its behavior with a fresh database context before using the single-child overload. A null selected child reaches `ChildNode` and is rejected.

### Preserve references with a nullable FK

The two-argument `AddConnecting` overload takes an `Expression<Func<TDependent, int?>>`. It does not accept an arbitrary FK type or a required integer FK as a nullable cleanup contract.

```csharp
public sealed class ReviewCategory : BaseWithChildren<ReviewCategory>
{
    public ICollection<ReviewReference> References { get; set; } = [];

    public override Expression<Func<
        ChildrenConfigurator<TConfigurator, ReviewCategory>,
        ChildrenConfigurator<TConfigurator, ReviewCategory>>>
        GetChildrenConfigurator<TConfigurator>()
        => config => config.AddConnecting(
            category => category.References,
            reference => reference.ReviewCategoryId);
}

public sealed class ReviewReference : BasePersistenceObject
{
    public int? ReviewCategoryId { get; set; }
    public ReviewCategory? ReviewCategory { get; set; }
}
```

This proposed pattern preserves references while clearing the category FK. The cleanup implementation determines the dependent type, queries its repository for rows whose selected FK equals the parent ID, and sets that property to null. It can query dependents even when the parent collection was not loaded. A deliberately unmapped collection selector can work in this particular overload because it does not use that collection's contents to find matches. That exception does not justify removing a real EF navigation mapping.

The current framework `LLMStep` uses this overload for `LLMPrompt.LLMStep_ID`. This is a useful source example because prompts survive the reference cleanup. Required venture FKs such as `Document.UploadId` and `OcrPage.UploadId` do not have equivalent nullable semantics.

### Preserve many-to-many targets

For a mapped, mutable collection of shared targets, the one-argument pattern is `config.AddConnecting(owner => owner.RelatedRecords)`. The related type only needs `BasePersistenceObject`, not `IHaveChildren`.

The helper includes the selected collection and removes each item from that parent-side collection. It does not delete the target rows. EF must track the join relationship for that removal to become persisted unlinking. A `[NotMapped]` collection does not acquire join-table behavior from the configurator. Check the corresponding relationship mapping and save/reload the result.

This overload also assumes collection removal is supported. It catches removal exceptions. Do not treat a return without an exception as evidence that every link was removed.

## Manual implementation when inheritance cannot change

If an entity must retain another base, implement both interfaces and delegate to `BaseWithChildrenGeneric<TEntity>`. The helper requires `TEntity : BasePersistenceObject, IHaveChildren<TEntity>`. Keep the same entity type in every generic argument.

The following methods belong inside a proposed `ManualOwner : BasePersistenceObject, IHaveChildren<ManualOwner>, IHaveChildren` class. Its empty configuration intentionally declares no children.

```csharp
public Expression<Func<ChildrenConfigurator<TConfigurator, ManualOwner>,
    ChildrenConfigurator<TConfigurator, ManualOwner>>>
    GetChildrenConfigurator<TConfigurator>() => config => config;

public IEnumerable<ChildNode> MyChildren<TRepo>(
    IBasePersistenceRepository<TRepo> repo,
    IDictionary<Type, object>? cache = null, int level = 0)
    where TRepo : BasePersistenceObject
    => BaseWithChildrenGeneric<ManualOwner>.MyChildren(
        this, repo, cache ?? new Dictionary<Type, object>(), level);

public int DeleteChildren<TRepo>(IBasePersistenceRepository<TRepo> repo,
    IDictionary<Type, object>? cache = null)
    where TRepo : BasePersistenceObject
    => BaseWithChildrenGeneric<ManualOwner>.DeleteChildren(
        this, repo, cache ?? new Dictionary<Type, object>());

public void DeleteMe<TRepo>(IBasePersistenceRepository<TRepo> repo,
    IDictionary<Type, object>? cache = null)
    where TRepo : BasePersistenceObject
    => BaseWithChildrenGeneric<ManualOwner>.DeleteMe(
        this, repo, cache ?? new Dictionary<Type, object>());
```

Do not call the static helper with an uninitialized cache. Base wrappers normally create one. Use a cache scoped to a single operation and compatible repository context, never a shared global dictionary.

## Actual deletion behavior in this checkout

The current implementation differs from the older Horus description of recursive `DeleteMe`. Follow the source and hierarchy behavior tests.

| Call | Current behavior |
| --- | --- |
| `MyChildren` | Reloads the parent by ID, gathers declared owned children, and exposes recursive `ChildNode` results |
| `DeleteChildren` | Performs the parent's connecting cleanup, enumerates configured descendants, calls each node's `DeleteMe`, and leaves the parent itself intact |
| `DeleteMe` | Performs connecting cleanup and stages deletion of this node with `DeleteObjectControlAutosave(ID, false)` |
| `SaveChanges` | Persists staged changes through the repository context |

`DeleteMe` does not call `DeleteChildren`. Neither hierarchy method calls `SaveChanges`. `DeleteChildren` returns the count of node deletion calls it performed, not a confirmed database row count or a count of join rows and nulled references.

A proposed service-level operation using a `ReviewBatch` repository would explicitly make both calls and then save.

```csharp
var cache = new Dictionary<Type, object>();
batch.DeleteChildren(repository, cache);
batch.DeleteMe(repository, cache);
repository.SaveChanges();
```

Before this sequence, the service must authorize the operation, verify the parent still exists, validate lifecycle constraints, and establish the intended transaction/context boundary. On failure, do not continue using partially staged tracked changes as if the operation had succeeded. Reconcile or discard the failed unit of work according to the service's transaction design.

The current `LLMStepAgent.ClearPrompts` is an actual consumer. It removes the prompt from its subject collection, invokes application output cleanup, calls `prompt.DeleteChildren` and `prompt.DeleteMe`, then saves after clearing prompts. That explicit sequence explains why merely changing an entity base does not create a complete deletion workflow.

Ordinary `EFBasePersistenceRepository.DeleteObject` removes a row through its own delete path and autosaves. It does not independently dispatch to `IHaveChildren`. Admin `EntityCrudService.DeleteAsync` calls its persistence abstraction's delete operation. Trace the whole selected entry point before claiming an Admin delete button invokes hierarchy cleanup.

## Traversal and failure limits

The helper rehydrates a parent from repository queries and caches repositories by entity type. Its traversal is lazy in parts, so enumeration can raise errors after the initial `MyChildren` call. A missing parent or malformed selector does not have a friendly application-level error contract here. Validate these cases at the service boundary.

The recursion guard allows level 10 and throws above it. It is a limit, not cycle detection. A self-cycle eventually triggers the guard during enumeration. Keep declared ownership acyclic and avoid shared children reachable through multiple ownership paths.

`ChildNode.AsFlatOrderedList` uses nested reversal and `Distinct`. The hierarchy tests preserve its observed ordering, including a nested example that produces IDs `4, 2, 3, 1`. Do not assume a general topological or strict deepest-first order for arbitrary graphs. Database constraints and realistic multi-level tests must confirm the intended deletion succeeds.

One-to-many cleanup catches and logs exceptions from dependent querying or FK updates. Many-to-many removal catches exceptions too. Child deletion failures can propagate after earlier cleanup and deletions have already been staged. No part of that behavior guarantees that all references are clean merely because a helper method returned. Verify the resulting database state and expected failure handling.

The file hierarchy base does not, through these helpers, delete external storage or invoke storage-source retrieval. The user hierarchy base does not enforce a caller's owner authorization. Ownership, storage retention, background jobs, audit records, and application output cleanup belong to the service or policy that invokes deletion.

## Verification for a hierarchy change

1. Enumerate every inbound FK and join relationship. Decide which records are deleted, preserved with null references, preserved with links removed, or intended to block deletion.
2. Confirm every owned child satisfies the `Add` constraints and every nullable cleanup selector points to an actual nullable integer FK.
3. Inspect the EF model and database constraints. Preserve mapped collections, explicit shadow FKs, and deletion restrictions unless the requested lifecycle change requires otherwise.
4. Trace the real operation from endpoint or agent to repository. Verify authorization happens before traversal and the caller controls saving and failure recovery.
5. Test with a fresh context so already loaded navigation properties do not hide missing includes. Cover empty collections, one level, several levels, shared targets, and unloaded dependents.
6. Verify after save and reload that owned rows are gone, retained rows survive, nullable links are cleared, unrelated rows remain unchanged, and join links are removed only for the intended parent.
7. Exercise a blocking FK or injected deletion failure and confirm partial staged work is not accidentally committed later. Check file cleanup separately if storage is involved.
8. Check current Admin restrictions and upload ownership rules still hold. A hierarchy change does not authorize broadening access.

## Source anchors

- [IHaveChildren.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Domain/Entities/Base/IHaveChildren.cs) and [ChildrenConfigurator.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Domain/Entities/Base/ChildrenConfigurator.cs) define signatures and generic constraints.
- [BaseWithChildren.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Domain/Entities/Base/BaseWithChildren.cs) defines all three current hierarchy base families and cache defaults.
- [BaseWithChildrenGeneric.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Domain/Entities/Base/BaseWithChildrenGeneric.cs) defines loading, cleanup, traversal limits, and deletion staging.
- [ChildNode.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Domain/Entities/Base/ChildNode.cs) defines flattening and null-node behavior.
- [HierarchyBehaviorTests.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Hierarchy.Tests/HierarchyBehaviorTests.cs) and [HierarchyBoundaryTests.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Hierarchy.Tests/HierarchyBoundaryTests.cs) document observed ordering, failure, save, ownership, and storage boundaries.
- [LLMStep.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Domain/Entities/LLMStep.cs) shows nullable reference cleanup. [LLMStepAgent.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Agents/LLM/LLMStepAgent.cs) shows explicit descendant/self deletion and a caller-owned save boundary.
- [EFBasePersistenceRepository.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Domain/Concrete/EFBasePersistenceRepository.cs) and [EntityCrudService.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Admin.Api/Services/EntityCrudService.cs) distinguish normal delete entry points from explicit hierarchy calls.

The complete Horus hierarchy guide informed this guide's practical scope. Its statement that `DeleteMe` recursively deletes children is not the behavior of the checked-in framework. Keep this distinction when borrowing examples, and recheck implementation and tests on a framework upgrade. Framework sources remain reference material for application changes.
