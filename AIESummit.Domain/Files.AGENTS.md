# Uploaded files, OCR pages, and extracted images

Framework source links in this guide are optional upstream references at the bundled revision and may require maintainer access. Normal development consumes `dependencies/blueshift`; no framework source checkout is required.

Read this guide when changing upload validation, retained file bytes, OCR image extraction, document page ranges, or image delivery. The current implementation stores source and extracted image bytes in the application database. It sends the source to the configured Mistral OCR API. It does not run the Horus PageImageContainer, render pages through Poppler, or use that container's cache and download endpoints.

Start with [AGENTS.md](AGENTS.md), [OcrAgent.cs](../AIESummit.Agents/OcrAgent.cs), [SplitStep.cs](../AIESummit.Agents/SplitStep.cs), and [UploadEndpoints.cs](../AIESummit.Api/UploadEndpoints.cs). Those files jointly define the contract. A change to one representation can affect page-range validation, image ownership, and browser display.

## Distinct representations

| Representation | Stored information | Relationship |
| --- | --- | --- |
| `UploadedFile` | Original `Source` bytes, detected `ContentType`, sanitized filename, owner, processing state, page count | Root of this upload's processing |
| `OcrPage` | One-based `Number` and OCR `Markdown` | Required `UploadId` and `Upload` reference |
| `Document` | Title, inclusive `FirstPage`/`LastPage`, joined Markdown | Required upload reference, represents a logical range of OCR pages |
| Framework `Image` | `FileData`, `FileMimeType`, `FileSize` | Binary image record |
| `PageImage` | OCR `Reference`, page FK, image FK | Associates an extracted image with its source OCR page |

An OCR page is not an image of the full page. Its extracted images are the images returned in the OCR response. A document is not a new PDF or a separately stored source binary. Its text is assembled from a contiguous range of OCR pages.

There is no `Document.Images` navigation. The API derives a document's image references by selecting `PageImage` rows whose page numbers fall within that document's inclusive range. Preserve the page link as the provenance source when changing document splitting.

The local unique indexes enforce `(UploadId, Number)` on pages, `(UploadId, FirstPage)` on documents, and `(PageId, Reference)` on page-image links. They do not implement automatic replacement, deduplication by image bytes, or idempotent reprocessing.

## Upload acceptance

`POST /api/v1/uploads` accepts an authenticated multipart request with the `IFormFile file` parameter. The frontend sends `FormData`. Authentication requires a subject claim, and `OwnerId` is taken from that claim rather than from posted form fields.

The endpoint rejects a file with zero bytes or more than `20 * 1024 * 1024` bytes with HTTP 400. This is 20 MiB, not 20 million bytes. It copies the incoming file into memory and inspects its byte prefix before saving the upload.

| Prefix checked | Stored MIME type |
| --- | --- |
| ASCII `%PDF-` | `application/pdf` |
| `89 50 4E 47 0D 0A 1A 0A` | `image/png` |
| `FF D8 FF` | `image/jpeg` |

An unrecognized prefix produces HTTP 415. The detected type is authoritative for this code path. A matching extension or client-supplied MIME type alone is insufficient. Conversely, a recognized prefix is not full validation that the entire file is a well-formed PDF or decodable image. A malformed file can pass intake and fail OCR later.

The original filename is normalized by replacing backslashes with slashes, taking `Path.GetFileName`, and truncating to 255 characters. It is retained as a label, not used as a filesystem destination. Do not introduce filename-based path writes without a separately designed storage boundary.

The API saves `UploadedFile.Source` and returns HTTP 202 with the upload ID and location. OCR and document splitting happen afterward. An accepted upload is not evidence that its pages or images are ready.

[Program.cs](../AIESummit.Api/Program.cs) configures Kestrel's maximum request body as the file limit plus 65,536 bytes of allowance. The endpoint still owns the 20 MiB file check. Proxy, server, and multipart limits can reject a request before this handler runs. Preserve that distinction when diagnosing an upload that never reaches application validation.

