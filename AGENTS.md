# AIESummit repository guide

## Purpose and scope

AIESummit turns a user's PDF or image into ordered Markdown documents. Firebase identifies the user. PostgreSQL retains uploads, OCR pages, extracted images, prompts and document results. BlueShift agents call the configured OCR and language-model deployments. A separate BlueShift Admin service exposes configuration and operational telemetry to operators.

This is a .NET 10 application with Entity Framework Core and PostgreSQL. The SQL Server container supports the Service Bus emulator only. It is not the application database. The repository is not the Horus clinical system and does not implement its Patient, client tenancy, widgets, CLI or legacy hosts.

Use this file as the starting point. Before changing a folder, read its nearest `AGENTS.md` and the focused guides relevant to the change. Check the actual source when a guide and implementation disagree. Record the discrepancy and update the guide with the implementation change. Do not import a legacy convention merely because it appears in a reference repository.

## Documentation routes

The companion files below organize the repository guidance by task. A companion file named for a legacy interface explains the modern equivalent or a proposed extension, not necessarily an interface implemented by this application.

| Work | Start here | Focused companions |
| --- | --- | --- |
| Fresh Windows environment and dependencies | [Installation guide](install.md) | Docker checks, PowerShell 7, Git, .NET 10 SDK, Node.js 24, credentials, startup and optional Python |
| Entity model and Admin presentation | [Domain guide](AIESummit.Domain/AGENTS.md) | `DisplayMetadata.AGENTS.md`, `Relationships.AGENTS.md` |
| LLM subject and persistence | [Domain guide](AIESummit.Domain/AGENTS.md) | `IAmLLMCapable.AGENTS.md`, `Persistence.AGENTS.md` |
| Children, stable tags and files | [Domain guide](AIESummit.Domain/AGENTS.md) | `IHaveChildren.AGENTS.md`, `IHaveUniqueTag.AGENTS.md`, `Files.AGENTS.md` |
| Background processing | [Agents guide](AIESummit.Agents/AGENTS.md) | `OcrAgent.AGENTS.md`, `SplitStep.AGENTS.md`, `Metadata.AGENTS.md` |
| Recovery and observability | [Agents guide](AIESummit.Agents/AGENTS.md) | `QueueRecovery.AGENTS.md`, `Telemetry.AGENTS.md` |
| HTTP behavior and startup seeds | [API guide](AIESummit.Api/AGENTS.md) | `Authentication.AGENTS.md`, `UploadEndpoints.AGENTS.md`, `Bootstrap.AGENTS.md` |
| Client contract | [API contract guide](AIESummit.Api/ApiSpec/AGENTS.md) | [Components](AIESummit.Api/ApiSpec/Components/AGENTS.md), [Paths](AIESummit.Api/ApiSpec/Paths/AGENTS.md) |
| Browser workspace | [Browser guide](AIESummit.Api/wwwroot/AGENTS.md) | `Session.AGENTS.md` |
| AIESummit React workspace | [React guide](web/AGENTS.md) | [Design brief](docs/ui.md), [UI workflow](docs/ui-workflow.md) |
| Containers and environment | [Deployment guide](deploy/AGENTS.md) | `Admin.AGENTS.md`, `Worker.AGENTS.md`, `Firebase.AGENTS.md` |
| Starting and stopping locally | [Startup guide](Startup.AGENTS.md) | [README](README.md), [Compose](compose.yaml), [Startup script](start.ps1) |
| Verification | [Scripts guide](scripts/AGENTS.md) | [Smoke guide](scripts/smoke.AGENTS.md) |

Unlinked focused companion filenames in a row are relative to that row's directory guide. Read [the documentation plan](docs/documentation-plan.md) for the full ownership and source map.

## Repository map

