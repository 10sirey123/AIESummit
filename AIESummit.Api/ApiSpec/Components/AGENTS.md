# Reusable upload API schemas

Read this guide before changing response components or adding DTO fields to the public upload API. [uploads.json](uploads.json) is the current component source. Compare every schema change with [UploadEndpoints.cs](../../UploadEndpoints.cs), not just the persistence entities.

## Files and build ownership

The Components directory currently contains one JSON source file, `uploads.json`. Its top-level keys define five schemas. A filename is a grouping choice, not a schema name. Keep closely related schemas together unless a concrete expansion warrants another immediate child JSON file.

[build.mjs](../build.mjs) reads immediate `.json` files in Components and Paths, sorted by filename. It does not recurse into subdirectories. It merges each file's top-level entries and rejects duplicate keys within each collected group. A new schema placed in a nested folder will not be included by the current builder.

The builder sets `spec.components.schemas` to the collected component entries, preserving the template's other component categories, including security schemes. It writes [wwwroot/openapi.json](../../wwwroot/openapi.json) with stable indentation and a trailing newline. Edit source fragments and rebuild rather than treating generated output as the only source of truth.

References use schema keys, independent of filenames.

```json
{"$ref": "#/components/schemas/Upload"}
```

Do not reference `uploads.json/Upload`, include the Components directory in an internal reference, or add a schema named after its grouping file. Verify both component-to-component and path-to-component references after renaming anything.

## Current response components

| Schema | Required members | Runtime source |
| --- | --- | --- |
| `AcceptedUpload` | `id` | Anonymous object returned by the upload POST |
| `Upload` | `id`, `fileName`, `created`, `pageCount`, `status`, `error` | `UploadSummary` projection used by list and detail |
| `UploadDetail` | `upload`, `documents` | Detail response wrapper |
| `Document` | `id`, `title`, `firstPage`, `lastPage`, `markdown`, `images` | Document projection plus page-derived image references |
| `ImageReference` | `id`, `page`, `reference` | Small reference DTO projected from `PageImage` |

These are API shapes. `Upload` is not the full `UploadedFile` entity, and `Document` is not serialization of the full domain object. The current handlers deliberately project fields and omit owner identifiers, navigations, workflow internals, prompt data, and source bytes.

There is no current `UploadInput` JSON component. Upload intake is multipart form data with a binary `file` part, defined in [Paths/uploads.json](../Paths/uploads.json). Do not create an editable JSON input schema from the response object and imply the endpoint accepts it.

### AcceptedUpload

The POST returns HTTP 202 with an `id` integer formatted as `int32`, minimum 1. Its Location header is described at the path response level. Do not add a `location` body property unless the handler returns one.

An accepted upload ID means intake saved the upload and accepted background work. It does not mean OCR or document splitting succeeded. Avoid examples that return documents as part of this response.

### Upload

`id` is a positive `int32` integer. `fileName` is a string. `created` is a `date-time` string corresponding to the upload's creation timestamp. `pageCount` is an integer with minimum 0. The component currently does not specify an integer format or maximum for `pageCount`.

`status` is a string enum with exactly `queued`, `splitting`, `ready`, and `failed`. These are projected API labels, not serialized numeric domain enum values. The handler derives them in the following priority order.

1. An upload error is non-null, or LLM step status is permanent error, produces `failed`.
2. Otherwise a processed LLM step produces `ready`.
3. Otherwise processed OCR produces `splitting`.
4. Otherwise the result is `queued`.

Keep that precedence when describing a status change. Adding another string to the component enum alone does not implement a new status, and changing internal enum values does not automatically change this public contract.

`error` is required and nullable. Its key belongs in the response even when the value is null. Required describes presence. Nullable describes an allowed value. Do not remove `error` from `required` to represent a successful upload, and do not replace null with an empty string unless the runtime behavior intentionally changes.

### UploadDetail

`upload` references `Upload`. `documents` is an array whose items reference `Document`. Both keys are required. A queued or unsuccessful upload can have an empty documents array. The schema does not set `minItems`, and clients must not assume that a detail response always contains a completed document.