## OCR request construction

`MistralOcrBrowser` uses API identifier `mistral-ocr`. It base64-encodes the retained source and creates a data URL using `UploadedFile.ContentType`.

For a PDF, the request document has type `document_url`. For supported image uploads, it has type `image_url`. Both carry the source as a data URL. The request uses `ApiBaseUri`, sends the API key in the `api-key` header, takes the model name from the configured `AppKeySecret`, and sets `include_image_base64 = true`.

The browser requires a successful HTTP response and deserializes its `pages`. A missing response or missing pages yields an empty array, which then fails the agent's page validation. Do not log the data URL, source bytes, key, or complete OCR response merely to diagnose a request failure.

No local PDF renderer determines page count before the OCR request. The 30-page constraint is checked on the returned OCR pages. A file within the byte limit may still be rejected after OCR because of its page count or response indexes.

## Page numbering and validation

The OCR agent accepts from 1 through 30 pages inclusive. It also requires the page indexes in the response's existing order to equal `0, 1, ..., pages.Length - 1` exactly.

The agent does not sort an out-of-order response or fill gaps. A duplicate, missing, negative, one-based, or out-of-order index fails the same validation. The current error message is `OCR page indexes are incomplete.`, including for an invalid page count.

The conversion to persisted numbering occurs once, when creating `OcrPage`.

```csharp
Number = source.Index + 1
```

Persisted pages, document ranges, image reference page numbers, and user-visible page labels are one-based. Do not subtract one again when serving images or presenting a document range. Array slicing in the splitter is the separate place where a one-based range is translated into a zero-based array offset.

The agent creates page and image objects in local lists before adding them to repositories. On success it adds those rows, sets `PageCount`, sets OCR to `Processed`, and advances LLM processing to `CheckForPrompts`. It returns to the framework's processing/save lifecycle rather than calling `SaveChanges` inside this method.

## Extracted image acceptance

Each OCR image carries an `id` and optional `image_base64` string. `PageImage.Reference` preserves the OCR image ID. It is a reference label, not a fetch URL or local file path.

The current parser accepts only a data URL with a comma, the exact lowercase prefix `data:image/`, a header ending in exact lowercase `;base64`, and one of the exact MIME types below.

| MIME type | Accepted from OCR response | Accepted as original upload |
| --- | --- | --- |
| `image/png` | Yes | Yes, by signature |
| `image/jpeg` | Yes | Yes, by signature |
| `image/webp` | Yes | No |
| `image/gif` | Yes | No |
| `image/svg+xml` or any other type | No | No |

An allowed header looks like `data:image/png;base64,` followed by base64 content. Header parameters such as an additional charset change the extracted MIME string and do not match the allow-list. Case variants do not satisfy the ordinal comparisons. A bare base64 string or remote image URL does not satisfy this contract.

| Response condition | Current result |
| --- | --- |
| `Images` missing or null | Treat page as containing no extracted images |
| `image_base64` null | Skip that image |
| Missing comma, wrong prefix, or wrong base64 suffix | Skip that image |
| Unsupported MIME type | Skip that image |
| Allowed header with invalid base64 | `FormatException`, caught as OCR failure for the upload |
| Allowed header with valid base64 | Create binary `Image` plus `PageImage` association |

Skipped images do not fail the page or upload. A document can therefore have Markdown that references an OCR image absent from the API image list. Do not claim every Markdown image reference has a retained binary.

Successful base64 decoding is the only binary validation performed here. The agent does not check decoded image signatures, dimensions, nonempty content, per-image size, or the cumulative decoded image size. It stores decoded bytes with the accepted MIME string and sets `FileSize = bytes.Length`. Any future validation of those properties is a new behavior requiring fixtures for both rejected and retained images.

`Image.ImageType` is not assigned by this extraction code. Do not use that enum as the authoritative format discriminator for these rows. The actual download uses `FileMimeType`, and the framework `FileMimeType` setter classifies the general file type.