| Path | Responsibility |
| --- | --- |
| `AIESummit.slnx` | Application projects plus BlueShift domain and agents library references |
| `AIESummit.Domain/` | One source file per entity, plus `AIESummitDb.cs` for table registration and EF mappings |
| `AIESummit.Agents/OcrAgent.cs` | OCR agent and API browser integration |
| `AIESummit.Agents/SplitStep.cs` | Code-defined split step, structured result, subject policies and agent pack services |
| `AIESummit.Agents/UploadQueueRecovery.cs` | Local queue recovery for eligible application work |
| `AIESummit.Agents/UploadExecutionContext.cs` | Prompt execution correlation to the root upload |
| `AIESummit.Api/Program.cs` | Firebase authentication, dependency registration, publishers and HTTP startup |
| `AIESummit.Api/Bootstrap.cs` | First-run schema creation and idempotent configuration seeds |
| `AIESummit.Api/UploadEndpoints.cs` | Upload validation, ownership checks and result projection |
| `AIESummit.Api/ApiSpec` | OpenAPI template and separate path/schema fragments |
| `AIESummit.Api/wwwroot` | Plain JavaScript/CSS workspace and generated `openapi.json` |
| `deploy` | Container builds, worker pack overlay and local service configuration |
| `scripts/smoke.ps1` | Synthetic end-to-end workflow against the local stack and live model deployments |
| `dependencies/blueshift` | Committed local NuGet packages, Linux worker/Admin hosts, Admin website, bootstrap helper and checksummed manifest |
| `NuGet.Config` | Bundled Blueshift package source plus public NuGet |
| `scripts/package-blueshift.ps1` | Maintainer-only refresh from a separate authorized source checkout |
| `scripts/verify-dependencies.ps1` | Verify bundled artifact hashes and sizes |
| `artifacts` | Ignored local outputs and generated Admin token |

The API references the agent project, which exposes pack configuration and the domain. Runtime worker and Admin hosts come from BlueShift rather than private application host projects. `deploy/Dockerfile` publishes only the application and packs, then extracts the prebuilt Linux hosts and Admin website from the committed bundle. A successful solution build alone does not validate every container target.

## Processing and persistence

1. The browser signs in through Firebase and sends its ID token to the app API.
2. `POST /api/v1/uploads` validates file size and magic bytes, derives `OwnerId` from the authenticated `sub`, saves `UploadedFile`, and returns HTTP 202 with a polling location. BlueShift persistence and publisher integration enqueue processing.
3. OCR stores ordered `OcrPage` rows and extracted BlueShift `Image` records connected through `PageImage`.
4. `LLMStepAgent<UploadedFile>` invokes the registered subject policy. That policy loads the actual upload and persisted prompts, then prepares ordered page JSON in the nonpersisted `SplitInput` property.
5. The code-defined `split-upload` step produces prompts. `LlmPromptAgent` runs the provider call and connects the structured `SplitResult` back to the subject.
6. `SplitResult.ConnectTo` accepts only contiguous, ordered, inclusive one-based ranges that cover all pages exactly once. It replaces documents after validating the full range set. Markdown preserves page boundaries with `<!-- page break -->`.
7. The browser polls the owner-scoped result endpoint. OpenSearch execution records let an operator trace OCR, step and prompt runs for the upload.

`UploadedFile` implements `Blueshift.Domain.LLM.ILlmCapable`. The implemented contract contains `LLMPrompts`, `LLMStepStatus` and `LLMJsonResult`. Do not substitute Horus's legacy `IAmLLMCapable` naming or assume its implementation is compatible.

The current upload does not implement `IHaveChildren` or `IHaveUniqueTag`. Companion guidance about those contracts is extension guidance. Current child relationships are explicit foreign keys. Upload/page and upload/first-page uniqueness protect page and document ordering. Framework configuration records and the split definition use stable identifiers or tags.

Preserve the distinction between `LLMPrompt.DocumentID`, which identifies a polymorphic processing subject, and the mapped `UploadedFile_ID` relationship that persists the upload's prompt collection. `SplitInput` is deliberately not mapped. `LLMPrompts` must remain mapped. `UsedModelID` may be absent before provider completion.

