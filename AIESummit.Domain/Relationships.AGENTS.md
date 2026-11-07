# Relationships and connected Admin lists

Read this guide when adding a reference, collection, join entity or related section in central Admin. [AIESummitDb.cs](AIESummitDb.cs) is the current EF model. [AGENTS.md](AGENTS.md) covers entity registration, [DisplayMetadata.AGENTS.md](DisplayMetadata.AGENTS.md) covers editors, and [IHaveChildren.AGENTS.md](IHaveChildren.AGENTS.md) covers ownership cleanup.

## Three separate contracts

| Contract | Mechanism | What it establishes |
| --- | --- | --- |
| Database relationship | EF navigation, scalar foreign key and fluent mapping | Stored references, nullability, uniqueness and database delete behavior |
| Admin presentation | Property `DisplayMetadata` and class `ForeignTypes` | Reference editors and connected sections |
| Access control | API owner predicates and Admin access configuration | Who can read, reassign, link, unlink or delete records |

Changing one contract does not automatically implement the others. A hidden field still exists in the database. A connected section does not grant permission to mutate records. A valid foreign key does not establish that the target belongs to the current user.

## Current relationships

| Dependent | Reference | Scalar FK | Meaning |
| --- | --- | --- | --- |
| `OcrPage` | `Upload` | `UploadId` | Page belongs to an upload |
| `Document` | `Upload` | `UploadId` | Split result belongs to an upload |
| `PageImage` | `Page` | `PageId` | Embedded image reference occurs on this OCR page |
| `PageImage` | `Image` | `ImageId` | Link points to framework image storage |
| `LLMPrompt` | No upload-side inverse reference | Shadow `UploadedFile_ID` | Prompt belongs to `UploadedFile.LLMPrompts` |
| `LLMPrompt` | `UsedModel` | Shadow `UsedModelID` | Optional model used for a prompt |

The first four relationships use EF conventions with required integer foreign keys. The prompt relationships are explicitly mapped in `AIESummitDb.OnModelCreating`. Prompt ownership cascades on upload deletion. `UsedModel` is optional and uses `DeleteBehavior.Restrict`.

The model declares direct `ForeignTypes` sections for upload pages, upload documents, and page-image links. It also declares a collection-backed processing-prompts section on `UploadedFile`. There are no persisted reverse collections for pages, documents, or page images; direct sections use the existing child-side foreign keys.

Documents describe inclusive one-based page ranges. Their images are queried through pages in that range. There is no direct `Document` to `PageImage` relationship and no `Document.Images` collection. Do not invent a direct foreign key solely to display this derived association.

## Reference selection on a property

`DisplayMetadata` belongs on the navigation when describing an editor for that reference. `SearchListSingle` expresses single-entity selection. It does not create a reverse connected section on the referenced entity.

The following is a proposed presentation change to the existing `OcrPage` properties. It preserves their stored names and keeps the generated page association read-only.

```csharp
[DisplayMetadata("", IsHidden = true)]
public int UploadId { get; set; }

[DisplayMetadata("glyphicon glyphicon-upload",
    IsEditable = false,
    ConnectorType = AttributeConnectorType.SearchListSingle)]
public UploadedFile Upload { get; set; } = null!;
```

This example requires `using Blueshift.Domain.Infrastructure;`, which the current entity file already has. `IsEditable = false` reinforces editor intent. The existing entity-wide access configuration is still responsible for rejecting writes. Do not enable upload reassignment for OCR output merely because a selector can render.

Use a nullable scalar key and nullable navigation together when a relationship is genuinely optional. For nonconventional names, use fluent `HasForeignKey` or an explicit `ForeignKey` attribute. A C# `null!` initializer suppresses a compiler warning and does not load or validate the referenced row.

## Connected sections for the current model

The following decorations describe the direct sections implemented in the individual entity files. Keep their targets and foreign-field names aligned with the model.

For `UploadedFile`, add both attributes.

```csharp
[ForeignTypes(typeof(OcrPage), nameof(OcrPage.Upload),
    ForeignConnectionTypes.Direct,
    Title = "OCR pages", Icon = "lucide:ScanText", Order = 10)]
[ForeignTypes(typeof(Document), nameof(Document.Upload),
    ForeignConnectionTypes.Direct,
    Title = "Documents", Icon = "lucide:FileText", Order = 20)]
```

For `OcrPage`, add this attribute.