## Logical documents and page boundaries

`SplitResult.ConnectTo` queries this upload's pages ordered by `Number`. It validates the proposed ranges before removing any existing document rows.

The first range starts at page 1. Every next range starts immediately after the preceding range. Each last page is at least its first page and no greater than the page-array length. The ranges must cover all pages exactly once. The result must contain at least one document. Titles must be nonblank and at most 250 characters before trimming.

After validation, the splitter removes existing `Document` rows for that upload and adds the replacement rows. Each document's Markdown uses the ordered pages in its inclusive range.

```csharp
Markdown = string.Join("\n\n<!-- page break -->\n\n",
    pages[(range.FirstPage - 1)..range.LastPage]
        .Select(page => page.Markdown));
```

The slice end is exclusive in C#, so `LastPage` correctly includes that one-based last page. For pages 2 through 3, the slice starts at offset 1 and ends before offset 3. Preserve the exact `<!-- page break -->` marker when changing surrounding formatting unless a requested format change updates every consumer.

The splitter relies on valid contiguous stored page numbers established by OCR. It does not independently revalidate every stored `Number` against the array position. Manually changing page numbers can therefore undermine the meaning of the array slice and the separately computed image range.

Replacing logical documents does not replace OCR pages or binary images. The new document image lists are derived from the same page-image relationships. Keep document regeneration and OCR regeneration separate in maintenance workflows.

## Owner-scoped image delivery

`GET /api/v1/uploads/{id}` first finds a summary belonging to the current owner. It returns 404 when that upload is unavailable to the caller. Only after that check does it query documents and page-image references for the upload.

The image reference DTO contains image ID, page number, and OCR reference. It contains neither binary bytes nor a public download URL. The API attaches references to documents by inclusive page-range membership.

`GET /api/v1/uploads/{id}/images/{imageId}` joins through `PageImage.Page.Upload` and checks all of the following together.

- The link points to the requested image ID.
- The page belongs to the requested upload ID.
- That upload's `OwnerId` matches the authenticated subject.

It returns the stored bytes with the stored MIME type, or 404 if no matching binary is found. An image ID alone is insufficient. Preserve the entire join and owner condition if refactoring this endpoint. Do not use a generic image route or `Image.PublicUrl` as a substitute for this application-specific authorization path.

The shared `Image` class can calculate a public/intake hint such as `/v1/images/{ID}`. AIESummit's frontend does not use that hint, and this upload API does not expose an original-source download endpoint. A future source download needs its own explicit owner-scoped query and response design.

## Browser image lifecycle

[app.js](../AIESummit.Api/wwwroot/app.js) displays document Markdown as `textContent` inside a `pre` element. It does not render Markdown HTML or turn embedded OCR image references into automatic network requests. The page-break marker is therefore part of the displayed source text.

For each returned image reference, the browser adds a page-labeled button. Clicking it performs an authenticated fetch to the owner-scoped route. On success, it creates a browser object URL from the response blob and uses the OCR reference as the image's alt text.

The object URL is a temporary browser resource. It is not an external object-storage service or a persisted public URL. The frontend records these URLs and revokes them when changing detail views or signing out. A request sequence check prevents a completed stale image request from attaching to a newer detail view. Preserve those cleanup and stale-response checks when adding galleries or previews.

Image loading is on demand, so a visible document with no open image is not evidence of a failed image download. Check the returned references, then the authenticated fetch, then blob rendering when diagnosing a missing image.

## Retention, retries, and cleanup limits

The source bytes remain in `UploadedFile.Source` after OCR. Extracted image bytes remain in framework `Image.FileData`. There is no source-clearing step, blob offload, hash-based deduplication, cache TTL, or application retention job in this path. Successful processing does not imply the original source has been discarded.

