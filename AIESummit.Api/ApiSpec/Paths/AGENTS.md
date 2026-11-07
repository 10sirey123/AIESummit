# OpenAPI path definitions

This directory describes the client API's operations. [uploads.json](uploads.json) currently contains all three path keys and four operations. Runtime behavior is handwritten in [UploadEndpoints.cs](../../UploadEndpoints.cs). Editing a path fragment does not create or change a C# endpoint.

Read the [parent guide](../AGENTS.md) for composition and [Components guide](../Components/AGENTS.md) for response schemas. Review the [endpoint behavior guide](../../UploadEndpoints.AGENTS.md) when changing status codes, parameters, authentication or file handling.

## File shape and composition

Each immediate `.json` file contains an object keyed by complete OpenAPI path strings. A file may contain several paths, and a path may contain several verbs. Keep operations on the same path together. The builder rejects a duplicate path key across collected files even if the two files intended to contribute different verbs.

`build.mjs` reads immediate files in sorted order. It does not recurse into subfolders. Place new fragments directly in this directory unless recursive discovery is explicitly implemented and tested. Horus's tag-named subfolder convention does not apply to this builder.

The current source pairs are `Paths/uploads.json` and `Components/uploads.json`. Inspect both before extending an operation. Add or revise a component when needed to describe the authorized change accurately. Do not invent an incompatible inline response merely to avoid updating its reusable schema.

Build from the repository root with `node AIESummit.Api/ApiSpec/build.mjs`. The result is `AIESummit.Api/wwwroot/openapi.json`. The script composes JSON and detects duplicate entry keys. It does not generate controllers, DTOs or clients, and does not perform full OpenAPI semantic validation.

## Current operation identities

| OpenAPI path | Verb | operationId | Runtime route |
| --- | --- | --- | --- |
| `/api/v1/uploads` | GET | `listUploads` | `/uploads` on the `/api/v1` group |
| `/api/v1/uploads` | POST | `createUpload` | `/uploads` on the `/api/v1` group |
| `/api/v1/uploads/{id}` | GET | `getUpload` | `/uploads/{id:int}` |
| `/api/v1/uploads/{id}/images/{imageId}` | GET | `getImage` | `/uploads/{id:int}/images/{imageId:int}` |

Preserve stable, unique lower-camel-case operation IDs unless the task intentionally changes the client contract. These IDs do not generate handler names here. The builder does not detect duplicate operation IDs, so review them across all verbs and paths.

Use the public OpenAPI placeholder `{id}`, without ASP.NET's `:int` suffix. Define the corresponding required path parameter with integer/int32 schema. Every placeholder needs a matching parameter, and parameter spelling must match the route. Schema minimums describe intended values but do not install runtime validation.

Each current operation uses the `Uploads` tag declared in `template.json`. Keep summaries short and use descriptions to explain asynchronous behavior, pagination or access semantics. New tags should be declared centrally. A tag does not define an authorization policy or controller.

## Security declaration

`template.json` defines global `FirebaseBearer` security using HTTP bearer authentication with Firebase ID tokens. Current upload operations inherit it. Runtime enforcement comes from `RequireAuthorization()` on the API group and the default policy requiring an authenticated principal with a `sub` claim.

Do not add an operation-level empty security array to a protected route. Conversely, if a public route such as health or browser configuration is added to this specification, explicitly model its public access rather than inheriting bearer requirements accidentally.

Bearer authentication does not imply access to every resource. Describe ownership restrictions in operation text and preserve runtime owner-scoped queries. Current detail and image operations use 404 for both absent and nonowned resources. Avoid changing the spec to promise 403 for foreign uploads when the implementation intentionally returns 404.

## Multipart intake

The POST request is required `multipart/form-data`. Its schema is an object with required field `file`, represented as a binary string. The property name must match the `IFormFile file` handler parameter.

```json
{
  "requestBody": {
    "required": true,
    "content": {
      "multipart/form-data": {
        "schema": {
          "type": "object",
          "required": ["file"],
          "properties": {
            "file": { "type": "string", "format": "binary" }
          }
        }
      }
    }
  }
}
```

Do not model the multipart body itself as a binary string. Do not document base64 JSON or multiple file arrays unless the runtime changes to accept them.

The current handler accepts up to 20 MiB of file content and recognizes PDF, PNG or JPEG signatures. Kestrel separately limits the whole request to that size plus 65,536 bytes. Explain the distinction between file size and request size when changing descriptions or errors.

The current field description mentions a 30-page PDF limit. Page validation happens after OCR responds, not during HTTP intake. Signature recognition also does not prove a complete valid PDF or image. Keep synchronous rejection separate from asynchronous processing failure.

## Accepted upload response

POST returns 202 with schema `AcceptedUpload` and a `Location` response header pointing to the upload detail path. The JSON contains the persisted upload ID. Keep the header declaration when changing the response schema.

A 202 acknowledges intake and background workflow initiation. It does not assert OCR completion, successful model configuration or a ready document result. Descriptions should direct clients to retrieve processing status, without promising a fixed completion time.

