# AIESummit.Domain entity guide

Page-section analysis and the page reader are implemented. See [the current page-analysis guide](../docs/page-analysis.md) for the `OcrPage` LLM subject, Gemini APIInfo, additive schema upgrade, queues, evidence validation and Admin checks; the detailed split/OCR guidance below remains applicable to that original workflow.

Framework source links in this guide are optional upstream references at the bundled revision and may require maintainer access. Normal development consumes `dependencies/blueshift`; no framework source checkout is required.

This project owns the persistent upload model shared by the API, worker and central BlueShift Admin. Read this guide before adding an entity or changing a property, relationship, index or base class. Preserve stored property names and relationship identities unless the task includes a deliberate database transition.

Each application entity has its own file: [UploadedFile](UploadedFile.cs), [OcrPage](OcrPage.cs), [Document](Document.cs), [PageImage](PageImage.cs), and [OcrAgentConfig](OcrAgentConfig.cs). [AIESummitDb.cs](AIESummitDb.cs) owns registration and EF mapping. The project targets .NET 10 and references the bundled `10123.Blueshift.Domain` NuGet package. Persistence uses EF Core and PostgreSQL. The Horus entity guide is a design reference, but its EF6 registration and application-specific conventions do not describe this application.

## Reading routes

Read the relevant companion before making the corresponding change.

| Change | Guidance |
| --- | --- |
| Admin labels, snippets, editors and connectors | [DisplayMetadata.AGENTS.md](DisplayMetadata.AGENTS.md) |
| Foreign keys, collections and connected Admin lists | [Relationships.AGENTS.md](Relationships.AGENTS.md) |
| LLM subject properties, hydration and prompt persistence | [IAmLLMCapable.AGENTS.md](IAmLLMCapable.AGENTS.md) |
| Ownership traversal and deletion cleanup | [IHaveChildren.AGENTS.md](IHaveChildren.AGENTS.md) |
| Stable code-defined identifiers and tagged samples | [IHaveUniqueTag.AGENTS.md](IHaveUniqueTag.AGENTS.md) |
| Database registration, schema changes and saves | [Persistence.AGENTS.md](Persistence.AGENTS.md) |
| Upload bytes, OCR pages and embedded images | [Files.AGENTS.md](Files.AGENTS.md) |
| Public projections and Firebase ownership | [API guide](../AIESummit.Api/AGENTS.md) |
| Admin discovery and access configuration | [Admin deployment guide](../deploy/Admin.AGENTS.md) |
| Worker activation and processing policies | [Agents guide](../AIESummit.Agents/AGENTS.md) |

The LLM companion retains the historical filename for discoverability. The implemented interface is `Blueshift.Domain.LLM.ILlmCapable`.

## Current model and invariants

| Type | Responsibility | Important relationship or constraint |
| --- | --- | --- |
| `UploadedFile` | Owner, original bytes, OCR state and LLM state | Indexed by `OwnerId, ID`. Has persisted `LLMPrompts` |
| `OcrPage` | One OCR page and its Markdown | Required `UploadId`. Unique `UploadId, Number` |
| `Document` | A logical document identified by the split step | Required `UploadId`. Unique `UploadId, FirstPage` |
| `PageImage` | Associates an embedded image with an OCR page | Required `PageId` and `ImageId`. Unique `PageId, Reference` |
| `OcrAgentConfig` | Configuration of the OCR worker | Derives from framework `BaseAgentConfig` |
| `AIESummitDb` | Application database model and save normalization | Derives from framework `EFBaseDbContext` |

`OcrPage.Number` and `Document.FirstPage`/`LastPage` use inclusive, one-based page numbers. The split policy validates complete ordered coverage before replacing documents. The unique document index prevents duplicate starts but does not itself enforce nonoverlapping ranges or complete coverage.

Images belong to OCR pages through `PageImage`. Documents expose the images in their page range through API queries. There is no persisted `Document.Images` navigation. Do not add a second image ownership model merely to make an Admin list visible.

`UploadedFile.SplitInput` is transient and has `[NotMapped]`. Its value is constructed from persisted pages during hydration. `UploadedFile.LLMPrompts` is a persisted collection. Applying `[NotMapped]` to that collection would prevent generated prompts from being stored.

## Adding an entity

Complete each applicable registration layer. A compiled class alone does not establish database persistence, Admin access or public API access.