The OCR agent catches HTTP failures, task cancellation, invalid page data, invalid operation, JSON parsing errors, and base64 format errors. It sets a generic upload error and marks OCR `PermanentError`. Logs contain the exception type and HTTP status when available, rather than the source document. Other failure types and later repository save failures depend on the surrounding framework path.

Do not describe the catch block as automatically retrying transient provider errors. Resetting a permanent error or rerunning OCR requires an explicit recovery design. The optional `UploadQueueRecovery` periodically re-enqueues pending objects after queue disruption. It is not output cleanup or an idempotent OCR retry implementation.

OCR does not remove previously persisted `OcrPage`, `PageImage`, or `Image` rows before adding replacements. Resetting a successfully processed upload to `New` can collide with unique page keys. Duplicate OCR image references on one page can also collide with `(PageId, Reference)` at persistence time. Image IDs are not deduplicated across pages or uploads.

The split policy's output-clear/reset hooks are currently empty. A valid split result replaces document rows, but clearing prompts alone must not be described as clearing all derived file data. A failed new split validation leaves existing documents untouched by that method.

Before implementing deletion or re-OCR, define the order and ownership of document, prompt, page-image, page, and binary-image cleanup. Removing a `PageImage` link does not by itself prove that its referenced `Image` has no other links. Inspect actual EF constraints and references before deleting binary rows. Follow [Relationships.AGENTS.md](Relationships.AGENTS.md) and [IHaveChildren.AGENTS.md](IHaveChildren.AGENTS.md) for those contracts.

## Verification cases

Use synthetic fixtures and a controlled OCR response when checking parsing behavior. Live-provider success alone will not cover malformed headers or ordering. Verify database state after the framework saves, not just the objects returned by the agent.

1. Check empty, exactly-limit, oversized, supported-signature, and unsupported-signature uploads. Include a misleading extension and a recognized prefix with malformed content to distinguish intake from OCR validation.
2. Check OCR responses containing 0, 1, 30, and 31 pages, and responses with duplicate, missing, or out-of-order indexes. Confirm persisted page 1 comes from OCR index 0.
3. Check each permitted extracted-image MIME type, null image data, bare base64, remote URLs, unsupported headers, and invalid base64. Distinguish skipping one image from failing the upload.
4. Verify bytes, MIME type, length, page association, and OCR reference survive save/reload. Include two pages using the same reference to confirm uniqueness is scoped to the page.
5. Check single-page and multi-page document ranges, exact coverage, gaps, overlaps, invalid titles, and the page-break marker. Confirm images follow page ranges without a direct document-image mapping.
6. With two users and two uploads, verify the owner can retrieve an associated image while another user, a mismatched upload ID, and an unrelated image ID cannot retrieve it through this route.
7. Open an image, change detail views, and sign out. Confirm stale responses do not attach to the new view and object URLs are revoked. Check a failed fetch restores a usable button.
8. Exercise the intended recovery workflow against existing outputs before promising safe retry. Confirm what remains after failure, what is replaced, and whether any orphan binary rows are created.

## Reference boundary

The complete Horus [PageImageContainer guide](../../horus/Voiston.Agents/Containers/PageImageContainer/AGENTS.md) was used for scope and practical depth. Its Poppler/ImageMagick rendering, Groq OCR and patient audit, rendered-page manifests, cache hashes and TTL, `PUBLIC_BASE_PATH`, 100 MB container limits, and Azure container deployment commands describe that separate system. They are not AIESummit capabilities or configuration requirements.

For current behavior, use the application sources linked above, [UploadQueueRecovery.cs](../AIESummit.Agents/UploadQueueRecovery.cs), framework [Image.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Domain/Entities/Image.cs), and [BasePersistenceFile.cs](https://github.com/10123IO/10.Blueshift.123/blob/cc69aa4da64bf313ec7120711624816c7f9d4254/Blueshift.Domain/Abstract/BasePersistenceFile.cs). Keep framework files as references and implement application-specific changes in this repository.
