# Local deployment and container topology

Contract extraction adds the `documentanalysis` queue subject to the worker overlay and emulator, with native pending-work recovery. Admin exposes extraction catalog/history/evidence read-only. Tester MongoDB is not the application database or part of this Compose stack; see [contract extraction](../docs/contract-extraction.md).

Meta Muse Wonder uses the native page LLMStep and persisted APIInfo; see [Meta setup](../docs/meta-analysis.md) for version provenance, explicit key import, model selection and verification. The reader labels are provider-neutral.

Page-section analysis and the page reader are implemented. See [the current page-analysis guide](../docs/page-analysis.md) for the `OcrPage` LLM subject, Gemini APIInfo, additive schema upgrade, queues, evidence validation and Admin checks; the detailed split/OCR guidance below remains applicable to that original workflow.

Use this guide for the checked-in AIESummit Compose stack, image builds, and bounded diagnostics. Start from the repository root with [start.ps1](../start.ps1). Follow [Admin.AGENTS.md](Admin.AGENTS.md), [Worker.AGENTS.md](Worker.AGENTS.md), and [Firebase.AGENTS.md](Firebase.AGENTS.md) for the individual service contracts.

The Compose project is `aiesummit`, defined in [compose.yaml](../compose.yaml). This is one local development stack. It does not use Cite8's Compose includes, profiles, operator script names, frontend ports, or separate database initialization image.

## Services and network boundaries

| Service | Role | Host-accessible surface |
| --- | --- | --- |
| `postgres` | Application entities, configuration, source/image bytes | No published port |
| `sql` | SQL Server dependency of the Service Bus emulator | No published port |
| `emulator` | Local Service Bus queues | No published port |
| `opensearch` | Agent execution logs/history | No published port |
| `firebase` | Local Firebase authentication emulator | `127.0.0.1:19099` forwards to container 9099 |
| `api` | Upload API, static application, bootstrap | `127.0.0.1:18080` forwards to container 8080 |
| `worker` | Shared agent host with AIESummit domain/agent packs | No published port |
| `blueshift-admin-api` | Shared Admin API with venture packs/configuration | No directly published port |
| `blueshift-admin` | Admin static frontend and reverse proxy | `127.0.0.1:13088` forwards to container 80 |

Use `http://localhost:18080` for the application and `http://localhost:13088` for Admin. The application OpenAPI document is served at `/openapi.json` on the application origin. Admin requests use `/admin/v1` through the Admin frontend's same-origin proxy. The API health endpoint is `/health`.

Inside Compose, service names are DNS names. The application database host is `postgres`, Service Bus uses `emulator`, and agent logging uses `http://opensearch:9200`. `localhost` inside a container refers to that container, not the Windows host or another service.

The API and worker wait for OpenSearch to report yellow or green cluster health before starting. The API initializes its agent logging index during startup and can exit if OpenSearch has only started its process.

The Admin Nginx configuration proxies `/admin/`, `/health`, and `/openapi/` to `blueshift-admin-api:8080`, with Docker DNS resolution that accommodates an API container replacement. Other paths fall back to the Admin SPA. An Admin UI page loading does not prove its API requests are authorized or successful.

## Bootstrap entry point

Run the existing PowerShell entry point from the repository root.

```powershell
./start.ps1
```

It changes to the repository directory, verifies the bundled dependency checksums, creates local Admin authentication settings/token through the framework bootstrap script, and runs `docker compose up --build --detach --wait --wait-timeout 180`. It checks external-command exit codes and prints the application/Admin addresses and the token file location.

The generated Admin token has the Admin role, subject `aiesummit-operator`, and a requested 240-minute lifetime. Its file is `artifacts/admin.token.local.txt`. The settings file is `deploy/admin-auth.local.json`. Treat both as local authentication material. Do not print their contents or commit them.

The normal entry point therefore prepares prerequisites that a raw first-time `docker compose up` does not. In particular, the Admin API expects its local settings bind mount to exist. Use the companion Admin guide when authentication setup or expiration is the issue.

The application requires Foundry values supplied through the intended local environment setup. [.env.example](../.env.example) documents variable names and safe defaults. Do not expose a real `.env` file in diagnostic output or put its secret values into this guide.

## Build stages and packed binaries

[Dockerfile](Dockerfile) has a shared build stage using `mcr.microsoft.com/dotnet/sdk:10.0.403`. It copies the application projects, `NuGet.Config`, `global.json` and bundled NuGet packages, then publishes only the application and agent packs. Framework hosts and the Admin website are prebuilt archives from `dependencies/blueshift`.