The detail handler first validates upload ownership, then reads that upload's documents and image references. The component does not expose another user's records or substitute an empty upload object for a missing/unauthorized upload. Those cases use the path's 404 response.

### Document

`id`, `firstPage`, and `lastPage` are `int32` integers with minimum 1. The page range is inclusive and one-based. `title` and `markdown` are strings. `images` is a required array of `ImageReference` objects that may be empty.

The image array is derived by checking whether an image's OCR page number falls between this document's first and last pages. There is no direct persisted `Document.Images` collection. Do not add a navigation-shaped property or a second independent page mapping merely to make the schema resemble a domain model.

The Markdown can contain the literal `<!-- page break -->` separator between original OCR pages. The API returns Markdown text, not rendered HTML or a downloadable document binary. A content-type or schema change should preserve that distinction.

### ImageReference

`id` identifies the framework image record, not the `PageImage` link row. `page` identifies the one-based OCR page within the upload. Both are positive `int32` integers. `reference` is the OCR image reference label.

The reference does not include an embedded base64 image, binary bytes, filesystem path, or public URL. Clients retrieve bytes through the separately authorized image route using the upload ID and image ID. The image response media types and binary schemas belong to the path response, not this JSON component.

## JSON names and null handling

The C# `UploadSummary` and `ImageReference` records use PascalCase member names. The public JSON contract uses the camelCase names in these components. Anonymous projections use a mixture of explicit `id` and inferred property names, which are serialized into the same camelCase shape by the current API setup.

Do not rename a schema property to match a CLR property's capitalization. Compare an actual response after changing serializer options, record members, or projections. Serializer changes can alter several responses at once even when the JSON fragment is unchanged.

An empty array, a null value, and an absent key are distinct. The current arrays are required and non-nullable. `Upload.error` is the current explicit nullable field. If adding a nullable property, decide separately whether it is always present and whether null is a valid value. Update projection and schema together.

## OpenAPI version and composition

[template.json](../template.json) declares OpenAPI `3.0.3`. Follow that version's schema vocabulary. Use `"nullable": true` alongside the field's ordinary type for a nullable value, as `Upload.error` already does. Do not introduce the OpenAPI 3.1-style `"type": ["string", "null"]` pattern without a deliberate specification upgrade and validation of consumers.

Use simple `$ref` objects for existing components. When adding documentation or constraints around a referenced type, avoid relying on `$ref` siblings being honored by all 3.0 tools. Use an appropriate composition pattern and verify its generated-client behavior. Do not introduce `allOf`, `oneOf`, or `anyOf` unless the response actually needs that structure.

The existing response objects have no `additionalProperties: false` and no `readOnly` annotations. Do not claim they reject undeclared properties or enforce request/response separation through those keywords. An output-only handler already determines which properties it returns.

The Horus guide recommends input/output composition for editable complex resources. Here the only write input is a multipart file. Do not manufacture `DocumentInput`, editable processing state, or a writable owner field solely to follow that pattern. For a requested new editing endpoint, design its actual allowed input separately and compose only when the resulting contract stays clear.

Be careful combining closed objects and composition. A newly added `additionalProperties: false` can constrain generated consumers or schema validation differently from what a serializer does. Treat tightening a public response schema as a compatibility decision and verify the intended tooling.

## Descriptions and examples

Current component properties are mostly structural and do not yet contain descriptive text. When changing or adding a schema, add useful descriptions of units, identity, lifecycle, page numbering, and null meaning. Describe the API behavior without claiming stronger checks than the handler performs.

The following is a proposed documentation example, not a real uploaded record and not currently embedded in `uploads.json`.

```json
{
  "id": 42,
  "fileName": "sample.pdf",
  "created": "2026-11-06T18:00:00Z",
  "pageCount": 0,
  "status": "queued",
  "error": null
}
```

