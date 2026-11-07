# AIESummit

Meta Muse Wonder is available through the same native Blueshift page-section LLMStep. See [Meta configuration and verification](docs/meta-analysis.md) for the bundled version, explicit `.env`-to-APIInfo import and the model-specific acceptance check.

AIESummit is the starting point for a contract intake and AI analysis workflow. The imported scaffold currently turns uploaded PDFs and images into individual Markdown documents. Per-page section descriptions and a first ten-target contract extraction backend are implemented. Legal risk analysis and frontend extraction cards remain future work. The app API and BlueShift Admin are separate services. Firebase authenticates app users, and each upload, result, and image is scoped to its owner.

## Run locally

For a fresh Windows machine with Docker Desktop installed, follow [install.md](install.md) to install and verify the remaining dependencies, configure credentials and start the app.

Install Docker and PowerShell 7. Clone this repository; [Blueshift binaries](dependencies/blueshift/README.md) are included, so no private framework access is needed. Create a local `.env` from `.env.example` and enter the Azure Foundry endpoint and key. The defaults use the `mistral-ocr-4-0` and `gpt-4.1-mini` deployments.

```powershell
./start.ps1
```

In a second PowerShell window, use Node.js 24 to start the React workspace.

```powershell
cd web
npm ci
npm run dev
```

| Service | URL |
| --- | --- |
| AIESummit React workspace | http://127.0.0.1:5173 |
| API and original browser client | http://localhost:18080 |
| OpenAPI 3 contract | http://localhost:18080/openapi.json |
| BlueShift Admin | http://localhost:13088 |

Create an email/password account in the workspace. The local stack runs Firebase Authentication Emulator. For a real Firebase project, enable Email/Password in Firebase and set `FIREBASE_EMULATOR=false`, `AIESUMMIT_API_ENVIRONMENT=Production`, `FIREBASE_PROJECT_ID`, and `FIREBASE_API_KEY` before starting. Keep the local emulator and its unsigned tokens restricted to local development.

Read [the AIESummit design brief](docs/ui.md) and [the UI workflow](docs/ui-workflow.md) before changing the React workspace.

The React workspace shows one source page at a time, with Original PDF and Markdown tabs that retain the same page selection. The PDF page is fitted completely; its matching OCR Markdown scrolls within the reader. The sidebar shows per-page section classifications, short summaries and expandable source quotes from the native Blueshift page-analysis step. See [page-analysis.md](docs/page-analysis.md) to persist the Gemini key as APIInfo, configure models/steps, inspect Admin and run the extended smoke test. Reviewer notes and legal risk scoring are not implemented. The original client on port 18080 remains separate.

Paste the token in `artifacts/admin.token.local.txt` into Admin. Use **Operations → Agent Operations** to inspect runs and queue status. **Configuration** contains the OCR connection, language model, split step, and agent controls. The startup seed initializes credentials and deployment names once so operator edits survive restarts. Update those records in Admin when rotating keys or changing models. The local token is renewed each time `start.ps1` runs.

## Processing

1. The API writes the source file and publishes its ID to Service Bus.
2. The BlueShift OCR agent calls the Mistral deployment through an API browser configured by BlueShift APIInfo. It stores page Markdown in order and retains extracted images as BlueShift Image records.
3. BlueShift’s LLM step and prompt agents run the code-defined split step. Structured results must cover every page exactly once, in order. Accepted ranges become Document records, with Markdown page breaks and references to the images within each range.
4. The app polls upload status and shows the resulting documents. OpenSearch stores agent run telemetry for BlueShift Admin.

The client contract is maintained in `AIESummit.Api/ApiSpec`, using separate paths and components following Horus's DUTai API conventions. Rebuild it with `node AIESummit.Api/ApiSpec/build.mjs`. The current Cite8 reference is Erick's `codex/certification-tier-checkout` branch at `903626a9bb4ce471bccc28490b857886d6aa5c78`, verified on November 6, 2026. Its shared worker and Admin pack layout informs this application; its product-specific features are separate. See [AGENTS.md](AGENTS.md) for development guidance.

## Verify

```powershell
dotnet build AIESummit.slnx --nologo
node AIESummit.Api/ApiSpec/build.mjs
node --check AIESummit.Api/wwwroot/app.js
docker compose config -q
cd web
npm ci
npm run build
npm run test:viewer
cd ..
```

