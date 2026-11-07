# Display metadata and Admin fields

Framework source links in this guide are optional upstream references at the bundled revision and may require maintainer access. Normal development consumes `dependencies/blueshift`; no framework source checkout is required.

Read this guide when adding or changing an entity field that the shared Blueshift Admin displays. Start with [AGENTS.md](AGENTS.md) for domain modeling and use [Relationships.AGENTS.md](Relationships.AGENTS.md) for relationship mapping and related-record lists. This guide describes the checked-in `net10` framework contract. Inspect the pinned framework again when upgrading it.

## Current application behavior

The entity source files linked in the [domain guide](AGENTS.md) contain the actual decorations. Entity icons use supported canonical Lucide names: `FileInput` for uploads, `ScanText` for OCR pages, `FileText` for documents, `Image` for extracted image links, and `SlidersHorizontal` for OCR settings. Verify names against the Admin's explicit `DOMAIN_ICONS` registry; an imported icon is not necessarily registered for canonical use.

Every locally declared entity property has a `Display` label and description. Workflow-owned values are explicitly read-only. Snippets summarize file name, processing states and page count; page number; document title and inclusive range; or OCR image reference. Navigations use `SearchListSingle`, Markdown uses plain `Text`, and the LLM result uses `Json`. Binary data, scalar foreign keys, transient split input, and the prompt collection are hidden from generic fields. The prompt collection remains mapped and is exposed through a related section. `SplitInput` remains `[NotMapped]`.

[deploy/admin.json](../deploy/admin.json) permits the configured entity types to appear in Admin. It disables create, update, and delete for `UploadedFile`, `OcrPage`, `Document`, `PageImage`, `Image`, and `LLMPrompt`. A property with `IsEditable = true` does not override those entity restrictions. The deployment overlay also defines navigation groups independently of class icons and menu names.

Every code example below is a proposed implementation pattern, not a description of additional fields or decorations already present in the individual entity files. Adapt examples to a requested change. Do not add fields merely to reproduce this guide.

## Attribute imports and defaults

Use the actual namespaces below when applying these patterns.

```csharp
using System.ComponentModel.DataAnnotations;        // Display, Required, StringLength, Range
using System.ComponentModel.DataAnnotations.Schema; // ForeignKey, NotMapped
using Blueshift.Domain.Infrastructure;              // DisplayMetadata, connector enums, SteppedRange
```

`DisplayMetadataAttribute` applies to classes and properties. It accepts one positional icon string and named fields. An empty icon string is valid. Only one instance is allowed per target, and the attribute declaration does not enable inheritance. Do not assume a derived class inherits its base class's class-level menu decoration.

| Member | Default | Meaning in this Admin |
| --- | --- | --- |
| `ConnectorType` | `Default` | Selects the field renderer, with server inference for several CLR types |
| `ShowInSnippet` | `false` | Includes a visible property in the catalog's snippet fields |
| `IsHidden` | `false` | Marks the field hidden for Admin presentation |
| `IsEditable` | `true` | Allows the field renderer to use an editor, subject to other restrictions |
| `Order` | `0` | Positive values order fields before unspecified values |
| `MenuName` | `null` | Entity menu label or fallback property display label |
| `MenuDescription` | `null` | Class metadata description, not the property help-text source |
| `TemporalKind` | `Auto` | Declares date, time, or timestamp meaning for temporal CLR types |

The attribute also defines `IconValid`, `IconInvalid`, `ImageURL`, `IsPersistenceObject`, and `IsValidated`. Their existence is not evidence that each has a visible effect in every shared Admin component. Confirm the catalog and renderer before designing a feature around them.

An undecorated property uses `prop.CanWrite` as its catalog editability. Once a `DisplayMetadata` instance is present, its default `IsEditable = true` is used. Explicitly set `IsEditable = false` for generated, computed, or workflow-owned values instead of relying on a missing setter or omitted named argument.

## Labels, descriptions, ordering, and snippets

