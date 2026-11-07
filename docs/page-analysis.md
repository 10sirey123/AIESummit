# Page reader and native Blueshift section analysis

## Reader

The React workspace on port 5173 reads the retained original upload through the authenticated, owner-scoped `GET /api/v1/uploads/{id}/source` endpoint. It returns the original bytes, supports byte ranges and uses `Cache-Control: private, no-store`. Anonymous requests receive 401 and another owner's requests receive 404. The file is never made public or sent to a third-party PDF viewer.

The Original PDF and Markdown tabs share one source-page selection across all logical documents. PDF.js paints only the selected page onto one canvas, scaled to fit both dimensions of the bounded viewer. It never embeds a continuous browser PDF viewer or mounts adjacent pages. PNG/JPEG originals are shown as one fitted image. Markdown uses the exact persisted `OcrPage.Markdown` for the selected page, in a bounded scrolling region. Switching pages resets that region's scroll position. Page navigation uses source page numbers, not an inferred split of concatenated Markdown. Original document Markdown remains downloadable.

## Workflow

This is the supplied Blueshift LLMStep system, not a separate LLM execution loop:

1. OCR persists each `OcrPage` and sets its `LLMStepStatus` to `CheckForPrompts`.
2. Framework `LLMStepAgent<OcrPage>` hydrates the full page and its mapped `LLMPrompts` collection.
3. The persisted, code-backed `LLMStep` tagged `classify-page-sections` creates one `LLMPrompt` per page through `LLMPromptFactory`.
4. Framework `LlmPromptAgent` / `LlmPromptWorker` resolves that prompt through the configured Gemini provider.
5. `PageSectionsResult.ConnectTo` validates the response and stores the sections on that page. Summaries/classifications are generated descriptions; each separate `quote` must be an exact substring of that page's original Markdown. Invalid evidence is rejected before applying any result. There are no legal risk scores or legal advice.
6. The framework re-evaluates the subject and reaches `Processed` after its prompts resolve. The API exposes per-page state/results; the browser polls while analysis is pending.

The existing `split-upload` step remains on `UploadedFile` and continues using Azure. Its result/state is separate from page-section analysis. `UploadedFile_ID` and `OcrPage_ID` are separate persisted prompt relationships; `DocumentType`/`DocumentID` are the framework's logical dispatch identity. Worker queues and recovery include `ocrpage`, and telemetry correlates page runs/prompts to their root upload.

Existing pages are **not** automatically backfilled or sent to a newly configured provider. The owner can explicitly use **Analyze pages with Gemini**, which posts to `/api/v1/uploads/{id}/analyze`. This queues only pages without existing analysis or prompts. Failed prompts are retained for inspection and are not silently reset or deleted. New uploads use the enabled page step automatically. An unconfigured/disabled step leaves pages without analysis; configure it before queueing work.

## Configure Gemini as APIInfo

Start the stack with `./start.ps1`. Bootstrap creates a disabled, empty-key APIInfo plus a Gemini model and page step if missing. It never copies a Gemini key from environment variables into the database. Runtime agents read the persisted `APIInfo` record, not `.env` or `GEMINI_API_KEY`.

