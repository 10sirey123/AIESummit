# Starting and inspecting the local stack

For first-time machine setup, start with [install.md](install.md). It covers dependency installation and verification from a Windows environment with only Docker Desktop installed.

The supported local entrypoint is [start.ps1](start.ps1). It verifies the committed framework binary bundle, prepares temporary Admin authentication and starts the Compose stack. Read [deploy/AGENTS.md](deploy/AGENTS.md) for service architecture and [scripts/smoke.AGENTS.md](scripts/smoke.AGENTS.md) before end-to-end verification.

## Prerequisites

- PowerShell 7 and the committed `dependencies/blueshift` bundle must be available. Git is needed to clone AIESummit; no Blueshift repository access is required.
- Docker must be running with Linux-container support, working Compose support for `--wait`, sufficient memory and access to the configured image registries.
- Populate a local `.env` using the keys in [.env.example](.env.example). A `GEMINI_API_KEY` must be available for API startup and live processing.
- Ensure local ports 18080, 13088 and 19099 are available for the application, Admin and Firebase Auth emulator.
- Use the repository's existing dependency revision. Do not regenerate dependency artifacts as an incidental startup repair.

The Docker build publishes .NET projects inside its SDK image. Use local .NET for host-side .NET builds and Node for OpenAPI checks and the separate [AIESummit React workspace](docs/ui-workflow.md).

Do not print `.env` contents while checking prerequisites. Check that required values are configured without exposing their values in logs or chat. The example file documents variable names and defaults, not usable provider secrets.

## Exact startup sequence

From the repository root, run `./start.ps1` in PowerShell. The script uses `Push-Location $PSScriptRoot` so its relative paths resolve against the repository, and always calls `Pop-Location` in `finally` to restore the caller's directory.

It performs these operations in order.

1. Runs `scripts/verify-dependencies.ps1` and throws if a bundled artifact is missing or its checksum/size differs from the manifest.
2. Invokes `dependencies/blueshift/bootstrap-admin-auth.ps1` with settings path `deploy/admin-auth.local.json` and token path `artifacts/admin.token.local.txt`.
3. Requests Admin role, subject `aiesummit-operator` and a 240-minute token lifetime from that helper.
4. Runs `docker compose up --build --detach --wait --wait-timeout 180`.
5. Throws if Compose returns failure, otherwise prints the application/Admin URLs and the token file location.

The script has `$ErrorActionPreference = 'Stop'`. Its auth helper invocation does not have a separate `$LASTEXITCODE` check like the native Compose call, so investigate helper output and terminating errors when credentials are not produced. Do not assume the script has independently validated every generated auth artifact.

The 180-second option bounds Compose's readiness wait. It is not a guarantee that image builds, provider processing or the whole invocation finish within three minutes. Image downloads and builds can take longer.

## Local surfaces and authentication

| Surface | Address | Access |
| --- | --- | --- |
| Browser application/API | `http://localhost:18080` | Public shell, Firebase login for upload operations |
| Central Admin UI | `http://localhost:13088` | Operator token created by the bootstrap helper |
| Firebase Auth emulator | `http://localhost:19099` | Local development identity service |

Compose publishes these services on loopback. The application PostgreSQL, Service Bus emulator, emulator SQL Server, OpenSearch and Admin API communicate over the Compose network without corresponding public ports in current configuration.

The generated operator token is stored in `artifacts/admin.token.local.txt`. Handle it as a credential. Do not paste it into documentation or include it in command output shared for diagnostics. Its requested lifetime is 240 minutes, so a previously working Admin session can expire independently of container health.

Firebase app login and Admin operator login are separate systems. An Admin token does not replace a Firebase ID token on `/api/v1` routes. See [deploy/Firebase.AGENTS.md](deploy/Firebase.AGENTS.md) and [deploy/Admin.AGENTS.md](deploy/Admin.AGENTS.md) for their own configuration paths.

## Configuration versus persisted state

Compose reads `.env` for substitutions such as the Gemini key, provider model defaults and Firebase mode. API bootstrap requires the Gemini key, creates a missing schema and seeds absent configuration rows.

Existing APIInfo, LLM model and step rows are preserved. Editing `.env` and restarting does not rotate a stored APIInfo credential or replace an existing model binding. Similarly, changing `SplitStep.Definition()` does not automatically overwrite the stored step template. Inspect effective persisted configuration before concluding that a new default is active.

Current local defaults select Firebase emulation with Development mode. Hosted Firebase operation requires coordinated server/browser configuration. Do not carry the unsigned emulator-token validation branch into a hosted environment to work around a login failure.

The worker receives application connection and queue settings from Compose, then loads the copied overlay. Queue environment `dev` is a separate setting from the ASP.NET host environment. Read [deploy/Worker.AGENTS.md](deploy/Worker.AGENTS.md) before changing queue naming or pack paths.

## Build freshness and mounted files

The API, worker packs, worker overlay and Admin application configuration are copied into their images by [deploy/Dockerfile](deploy/Dockerfile). They are not live source-code mounts. A host edit does not change running assemblies until the relevant image is rebuilt and container recreated.

The emulator configuration is a read-only bind mount from `deploy/servicebus.json`. Admin auth settings are mounted from `deploy/admin-auth.local.json`. Their source-file lifecycle differs from copied image content. Inspect the actual mount and service configuration when diagnosing stale behavior.