```csharp
[ForeignTypes(typeof(PageImage), nameof(PageImage.Page),
    ForeignConnectionTypes.Direct,
    Title = "Extracted images", Icon = "lucide:Image", Order = 10)]
```

All symbols come from `Blueshift.Domain.Infrastructure` and the existing application model. The resolver translates `Upload` into `UploadId` on `OcrPage` or `Document`, and `Page` into `PageId` on `PageImage`. The parent does not need an `ICollection` for these direct queries. These metadata additions alone do not require database schema changes.

Both ends must remain discoverable in Admin. The current `deploy/admin.json` allowlist already includes `UploadedFile`, `OcrPage`, `Document`, `PageImage` and `Image`. It also disables create, update and delete for these generated result types. Preserve those permissions when adding sections.

Use meaningful snippet or list-column metadata on the target type so a section has useful columns. Current snippets include `Document.Title`, `FirstPage`, `LastPage`, `OcrPage.Number`, and `PageImage.Reference`. Relationship attributes do not choose those columns; property metadata does.

## How the current resolver interprets metadata

`ForeignTypesAttribute` is a repeatable class attribute and is not inherited. It carries target type, foreign field, connection mode, title, optional icon and order. A blank icon inherits the target entity's display icon. The catalog places nonzero orders before zero-order declarations and projects each relation into its entity detail response.

For `Direct`, `EntityCrudService.ResolveFkProperty` checks the target type in this order.

1. `<ForeignField>_ID`
2. `<ForeignField>Id`
3. `<ForeignField>` itself

Consequently `nameof(OcrPage.Upload)` works because `UploadId` exists. `nameof(OcrPage.UploadId)` also resolves through the exact-property fallback, but navigation names provide a clearer declaration for the current pattern. This resolver is a specific reflection convention, not the full EF relationship inference algorithm. It does not consult arbitrary `ForeignKey` attributes or discover shadow properties for this lookup.

Do not declare a direct prompt section with `UploadedFile_ID` merely because that shadow key exists in EF. The resolver requires a CLR property. The implemented prompt section instead uses `ForeignConnectionTypes.MultipleIndirect` with `nameof(UploadedFile.LLMPrompts)`, loading the existing mapped collection. This mode describes how Admin reads the association; it does not change the EF relationship into many-to-many. The hidden field metadata does not prevent the related section from loading it.

For `MultipleIndirect`, `ForeignField` names a collection on the parent. The resolver loads that collection and obtains connected IDs. It does not interpret that string as a child-side foreign key. Mixing these meanings produces missing-property failures or incorrect queries.

Relation requests are addressed by parent type and foreign type. The current resolver selects the first matching foreign type from metadata. Two independently qualified sections targeting the same type cannot safely be distinguished by inventing different titles or fields alone. Review the host contract before designing multiple roles to the same target type.

## Connected, available and all views

The related-list endpoint accepts `view=connected`, `view=available` or `view=all`. Omitted view means connected. Direct connected queries compare the resolved foreign key with the parent ID. Available queries select rows whose key differs or is null. All returns the target set before optional search and pagination.

Available therefore includes rows assigned to other parents. It does not mean unowned, safe to reassign or owned by the same Firebase user. This is particularly important for upload-owned records. Keep their write permissions disabled and do not expose generic Admin relation endpoints as end-user APIs.

The service bounds page size to 1–200 and projects target list columns or snippet fields. Test pagination and search on the connected set instead of assuming the whole table is eagerly included with the parent.

## Link, unlink and deletion semantics

The resolver infers write mode from the relationship shape. A direct target with at least two required FK-shaped integer properties receives `createJoin`. Other direct targets receive `setFk`. A collection relation receives `collection`.

For the current-model sections this produces the following behavior if writes were enabled.

| Section | Inferred mode | Mutation meaning |
| --- | --- | --- |
| Upload to OCR pages | `setFk` | Reassign an existing page's `UploadId` |
| Upload to documents | `setFk` | Reassign an existing document's `UploadId` |
| OCR page to page images | `createJoin` | Create or delete a `PageImage` join row |

This is why display-only sections should retain the existing access restrictions. Reassigning a generated page or document could violate owner boundaries or derived page-range invariants. Creating a page-image link also needs valid `Reference` data and must satisfy the unique `PageId, Reference` index.

Linking requires parent update permission and target update permission, or target create permission for `createJoin`. Unlinking requires parent update permission and target update permission, or target delete permission for `createJoin`. These service checks matter even when the UI hides a button.