1. Define the entity in `AIESummit.Domain`, in its own `.cs` file. Keep related small enums beside their entity when that helps comprehension. Keep response DTOs and processing services in their existing API or agent layer.
2. Select a base class and interfaces for actual behavior. Give required strings and collections sensible initial values. Make optional references and their foreign keys consistently nullable.
3. Add a public `DbSet<NewEntity>` to `AIESummitDb`. Map ambiguous relationships, delete behavior and uniqueness explicitly in `OnModelCreating`. Preserve its call to `base.OnModelCreating(modelBuilder)`.
4. Plan how the database schema reaches the new model. `Bootstrap.InitializeAsync` currently calls `EnsureCreatedAsync`. It creates a missing schema and does not upgrade an existing one. See the persistence companion before changing a database with retained data.
5. If operators need the entity, add its name to `Admin.EntityAllowList` in `deploy/admin.json`, place it in the appropriate navigation menu and choose `EntityAccess` permissions. Verify that the loaded Domain assembly and EF model contain it. The current catalog filters discovered pack types against the mapped model when available.
6. Add metadata, friendly naming and any deliberate connected-list declarations. Verify the rendered Admin behavior with the actual central Admin version.
7. If end users need access, add owner-scoped API queries and explicit response projections. Update the matching OpenAPI components and paths. Admin registration does not create a public API contract.
8. If processing is required, register the agent or LLM policies and update recovery eligibility as needed. An entity interface does not activate a worker by itself.
9. Add idempotent startup data only when needed. Preserve operator-managed values and reference environment-specific rows through stable identifiers.
10. Verify the relevant persistence, authorization and processing paths before considering the change complete.

The SDK-style [project file](AIESummit.Domain.csproj) automatically includes source files. Do not add individual `Compile Include` entries. Existing host and pack configuration already refers to this Domain project. A new assembly or a moved context requires a separate deployment review.

## Choosing a base class

Use `BasePersistenceObject` for ordinary rows. It supplies `ID`, `DateCreated`, `LastModified`, friendly-name hooks and framework persistence behavior. `ID` is the inherited identifier spelling. Do not introduce a second `Id` property.

Override `GetFriendlyName()` when a title or filename is more useful than the default ID-based name. `UploadedFile` returns `FileName` and `Document` returns `Title`. Friendly names must remain safe to display and must not contain source bytes, credentials or full extracted documents.

Use `BaseAgentConfig` for a configuration consumed by an agent. `OcrAgentConfig` follows this pattern. It is a different responsibility from a row representing a processing result.

`BaseWithChildren<T>` is available in `Blueshift.Domain.Entities.Base` for framework ownership traversal and cleanup. No local application entity currently derives from it. Adding it is a future design change that requires reviewing the children companion and actual deletion call path. A navigation collection alone does not establish ownership.

Framework file and user-oriented bases may be useful for future features, but do not replace `UploadedFile` solely because it stores bytes or belongs to a user. Such a change affects mapping, file handling and identity semantics. The current application deliberately stores `Source` on `UploadedFile` and the Firebase subject in `OwnerId`.

Horus-specific `DataPartnerObject<T>`, patient interfaces and data-partner ownership are not application contracts here. Neither `ISamplesProvider<T>` nor `ISearchable<T>` is required on every current local entity. Add an optional framework interface only after identifying its consumer and checking the pinned implementation.

## Property and metadata design

Use `System.ComponentModel.DataAnnotations.DisplayAttribute` for explanatory names and descriptions when introducing fields. Use `Blueshift.Domain.Infrastructure.DisplayMetadataAttribute` for Admin presentation. Class metadata controls labels such as `MenuName`. Property metadata selects snippet fields, visibility, editability and connectors.

Keep snippets short and useful. File name, processing status and document title are existing examples. Do not make extracted Markdown, source bytes or full prompt bodies snippet content. Choose editability according to the workflow. Processing outputs currently have entity-wide create, update and delete disabled in Admin.

`IsHidden` affects presentation. It does not enforce authorization or suppress every serializer. `Source` is hidden in Admin metadata, while API projections independently prevent the entity from being returned wholesale. Use the serializer appropriate to the actual consumer when serialization attributes are needed. The legacy Horus blanket Newtonsoft requirement does not apply to every host here.

Choose connectors that match the field type and verify central Admin support. `SearchListSingle` describes selecting a reference. `Text`, `Switcher`, `Date`, `Secret` and other connector values describe editor intent. A masked secret field remains sensitive data. Detailed connector examples and supported rendering behavior belong in the metadata companion.

## Relationships and indexes

Keep a scalar foreign key beside each ordinary reference navigation, following the established `UploadId`/`Upload`, `PageId`/`Page` and `ImageId`/`Image` patterns. EF conventions cover these simple required references. Use fluent mapping or an explicit `ForeignKey` attribute when naming or multiple relationships makes inference ambiguous.

The following is a proposed extension example, not a current entity. It shows a required upload reference and per-upload uniqueness. Registration and a schema transition would still be required.