Keep calls to the framework base context when overriding persistence. `AIESummitDb.SaveChangesAsync` normalizes nullable prompt strings and then calls `base.SaveChangesAsync`. Bypassing that base path can skip the framework's publishing behavior.

## HTTP and browser boundaries

All `/api/v1` routes require an authenticated principal with `sub`. Lists filter by owner before projection. Individual upload requests return 404 for a different owner. Image reads require both the requested upload relationship and that upload's owner. A valid Firebase token alone does not authorize reading an arbitrary row.

Uploads accept PDF, PNG and JPEG signatures and a maximum of 20 MiB. The server derives MIME type from bytes rather than trusting the submitted content type. Names are reduced to a filename and bounded to 255 characters. Keep transport overhead distinct from the file limit when adjusting Kestrel configuration.

The API serves the original static browser client. The separate AIESummit React workspace in `web/` uses the same API and Firebase configuration. Keep authentication state in memory and preserve refresh coordination and stale-request protection when editing session behavior. Render uploaded names, model titles and Markdown as text. Introducing HTML or Markdown rendering requires deliberate handling of untrusted content. Revoke object URLs when images are cleared.

`/health`, `/firebase-config` and the static assets are publicly readable by design. Firebase web configuration is client configuration. Gemini API keys and the Admin bearer token are not client configuration and must never be returned there.

The contract source is `AIESummit.Api/ApiSpec/template.json` plus JSON fragments under `Paths` and `Components`. `build.mjs` merges them in stable filename order and rejects duplicate entries. It produces `wwwroot/openapi.json`. It does not generate endpoint implementations or C# DTO classes. Update contract fragments and actual endpoint behavior together.

## Local runtime and configuration

For a machine with only Docker Desktop installed, follow [install.md](install.md) before the operating instructions below. Keep that guide current whenever prerequisites, setup commands, SDK selection, configuration or first-run workflow changes.

Use PowerShell 7 and Docker for the local stack. Host-side build checks additionally need the .NET 10 SDK. Contract generation and JavaScript syntax checks need Node.js. Blueshift binaries are bundled in `dependencies/blueshift`; private framework access is not required.

Create `.env` locally from `.env.example` and provide a Google AI Studio `GEMINI_API_KEY`. Do not copy credential values into documentation or tool output. Then run from the repository root.

```powershell
./start.ps1
```

Startup verifies the checksums of the pinned binary bundle, generates local Admin authentication files and a four-hour operator token, and starts the Compose stack with a readiness wait. It prints service locations and the token file path, not the token. Re-running startup renews the local token.

| Service | Local entry point or role |
| --- | --- |
| `api` | Workspace at `http://localhost:18080`, OpenAPI at `/openapi.json` |
| `blueshift-admin` | Operator UI and proxied Admin API at `http://localhost:13088` |
| `firebase` | Authentication emulator at `127.0.0.1:19099` |
| `postgres` | Application and framework persistence |
| `sql`, `emulator` | SQL-backed local Service Bus emulator |
| `opensearch` | Agent execution telemetry |
| `worker` | Shared BlueShift worker with the AIESummit packs |
| `blueshift-admin-api` | Separate shared Admin host with the same packs and database |

Published ports bind to loopback. The local OpenSearch security plugin is disabled and the emulator credentials in Compose are development values. This Compose setup is not a production deployment specification. Keep those services private when changing topology.

The worker loads `/packs/AIESummit.Domain.dll` and `/packs/AIESummit.Agents.dll` through `deploy/worker.overlay.json`. The overlay names `AIESummit.Domain.AIESummitDb`, connection string `AIESummitDb`, venture prefix `aiesummit`, and the allowed upload/prompt queue pattern. Keep queue configuration, publisher names, agent definitions and pack loading consistent.

`deploy/Dockerfile` disables the shared worker's global initial enqueue sweep with `--enqueue-objects false`. The application enables `AIESUMMIT_QUEUE_RECOVERY=true` only in the worker. Recovery checks enabled OCR, step and prompt agents every 45 seconds through their existing pending-object enqueue API. It is recovery for eligible work, not a blanket reset of successful or permanently failed records.