Use `Display(Name = ..., Description = ...)` for a human-readable field label and useful help text. The catalog chooses `Display.Name`, then `DisplayMetadata.MenuName`, then a label derived from the CLR property name. Property help comes from `Display.Description`. Explain purpose, units, accepted format, or consequences in the description. Do not rename a persisted property merely to improve its displayed label.

This proposed decoration improves `Document.Title` without changing its storage name.

```csharp
[Display(Name = "Document title",
    Description = "Title extracted for this document within the uploaded file.")]
[DisplayMetadata("glyphicon glyphicon-file", ShowInSnippet = true,
    IsEditable = false, ConnectorType = AttributeConnectorType.Text, Order = 10)]
public string Title { get; set; } = "";
```

Prefer a small set of recognizable snippet fields, normally at most five. Good choices identify the record or summarize its state. Avoid full Markdown, JSON responses, binary content, large prompts, credentials, and collections. `ShowInSnippet` is separate from hiding and editing. A hidden property is excluded from the entity's snippet field list even if both flags are set.

Positive `DisplayMetadata.Order` takes precedence over positive `Display.Order`. Zero and unspecified order sort after explicitly ordered fields. Use gaps such as 10, 20, and 30 to leave room for additions. Do not depend on reflection enumeration order as a durable layout contract.

Related-record labels also consult up to five snippet-decorated properties on the related CLR type. Keep those properties safe and compact. Check both the main record list and relationship pickers after changing snippets. An explicit `EntityAccess.ListColumns` configuration can select list columns independently of the snippets.

## Connector selection

The enum and React registry currently contain all the following names. A registered connector still requires a compatible persisted value and a supported save path.

| Connector | Intended selection and checks |
| --- | --- |
| `Default` | Let the catalog infer an enum, boolean, temporal value, or persistence-object reference. Otherwise use the default renderer |
| `Text` | Ordinary text. Add length and required constraints separately |
| `Json` | Structured JSON text. Verify the stored representation and invalid-input behavior |
| `Html` | HTML-oriented content. Inspect the renderer's sanitization and preview behavior before exposing untrusted content |
| `Secret` | Password-style editor and masked preview. Apply the security checks below |
| `SimpleListSingle` | Single enum choice. The catalog supplies enum names and descriptive labels |
| `VisibleListSingle` | A single choice displayed through its dedicated renderer. Verify available options |
| `SimpleListMultiple` | Multiple simple values. Verify the connector's string-list representation rather than assuming flags-enum persistence |
| `Switcher` | Boolean value, inferred for `bool` and nullable `bool` |
| `Date` | `DateOnly`, `TimeOnly`, `DateTime`, or `DateTimeOffset`, with temporal semantics |
| `Range` | Numeric value with `Range` or `SteppedRange` metadata |
| `Progress` | Progress presentation. Usually mark workflow-produced progress read-only |
| `SearchListSingle` | One related persistence object selected by ID |
| `SearchListMultiple` | Multiple selection UI. Confirm relationship/save support before choosing it |
| `OrderableListMultiple` | Ordered simple-list UI. Ordering must survive the actual stored representation |
| `Image` | Image presentation. Confirm the value shape and file access path supported by the renderer |
| `Location` | Location-specific UI. Inspect its value format before defining storage |
| `Condition` | Condition value with condition-rule badges. It does not itself define application authorization |
| `ModelType` | LLM subject type selected from the framework registry using `Type.Name` |
| `ModelTypeSchema` | Metadata type selected from the framework registry using full type names |

`Condition` attributes supply target/type/value metadata. The current Condition renderer displays the rules alongside an input. Do not assume those badges implement business validation or conditional authorization.

The shared Admin has separate edit and preview components. `ConnectorField` uses preview rendering for noneditable fields, disables editing when the caller disables it, and dispatches by connector name. Unknown names fall back to the default renderer. A typo or unsupported name can therefore appear to render successfully while losing the intended behavior.

### Common scalar patterns

This proposed field demonstrates a bounded integer setting. Choose domain-appropriate limits and retain server validation.