It illustrates a required nullable error and a zero page count before OCR. Use synthetic filenames, IDs, and content in examples. Do not copy user documents, tokens, owner IDs, model credentials, or provider error bodies into the generated public specification.

For a document example, ensure `firstPage <= lastPage`, use one-based page numbers, and put every example image within the document range. An example with three image entries is not a promise that every OCR page produces an image.

## Schema constraints versus implemented validation

The schema is documentation and a client contract. `build.mjs` does not install request validation into the handlers. A schema minimum on a path parameter does not by itself produce a matching runtime 400 response.

| Behavior | Current implementation | Current component expression |
| --- | --- | --- |
| Upload limit | Nonempty file up to 20 MiB, recognized PDF/PNG/JPEG prefix | Described on multipart path input, not on a response component |
| OCR pages | 1–30 returned pages with exact zero-based indexes before conversion | `pageCount` has minimum 0, no maximum |
| Document ranges | Ordered contiguous complete coverage, inclusive bounds | First/last each have minimum 1, no cross-field relation |
| Document title | Nonblank and at most 250 characters before trimming | String, no `maxLength` or nonblank pattern |
| Upload filename | Basename normalized and truncated to 255 characters | String, no `maxLength` |
| Owner isolation | Authenticated subject used in database predicates | Global bearer security plus response shapes, no owner field |

Do not add schema constraints purely because they seem reasonable. Confirm whether they describe the returned data, accepted request data, or an intended new behavior. Cross-field partition rules still need application validation even if descriptions explain them.

Generic error response bodies are not defined as reusable components in this file. The path document currently lists error status descriptions without a shared problem schema. Some handlers use `Results.Problem`, while other failures are plain not-found or authentication responses. Do not claim all errors have `Upload.error` or one identical JSON envelope.

## Extending a DTO safely

Start from the actual endpoint projection. Determine the field's purpose, exact JSON name, type/format, presence, nullability, and source. If a field is sensitive or owner-specific, confirm the authorization and filtering occur before materializing it.

Do not expose `UploadedFile.Source`, `OwnerId`, prompt bodies, model results, APIInfo credentials, or EF navigation graphs simply because those properties exist on an entity. Admin metadata such as `IsHidden` does not define this public response contract. A schema omission also does not stop an accidentally serialized field from leaking.

Reuse `Upload`, `Document`, and `ImageReference` when their meanings remain identical. Create another schema when semantics actually differ, such as a future explicitly authorized write request. Avoid reshaping existing components just to remove duplication if that hides an important input/output boundary.

When extending a required property list, account for clients consuming older and newer server versions. When renaming or removing a property, inspect frontend usage in [app.js](../../wwwroot/app.js) and the path references. Keep type names, operation responses, and projections synchronized.

## Build and verification

Run the existing builder from the repository root after changing component JSON.

```powershell
node AIESummit.Api/ApiSpec/build.mjs
```

The builder validates JSON parsing and duplicate collected keys. It does not validate every OpenAPI rule, resolve every `$ref`, compare schemas with C# handlers, or test authorization. Use an OpenAPI 3.0-compatible validator if required by the task and report which validation actually ran.

Review the generated diff for the intended schemas only. Check every required property exists in `properties`, every referenced schema key exists, and arrays identify their item schema. Verify `error` remains required and nullable, page numbers remain integers, and no binary/source fields have entered JSON responses.

Compare representative serialized responses for accepted upload, queued detail with empty documents, ready detail with an image reference, and failed upload with a non-null error. Test the separate image response as binary with its MIME type rather than trying to validate it against `ImageReference`.

For changes affecting public exposure, verify owner and other-user requests independently. A well-formed OpenAPI file does not prove the handler enforces ownership. For documentation-only changes, state that validation was static if no server behavior was exercised.

## Reference boundary

The complete Horus Components guide informed this guide's emphasis on projection-based contracts, meaningful descriptions, reuse, and references. Its recursive folder layout and automatic input/output composition rules do not match this builder or this API's current multipart workflow. Follow the actual source layout and runtime projections here, and keep domain/framework changes out of a schema-only task.
