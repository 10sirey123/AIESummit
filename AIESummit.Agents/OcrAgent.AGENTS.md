# OCR agent and Mistral adapter

Read the [repository guide](../AGENTS.md) and the [agent directory guide](AGENTS.md). The current implementation is entirely in [OcrAgent.cs](OcrAgent.cs). It uses BlueShift's `BaseAPIAgent` to load persisted connection configuration and sends one HTTP request containing the uploaded source to the configured Mistral OCR endpoint.

This is not Horus's PageImageContainer integration. AIESummit does not run Poppler rendering, ImageMagick normalization, Groq OCR, patient audits, rendered-page manifests or a PDF-hash OCR cache. The Horus guide supplies useful separation between agent, API browser and page output, but its endpoints and deployment commands are not applicable here.

## Agent contract and activation

`OcrAgent` derives from `BaseAPIAgent<UploadedFile, ProcessedStatus, OcrAgentConfig>`. Its static definition enables asynchronous processing, selects `UploadedFile.OcrStatus`, and uses `WhereExpression = _ => true`.

| Setting | Current value |
| --- | --- |
| Original eligible state | `ProcessedStatus.New` |
| Success state | `ProcessedStatus.Processed` |
| Permanent failure state | `ProcessedStatus.PermanentError` |
| Temporary error states | Not configured by this agent |
| Execution entrypoint | `ExecuteAgentAsync` |
| Synchronous entrypoint | Throws `NotSupportedException` |

The framework's default subject hydration remains enabled for OCR. Unlike the generic LLM step agent's ID-only path, OCR needs the loaded `Source` and `ContentType`. Preserve this distinction when refactoring shared processing policies.

`Bootstrap.InitializeAsync` creates an enabled `OcrAgentConfig` named `Mistral OCR` with `AgentClassName = nameof(OcrAgent)` only if its configuration set has no rows. A static enabled definition does not override an operator-disabled runtime configuration. Pack activation, worker queue configuration and the enabled configuration must agree.

The worker's recovery service considers enabled `OcrAgent` instances and calls their pending-object enqueue method. It does not reset permanently failed uploads. See [QueueRecovery.AGENTS.md](QueueRecovery.AGENTS.md) and [Worker.AGENTS.md](../deploy/Worker.AGENTS.md).

## APIInfo configuration

`MistralOcrBrowser.APIIdentifier` is exactly `mistral-ocr`. `BaseAPIAgent.GetApiBrowser<T>` constructs the browser and reads an `APIInfo` whose identifier matches case-insensitively. It uses a no-tracking query and returns no browser when the agent repository is unavailable, the record is absent, or the record is disabled.

The framework copies the stored context as follows.

| Stored APIInfo field | Browser field | OCR use |
| --- | --- | --- |
| `BaseURI` | `ApiBaseUri` | Complete POST destination |
| `AppKey` | `AppKey` | `api-key` request header |
| `AppSecret` | `AppKeySecret` | JSON `model` deployment name |
| `AppAuthToken` | `UserAccessToken` | Not used by the current OCR request |

The seed uses `AZURE_FOUNDRY_ENDPOINT` with trailing slashes removed and appends `/providers/mistral/azure/ocr`, unless `OCR_ENDPOINT` is supplied to API configuration. It takes the key from `AZURE_FOUNDRY_KEY` and the deployment name from `OCR_MODEL`, defaulting to `mistral-ocr-4-0`. The field named `AppSecret` holds the deployment name for this adapter, not a second authentication secret.

The seed marks the connection with `ApiAuthorizationType.ApiKey`, but the custom request explicitly writes its own header. Do not assume a generic BaseAPIBrowser authorization handler adds it automatically. The browser uses its own static `HttpClient` and direct `SendAsync`, not the base request helper.

Existing connection records survive restart. Changing `.env` does not replace an already persisted endpoint, key or model. Update the appropriate Admin record when rotating configuration. `OCR_ENDPOINT` is read by bootstrap code but is not currently forwarded as an explicit variable in `compose.yaml`. An override must actually reach the API configuration or be applied to the persisted record.

No OAuth token acquisition or refresh occurs in this adapter. Its token-expiry, invalid-token and permission-check methods return false, and its corresponding fix methods return false. Do not document automatic credential repair.

## Exact request shape

`ReadAsync` converts the complete `UploadedFile.Source` bytes into a base64 data URL using the stored content type. For a PDF, the document object uses `type = "document_url"` and a `document_url` field. For any other content type, it uses `type = "image_url"` and an `image_url` field.

The following is a representative payload shape, with synthetic placeholders rather than actual encoded source content.

```json
{
  "model": "configured-deployment-name",
  "document": {
    "type": "document_url",
    "document_url": "data:application/pdf;base64,<encoded source bytes>"
  },
  "include_image_base64": true
}
```