For hosted Firebase, enable Email/Password in the intended project and configure `FIREBASE_EMULATOR=false`, `AIESUMMIT_API_ENVIRONMENT=Production`, `FIREBASE_PROJECT_ID` and `FIREBASE_API_KEY`. `Program.cs` enables unsigned emulator tokens only when the API environment is Development and emulator mode is enabled. Preserve both conditions.

`Bootstrap.InitializeAsync` calls `EnsureCreatedAsync`, then uses a PostgreSQL advisory transaction lock around seeds. Existing API connections, model and step records are preserved. Changing `.env` after initial creation does not rotate credentials already stored in `APIInfo`. Use Admin to update persisted configuration when rotating keys or changing deployments.

There is no application migration project or established migration workflow yet. `EnsureCreated` initializes an empty database and does not upgrade an existing schema. Before changing a populated database, plan and validate an explicit migration that preserves data. Do not delete volumes to make a schema problem disappear.

```powershell
docker compose down
```

Normal shutdown retains named volumes for PostgreSQL, queue infrastructure, Firebase, telemetry and Admin audit. Adding `--volumes` destroys persisted local data and is not a routine stop operation.

## Change workflow

1. Inspect `git status --short` and run `./scripts/verify-dependencies.ps1`. Preserve unrelated changes and the pinned Blueshift bundle.
2. Read the nearest guide and implementation entrypoints. Use the bundled package contracts and companion guides when a framework contract is involved. Upstream source citations are optional maintainer references.
3. Identify all affected layers. An entity change can affect EF mapping, Admin metadata, pack discovery, seed configuration, API projections, contract fragments and browser rendering.
4. Make the smallest coherent change. Use the existing framework extension points rather than adding a private processing loop or parallel host. Explain a new dependency's purpose before adding it to the implementation.
5. Update relevant guides and generated outputs. Label future designs explicitly and avoid presenting plans as current features.
6. Run checks appropriate to the affected layers. Report which commands passed, which were not run and any remaining runtime uncertainty.
7. Review the final diff for credentials, unrelated edits and bundled dependency changes. Include a concise explanation of resulting behavior and verification evidence.

Use `rg` or `rg --files` for bounded discovery. Place local diagnostic outputs under ignored `artifacts/`. Horus's suggested `temp/` convention is not the ignore rule here. Never commit `.env`, `deploy/*.local.*`, tokens or provider responses containing real user documents.

Blueshift is distributed here as compiled artifacts, not a Git submodule. Normal restore/build/startup requires no private framework access. Follow [the bundle guide](dependencies/blueshift/README.md) to refresh from a separate authorized source checkout; update packages, Linux hosts, frontend and manifest together. Do not replace a published package version with different bytes. Verify dependency hashes and test the solution, Docker targets and provider-backed smoke flow after a refresh. The `.gitattributes` rule preserves checksummed bytes across Windows checkouts.

## Verification commands

Run commands from the repository root. These are actual repository entrypoints, not claims that a particular change has already passed them.

```powershell
dotnet build AIESummit.slnx --nologo
./scripts/verify-dependencies.ps1
docker compose config -q
node AIESummit.Api/ApiSpec/build.mjs
node --check AIESummit.Api/wwwroot/app.js
git diff --check
```

The Compose validation requires the environment values referenced by `compose.yaml`. Keep it quiet with `-q` so resolved secrets are not dumped. Inspect the regenerated OpenAPI diff. Syntax and merge checks do not prove that endpoint behavior matches the contract or that the browser behaves correctly.

Do not inherit Horus's blanket warning suppression or disabled package auditing. Build output is useful evidence. Investigate failures and separate existing warnings from changes introduced by the work.

For container and integration changes, start or rebuild the stack with `./start.ps1`, then inspect readiness and run the end-to-end check.

```powershell
docker compose ps
Invoke-RestMethod http://localhost:18080/health
./scripts/smoke.ps1
```