| Publish output | Purpose |
| --- | --- |
| `/out/api` | Published `AIESummit.Api` and dependencies |
| `/out/packs` | Published `AIESummit.Agents` and its domain/framework dependencies |
| `dependencies/blueshift/worker-linux-x64.tar.gz` | Prebuilt shared `Blueshift.Agents.Worker` host |
| `dependencies/blueshift/admin-api-linux-x64.tar.gz` | Prebuilt shared `Blueshift.Admin.Api` host |

The runtime base is `mcr.microsoft.com/dotnet/aspnet:10.0`, sets `ASPNETCORE_URLS=http://+:8080`, and installs `libgssapi-krb5-2` and `curl`. The `api`, `worker`, and `admin` target stages copy their published output into `/app`.

The worker and Admin also copy the published venture pack to `/packs`. The worker copies `worker.overlay.json` into `/config/worker.overlay.json`. The Admin target copies `admin.json` into `/app/appsettings.Development.local.json`.

These are image-contained binaries and overlays. There is no host DLL bind mount or live source-code mount for the application services. Editing or rebuilding a host DLL does not change a running container's pack. Rebuild the relevant images and recreate their containers to use new code or baked-in overlays.

The separate Admin frontend image uses the `admin-web` target in `deploy/Dockerfile`, extracting `dependencies/blueshift/admin-web.tar.gz` into Nginx with the bundled proxy configuration. The Firebase image builds from `Firebase.Dockerfile` in this directory, installs the pinned Firebase tools version, and copies `firebase.json`.

## Framework and image versions

The committed `dependencies/blueshift/manifest.json` pins the `net10` source revision, package version, build images, artifact sizes and hashes. Normal builds use no framework source or private feed. See [the bundle guide](../dependencies/blueshift/README.md) for the maintainer-only refresh workflow and Linux x64 host limitation. Update and validate all artifacts together when upgrading.

Compose currently names PostgreSQL 16, Service Bus emulator 2.0.1, OpenSearch 2.18.0, and SQL Server 2022's `latest` tag. Firebase uses Node 22 Alpine and Firebase tools 14.12.1. A version tag is not a digest pin. Do not describe every upstream base image as immutable merely because venture binaries are packed into images.

## Build context hygiene

The root [.dockerignore](../.dockerignore) excludes Git metadata, `.env` and `.env.*`, build output folders, `node_modules`, `artifacts`, and `deploy/*.local.*`. The local Admin settings are therefore supplied at runtime, not copied into the main image build.

Preserve those exclusions when changing build contexts. A separate context, such as the Firebase or Admin frontend directory, has its own applicable ignore boundary. Do not assume the repository-root ignore file automatically governs every other context.

The .NET publish commands use a BuildKit cache mount for NuGet packages. That cache accelerates builds but does not replace rebuilding after source changes. Do not routinely disable caches or delete volumes to diagnose a stale application container.

## Shared and service-specific configuration

The shared environment block supplies application database connectivity, environment `dev`, queue enablement/venture prefix, the local emulator connection, and OpenSearch logging settings. API, worker, and Admin API inherit this block.

The API receives Foundry endpoint/key, OCR/LLM model defaults, Firebase settings, and its ASP.NET environment selection. `OCR_ENDPOINT` and `LLM_ENDPOINT` are supported in application bootstrap code but are not forwarded in this checked-in Compose block. Adding them to a local interpolation file alone does not inject them into the API container.

The worker enables `AIESUMMIT_QUEUE_RECOVERY=true`. Its entry point loads `/config/worker.overlay.json` and passes `--enqueue-objects false`. The overlay specifies the domain/agent pack paths, `AIESummit.Domain.AIESummitDb`, the `AIESummitDb` connection name, venture prefix `aiesummit`, and the upload/prompt queue selection regex. Use [Worker.AGENTS.md](Worker.AGENTS.md) before changing queue scope or recovery behavior.

The Admin API receives explicit pack/context settings, its authentication/audit file paths, and `ASPNETCORE_ENVIRONMENT=Development`. This matches the baked-in Development local overlay. Changing the host environment requires checking how that overlay is loaded, rather than assuming its branding, navigation, and access restrictions still apply.

Bootstrap inserts missing database configuration and preserves existing credentials, models, and steps. Changing container environment defaults is not automatic rotation or synchronization of existing records. Use [../AIESummit.Api/Bootstrap.AGENTS.md](../AIESummit.Api/Bootstrap.AGENTS.md) for that boundary.

## Startup dependencies and readiness

PostgreSQL has a `pg_isready` health check. The API waits for PostgreSQL health and has an HTTP `/health` probe. Its startup performs database/bootstrap work before serving requests.

The worker waits for API health and for the emulator/OpenSearch services to be started. It has `restart: on-failure`. Started is weaker than ready. The emulator depends on SQL Server startup and has its own configured wait interval, but Compose does not establish a SQL health condition here.