```csharp
[Display(Name = "Maximum pages",
    Description = "Maximum number of pages accepted for one processing request.")]
[DisplayMetadata("glyphicon glyphicon-list-alt",
    ConnectorType = AttributeConnectorType.Range, IsEditable = true)]
[SteppedRange(1, 100, step: 1)]
public int MaximumPages { get; set; } = 20;
```

`SteppedRange` derives from `RangeAttribute` and adds `Step`. The catalog reads its minimum, maximum, and step. Ordinary `Range` supplies minimum and maximum without a step. These annotations do not replace domain invariants or database constraints.

This proposed calendar date uses the temporal contract explicitly.

```csharp
[Display(Name = "Review date",
    Description = "Calendar date scheduled for review, without a time of day.")]
[DisplayMetadata("glyphicon glyphicon-calendar",
    ConnectorType = AttributeConnectorType.Date,
    TemporalKind = AttributeTemporalKind.Date)]
public DateOnly? ReviewDate { get; set; }
```

With `Auto`, `DateOnly` becomes a date, `TimeOnly` becomes a time, and `DateTime` or `DateTimeOffset` becomes a timestamp. A temporal override only applies to recognized temporal CLR types. Do not use a calendar-date override on an audit timestamp merely to hide the time.

Enum properties already infer `SimpleListSingle`. An explicit connector is useful when documenting intent, but it does not authorize operators to modify workflow states. Keep OCR and LLM status transitions governed by the processing code and current entity access policy.

## Single-reference selectors

Put a single-reference selector on the navigation property. Its CLR type must identify the related persistence object for the catalog to provide `RelatedTypeName`. Putting `SearchListSingle` on an integer alone does not establish that relationship. The current renderer falls back to numeric input when it has no related type.

The following is a proposed decoration of `Document.Upload` and `UploadId`. It preserves the existing required relationship and treats its origin as read-only.

```csharp
[Display(Name = "Source upload",
    Description = "Uploaded file from which this document was extracted.")]
[DisplayMetadata("glyphicon glyphicon-upload", IsEditable = false,
    ConnectorType = AttributeConnectorType.SearchListSingle)]
[ForeignKey(nameof(UploadId))]
public UploadedFile Upload { get; set; } = null!;

[DisplayMetadata("", IsHidden = true, IsEditable = false)]
public int UploadId { get; set; }
```

Here `ForeignKey` names the scalar FK because the attribute is on the navigation. If placed on the scalar, it must name the navigation instead. The existing `UploadId`/`Upload` convention already supplies EF mapping, so do not add redundant or conflicting configuration without a reason.

For an editable future relation, verify that the related type is available in the Admin catalog, that its list query returns appropriate records, that ID selection saves and reloads correctly, and that nullability matches the domain. A required relation must not silently become nullable just to expose the clear button. Search behavior depends on the related entity's query/search contract, not the connector alone.

Use [Relationships.AGENTS.md](Relationships.AGENTS.md) for `ForeignTypes`, parent-side related lists, many-to-many mappings, and deletion behavior. A navigation selector and a related-record list are separate features. Adding display metadata creates neither a foreign key nor a relationship list. Do not copy Horus's `[NotMapped]` collection pattern onto a real EF collection such as `UploadedFile.LLMPrompts`.

## Hidden fields, secrets, and policy

Treat metadata as one part of the Admin contract. Entity allow-listing, entity mutation restrictions, host authorization policies, ownership checks, payload projection, persistence rules, and database constraints have separate responsibilities.

`IsHidden` controls presentation. It is not a promise that a property is absent from every read response, serializer, log, export, or endpoint. `UploadedFile.Source` is a concrete example that requires checking the read path as well as its hidden decoration. `Source` is not included in the default sensitive-property-name list. The current `EntityCrudService.Project` independently skips byte arrays, which keeps this binary field out of that projection. Do not infer safe binary handling in other paths solely from the attribute.

`IsEditable = false` gives a read-only field presentation and participates in framework metadata-driven persistence behavior. Never use it as a substitute for operation authorization. Test an attempted write through the real server path when protecting workflow-owned state, including the scalar FK behind a read-only navigation.