```csharp
using Blueshift.Domain.Abstract;
using Blueshift.Domain.Infrastructure;

namespace AIESummit.Domain;

[DisplayMetadata("glyphicon glyphicon-tag", MenuName = "Upload labels")]
[Microsoft.EntityFrameworkCore.Index(nameof(UploadId), nameof(Name), IsUnique = true)]
public sealed class UploadLabel : BasePersistenceObject
{
    public int UploadId { get; set; }
    public UploadedFile Upload { get; set; } = null!;

    [DisplayMetadata("", ShowInSnippet = true)]
    public string Name { get; set; } = "";

    public override string GetFriendlyName() => Name;
}
```

Add `public DbSet<UploadLabel> UploadLabels => Set<UploadLabel>();` to the context for that proposed model. Validate nonblank labels at the write boundary. An initialized empty string satisfies C# initialization but is not a business validation rule.

Place EF Core indexes on the entity class and name mapped scalar properties. Existing source fully qualifies `Microsoft.EntityFrameworkCore.Index` to avoid ambiguity with legacy schema attributes. Choose index order from actual filter and ordering paths. Uniqueness should encode a business invariant, as with one page number per upload.

An Admin related section needs class-level `ForeignTypes` metadata. A navigation property's `DisplayMetadata` does not declare that section. `UploadedFile` currently exposes OCR pages and documents through direct child references, plus processing prompts through its mapped collection. `OcrPage` exposes extracted image links through `PageImage.Page`. Verify the resolver, mapped foreign key, target catalog visibility and access policy when adding another section. These sections retain the existing read-only permissions.

Keep EF mapping, Admin presentation and JSON serialization decisions separate. `[NotMapped]` is appropriate for calculated or transient values. It is inappropriate for a collection whose relationship must persist, including the current LLM prompt collection. Hiding a navigation in Admin does not require removing it from the EF model.

For a future many-to-many relation, choose either EF Core skip navigations with deliberate mapping or an explicit join entity. Use a join entity when the connection itself carries data such as sequence, role or provenance. Qualified relationships need separate unambiguous navigations and mappings. Horus's EF6 `.Map(...)` example is not valid EF Core mapping syntax. See the relationships companion for current examples and connected-list behavior.

## Owner scoping and public data

The authenticated Firebase subject determines `UploadedFile.OwnerId`. Never accept the authoritative owner from a request body. Dependent rows inherit access through their upload. Adding an independent owner column to a dependent requires a consistency strategy and a clear reason.

Every public read must constrain ownership in its query. A lookup by document or image ID is insufficient. Current image access joins `PageImage` through `Page.Upload` and checks its `OwnerId` as well as the requested upload ID. Preserve that relationship when changing storage or routing.

Return selected DTO fields. Do not return the EF graph and rely on hidden metadata to protect `Source`, prompts or credentials. Existing endpoint-local records and projections are the current pattern. Horus's nested `PublicType`/`ToPublicObject()` convention is optional design inspiration, not a missing registration step.

Central Admin is an operator surface with its own access configuration. Do not assume it applies Firebase end-user ownership filters automatically. New Admin write capabilities need explicit consideration of owner assignment, state transitions and related-row selection.

## LLM-capable entities

`ILlmCapable` requires `LLMPrompts`, `LLMStepStatus` and `LLMJsonResult`. `UploadedFile` implements it. The context maps its prompts with shadow foreign key `UploadedFile_ID` and cascading deletion. `LLMPrompt.UsedModel` uses optional shadow foreign key `UsedModelID` with restricted deletion, since a new prompt can exist before a model has been used.

Keep prompt string normalization in `AIESummitDb.SaveChangesAsync`. Framework-created prompt objects may contain null values for required string columns. The current override normalizes them before delegating to the framework base context. Preserve the base save path and its publication behavior when extending this override.

The step agent receives an ID-oriented subject. `SplitPolicy.Hydrate` reloads the persisted `UploadedFile` with its `LLMPrompts`, then builds transient input from ordered pages. Returning an unhydrated placeholder risks overwriting stored scalar fields and processing state. A new LLM subject needs equivalent hydration, mapped prompt ownership, registry entries, policies and worker configuration.

The current split definition uses tag `split-upload`. The bootstrap model uses `foundry-split`. Response metadata validates ranges before replacing `Document` rows. Model output is data to validate, not permission to update arbitrary entities. Read the LLM and agent companions before adding a subject or changing these contracts.

## Children, cleanup and stable tags

Use `IHaveChildren` only when framework deletion or traversal must understand ownership. A child is an object whose lifecycle belongs to the parent. Shared configuration, model references and unrelated followers are not children. `GetChildrenConfigurator` and connecting-reference cleanup must agree with nullable foreign keys and database delete behavior.