The Admin API waits for API health. The frontend waits for the Admin API to start. Firebase has no dependency ordering that proves authentication requests are ready when another service starts. Services without explicit health checks can be merely running when Compose's wait completes.

Successful `up --wait` is therefore a useful startup signal, not an end-to-end proof of Firebase authentication, queue delivery, OCR provider access, model deployment, document splitting, or retained telemetry. Follow with the existing controlled smoke workflow when the task requires functional verification.

## Retained data and ordinary stops

| Named volume | Retained data |
| --- | --- |
| `database` | PostgreSQL application records, configuration, source/image bytes |
| `queue-db` | Emulator SQL Server data |
| `firebase` | Auth emulator import/export directory |
| `telemetry` | OpenSearch index data |
| `audit` | Admin audit output |

Normal container restarts, rebuilds, and `docker compose down` without volume removal retain named volumes. Do not append volume-removal flags to routine shutdown or troubleshooting commands. A reset can erase user uploads, credentials/configuration, emulator users, telemetry, and audit history.

Firebase is configured to import from `/data` and export there on exit. Abrupt termination can differ from a graceful export. Similarly, retained emulator SQL files do not guarantee queue contents survive every emulator restart. The worker's recovery path exists for lost pending work and should be understood separately from volume retention.

## Rebuild and bounded diagnostics

For normal full startup/rebuild, use `./start.ps1`. For an already initialized stack, targeted commands below run from the repository root and preserve volumes.

```powershell
docker compose up --build --detach api worker blueshift-admin-api
docker compose up --build --detach blueshift-admin
docker compose up --build --detach firebase
```

The first command is appropriate when a domain/agent change needs consistent API, worker, and Admin packs. An API-only change may only require rebuilding `api`. Worker/Admin overlay changes require rebuilding the corresponding image because those files are copied into it. The authentication settings and servicebus configuration are runtime bind mounts, with reload/restart behavior determined by the consuming service.

Use bounded read-only diagnostics before restarting anything.

```powershell
docker compose ps
docker compose logs --tail 80 --since 10m api worker
docker compose logs --tail 80 --since 10m blueshift-admin-api
docker compose logs --tail 60 --since 10m emulator opensearch
Invoke-RestMethod http://localhost:18080/health
```

Review log output before sharing it, and report only necessary identifiers, states, and sanitized errors. Avoid printing fully resolved `docker compose config`, unrestricted container environment inspection, `.env`, or authentication files, because those can expose credentials.

If a service exits, inspect its bounded logs and dependency state. If an image built but behavior is stale, confirm the intended service was recreated and uses the expected pack. If API health passes but processing does not, inspect queue delivery, persisted configuration, and the worker's execution history through the companion guides.

Do not run broad pruning commands, delete volumes, change unrelated containers, or expose internal service ports as a routine diagnostic step. Prefer a concrete fix for the observed dependency or configuration issue.

## Local development versus hosted deployment

Loopback-only published ports, demo Firebase settings, local database/emulator credentials, disabled OpenSearch security, and Development Admin configuration belong to this local stack. They are not a hosted deployment baseline.

A hosted configuration needs an intentional authentication/provider setup, protected service networking, persistent data/backup policy, secret distribution, TLS/ingress, and an appropriate queue/logging service choice. Follow the Firebase companion for real token validation and the Admin companion for operator access. Do not merely change `127.0.0.1` to a public bind address.

The application still sends requests to configured external OCR/LLM providers. Running the rest locally does not make those provider calls offline or remove their credentials and cost requirements.

## Verification and reference boundary

After a deployment change, verify the affected images build, expected containers are running/healthy where checks exist, local routes respond, and the specific modified configuration is effective. Run [scripts/smoke.ps1](../scripts/smoke.ps1) when end-to-end provider-backed processing is required and its prerequisites are satisfied. It does not replace browser checks or every startup/failure-path test.

This guide draws structural lessons from the complete Horus Containers guide and Cite8's `deploy/AGENTS.md` at commit `903626a9bb4ce471bccc28490b857886d6aa5c78`, the supplied Erick reference. Cite8's packed-build approach informed the distinction between image contents and live host mounts. Its ports, scripts, profiles, initialization strategy, and optional services were not transplanted into AIESummit instructions.

Keep venture deployment changes in these files and the root Compose/startup configuration. Shared framework files are references unless a framework change is explicitly part of the task.

## Configuration seeding

See [the implemented seeding workflow](../docs/configuration-seeding.md). `AIESummit.Configuration` exports the existing step definitions; the bundled Blueshift CLI performs validation, read-only planning and previewed transactional application. `SeedIdentities` and the idempotent `SeedSchema` upgrade support registry provenance. Default exports preserve model/enabled selections and all credentials. No publishers or provider calls run in the seed host. The one-off Docker configuration image and isolated PostgreSQL tests are documented there.