Compose validation requires Foundry settings in the local environment. After starting the stack, run the provider-backed acceptance check:

```powershell
./scripts/smoke.ps1
```

The smoke test creates two local Firebase accounts, uploads a synthetic three-page PDF, checks cross-account isolation, verifies that live OCR and the LLM produce ranges 1–2 and 3, and checks the agent runs in Admin. It uses the configured Azure deployments.

The initial import passed the solution, shared worker and shared Admin API builds, React production build, OpenAPI generation, JavaScript/PowerShell syntax checks and Compose configuration validation. The solution emits existing nullable/obsolete API warnings, and the React build emits Lucide directive warnings.

Local startup and the provider-backed smoke test passed on November 7, 2026 (synthetic upload 1): Firebase sign-up/sign-in, user isolation, Mistral OCR, document ranges 1–2 and 3, preserved Markdown and Admin telemetry. The workspace, proxied Firebase configuration, API health and Admin page returned HTTP 200. An initial Docker DNS failure cleared on retry; an emulator queue-creation conflict cleared after restarting the emulator and worker. Azure provider settings were reused from the sibling Hackathon project's local configuration into ignored `.env`.

OCR and LLM prompt response caching are enabled. On an existing installation, restart the API once after updating to apply the additive OCR fingerprint column/index and turn on `MD5HashCheck` in the persisted prompt-agent configuration. New uploads are fingerprinted by exact file bytes; the first OCR of a repeated file can then reuse completed OCR pages and image data. No historical backfill is required. LLM cache hits reuse a prior successful response when the provider/model, prompt, system prompt, settings and response format match. The existing agent configuration is enabled by bootstrap; verify `Uploads.OcrCacheKey` exists and `LLMPromptAgentConfigs.MD5HashCheck` is true in PostgreSQL before processing.

The page-reader/analysis extension passed on November 7, 2026 using synthetic MSA/NDA upload 10: all three native page prompts completed with Gemini, exact evidence validation and source-access isolation passed, and Admin displayed the enabled step, processed prompts, selected model and successful agent execution. Desktop and 390px browser checks verified a single fitted PDF canvas, same-page tab switching, scrolling Markdown and scroll reset on page changes. Three focused .NET tests, the React build/viewer checks and bundled-dependency verification passed. Gemini credentials reside in persisted APIInfo; see [configuration and verification](docs/page-analysis.md).

The binary dependency transition also passed on November 7, 2026: an exported Git checkout without framework source built successfully with an empty NuGet cache, all Docker targets rebuilt and started from the bundle, and the full provider-backed smoke test passed for synthetic upload 2. Dependency checksums passed after Git checkout, and the verifier rejected a deliberately modified disposable artifact.

Stop the stack with `docker compose down`. Named volumes retain PostgreSQL data, Firebase emulator accounts, Admin audit, and OpenSearch telemetry. The database schema is initialized on first start. Plan migrations before evolving a database that contains data you need to keep.


## Starting point and contract analysis

Imported from the working tree of `../hackathon` (base commit `da68b8149fa1b825fb4b1735862c2d16dff81b39`), including its React workspace, development guides, deployment setup, synthetic smoke test and local Impeccable harness. The installed harness under `.agents/skills/impeccable/` is local and ignored; its reinstall command is in `docs/ui-workflow.md`. No local credentials or runtime data were copied. Blueshift is bundled as local NuGet packages and prebuilt Linux worker/Admin artifacts from `net10` commit `cc69aa4da64bf313ec7120711624816c7f9d4254`; there is no submodule. See [the manifest](dependencies/blueshift/manifest.json) and [refresh instructions](dependencies/blueshift/README.md).

The backend now uses one BlueShift LLMStep to extract ten selected CUAD targets together, including dates, renewal, non-compete, convenience termination, liability, audit and insurance terms. General indemnification, termination for cause and the healthcare targets remain extensions. Extraction evidence (document, page when available and quoted text) must remain separate from risk interpretations. Model output is not legal advice.

The [extraction target specification](docs/extraction-targets.md) preserves that original scope and proposes 15 benchmark-supported targets plus 10 additional healthcare targets, including planned card fields and evaluation boundaries. B01–B10 now have a naive backend implementation; the other targets remain product goals. EDGAR remains unlabeled.