Database cascades and framework cleanup are distinct mechanisms. Neither a `ForeignTypes` section nor a populated collection proves that a deletion path is safe. Current generated records are read-only through Admin, and local entities do not implement a children hierarchy. A future delete feature must define retention and cleanup of pages, documents, prompt rows, image links and potentially orphaned image bytes before enabling deletion.

`IHaveUniqueTag` declares a mutable string `Tag` contract and expects implementations to add a unique index. It does not enforce uniqueness by itself. Use stable tags for code-defined configuration references that must survive different database IDs. Do not turn user filenames or document titles into configuration identifiers.

Bootstrap currently resolves existing model and step rows by tag and creates missing rows under a PostgreSQL transaction and advisory lock. Existing operator configuration is preserved. A tag rename can therefore create a second row and leave consumers referring to the old one. Treat tag changes as data transitions. There is no local CLI project or automatic tagged-sample synchronization workflow to invoke.

## Verification before completion

- Build the affected project from the repository root with `dotnet build AIESummit.Domain/AIESummit.Domain.csproj`. Build the solution when signatures used by API or agents change.
- Inspect the EF model for every new entity, foreign key, nullable relationship, unique index and delete behavior. Check both a fresh database and the explicit upgrade path for retained data when a schema changes.
- Verify that a new Admin entity is present in the catalog and intended menu, has useful names and snippet fields, and enforces its configured write permissions. Check related sections against known parent and unrelated rows.
- Verify public reads with two accounts. Knowing another account's upload, document or image identifier must not grant access. Confirm response projections exclude source bytes and internal prompts.
- For LLM changes, verify prompts persist, the initial optional model reference inserts successfully, hydration preserves upload fields, completion applies valid documents, and invalid output leaves a useful error without partial replacement.
- Exercise the relevant restart or recovery path when state or queue eligibility changes. Preserve the framework base save calls and check processing evidence in Admin.
- Use the existing smoke workflow when changing the full OCR-to-document flow. Read [smoke guidance](../scripts/smoke.AGENTS.md) for prerequisites and provider usage. A documentation-only edit does not require a live model call.
- Report what was verified and any remaining database transition requirement. Do not reset a retained database to make a schema check pass.

## Source references and legacy translation

The implementation sources are [AIESummitDb.cs](AIESummitDb.cs), [Bootstrap.cs](../AIESummit.Api/Bootstrap.cs), [Program.cs](../AIESummit.Api/Program.cs), [UploadEndpoints.cs](../AIESummit.Api/UploadEndpoints.cs), [SplitStep.cs](../AIESummit.Agents/SplitStep.cs) and [admin.json](../deploy/admin.json).

Framework contracts are in [BasePersistenceObject.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Domain/Abstract/BasePersistenceObject.cs), [EFBaseDbContext.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Domain/Concrete/EFBaseDbContext.cs), [ILlmCapable.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Domain/LLM/ILlmCapable.cs), [BaseWithChildren.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Domain/Entities/Base/BaseWithChildren.cs), [IHaveUniqueTag.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Domain/Abstract/IHaveUniqueTag.cs), [ForeignTypesAttribute.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Domain/Infrastructure/ForeignTypesAttribute.cs) and the central Admin [CatalogService.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Admin.Api/Services/CatalogService.cs). Treat the pinned binary bundle as a dependency and inspect it read-only for application work.

The full reference guide at `../../horus/Horus.Domain/Entities/AGENTS.md` motivates the registration checklist, relationship design and metadata guidance. Translate its assumptions as follows.

| Horus instruction | Current application equivalent |
| --- | --- |
| EF6 context and relationship mapping | EF Core `AIESummitDb` with PostgreSQL |
| Add every entity source to the project file | SDK implicit source inclusion |
| Add an entity controller to `CommonController` | Central Admin catalog discovery, allowlist, navigation and access configuration |
| Remind the user to manually create migrations | Identify the actual schema transition needed because current startup uses `EnsureCreatedAsync` and no local migration workflow is established |
| Implement sample/search interfaces on every entity | Add only contracts required by a verified consumer |
| Use patient/data-partner ownership bases | Use Firebase subject ownership and scoped upload queries |
| Put `NotMapped` on display collections | Decide persistence independently and keep real relationships mapped |
| Use the historical LLM interface name | Implement current `ILlmCapable` plus registered policies |
| Require nested public DTOs | Preserve explicit API projections and matching OpenAPI contracts |

## Configuration seeding

See [the implemented seeding workflow](../docs/configuration-seeding.md). `AIESummit.Configuration` exports the existing step definitions; the bundled Blueshift CLI performs validation, read-only planning and previewed transactional application. `SeedIdentities` and the idempotent `SeedSchema` upgrade support registry provenance. Default exports preserve model/enabled selections and all credentials. No publishers or provider calls run in the seed host. The one-off Docker configuration image and isolated PostgreSQL tests are documented there.
