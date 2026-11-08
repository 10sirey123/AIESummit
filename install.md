# Install AIESummit from scratch

This guide sets up the complete local development environment on Windows x64, starting with only Docker Desktop installed and running. Run the commands in order. A web browser, internet access, access to this AIESummit repository, and a Google Gemini API key from Google AI Studio are also needed. Blueshift binaries are included in this repository; no private framework repository or private package-feed access is required. The current app performs document intake, OCR and splitting; contract risk analysis is future work.

## 1. Check Docker Desktop

Open **Windows PowerShell** from Start (the built-in version is sufficient for this first step):

```powershell
docker version
docker info --format '{{.OSType}}'
docker compose version
docker compose up --help | Select-String -- '--wait'
docker run --rm hello-world
```

`docker version` must show both Client and Server sections, the OS type must be `linux`, Compose must be available with `--wait` support, and the test container must print its success message. If the server cannot be reached, open Docker Desktop and wait for its engine to start. If the OS type is `windows`, switch Docker Desktop to Linux containers. If Docker is not found, reopen the terminal after installing Docker Desktop.

Use Docker Desktop's supported WSL 2/Linux setup. See [Docker's Windows setup and requirements](https://docs.docker.com/desktop/setup/install/windows-install/) if the checks fail. Allow disk space for multiple container images, database volumes and build caches. The stack runs PostgreSQL, SQL Server, Service Bus, OpenSearch, Firebase and application services together; if containers exit for lack of memory, increase Docker/WSL resources. Benchmark downloads require additional space described below.

## 2. Install the host tools

Check Windows Package Manager:

```powershell
winget --version
```

If it is missing, install or update **App Installer** in Microsoft Store, then reopen Windows PowerShell. See [Microsoft's WinGet setup](https://learn.microsoft.com/en-us/windows/package-manager/winget/). If Microsoft Store/WinGet is unavailable, use the official installers linked below instead. Approve any Windows installer elevation prompts.

Install PowerShell 7, Git and the .NET 10 SDK:

```powershell
winget install --id Microsoft.PowerShell --exact --source winget
winget install --id Git.Git --exact --source winget
winget install --id Microsoft.DotNet.SDK.10 --exact --version 10.0.403 --source winget
```

Install the **SDK**, which includes the runtimes needed for local development. A runtime-only installation cannot build this project. The repository's `global.json` requires SDK **10.0.403** or a later compatible stable .NET 10 SDK; installing .NET 8, .NET 9, or only .NET 11 does not satisfy it. If that exact WinGet version is unavailable, install a newer compatible .NET 10 SDK or download 10.0.403 from the [.NET 10 downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). Docker uses its own SDK image for container builds, independently of this host installation. No Visual Studio installation or additional .NET workload is required.

Official installer alternatives and background: [PowerShell 7](https://learn.microsoft.com/en-us/powershell/scripting/install/installing-powershell-on-windows), [Git for Windows](https://git-scm.com/install/windows), [.NET on Windows](https://learn.microsoft.com/en-us/dotnet/core/install/windows).

Install **Node.js 24 x64** using the Windows `.msi` installer in the [official latest Node.js 24 release directory](https://nodejs.org/download/release/latest-v24.x/). Keep npm and the PATH integration selected. Use the 24.x line used by this project; the generic latest/LTS download may select a different major version over time. Node/npm run the React workspace and OpenAPI builder. React, TypeScript, Vite and the other project packages are installed later by `npm ci`.

Close existing terminals and launch **PowerShell 7** from Start, or launch `pwsh` from a newly opened terminal. Use PowerShell 7 for every remaining command:

```powershell
$PSVersionTable.PSVersion
git --version
dotnet --list-sdks
node --version
npm --version
```

Expect PowerShell 7.x, an installed Git, SDK 10.0.403 or a compatible newer 10.0 SDK, Node v24.x and an npm version. If a command is missing, reopen the terminal to refresh PATH. Windows PowerShell 5.1 cannot run all application scripts.

## 3. Clone the application

Use a short local path to leave room for long benchmark filenames. The following example creates a new checkout; if the directory already exists, use that checkout instead of cloning over it.

```powershell
New-Item -ItemType Directory -Force C:\src | Out-Null
Set-Location C:\src
git clone https://github.com/10sirey123/AIESummit.git
Set-Location AIESummit
./scripts/verify-dependencies.ps1
dotnet --version
```

If Git requests authentication for AIESummit, use an account granted access to this repository. Do not put a token in the clone URL or a tracked file. GitHub CLI is not required. There are no submodules to initialize. The checksum verification must report seven valid artifacts from `dependencies/blueshift`; its manifest records the upstream `net10` revision. See [the bundle guide](dependencies/blueshift/README.md) for details. The local NuGet feed is configured automatically by the repository's `NuGet.Config`.

Run `dotnet --version` **inside this repository** to verify the SDK selected by `global.json`. Confirm `AIESummit.slnx`, `start.ps1`, `compose.yaml` and `web/package-lock.json` exist. If they are absent, obtain the application branch containing the intake scaffold before continuing.

If PowerShell reports that downloaded scripts are blocked, review the file and use `Unblock-File` on that specific trusted file. If local script execution is disabled, the per-user development setting is `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`. Organization policy can override this; follow your administrator's policy rather than disabling it globally.

## 4. Configure the model providers

From the repository root, create the local settings file once:

```powershell
if (-not (Test-Path .env)) { Copy-Item .env.example .env }
notepad .env
```

Fill in the following values, save, and close the editor:

| Setting | What to enter |
| --- | --- |
| `GEMINI_API_KEY` | A Gemini API key created in Google AI Studio (https://aistudio.google.com/apikey) |
| `OCR_MODEL` | Gemini model for OCR; default `gemini-3.8-flash` |
| `LLM_MODEL` | Gemini model for document splitting; default `gemini-3.8-flash`. `gemini-4-argon` is also available |

OCR calls the Gemini API directly. The worker's LLM calls go through the local `gemini-gateway` Compose service, which translates them to Gemini using the same key; see [the gateway guide](deploy/gemini-gateway/AGENTS.md). A Google Cloud service account or Vertex AI setup is not needed. The repository does not create a Google account, API key, quota or billing. Missing credentials prevent startup; an invalid key or model name prevents processing. Provider calls use external services and may incur charges.

Keep these local defaults for the first run:

```dotenv
FIREBASE_EMULATOR=true
FIREBASE_PROJECT_ID=demo-aiesummit
FIREBASE_API_KEY=demo-key
AIESUMMIT_API_ENVIRONMENT=Development
```

No real Firebase project is needed for local testing. PostgreSQL, SQL Server, Service Bus, OpenSearch and Firebase tools run in Docker; do not install them separately on Windows. The container builds restore application packages from the bundled Blueshift feed and public NuGet. Worker/Admin hosts and the Admin website are already built and included in `dependencies/blueshift`.

`.env`, generated Admin settings and tokens are ignored by Git. Never paste their contents into logs or commit them. Startup seeds missing provider/model records into PostgreSQL. After the first successful initialization, change persisted provider credentials/models through Admin; editing `.env` alone does not overwrite existing configuration. Do not delete data volumes to rotate credentials.

## 5. Build and start the services

From the repository root in PowerShell 7:

```powershell
docker compose config -q
./start.ps1
```

The quiet configuration check should exit successfully without printing resolved secrets. Startup verifies the bundled dependency checksums, creates Admin settings and a four-hour token, builds the images and starts the services. Initial downloads/builds can take several minutes; the readiness timeout starts after the build phase.

Check the running services and API:

```powershell
docker compose ps
Invoke-RestMethod http://localhost:18080/health
```

All services should be running, the API/PostgreSQL/OpenSearch health checks should pass, and the worker should remain running instead of repeatedly restarting. A served health response alone does not prove OCR/LLM processing works.

## 6. Start the React workspace and test

Open a second PowerShell 7 terminal, leaving the services running:

```powershell
Set-Location C:\src\AIESummit\web
npm ci
npm run dev
```

Keep that terminal open. If you chose another checkout directory, use its `web` folder instead.

| Surface | URL | How to sign in |
| --- | --- | --- |
| React workspace | [http://127.0.0.1:5173](http://127.0.0.1:5173) | Create a local email/password account |
| Original browser client and API | [http://localhost:18080](http://localhost:18080) | Same local Firebase account |
| Blueshift Admin | [http://localhost:13088](http://localhost:13088) | Paste the generated operator token |
| OpenAPI contract | [http://localhost:18080/openapi.json](http://localhost:18080/openapi.json) | Public |

Open `artifacts/admin.token.local.txt` locally in an editor to copy the token into Admin. Do not print or share it. Rerunning `./start.ps1` renews it. App email/password login and Admin operator authentication are separate.

Upload a synthetic PDF, PNG or JPEG, up to 20 MiB. Processing accepts at most 30 OCR pages. Expect an upload status transition followed by document titles, page ranges and Markdown. Use synthetic/public test material; sending private contract contents to a provider requires authorization.

For a full acceptance check, run this from another PowerShell 7 terminal at the repository root:

```powershell
./scripts/smoke.ps1
```

This calls the real Gemini OCR/LLM models with a synthetic three-page PDF and verifies sign-up/sign-in, upload acceptance, user isolation, two document ranges and Admin telemetry. It creates two emulator accounts and retained upload/log records, and removes only its temporary PDF. Read [the smoke guide](scripts/smoke.AGENTS.md) for details. A successful run prints `Verified Firebase sign-up and sign-in...`; a timeout or exception means the full workflow has not passed.

## 7. Development checks

These commands run from the repository root, with `.env` already configured:

```powershell
dotnet build AIESummit.slnx --nologo
node AIESummit.Api/ApiSpec/build.mjs
node --check AIESummit.Api/wwwroot/app.js
docker compose config -q
Push-Location web
try { npm run build } finally { Pop-Location }
```

NuGet packages restore during the .NET build; no separate NuGet CLI is needed. There is no dedicated application unit-test project yet. Framework compiler warnings and Lucide build warnings may appear; check the exit status and errors rather than assuming every warning is fatal. The optional UI review harness can be installed using [the UI workflow](docs/ui-workflow.md); Codex and that harness are not prerequisites for running the application.

## 8. Optional benchmark tools

Python is needed only for preparing datasets. Skip this section to test the app. Install Python 3.13 (the utility supports Python 3.12+):

```powershell
winget install --id Python.Python.3.13 --exact --source winget
```

Reopen PowerShell, then run from the repository root:

```powershell
py -3.13 --version
py -3.13 scripts/download_benchmarks.py
py -3.13 scripts/download_benchmarks.py --verify-only
```

If the launcher is unavailable, use `python` after verifying `python --version` is at least 3.12. The utility has no pip dependencies. Official installer alternative: [Python for Windows](https://www.python.org/downloads/windows/). Allow approximately 11.2 GB for downloads plus space for extraction; retain long-filename support. Payloads and generated reports remain local and ignored. See [the benchmark catalog](corporea/README.md) for provenance, licenses, split policy and PDF limitations. Dataset download is not part of application startup.

## 9. Stop and restart

Press Ctrl+C in the React terminal. From the repository root:

```powershell
docker compose down
```

This retains named volumes and their uploaded documents, accounts, configuration and logs. Do not add `--volumes`/`-v` for a routine stop. Start again with `./start.ps1`, then `npm run dev` from `web`. The schema uses `EnsureCreated`; schema upgrades need an explicit migration plan for retained data.

## Troubleshooting

| Symptom | What to check or do |
| --- | --- |
| Command not found after installation | Open a fresh PowerShell 7 terminal and verify PATH/version checks again. |
| SDK not found in the repository | Run `dotnet --list-sdks`; install 10.0.403 or a compatible newer stable 10.0 SDK selected by `global.json`. |
| Bundled dependency missing or checksum fails | Restore the tracked `dependencies/blueshift` files from the intended AIESummit revision; rerun `./scripts/verify-dependencies.ps1`. Private Blueshift access is not needed. |
| Missing `GEMINI_API_KEY` | Edit `.env`, save it, then rerun `docker compose config -q`. Never dump the full resolved configuration. |
| Build cannot resolve `api.nuget.org` or download packages/images | Check host and Docker DNS/proxy/VPN connectivity to NuGet, npm and container registries; once restored, rerun `./start.ps1`. Existing build caches are reused. |
| Port already in use | Check 5173, 18080, 13088 and 19099. A running Hackathon stack uses the same ports. Stop the known conflicting application deliberately or coordinate a port change; do not kill unrelated processes. |
| Worker repeatedly restarts with emulator `40901` conflict | Inspect bounded worker/emulator logs. On the first local run, restarting `emulator`, waiting for its `User defined entities created for SB Emulator` message, then restarting `worker` resolved this startup race. Emulator restart recreates queue state; pending eligible work is recovered from PostgreSQL by the worker's 45-second recovery cycle. Retain volumes and inspect any remaining failures. |
| Upload accepted but stuck/failed | Check `docker compose logs --tail 80 api worker` and Admin execution history. Verify persisted provider settings, model access, `gemini-gateway` health and worker readiness. Permanent failures need diagnosis, not repeated upload attempts. |
| Workspace loads but login fails | Verify `/firebase-config` through the workspace proxy, Firebase container state, matching demo project and Development/emulator settings. |
| Admin rejects a previously working token | Rerun `./start.ps1` and copy the renewed token from its local file. |

For the emulator recovery above, commands are `docker compose restart emulator`, inspect its readiness logs, then `docker compose restart worker`. Do not run the restart repeatedly while setup is still progressing. Review logs locally and redact credentials/document content before sharing them. For more detail, see [Startup.AGENTS.md](Startup.AGENTS.md), [deployment guidance](deploy/AGENTS.md) and [AGENTS.md](AGENTS.md).
