# Upload endpoint contracts

[UploadEndpoints.cs](UploadEndpoints.cs) owns the current client upload routes. They are handwritten minimal endpoints registered by `MapUploadEndpoints`, with dependencies bound through handler parameters. Read [Authentication.AGENTS.md](Authentication.AGENTS.md) for token validation and [ApiSpec/AGENTS.md](ApiSpec/AGENTS.md) before changing their public contract.

## Routes and outcomes

All four operations belong to `/api/v1`, which calls `RequireAuthorization()`. Successful authentication also requires the Firebase `sub` claim through the default policy.

| Method and route | Handler | Successful result | Explicit handler failures |
| --- | --- | --- | --- |
| `POST /api/v1/uploads` | `Upload` | 202, `{ "id": ... }`, `Location` pointing to detail | 400 for empty/oversized bound file, 415 for unsupported signature |
| `GET /api/v1/uploads` | `List` | 200, array of up to 50 summaries | No custom cursor-validation response |
| `GET /api/v1/uploads/{id:int}` | `Get` | 200, upload summary and documents | 404 for missing or nonowned upload |
| `GET /api/v1/uploads/{id:int}/images/{imageId:int}` | `GetImage` | 200, image bytes with stored MIME type | 404 for unavailable or nonowned image |

Authentication, authorization, route matching, multipart binding and request-size enforcement can reject a request before its handler executes. Keep those framework outcomes separate from the explicit returns above. Missing or invalid authentication normally produces 401. An authenticated principal failing the required-claim policy can receive 403, even though the current route fragments do not list that response.

There are no current public update, delete, rerun, source-download or prompt-management routes. Do not describe them as existing extensions of this group.

## Intake limits and type detection

`MaxBytes` is `20 * 1024 * 1024`, or 20 MiB. The handler rejects `file.Length <= 0` and values above that limit. It accepts a multipart field bound to `IFormFile file`, reads the stream into a `MemoryStream`, and copies it into a byte array for persistence.

`Program.cs` sets the Kestrel maximum request body size to `MaxBytes + 65536`. The extra allowance is for multipart overhead. The outer request limit is separate from the handler's per-file limit. A request can receive 413 before the handler's 400 check runs. Larger multipart headers or extra parts can consume that overhead.

Accepted signatures are exactly the following.

| Leading bytes | Stored type |
| --- | --- |
| `%PDF-` | `application/pdf` |
| `89 50 4E 47 0D 0A 1A 0A` | `image/png` |
| `FF D8 FF` | `image/jpeg` |

The handler ignores a claimed extension or client MIME value when selecting the stored content type. It does not parse the full file, validate PDF structure or count pages. A truncated or malformed file with a recognized prefix can pass intake and later fail OCR.

The OpenAPI file description says PDFs can contain up to 30 pages. This is enforced downstream by `OcrAgent` validating the returned OCR page array, not synchronously by the upload endpoint. Do not promise an immediate 400 for a 31-page PDF based on that description.

The endpoint buffers uploads in memory and stores their bytes in PostgreSQL. It is not a streaming object-storage path. Raising limits or adding multi-file intake requires reviewing memory use, concurrency, storage and provider restrictions as a coordinated change.

## Filename and ownership assignment

The filename is normalized with `file.FileName.Replace('\\', '/')`, reduced with `Path.GetFileName`, then truncated to at most 255 characters. This removes directory components supplied by either common path separator. The resulting name is display metadata, not a server filesystem destination.

The current code does not replace a blank filename with a fallback name or apply a separate text sanitization policy. Browser rendering must treat it as text. A filename policy change is distinct from content-type validation.

`Owner(user)` reads the authenticated `sub` claim. The new `UploadedFile` receives that value, the normalized filename, detected MIME and source bytes. The request cannot supply the authoritative owner. Preserve this boundary for any alternative intake endpoint.

After `SaveChangesAsync(token)`, the endpoint returns `Results.Accepted` with the persisted ID. The response does not wait for OCR or LLM work. API publisher registration and framework save hooks handle eligible processing work, while the separate worker receives it. A 202 does not prove provider availability or eventual processing success.

The multipart route disables antiforgery under the current bearer-token design. That does not waive authorization. Review antiforgery requirements afresh if a future route authenticates through browser cookies.

## List query and cursor semantics

`List` defaults `before` to `int.MaxValue`, filters `OwnerId` to the caller and `ID < before`, orders descending by ID and takes 50 rows. It returns the array directly with no total count, cursor envelope or `hasMore` field.

To request older rows, a client uses the last returned ID as the next `before`. The comparison is exclusive. Reusing the same cursor on an unchanged dataset yields the same window. Newer inserted uploads do not shift the older window because pagination is based on IDs rather than an offset.

The OpenAPI parameter has minimum 1, but the handler does not explicitly validate positivity. A bound zero or negative cursor normally returns an empty array for positive persisted IDs. A malformed or out-of-range integer is a binding issue rather than an application-defined cursor error. Adding strict cursor validation would be a behavior change and should update the contract and tests.

Do not remove the owner filter when introducing search or counts. Any future total count must apply the same scope as the returned records.

## Summary projection and status precedence

`Summaries` projects only `id`, `fileName`, `created`, `pageCount`, `status` and nullable `error`, using the host's standard JSON naming behavior. It does not serialize the Domain entity.

Status selection is ordered.

1. Return `failed` if `Error` is nonnull or `LLMStepStatus` is `PermanentError`.
2. Otherwise return `ready` if `LLMStepStatus` is `Processed`.
3. Otherwise return `splitting` if `OcrStatus` is `Processed`.
4. Otherwise return `queued`.