The smoke test creates two local Firebase accounts and a temporary synthetic three-page PDF. It uses real configured OCR and LLM deployments, which may incur charges. It checks signup/signin, a 202 upload response, anonymous 401, another user's 404, owner-isolated lists, three OCR pages, two document ranges 1–2 and 3, preserved Markdown page breaks, and successful correlated agent runs. It also verifies anonymous Admin telemetry access is denied.

The fixture file is deleted in `finally`. The created local accounts, uploaded records and execution logs remain. The script is tied to local emulator ports and does not validate hosted Firebase. Do not run it against an unrelated environment by casually changing URLs.

There is no dedicated AIESummit unit-test project or application CLI in `AIESummit.slnx`. Do not report `dotnet test` with zero discovered tests as functional coverage. Add focused tests when changing meaningful logic and verify the full workflow when the change crosses authentication, persistence or agent boundaries. Browser layout, keyboard behavior and session-race interactions need browser checks beyond the smoke script.

## Troubleshooting and lessons to preserve

| Symptom or change | Evidence to inspect | Required invariant |
| --- | --- | --- |
| Upload remains queued after emulator restart | Worker logs, queue configuration, enabled agent records and recovery service | Recovery enqueues eligible pending work using framework APIs |
| Upload loses owner, MIME type or status after splitting | `SplitPolicy.Hydrate`, framework `LLMStepAgent.ShouldHydrate` | Load the full persisted subject, not the ID-only placeholder |
| Step creates no durable prompt work | `UploadedFile.LLMPrompts` mapping and framework save hooks | Persist prompt relationships and call base context save |
| Prompt insert fails before a provider run | `UsedModelID` optional mapping and normalized prompt strings | Do not require a used model before it has been chosen |
| List or detail projection fails at runtime | `UploadEndpoints.Summaries` and calling queries | Apply owner filters to entities before record projection |
| New environment settings appear ignored | Existing `APIInfo`, `LLMModel` and `LLMStep` records | Seeds initialize missing configuration without overwriting operator edits |
| Prompt succeeds but upload trace is incomplete | `UploadExecutionContext` and OpenSearch settings | Correlate `LLMPrompt.DocumentID` to the root `UploadedFile` |
| Model returns gaps, overlaps or invalid titles | `SplitResult.ConnectTo` | Validate every range before replacing documents |
| Admin sign-in fails after time has passed | Local token expiry and generated auth settings mount | Renew through startup and keep the token out of logs |

Use `docker compose logs --tail 100 api worker` for bounded runtime diagnostics, taking care before sharing logs. Check the persisted processing state and Admin execution history before forcing retries. An HTTP health response proves the API is serving, not that cloud credentials, queue processing or telemetry are healthy.

## Reference provenance and limits

This guide follows the hierarchical project routing and operating-guide structure of `../horus/AGENTS.md`. Horus remains inspiration for entity metadata, connected lists, LLM steps, split OpenAPI fragments and focused subsystem guides. Its .NET Framework hosts, EF6 conventions, SQL Server domain database, clinical tenancy and CLI instructions are not AIESummit requirements.

The current Cite8 reference is Erick's most recently authored branch head found after fetching origin, `origin/codex/certification-tier-checkout`, at commit `903626a9bb4ce471bccc28490b857886d6aa5c78` dated 2026-11-06 18:55 UTC. Its root `AGENTS.md` was read directly from that commit with `git -C ../cite8 show 903626a9bb4ce471bccc28490b857886d6aa5c78:AGENTS.md`, independently of the sibling checkout's active branch. This supersedes the earlier staging reference.

Cite8 confirms the modern shared Admin, EF Core/PostgreSQL domain pack and BlueShift worker arrangement. Its product App, CLI overlays, migration pipeline, authored skills, Stripe/Auth0 setup and issue-management workflow are Cite8-specific and are not implemented or mandated here. Recheck recent branch activity before a later reference refresh. Do not change AIESummit's dependency pin merely to match a reference branch.

