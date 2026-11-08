# AIESummit

AIESummit is the starting point for a contract intake and AI analysis workflow. The imported scaffold currently turns uploaded PDFs and images into individual Markdown documents. Contract clause extraction and risk analysis are not implemented yet. The app API and BlueShift Admin are separate services. Firebase authenticates app users, and each upload, result, and image is scoped to its owner.

## Run locally

For a fresh Windows machine with Docker Desktop installed, follow [install.md](install.md) to install and verify the remaining dependencies, configure credentials and start the app.

Install Docker and PowerShell 7. Clone this repository; [Blueshift binaries](dependencies/blueshift/README.md) are included, so no private framework access is needed. Create a local `.env` from `.env.example` and enter a Google AI Studio `GEMINI_API_KEY`. OCR and the LLM both default to `gemini-3.8-flash`.

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

Paste the token in `artifacts/admin.token.local.txt` into Admin. Use **Operations → Agent Operations** to inspect runs and queue status. **Configuration** contains the OCR connection, language model, split step, and agent controls. The startup seed initializes credentials and model names once so operator edits survive restarts. Update those records in Admin when rotating keys or changing models. The local token is renewed each time `start.ps1` runs.

## Processing

1. The API writes the source file and publishes its ID to Service Bus.
2. The BlueShift OCR agent sends the file to Gemini through an API browser configured by BlueShift APIInfo. It stores page Markdown in order. Gemini returns no image crops, so images are described inline in the Markdown.
3. BlueShift’s LLM step and prompt agents run the code-defined split step. The worker sends its Responses API calls to the `gemini-gateway` Compose service, which translates them to Gemini. Structured results must cover every page exactly once, in order. Accepted ranges become Document records, with Markdown page breaks.
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
cd ..
```

Compose validation requires `GEMINI_API_KEY` in the local environment. After starting the stack, run the provider-backed acceptance check:

```powershell
./scripts/smoke.ps1
```

The smoke test creates two local Firebase accounts, uploads a synthetic three-page PDF, checks cross-account isolation, verifies that live OCR and the LLM produce ranges 1–2 and 3, and checks the agent runs in Admin. It uses the configured Gemini models.

The initial import passed the solution, shared worker and shared Admin API builds, React production build, OpenAPI generation, JavaScript/PowerShell syntax checks and Compose configuration validation. The solution emits existing nullable/obsolete API warnings, and the React build emits Lucide directive warnings.

Local startup and the provider-backed smoke test passed on November 7, 2026 with the earlier Azure Foundry setup, Mistral OCR and `gpt-4.1-mini` (synthetic upload 1): Firebase sign-up/sign-in, user isolation, Mistral OCR, document ranges 1–2 and 3, preserved Markdown and Admin telemetry. The workspace, proxied Firebase configuration, API health and Admin page returned HTTP 200. An initial Docker DNS failure cleared on retry; an emulator queue-creation conflict cleared after restarting the emulator and worker. Provider settings were reused from the sibling Hackathon project's local configuration into ignored `.env`. Browser interaction and layout remain unverified.

The binary dependency transition also passed on November 7, 2026: an exported Git checkout without framework source built successfully with an empty NuGet cache, all Docker targets rebuilt and started from the bundle, and the full provider-backed smoke test passed for synthetic upload 2, also with the Azure Foundry setup. Dependency checksums passed after Git checkout, and the verifier rejected a deliberately modified disposable artifact.

The model provider moved to Google Gemini on November 7, 2026. The Gemini path passed the full provider-backed smoke test the same day on an Apple Silicon Mac (synthetic upload 1): Gemini OCR (`gemini-3.8-flash`) extracted 3 pages, the split step ran through `gemini-gateway`, and ranges 1–2 and 3, user isolation and Admin telemetry were verified. On Apple Silicon, compose runs the bundled linux-x64 worker and Admin API as `linux/amd64` under emulation.

Stop the stack with `docker compose down`. Named volumes retain PostgreSQL data, Firebase emulator accounts, Admin audit, and OpenSearch telemetry. The database schema is initialized on first start. Plan migrations before evolving a database that contains data you need to keep.


## Starting point and planned analysis

Imported from the working tree of `../hackathon` (base commit `da68b8149fa1b825fb4b1735862c2d16dff81b39`), including its React workspace, development guides, deployment setup, synthetic smoke test and local Impeccable harness. The installed harness under `.agents/skills/impeccable/` is local and ignored; its reinstall command is in `docs/ui-workflow.md`. No local credentials or runtime data were copied. Blueshift is bundled as local NuGet packages and prebuilt Linux worker/Admin artifacts from `net10` commit `cc69aa4da64bf313ec7120711624816c7f9d4254`; there is no submodule. See [the manifest](dependencies/blueshift/manifest.json) and [refresh instructions](dependencies/blueshift/README.md).

The planned workflow will use BlueShift LLMSteps and LLMPrompts to extract indemnity and liability limits, non-compete restrictions, renewal dates and termination triggers from MSAs and NDAs. Extraction evidence (document, page when available and quoted text) must remain separate from risk interpretations. Model output is not legal advice.

## Benchmark preparation

- Install a stable .NET 10 SDK. `global.json` selects SDK 10.0.403 with forward roll to later stable .NET 10 feature bands.
- Use Python 3.12 or later for the dependency-free dataset preparation utility.
- Read [AGENTS.md](AGENTS.md) for working conventions and the requirement to maintain these instructions alongside project changes.

```powershell
python scripts/download_benchmarks.py
python scripts/download_benchmarks.py --verify-only
```

The first command downloads all pinned CUAD, MAUD, and EDGAR-CORPUS files into `corporea/` and extracts the archives. Allow approximately 11.2 GB for downloads plus space for extracted data. Existing valid downloads are reused; interrupted downloads resume when the server supports byte ranges. The second command checks payload checksums and extracted file integrity without network access. Either command returns a nonzero exit code if verification fails and writes a local `corporea/manifest.json` report. Download concurrency can be adjusted with `--workers` (default: 6).

See [the benchmark catalog](corporea/README.md) for original-PDF availability, sources, licenses, and evaluation limitations. Dataset payloads are excluded from Git; download tooling, documentation, and source pins are tracked.

Build the application with `dotnet build AIESummit.slnx --nologo`. No dedicated application unit-test project exists yet.
