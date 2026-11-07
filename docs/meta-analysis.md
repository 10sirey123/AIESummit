# Meta Muse Wonder through native Blueshift LLMSteps

The bundled framework includes the sibling Blueshift Meta release (`10.0.0-preview.meta.1` source changes), combined with AIESummit's existing Gemini and write-only Admin credential patches. The resulting exact package version is `10.0.0-aiesummit.cc69aa4.meta1`. The upstream base commit remains `cc69aa4da64bf313ec7120711624816c7f9d4254`; `deploy/patches/meta-provider.patch` records the otherwise uncommitted Meta source changes reproducibly. All three patch hashes and seven binary artifacts are pinned in `dependencies/blueshift/manifest.json`. Both application references, worker and Admin use matching libraries. Consumers still need no private Blueshift access.

## Configuration

Run `./start.ps1` first. Bootstrap inserts a missing **disabled, empty-key** APIInfo with identifier `meta`, and a model tagged `meta-muse-wonder` with provider **Meta** (enum value 5) and name `muse-wonder-1.0`. Existing records and the current page-step model selection are preserved. No schema change is needed for the new enum value.

Explicitly import the provided local key into persisted APIInfo and select Muse Wonder for the existing page LLMStep:

```powershell
./scripts/configure-meta.ps1 -ImportLocalKey
docker compose restart worker
```

The helper reads `META_API_KEY` from the local ignored `.env` first, then the process environment. Alternatively, use `-KeyFile C:\private\meta-key.txt`; that file must contain only the key. It sends the key only in an authenticated Admin request body and never prints it. Runtime workers read `APIInfo.AppKey` from PostgreSQL, never `.env`. Admin credential fields remain write-only; blank values preserve the stored key. Restart after rotation to clear the provider's five-minute credential cache.

Manual setup in [Admin](http://localhost:13088):

| Record | Fields |
| --- | --- |
| APIInfo | Identifier `meta`, Base URI `https://api.meta.ai/v1`, App Key = actual key, Enabled = Yes |
| LLM Model | Tag `meta-muse-wonder`, Provider = Meta, Name `muse-wonder-1.0` |
| LLM Step | Tag `classify-page-sections`, Model = Meta / muse-wonder-1.0, Enabled = Yes, Reasoning effort empty |

The helper deliberately changes that existing page step's model and clears reasoning effort to use the model default. Azure OCR/document splitting and the Gemini configuration remain available. To switch page analysis back, select the Gemini model on the same step in Admin. The Gemini import helper configures its key/model and enables the step but does not change the step's current model selection.

Meta uses `Authorization: Bearer` against the official HTTPS `/v1/responses` endpoint. The adapter sends the framework's expected DTO schema as strict `text.format` JSON schema, accepts only completed usable output, and retains Meta provider/model identity and usage. See [Meta Responses API](https://dev.meta.ai/docs/protocols/responses) and [Muse Wonder model ID](https://dev.meta.ai/docs/models).

## Native processing and verification

No application-specific model execution loop was added. The [page workflow](page-analysis.md) still runs `LLMStepAgent<OcrPage>` → `LLMPromptFactory` → persisted `LLMPrompt` → `LlmPromptAgent` / `LlmPromptWorker` → `MetaAPI` → validated `PageSectionsResult`. Existing analyzed/failed pages are preserved. New uploads use the selected page model; use synthetic content for provider verification. The reader uses provider-neutral labels, and the explicit analysis endpoint checks the APIInfo matching the selected step model (Gemini, Meta or Azure/OpenAI).

```powershell
dotnet test AIESummit.Tests/AIESummit.Tests.csproj
./scripts/verify-dependencies.ps1
./scripts/smoke.ps1 -VerifyPageSections -ContractFixture -KeepTestSession -ExpectedPageModel muse-wonder-1.0 -TimeoutSeconds 240
```

The live smoke checks original-byte fidelity, owner isolation, OCR/splitting, three completed page results with exact evidence quotes, and successful Admin runs. The extra model assertion resolves each prompt's **Used Model** and requires both `muse-wonder-1.0` and provider **Meta**; an Azure/Gemini fallback cannot pass. It retains synthetic accounts, records and telemetry. Optional test login details remain under ignored artifacts. Unit tests intercept HTTP and cover bearer authentication, the strict schema, usage/provider identity and rejection of partial output; they do not establish live account access.

Verified November 7, 2026: synthetic upload **12**, pages **49–51**, APIInfo **4**, model **3**, page step **2**, and processed Meta prompts **27, 29, 30**. The model-specific live smoke, five .NET tests, frontend build/viewer checks and dependency checksums passed. Admin was inspected directly: step 2 was enabled/valid with Muse Wonder selected, and prompt 30 showed a response, Processed status, Meta used/preferred model and a successful execution. Its later duplicate delivery was safely skipped as outdated. Evidence screenshots are in ignored `artifacts/meta-admin-prompt.png` and `artifacts/meta-admin-execution.png`.