Current local evidence is `README.md`, `AIESummit.slnx`, `compose.yaml`, the implementation paths listed above, `deploy/Dockerfile`, `deploy/worker.overlay.json`, `start.ps1`, `scripts/smoke.ps1`, `.gitignore`, and the bundled Blueshift package contracts. Features such as a standalone CLI, migration pipeline, generated C# API implementation, upload deletion workflow or richer rendered Markdown viewer require implementation work before they can be documented as available.

## Contract-analysis scope and benchmark guidance

The intake scaffold above was imported from the working tree of `../hackathon` (base commit `da68b8149fa1b825fb4b1735862c2d16dff81b39`), including its uncommitted React workspace and development harness, and renamed to AIESummit. Current processing performs OCR and document splitting. Contract clause extraction, risk interpretation and benchmark evaluation are future work. Extend the existing BlueShift LLM step/prompt policies for that work.

# Project guidance

## Purpose and technology
- Build a contract intake and analysis system for legal contract risk and clause auditing.
- Initial extraction targets: indemnity and liability limits, non-compete restrictions, renewal dates, and termination triggers in MSAs and NDAs.
- Use C# and .NET 10 (`net10.0`) for the application. The installed .NET 10 SDK is 10.0.403.
- Keep extraction evidence (document, page when available, and quoted text) separate from risk interpretations. Do not invent missing clauses or treat model output as legal advice.

## Keep this file current
- Read this file before changing the project.
- Update this file in the same change whenever architecture, commands, dependencies, project layout, benchmark policy, or development workflow changes.
- Before finishing work, check that these instructions and the README describe the actual repository. Record unfinished work honestly; do not describe planned features as implemented.

## Repository layout and commands
- Public GitHub repository: `https://github.com/10sirey123/AIESummit`; `origin` points to this repository.
- `README.md`: project status, setup, and contribution guidance.
- `corporea/`: benchmark datasets (spelling requested by the project owner).
- `corporea/sources.lock.json`: pinned upstream downloads and integrity metadata.
- `corporea/manifest.json`: generated local download and archive verification report.
- `corporea/README.md`: dataset provenance, format availability, and benchmark limitations.
- `scripts/download_benchmarks.py`: dependency-free Python 3.12+ bootstrap utility; Python is used only for dataset preparation, not the application stack.
- Download and verify: `python scripts/download_benchmarks.py`.
- Recheck local payloads without downloading: `python scripts/download_benchmarks.py --verify-only`.
- Check downloader recovery without external network access: `python -m unittest discover -s scripts -p test_download_benchmarks.py -v`. These checks use a local HTTP server to verify premature-response recovery and servers that ignore byte ranges.
- Application build: `dotnet build AIESummit.slnx --nologo`. See the inherited verification commands above; there is no dedicated application unit-test project yet.

## Benchmark policy
- Preserve original downloaded datasets and upstream train/dev/test splits. Keep transformations, OCR, model outputs, and evaluation results separate from raw files.
- CUAD is the initial annotated clause extraction and original-PDF benchmark. MAUD covers merger agreement understanding. EDGAR-CORPUS provides annual-report text for ingestion and domain stress testing, not gold clause-risk annotations.
- MAUD and EDGAR-CORPUS do not ship original contract/filing PDFs in these releases. Documentation PDFs do not count as input documents. Never label rendered text or synthetic PDFs as originals.
- Keep dataset provenance, licenses, versions, checksums, and limitations documented. Do not train or tune on evaluation splits. MAUD's upstream splits share contract names; preserve them for published-benchmark comparisons, but create a separately documented document-held-out protocol for unseen-contract evaluation. Prevent document overlap in that protocol.
- Raw downloads, extracted corpora, and generated reports are local and ignored by Git. Track download tooling, source locks, and dataset documentation.
- Preserve long original filenames; several CUAD paths exceed the legacy Windows 260-character limit. Use tools that support long paths when counting or opening these files.
- Do not place real private contracts, credentials, or personal data in Git. Do not send contract contents to an external service without authorization.