## Extraction tester and instrumentation

See [backend entities, typed parameters and CLI commands](docs/contract-extraction.md), [MongoDB collections and graph consumer contract](docs/evaluation-instrumentation.md), and [the measured 20-document baseline](docs/extraction-baseline-results.md). Startup seeds insert missing target/step records without overwriting existing configuration. Analyses preserve their page text, evidence offsets and target definitions.

```powershell
dotnet run --project AIESummit.Evaluation -- run path/to/document.pdf --gold path/to/cuad-sidecar.json
./scripts/test-extractions.ps1
```

The tester uses the running application and live providers. It reads the MongoDB URI from local `.env` (or `--env-file`) without printing it. `--mongo-offline` preserves telemetry for replay. Preserve its generated artifact folder. PostgreSQL remains the application database; MongoDB holds test instrumentation only.

## Benchmark preparation

The prepared working collection lives in `corporea/benchmarks/`, with separate `cuad`, `maud`, and `edgar-corpus` folders. Each numbered PDF has a matching JSON label file and reference TXT: 510 CUAD contracts, 152 MAUD agreements, and 100 EDGAR reports. CUAD PDFs are original; MAUD/EDGAR PDFs are rendered from source text. EDGAR labels are explicitly unknown, not negative gold labels.

Downloads have been stopped as requested; the current sources are sufficient for this collection. To prepare from the existing files without network access or model calls (after dependencies are installed):

```powershell
python -m pip install -r scripts/benchmark-requirements.txt
python scripts/prepare_benchmarks.py
```

The commands below are for optional future full-release downloads. They are not required to use the prepared collection.

- Install a stable .NET 10 SDK. `global.json` selects SDK 10.0.403 with forward roll to later stable .NET 10 feature bands.
- Use Python 3.12 or later for the dependency-free dataset preparation utility.
- Read [AGENTS.md](AGENTS.md) for working conventions and the requirement to maintain these instructions alongside project changes.

```powershell
python scripts/download_benchmarks.py
python scripts/download_benchmarks.py --verify-only
```

The first command downloads all pinned CUAD, MAUD, and EDGAR-CORPUS files into `corporea/` and extracts the archives. Allow approximately 11.2 GB for downloads plus space for extracted data. Existing valid downloads are reused; interrupted downloads resume when the server supports byte ranges. The second command checks payload checksums and extracted file integrity without network access. Either command returns a nonzero exit code if verification fails and writes a local `corporea/manifest.json` report. Download concurrency can be adjusted with `--workers` (default: 6).

See [the benchmark catalog](corporea/README.md) for original-PDF availability, sources, licenses, and evaluation limitations. Dataset payloads are excluded from Git; download tooling, documentation, and source pins are tracked.

Build the application with `dotnet build AIESummit.slnx --nologo`. Run `dotnet test AIESummit.Tests/AIESummit.Tests.csproj` for focused section-validation and mapping tests.

## Synchronize workflow configuration from code

The bundled Blueshift CLI supports reviewed, transactional code-to-database configuration updates. Run `./scripts/admin.ps1 build`, `schema`, `export`, then `plan`; apply with `./scripts/admin.ps1 apply -Preview <reviewed-token>`. Default exports update the actual LLMStep definitions while preserving credentials, selected models, enabled settings and prompt history. See [configuration seeding](docs/configuration-seeding.md) for the complete flow, optional model bindings, policies and isolated verification. No private Blueshift access is required.

For a prompt change, edit `SplitStep.Definition()` or `PageSectionsStep.Definition()`, then follow the guide's build/export/validate/plan/apply sequence. Changing C# alone does not update an existing database row. Changing a metadata class or its `ConnectTo` logic also requires deploying the application/worker code; seeding only updates configuration. `export -IncludeBindings` deliberately includes model/enabled settings and can replace an existing model selection.

MongoDB primary connectivity and tester writes are now verified, and the saved 20-document baseline is persisted in Atlas. See [connectivity diagnostics](docs/evaluation-instrumentation.md#connectivity-checks-and-the-resolved-tls-failure) for the read-only mongosh probe and the resolved multi-egress-IP access-list issue.