Even an empty nonnull error string selects `failed`. The projection does not separately test `OcrStatus.PermanentError`. Current OCR failure handling also sets `Error`, so its failures follow the first branch. If changing worker error handling, preserve that agreement or deliberately update the projection.

These four public strings summarize a richer internal lifecycle. Do not expose raw enum names or numeric values accidentally when adding DTO fields. Existing schema consumers expect the public status vocabulary.

## Detail reads and image grouping

`Get` first queries a summary with both the requested ID and caller ownership. If absent, it returns 404 before reading dependent documents or image references. This makes an unknown upload and another account's upload indistinguishable at the response level.

After that check, it queries all `PageImage` references for the upload and all `Document` rows ordered by `FirstPage`. Each document projection contains `id`, `title`, `firstPage`, `lastPage`, `markdown` and `images`. It assigns images whose one-based page lies within the document's inclusive endpoints.

An `ImageReference.id` is the framework `ImageId`, not the `PageImage` link ID. Its `reference` is the OCR-provided reference string and its `page` is the page number. Use the upload ID plus that image ID to request bytes.

The relationship is derived from page ranges. There is no direct document-image join in the model. The image reference query has no explicit ordering, so clients should not depend on stable image-array order. Documents remain ordered by their first page.

The detail handler performs multiple database queries without an explicit read transaction. A worker can update processing results between those reads. Treat the response as current observations rather than a guaranteed snapshot of every workflow field at one instant. A stronger snapshot contract would require deliberate implementation and verification.

Documents can be empty while processing is pending or failed. Existing documents can also remain after a failed rerun because current metadata reset hooks do not clear them. Do not infer `ready` solely from a nonempty document array.

## Image-byte authorization

`GetImage` constrains `ImageId`, the requested upload ID and `Page.Upload.OwnerId` in one query. It projects only image bytes and MIME type, then returns 404 if no matching bytes exist. This prevents using a known image ID under another account or an unrelated upload path.

Do not simplify the lookup to `db.Images.Find(imageId)`. The storage entity itself does not carry the upload owner in this model. Ownership is proved through the `PageImage` and page/upload relationship.

`Results.File` returns stored MIME information without an explicit download filename. The route does not implement a custom cache policy, signed URL, thumbnail transform or source-file download. API input accepts PDF/PNG/JPEG, while extracted images may include PNG/JPEG/WebP/GIF, matching the downstream OCR image handling and OpenAPI response media types.

## Cancellation, binding and error boundaries

Handlers accept `CancellationToken` and pass it through stream copying, database saving and asynchronous queries. Preserve cancellation propagation when extending them. Do not wrap cancellations indiscriminately into a misleading successful or validation response.

Required multipart binding, malformed query values and `:int` route matching are handled by ASP.NET Core before or around application logic. A missing multipart field does not run the same branch as a bound zero-length file. A noninteger route segment can fail route matching rather than produce the handler's 404 lookup response. Verify actual HTTP behavior when tightening the contract.

The handlers have no global custom exception wrapper. Database connectivity, constraint or unexpected execution failures do not automatically become one of the listed validation responses. Avoid claiming all errors share a single JSON envelope. The explicit upload failures use `Results.Problem`, while missing resources use `Results.NotFound`.

## Contract maintenance

The source contract is split between [Paths/uploads.json](ApiSpec/Paths/uploads.json) and [Components/uploads.json](ApiSpec/Components/uploads.json). Rebuild the served document with `node AIESummit.Api/ApiSpec/build.mjs` from the repository root after changing fragments.

Review current boundaries when changing the API. Schema minimums for cursors and IDs do not add runtime checks. The PDF page limit is asynchronous. Authorization can produce outcomes beyond the listed 401. The composition script does not validate these semantics or generate C#.

Keep request binding, projected field casing, required/nullable properties, media types and status codes aligned. Proposed improvements such as a cursor envelope, explicit positive-ID rejection or a read snapshot should be labeled as future work until implemented, not silently described as existing behavior.

## Meaningful verification

| Scenario | Check |
| --- | --- |
| Valid accepted file | 202 body and Location agree with the persisted upload ID and authenticated owner |
| Empty and oversized bound file | Handler validation, with separate verification of outer 413 behavior |
| Spoofed extension or MIME | Actual signature determines acceptance and stored type |
| Recognized prefix with malformed contents | Intake acceptance is distinct from later OCR failure |
| Filename with both path separators | Stored display name has no supplied directory components and respects length cap |
| More than 50 owned uploads | Descending exclusive-ID pagination has no overlap and exposes no count envelope |
| Two Firebase accounts | Lists exclude the other account and known foreign detail/image IDs return 404 |
| Own image under wrong upload | 404 despite ownership of one of the IDs |
| Each status branch and conflicting state | Error precedence and current public strings remain stable |
| Images at range boundaries | Inclusive first/last pages attach to the correct document |
| Missing form field, malformed cursor and noninteger route | Actual framework responses are recorded separately from handler validation |
| Canceled request | No fabricated success and persistence outcome checked where a save could have completed |

Use focused HTTP/database checks for endpoint behavior and the existing [smoke workflow](../scripts/smoke.AGENTS.md) for full processing and isolation. A mock or accepted upload alone does not demonstrate live OCR/LLM completion.

The complete historical `../../horus/DUTai.Api/Controllers/AGENTS.md` was read for implementation discipline. Its generated abstract controllers and constructor-injection pattern are not the current architecture. Here, handlers are handwritten and services are injected through their parameters. Relevant host limits and authentication are in [Program.cs](Program.cs).