The current path declares 400, 401, 413 and 415 in addition to 202. Handler-generated 400 and 415 use `Results.Problem`, while some errors arise from framework binding or request-body enforcement. The fragment currently supplies descriptions for those errors without a reusable body schema. Do not claim all failures have a uniform response body unless implementation and specification establish one.

## List pagination and schema

GET `/uploads` returns a top-level array of `Upload`, with at most 50 entries. It has no count, next-cursor object or pagination headers. The optional `before` query parameter is integer/int32 and uses an exclusive lower-ID boundary.

The runtime filters owned uploads by `ID < before`, orders newest IDs first and defaults to `int.MaxValue`. The next request can use the last returned ID. Describe this as ID-based pagination, not a timestamp or offset cursor.

The schema gives `before` a minimum of 1. The handler does not explicitly reject a bound zero or negative value. Such values normally yield an empty result for positive database IDs. Malformed or out-of-range integer strings are framework binding cases. Any stricter parameter validation is a proposed behavior change until implemented.

`Upload` carries the public status vocabulary `queued`, `splitting`, `ready` and `failed`. Its required `error` field is nullable. Keep references pointed to the component rather than duplicating a subtly different summary inline.

## Detail and binary images

GET upload detail returns `UploadDetail`, which contains the summary and an array of `Document`. Documents contain inclusive one-based page ranges, persisted Markdown and `ImageReference` arrays. The endpoint can return no documents while processing is underway. A nonempty array alone is not a readiness guarantee after a failed rerun.

Image references identify the stored image, its OCR page and reference string. The image route requires both upload ID and image ID, and the runtime verifies their relationship and caller ownership. Neither a known image ID nor a known upload ID alone establishes access.

The image operation declares binary string responses under `image/png`, `image/jpeg`, `image/webp` and `image/gif`. These are extracted-image types. They do not expand intake to accept GIF or WebP uploads. Do not describe the binary response as a JSON object or base64 field.

No current image operation promises range processing, content-disposition download names, signed URLs or a specific cache policy. Add those declarations only with corresponding server behavior and verification.

## Schema references and error distinctions

Use references such as `#/components/schemas/UploadDetail`. Component filenames and directories are not part of the schema reference. Verify every referenced name exists in the composed `components.schemas` object.

Separate these outcomes when describing a route.

- Authentication or authorization can reject the request before the handler.
- Route constraints can prevent an operation from matching a noninteger segment.
- Query or multipart binding can fail before handler validation.
- Handler validation explicitly rejects invalid file length or unsupported signatures.
- Owner-scoped lookup returns 404 for missing or foreign resources.
- Database or unexpected failures are not automatically covered by the declared validation errors.

Do not invent a shared error envelope based on descriptions alone. Add a verified reusable component when the contract actually standardizes those responses.

## Current parity limits and proposed improvements

The following observations describe current boundaries, not implemented fixes.

| Current boundary | If the contract needs stronger guarantees |
| --- | --- |
| Schema minimum 1 for cursors/IDs, no explicit positive-value handler guard | Implement and test a deliberate validation response before promising it |
| Operations list 401 but not every authorization outcome | Verify required-claim failures and document relevant 403 behavior |
| Error descriptions omit body schemas | Standardize or characterize actual responses before adding schemas |
| PDF page limit stated in request description | Clarify asynchronous enforcement rather than implying intake parsing |
| Builder checks JSON and duplicate path keys only | Add or run a separate validator when semantic validation is required |
| Detail uses separate database reads | Avoid describing it as a transactionally consistent processing snapshot |

These observations should guide changes within the requested scope. Do not silently change runtime semantics during a documentation-only task.

## Extension and parity checklist

1. Locate the exact mapped runtime route and verb. Confirm authorization and handler binding before writing its path item.
2. Choose a unique stable operation ID and a declared tag. Keep the public template free of ASP.NET route constraints.
3. Define every path/query parameter, required request field and media type to match the handler.
4. Reference existing response components when their shapes match. Update components and consumers when the public shape changes.
5. Describe success accurately, including asynchronous 202 behavior, Location headers, pagination limits and binary media types.
6. Enumerate meaningful failures by their actual source. Preserve the intentional 404 treatment of foreign resources.
7. Regenerate the composed document and inspect that the new path, security and references appear in it.
8. Check semantic correctness separately from composition. Review duplicate operation IDs and unresolved references.
9. Build the API when C# changes and exercise success, malformed input, missing authentication and second-account access.
10. Compare actual serialized responses and headers with the document. Test the outer request-size boundary separately from the bound file-size check.

The full historical `../../../../horus/DUTai.Api/ApiSpec/Paths/AGENTS.md` informs multipart object modeling, clear responses and reusable references. Its recursive tag folders, generated-controller operation naming and code-generation assumptions do not apply here. Current authority is [uploads.json](uploads.json), [template.json](../template.json), [build.mjs](../build.mjs), [UploadEndpoints.cs](../../UploadEndpoints.cs) and [Program.cs](../../Program.cs).