Do not infer a stale bind mount, image-cache defect or framework bug solely because behavior did not change. First establish which image/container is running, whether the changed file is copied or mounted, and whether the active setting is actually stored in PostgreSQL. Many configuration changes intentionally preserve old persisted rows.

Use `docker compose build worker` followed by `docker compose up -d worker` for a scoped worker rebuild when appropriate, or rerun the supported startup script for a full build/start cycle. Avoid clearing build caches or data volumes as the first troubleshooting step.

## Readiness and success criteria

PostgreSQL has a readiness check. The API waits for healthy PostgreSQL and has an HTTP `/health` check. API bootstrap runs before the host begins serving, so a healthy API is useful evidence that its startup reached serving state.

The API waits for healthy PostgreSQL and OpenSearch. The worker waits for healthy API and OpenSearch and a started Service Bus emulator. The OpenSearch check waits for yellow or green cluster health so the API can initialize its logging index on a cold start. The worker's `restart: on-failure` can recover some startup races, but repeated restart behavior also needs investigation for invalid configuration or missing packs.

Compose success does not prove a real OCR/LLM request completed, that the right stored model is selected or that account isolation holds. The browser may load while processing is unavailable. Confirm a known upload's persisted pages, prompt status and final documents through the smoke workflow before claiming end-to-end success.

The worker entrypoint disables initial host catch-up using `--enqueue-objects false`, while Compose enables the application's 45-second periodic recovery service. After queue recreation, eligible persisted work may wait for that recovery tick. Permanent failures are not automatically repaired by this mechanism.

## Safe diagnostics

Use bounded read-only commands before changing state.

```powershell
docker compose ps
docker compose logs --tail 100 api worker
docker compose logs --tail 100 emulator sql
docker compose logs --tail 100 blueshift-admin-api
```

Review output before sharing it. Logs can still contain identifiers or operational detail even when application code avoids credential dumps. Do not paste resolved Compose configuration or full environment listings because substitutions can reveal provider keys and connection strings.

Diagnose by the failing boundary.

| Symptom | First evidence to inspect |
| --- | --- |
| Dependency verification fails | Restore the intended tracked bundle and manifest; run `./scripts/verify-dependencies.ps1` |
| Build fails | First relevant publish/compiler error and whether dependency source exists |
| API exits at startup | Required configuration, PostgreSQL readiness and bootstrap error |
| Browser opens but login fails | Firebase mode/project alignment and emulator reachability |
| Upload remains queued | Publisher settings, persisted status, worker health and broker queue configuration |
| OCR or split fails | Effective APIInfo/model/step configuration and correlated worker evidence |
| Admin rejects login | Auth settings file, token lifetime and correct operator-token use |
| Compose wait times out | Per-service health and logs, without assuming all services failed |

A timeout is diagnostic evidence, not permission to erase state. Some containers can remain running after a failed wait. Inspect them before retrying or stopping services.

## Stop, restart and retained data

Use `docker compose stop` to stop the existing containers while retaining them and their volumes. `docker compose start` starts those existing containers without rebuilding images. Use `docker compose up -d` when configuration or service creation needs reconciliation.

`docker compose down` removes stack containers and network while retaining named volumes by default. Do not append `--volumes` or `-v` as routine cleanup. Current named volumes retain application data, emulator backing data, Firebase accounts, telemetry and Admin audit data.

The application database uses `EnsureCreatedAsync`, not an automatic migration runner. A schema mismatch after a code change needs a deliberate transition. Resetting PostgreSQL to make startup succeed discards user data and does not verify an upgrade path.

The emulator can recreate queue state independently of retained application records. This is the reason for recovery from persisted eligibility. Do not confuse an empty queue with an empty application database or assume an emulator volume guarantees message retention.

## Ignore rules and secret handling

[.gitignore](.gitignore) excludes `.env`, `.env.*` except `.env.example`, build output, node modules, `artifacts` and `deploy/*.local.*`. [.dockerignore](.dockerignore) excludes Git metadata, environment files, host build output, node modules, artifacts and local deployment files from image build context.

Ignored files are still local sensitive files. The Admin auth settings are intentionally supplied by a runtime mount rather than baked into the image. Verify that new credential artifacts remain excluded from both version control and unintended build-context copying.

Do not inspect or reproduce provider secrets merely to write startup guidance. Report required variable names, missing configuration categories and relevant error types. Keep real values in the existing local configuration channel.

## Verification and reference projects

After startup, inspect service status, open the browser and Admin surfaces, then follow [scripts/smoke.AGENTS.md](scripts/smoke.AGENTS.md) for the authorized full-flow check. Read that guide before running provider-consuming acceptance. A documentation-only edit needs no live OCR or model call.

The full Horus `../horus/Voiston.AgentHostCore/build_to_temp.AGENTS.md` describes temporary publish folders and split worker processes. This application uses copied Docker packs and the shared worker. Its `build_to_temp.sh`, legacy `AgentHostCore.dll`, broad process-kill examples and old queue-filter configuration are not local commands to reuse.

If a future task requires isolated worker experiments, establish exact process/container ownership and queue boundaries first. Avoid running competing untracked receivers while validating an individual record. Current authority is [start.ps1](start.ps1), [compose.yaml](compose.yaml), [deploy/Dockerfile](deploy/Dockerfile), [.env.example](.env.example), [.gitignore](.gitignore) and [.dockerignore](.dockerignore).