The image variant changes the two document fields to `image_url` and uses the persisted image MIME type. This adapter does not upload multipart content, provide a remotely hosted document URL, or perform one request per page.

The serialized JSON is sent with `StringContent(payload, Encoding.UTF8, "application/json")`. Preserve explicit JSON content encoding when changing request construction. `EnsureSuccessStatusCode` rejects non-success responses before JSON parsing. The browser deserializes `OcrResponse` and returns its `Pages`, falling back to an empty array when the response or pages value is null.

The public upload API admits PDF, PNG and JPEG magic bytes with a 20 MiB limit. The browser itself does not repeat that validation. Internal creation or mutation of uploads can therefore bypass the HTTP input boundary. Do not assume an arbitrary stored non-PDF content type is safe simply because the browser chooses its image branch.

There is no adapter-specific timeout configuration or request cancellation token in `ReadAsync`. The static client retains its normal timeout behavior. A timed-out request can raise `TaskCanceledException`, which the agent treats as a permanent failure. Do not promise host shutdown immediately cancels an in-flight provider request.

## Response and page numbering

The local response records are `OcrResponse`, `OcrPageResponse` and `OcrImageResponse`. An OCR page carries `Index`, `Markdown` and optional `Images`. An image carries `Id` and nullable `Data`, with `Data` explicitly bound to the JSON field `image_base64`.

Before creating repository rows, the agent requires between 1 and 30 returned pages, inclusive. Their `Index` sequence must exactly equal `0, 1, ... pages.Length - 1` in the returned order. It does not sort or repair the response. Empty output, more than 30 pages, reordered indexes, duplicate indexes or a missing index fail this check.

Accepted provider indexes become one-based `OcrPage.Number = source.Index + 1`. Preserve that conversion. Split ranges and UI page references use one-based inclusive page numbers, while the provider response check is zero-based.

The 30-page bound is checked after the cloud call. It is not a local preflight PDF page-count check and does not prevent provider usage for a source that exceeds the limit. Do not describe it as a guaranteed cost limit.

The code copies `Markdown` directly. It does not normalize text, concatenate pages at OCR time or replace missing text with a placeholder. C# record annotations are not comprehensive hostile-response validation. Explicitly null or malformed page members may fail outside the documented caught cases or at persistence time. If strengthening response validation, add targeted cases rather than claiming the current parser already rejects every malformed shape safely.

## Extracted images

For each accepted page, the agent loops over `Images`, treating a null collection as empty. Current behavior is precise and intentionally narrower than general image decoding.

| Input condition | Current behavior |
| --- | --- |
| `image_base64` is null | Skip the image |
| No comma in the data URL | Skip |
| Prefix is not ordinal `data:image/` | Skip |
| Header before comma does not end with ordinal `;base64` | Skip |
| MIME type is not PNG, JPEG, WebP or GIF | Skip |
| Allowed header but invalid base64 body | `FormatException`, failing the OCR attempt |
| Allowed header and decodable body | Stage an image record and page link |

The MIME comparison is case-sensitive. A skipped image does not fail the page or strip its reference from the page Markdown. Conversely, an invalid base64 body for an allowed MIME type fails the whole attempt rather than silently dropping just that image.

Accepted bytes are stored in a framework `Image` with `FileData`, `FileMimeType` and `FileSize`, linked by a `PageImage` containing the provider's `Id` as `Reference`. The link points to the new page through its navigation so EF can establish IDs when saving the graph.

This is header and base64 validation, not a full image decoder or magic-byte check. There is no explicit per-image size limit, dimension validation, content re-encoding, image-count limit or nonempty-body check here. Duplicate references on one page can conflict with the unique `PageId, Reference` index at save time. Do not claim those cases are caught by the adapter's HTTP error handling.

The image API later authorizes access through the page's upload owner. Preserve the relationship when changing storage. See [Files.AGENTS.md](../AIESummit.Domain/Files.AGENTS.md) for ownership and storage details.

## Staging and save boundary

The agent first builds ordinary `List<OcrPage>` and `List<PageImage>` collections. It adds them to repositories only after all pages and image decoding have completed. Thus an invalid base64 image encountered during construction does not leave earlier staged page/image objects added by this method.

After staging succeeds, it adds all page rows and image links, sets `PageCount`, sets `OcrStatus = Processed`, and moves `LLMStepStatus` to `CheckForPrompts`. It logs the page count and returns the repository and updated upload to the framework.

The method does not call `SaveChanges` itself. The surrounding framework processing path persists the changes and applies its modification/publication behavior. Do not add a save inside each page loop. That would change the failure boundary and could expose incomplete OCR output to downstream processing.