`setFk` unlink clears the FK and therefore rejects nonnullable keys. It does not delete the child. `createJoin` unlink deletes the join row. Collection unlink removes membership. Removing a `PageImage` row does not by itself delete the referenced `Image` bytes. Define orphan retention and cleanup explicitly if a delete workflow is introduced.

For the conventional required relationships, inspect the effective EF model and database constraints before depending on cascade behavior. `ForeignTypes` does not set database delete rules. Framework `IHaveChildren` cleanup is a separate traversal mechanism, and no local application entity currently implements that hierarchy. Read the children and persistence companions before enabling any deletion path.

## Future many-to-many relationships

Use mapped collections for a true many-to-many association, or an explicit join entity when the association carries properties. `PageImage` already demonstrates an association carrying its own `Reference` and uniqueness rule. A future ordered, qualified or provenance-bearing relationship generally needs equivalent explicit data.

With mapped skip navigations, declare `MultipleIndirect` on each parent using that parent's collection name. EF Core mapping must establish the database relationship separately. Do not add `[NotMapped]` to a collection intended to persist membership. `IsHidden` can suppress a generic property editor without changing persistence.

Horus's EF6 `.HasMany(...).WithMany(...).Map(...)` sample is not an EF Core mapping recipe. Its hidden, unmapped reverse collection pattern is also unnecessary for the direct sections proposed here. Copy the relationship intent, then use the current EF and Admin contracts.

For derived associations such as document images, a read-only query relation may be a future option. The current Admin supports registered `IAdminQueryRelationProvider` implementations and rejects mutations for query-backed relations. This application does not currently register such a document-image provider. Design the query, pack registration, ownership assumptions and expected target rows before claiming that feature exists.

## Verification workflow

1. Inspect the EF model and confirm each reference uses the intended scalar FK. Check for accidental duplicate or shadow keys when renaming navigation properties.
2. Build the Domain project and affected hosts. Attribute arguments should use `nameof` for existing CLR members so refactors expose broken declarations.
3. Start central Admin with the application pack and inspect the entity catalog. Verify target types, foreign fields, modes, titles, icons and ordering.
4. Use two uploads with distinguishable pages and documents. Each connected section must show only its own direct dependents. Open an empty parent and verify an empty result.
5. Open an OCR page containing multiple embedded images. Compare the section rows with persisted `PageImage.PageId` values and image references.
6. Verify lists have useful columns and handle pagination. A blank label can be a metadata issue even when the relationship query is correct.
7. Attempt link and unlink through the service endpoint under current configuration. Generated results must remain protected even if client-side controls are bypassed.
8. For a newly authorized mutable relationship, test duplicate linking, missing IDs, required-FK unlink, unique-index conflicts and cross-owner targets. Validate the transaction outcome and intended deletion scope.
9. Verify public image/document reads with another Firebase account. Admin relationship presentation must not weaken the API's existing upload-owner predicates.
10. Record schema impact separately from display impact. Metadata-only sections need no new reverse collection or database migration. Relationship changes require the explicit schema-transition workflow described in [Persistence.AGENTS.md](Persistence.AGENTS.md).

## Source references

- Current model and mappings in [AIESummitDb.cs](AIESummitDb.cs).
- Operator permissions and discoverable types in [admin.json](../deploy/admin.json).
- Public ownership joins in [UploadEndpoints.cs](../AIESummit.Api/UploadEndpoints.cs).
- Attribute contracts in [ForeignTypesAttribute.cs](../external/blueshift/Blueshift.Domain/Infrastructure/ForeignTypesAttribute.cs) and [DisplayMetadataAttribute.cs](../external/blueshift/Blueshift.Domain/Infrastructure/DisplayMetadataAttribute.cs).
- Catalog projection in [CatalogService.cs](../external/blueshift/Blueshift.Admin.Api/Services/CatalogService.cs).
- FK lookup, connected queries and mutation modes in [EntityCrudService.cs](../external/blueshift/Blueshift.Admin.Api/Services/EntityCrudService.cs).
- Route contracts in [AdminEndpointExtensions.cs](../external/blueshift/Blueshift.Admin.Api/Infrastructure/Endpoints/AdminEndpointExtensions.cs).
- Historical design reference in `../../horus/Horus.Domain/Entities/AGENTS.md`, read in full. Its relationship intent is retained here with current EF Core and central Admin semantics.