In [Blueshift Admin](http://localhost:13088), sign in using the token from `artifacts/admin.token.local.txt`, then use **Configuration → API Infos** and edit:

| Field | Value |
| --- | --- |
| Identifier | `geminiapi` (must match the framework adapter) |
| Base URI | `https://generativelanguage.googleapis.com/v1beta/models/` |
| Authorization Type | API key |
| App Key | The actual Gemini API key |
| Enabled | Yes |

App Key, App Secret and Auth Token are **write-only** in Admin: a supplied nonblank value replaces the stored value; omitted/blank values preserve it. Stored values are excluded from list/detail responses, related records, and broadcasts. The database still contains the usable credential; protect its volume/backups. Do not copy keys into screenshots, commands, documentation or Git.

Then use **Configuration → LLM Model** to inspect the model tagged `gemini-page-sections`. Set Provider to **Gemini** and Name to a model available to your key. The verified model for this environment is `gemini-3.8-flash`; model access changes, so consult [Google's model catalog](https://ai.google.dev/gemini-api/docs/models). The older `gemini-2.5-flash` returned 404 for this key because it is unavailable to new users.

Under **Configuration → LLM Step**, enable `classify-page-sections`, with subject **OCR pages**, DTO response type `AIESummit.Agents.PageSectionsResult`, validation **Valid**, and the Gemini model. Seeds preserve operator edits after creation. Changing `.env` does not change persisted credentials/model/step settings.

For repeatable local setup, a helper makes the same authenticated Admin API writes:

```powershell
./scripts/configure-gemini.ps1 -KeyFile C:\private\gemini-key.txt -Model gemini-3.8-flash
```

The file must contain only the key, must remain outside Git, and is never printed by the script. If the provided key already lives in your local `.env`, opt into a **one-time import** explicitly:

```powershell
./scripts/configure-gemini.ps1 -ImportLocalKey
docker compose restart worker
```

This import prefers the current local `.env` entry, then the process environment if no entry exists. This matters when an older shell still has a stale key. After import, the key persists in `APIInfo.AppKey`; the worker does not need the environment variable. Restarting the worker after key rotation clears the framework's short-lived provider configuration cache. Model and step remain operator-managed records.

Gemini's Developer API key support is a narrow patch to the bundled framework: for the official HTTPS `generativelanguage.googleapis.com` host it uses the APIInfo key in `x-goog-api-key` and `responseJsonSchema`. The existing Vertex service-account path remains separate. A second patch corrects Admin's previously ignored APIInfo secret writes while keeping those properties unreadable. Both patches, their hashes, and the new bundle version are recorded in `dependencies/blueshift/manifest.json`; source patches are in `deploy/patches`. Ordinary contributors still need no private framework access. See [bundle maintenance](../dependencies/blueshift/README.md). Google's [structured-output documentation](https://ai.google.dev/gemini-api/docs/structured-output) describes the provider schema contract.

If Gemini is unavailable, keep the same native page step and change its Model to the existing Azure `foundry-split` model in Admin. The APIInfo identifier for that provider is `openai`. Do not replace LLMSteps with an application-specific provider loop.

## Persistence upgrade

`AIESummit.Api/Migrations/001-page-sections.sql` is an embedded, idempotent additive upgrade. Bootstrap applies it under the existing PostgreSQL advisory transaction lock after initial schema creation. It adds page analysis columns and the nullable `OcrPage_ID` prompt relationship/index/foreign key without changing existing content, ownership or prompt history. Fresh databases get the matching EF model; repeated startup preserves existing rows. This targeted upgrade is not a general EF migration pipeline. Future schema changes need their own reviewed transition.

## Verify in Admin and the app

```powershell
dotnet test AIESummit.Tests/AIESummit.Tests.csproj
cd web
npm run build
npm run test:viewer
cd ..
./scripts/smoke.ps1 -VerifyPageSections -KeepTestSession
```

The extended smoke creates synthetic material, calls the configured live providers and checks source-byte fidelity, anonymous/second-owner rejection, three completed page classifications, nonempty summaries, exact evidence quotes and successful Admin page executions. It retains the synthetic accounts/upload/prompt/telemetry records. `-KeepTestSession` optionally saves only that synthetic account's credentials and upload ID to ignored `artifacts/page-smoke-session.local.json` for browser verification. Never use that option with real accounts/documents. No source file is retained outside the uploaded database record.

In Admin, inspect **LLM Step** for the enabled page definition; open **Operations → LLM Prompt** and verify one prompt per `OcrPage`, its model, response, and **Processed** state. Inspect **Agent Operations** for the page step/prompt runs and root-upload correlation. A healthy HTTP endpoint or a successful split alone does not prove page analysis completed. Earlier failed synthetic Gemini attempts remain in history and should be distinguished from the final verified upload.

Verified locally on November 7, 2026 with `-ContractFixture -VerifyPageSections -KeepTestSession`: synthetic upload **10**, pages **43–45**, page step **2**, and processed page prompts **20–22**. Prompt **22** showed Gemini as its used model and a successful agent execution in Admin. A later duplicate delivery was safely skipped because its status was outdated; this does not undo the earlier success. The EF mapping eagerly loads `LLMPrompt.UsedModel` so Admin can show this legacy shadow-key relationship. Admin APIInfo reads omitted all credential properties. The browser check covered desktop and 390px widths, same-page keyboard tab switching, exactly one fitted PDF canvas and Markdown scroll reset.

In the app, check each PDF page fits entirely, move forward/back, switch to Markdown on the same page and scroll its text, then inspect the section descriptions and expand source evidence. Test a second account and a narrow viewport. Test fixtures must be synthetic or explicitly authorized; do not silently replay private documents through a new provider.