Staging is not a claim of end-to-end exactly-once processing. The code does not clear existing pages or image links, perform an upsert, or check whether a prior OCR attempt already stored results. Forcing a processed upload back to `New` can collide with unique page indexes. The successful branch also does not explicitly clear a pre-existing `file.Error`, so an unsupported manual reset is not a complete retry workflow.

If adding reprocessing, design cleanup and state transitions together, including documents, prompts, page links and potentially orphaned image bytes. Verify the database transaction and recovery behavior rather than treating an assignment to `OcrStatus` as an idempotent retry API.

## Failure behavior

The agent catches `HttpRequestException`, `TaskCanceledException`, `InvalidDataException`, `InvalidOperationException`, `System.Text.Json.JsonException` and `FormatException`. It sets a safe public error message, marks OCR `PermanentError`, logs exception type and optional HTTP status, and returns failure.

This covers a missing/disabled API context, non-success HTTP responses, timeout, rejected page sequence, malformed JSON and invalid base64 in the supported image path. It does not classify 429 or 5xx as temporary errors. There is no configured automatic OCR retry policy for those responses.

Exceptions outside that filter and failures during the framework's later save follow the outer framework path. Do not promise that every database constraint failure becomes the same safe error string. Investigate the actual execution record and durable state.

The logged message intentionally omits request body, response body, source bytes and API key. Do not replace it with raw exception/provider payload dumps. Safe diagnostic context can include upload ID, agent name, exception category, HTTP status, page count and persisted processing state.

## Verification and diagnostics

Build from the repository root after implementation changes.

```powershell
dotnet build AIESummit.Agents/AIESummit.Agents.csproj --nologo
dotnet build AIESummit.slnx --nologo
docker compose logs --tail 100 worker
```

Inspect the `mistral-ocr` configuration's presence, enabled state, endpoint shape and deployment name without displaying its key. Check whether the worker loaded the pack and the runtime OCR configuration is enabled. Use Admin execution records correlated to the upload before changing its status.

Proposed targeted regression cases should cover the following. These are test requirements for future implementation changes, not a claim that a dedicated OCR unit-test project already exists.

- A PDF request uses the document variant and an image request uses the image variant, both with explicit JSON content type, configured deployment and key header.
- One page and 30 pages pass. Zero and 31 pages fail. Reordered, duplicated and missing zero-based indexes fail before repository additions.
- Page boundaries and Markdown remain separate, with saved numbers starting at 1.
- Null images and unsupported data URLs skip. Allowed PNG/JPEG/WebP/GIF headers with valid bytes produce linked records. Invalid base64 in a supported header fails before any staged rows are added.
- Duplicate image references, null Markdown and malformed page objects expose the actual current failure boundary instead of being assumed to work.
- HTTP 401, 429, 500 and timeout produce permanent OCR failure under the current policy. A later policy change must deliberately update expected behavior.
- A successful new upload transitions to split eligibility and preserves its owner and content type. An operator-forced rerun is tested separately from the supported first run.

For full integration, read [smoke.AGENTS.md](../scripts/smoke.AGENTS.md), then use the existing local startup and smoke scripts. The smoke sends a synthetic three-page PDF to the configured live deployment and verifies page count, split ranges, owner isolation and Admin telemetry. It may incur provider charges and does not exercise all image or failure cases.

Do not use Horus's PageImageContainer test scripts or bind its suggested port 18080 alongside this stack. That port already serves the AIESummit workspace. No extra OCR container is needed for the current design.

## Sources and historical inspiration

Application sources are [OcrAgent.cs](OcrAgent.cs), [Bootstrap.cs](../AIESummit.Api/Bootstrap.cs), [UploadedFile.cs](../AIESummit.Domain/UploadedFile.cs), [UploadEndpoints.cs](../AIESummit.Api/UploadEndpoints.cs) and [compose.yaml](../compose.yaml).

Framework context loading is defined by [BaseAPIAgent.cs](../external/blueshift/Blueshift.Agents/Abstract/BaseAPIAgent.cs) and `BaseAPIExtensionMethods.SetContext` in [BaseAPIBrowser.cs](../external/blueshift/Blueshift.Domain/Infrastructure/APIBrowser/BaseAPIBrowser.cs). [BaseAgent.cs](../external/blueshift/Blueshift.Agents/Abstract/BaseAgent.cs) controls the surrounding execution, state and save lifecycle.

The complete Horus reference is `../../horus/Voiston.Agents/Containers/PageImageContainer/AGENTS.md`. Its detailed request/output and operational documentation motivated this guide. Its Groq credentials, cache flags, patient-audit fields, rendering dependencies and Azure Container Apps deployment instructions are not AIESummit functionality.