`Secret` renders a password input and masks nonempty previews. It does not, by itself, establish an encrypted column or a write-only credential protocol. The Admin broadcast sanitizer recognizes Secret-decorated properties, while other read/write paths have their own sensitive-name and credential rules. Review all relevant paths before adding any new credential field.

The default sensitive names include `FileData`, `Password`, `PasswordHash`, `PasswordSalt`, `Salt`, `SecurityStamp`, `ConcurrencyStamp`, `AppKey`, `AppSecret`, `AppAuthToken`, `UserAccessToken`, `UserRefreshToken`, and `UserAccessTokenSecret`. Catalog filtering is case-insensitive. The framework has explicit write-only exceptions for particular `User` and `OAuthAccount` credentials. A new field with a similar purpose does not automatically gain that behavior.

Keep secrets out of `deploy/admin.json`, snippets, descriptions, examples, and source control. Review custom API projections separately. Admin metadata does not alter the ownership rules or DTOs in [UploadEndpoints.cs](../AIESummit.Api/UploadEndpoints.cs). Choose serialization attributes according to the actual serializer in use rather than copying the older Horus Newtonsoft-only rule globally.

## Change and verification workflow

1. Read the current entity, its EF mapping, the Admin overlay, and the processing or API code that owns the value.
2. Decide whether the request changes presentation only or also changes schema, validation, relations, or authorization. Keep those decisions explicit in the implementation.
3. Add a clear label, useful description, compatible connector, deliberate editability, and a small snippet selection. Preserve storage names and mappings unless the task requires changing them.
4. Build the affected application projects if C# changes. Inspect the generated catalog for display name, connector, hidden/editable flags, nullability, options, and related type.
5. Check list, detail, edit, and relationship-picker views relevant to the change. Confirm empty and long values render understandably and generated fields remain read-only.
6. For writable fields, save a representative value and reload it. For protected fields, verify the server's response to an attempted mutation. Check relevant entity access restrictions without weakening the deployment overlay to make an example work.
7. Review read payloads and broadcasts for a sensitive field. Use synthetic values, and remove test secrets from logs and screenshots.

Metadata-only label or icon changes do not justify a schema migration by themselves. A new persisted property or changed relationship needs the repository's normal schema workflow regardless of how little the Admin UI changes.

## Source anchors

- [DisplayMetadataAttribute.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Domain/Infrastructure/DisplayMetadataAttribute.cs) defines the attributes, connector names, temporal kinds, and defaults.
- [CatalogService.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Admin.Api/Services/CatalogService.cs) defines catalog inclusion, inferred connectors, labels, snippets, ordering, and related types.
- [AdminOptions.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Admin.Api/Options/AdminOptions.cs) defines allow-list, mutation restrictions, sensitive names, and optional list columns.
- [ConnectorField.tsx](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Admin/src/connectors/ConnectorField.tsx) and [registry.tsx](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Admin/src/connectors/registry.tsx) define rendering dispatch.
- [SearchListSingleField.tsx](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Admin/src/connectors/SearchListSingle/SearchListSingleField.tsx) defines reference selection and label loading.
- [SecretField.tsx](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Admin/src/connectors/Secret/SecretField.tsx), [WriteOnlyCredentials.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Admin.Api/Services/WriteOnlyCredentials.cs), and [AdminBroadcastSanitizer.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Admin.Api/Infrastructure/AdminBroadcastSanitizer.cs) define distinct secret-handling boundaries.
- [EntityCrudService.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Admin.Api/Services/EntityCrudService.cs) connects Admin read/write behavior and entity mutation policy to persistence.
- [EFBasePersistenceRepository.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Domain/Concrete/EFBasePersistenceRepository.cs) checks noneditable metadata during Admin saves, with explicit file-field and password-salt exceptions.

The full Horus entity guide informed the practical examples and level of detail. Its EF6 mapping syntax, per-entity MVC controller setup, project-file inclusion steps, and serializer assumptions are not the current AIESummit `net10` contract. Framework source is reference material. Make application-specific changes in this repository rather than modifying framework binaries to accommodate local metadata.
