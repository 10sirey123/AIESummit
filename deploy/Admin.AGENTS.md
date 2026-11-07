# Shared BlueShift Admin deployment

AIESummit uses the shared BlueShift Admin API and SPA from the pinned submodule. It does not have a private `AIESummit.Admin` host or a second implementation of catalog CRUD. The venture supplies domain and agent packs, database configuration, navigation and entity restrictions.

Read [AGENTS.md](AGENTS.md) for the wider container topology, [../AIESummit.Domain/AGENTS.md](../AIESummit.Domain/AGENTS.md) for entity registration, and [../AIESummit.Agents/Telemetry.AGENTS.md](../AIESummit.Agents/Telemetry.AGENTS.md) for execution evidence.

## Two Admin services, separate from the app API

| Compose service | Responsibility |
| --- | --- |
| `blueshift-admin` | Framework SPA served by nginx, published at `127.0.0.1:13088` |
| `blueshift-admin-api` | Framework ASP.NET Admin API on internal port 8080 |
| `api` | Separate Firebase-authenticated document app at `127.0.0.1:18080` |

The SPA nginx configuration proxies `/admin/`, `/health` and `/openapi/` to `http://blueshift-admin-api:8080`, preserving the request URI. Other paths fall back to the SPA's `index.html`. Docker DNS is resolved at request time through the configured variable upstream so replacing the API container does not permanently bind the proxy to an old address.

The Admin API has no directly published host port in current Compose. Use `http://localhost:13088` for the UI and its proxied API. A loaded SPA shell does not prove the backend or authentication is working. Conversely, a proxy error is not necessarily a bad token.

Firebase user credentials do not authorize Admin. The application workspace does not hold an Admin token, and Admin's operator access is not filtered to the currently signed-in Firebase user. Preserve this separation when adding links or embedding operational views.

## Pack and context configuration

The `admin` Docker target publishes `Blueshift.Admin.Api`, copies the built venture assemblies into `/packs`, and copies `deploy/admin.json` into `/app/appsettings.Development.local.json`. Compose sets the Admin API's environment to Development, so that file is loaded by the shared host.

| Compose key | Current value |
| --- | --- |
| `Admin__DomainAssemblyPath` | `/packs/AIESummit.Domain.dll` |
| `Admin__AgentAssemblyPath` | `/packs/AIESummit.Agents.dll` |
| `Admin__DbContextTypeName` | `AIESummit.Domain.AIESummitDb` |
| `Admin__ConnectionStringName` | `AIESummitDb` |
| `AdminAuth__SettingsFile` | `/config/admin-auth.json` |
| `AdminAuth__AuditFile` | `/audit/admin-audit.jsonl` |

The connection string comes from the shared application environment. The API, worker and Admin therefore use the same domain model and database. Admin's pack paths and context type must match the published assemblies, including case-sensitive filesystem paths inside Linux containers.

The shared host supports `Admin:OverlayFile`, but this deployment currently supplies its venture JSON through the Development local settings filename instead. Do not describe an unused overlay flag as the active loading path. If changing the Admin environment name, review that filename dependency as well as authentication behavior.

Environment variables and command-line configuration are added after local settings, and again after the optional auth settings file. Loaded JSON files use `reloadOnChange: false`. Editing mounted auth settings does not imply the running process has reloaded them.

## Local token bootstrap

Run `./start.ps1` from the repository root for the existing local setup. It calls the framework `bootstrap-admin-auth.ps1` with role `Admin`, subject `aiesummit-operator` and duration 240 minutes before starting Compose.

| Local file | Purpose |
| --- | --- |
| `deploy/admin-auth.local.json` | Private local issuer/audience/signing configuration, mounted read-only into Admin API |
| `artifacts/admin.token.local.txt` | Generated operator bearer token for local sign-in |

The startup script prints those locations, not token contents. Both locations are ignored by the repository's rules. Never paste signing keys or bearer values into guides, issues, tool output or shared screenshots.

On first creation, the bootstrap generates random signing material and an HS256 token. On later runs it reuses valid local signing settings and generates a new token. It refuses an existing settings file that lacks local signing material or specifies an authority. This helper is for local operator authentication, not a way to overwrite a hosted identity-provider configuration.

The token expires after four hours. Re-running the supported startup renews the local token. A newly written token does not extend an existing browser session automatically. Sign in with the renewed token when needed.

The bootstrap supports an optional development-passphrase flow, but `start.ps1` does not enable it. Do not invent a shared local password or instruct users to look for a passphrase file that this stack never creates. On Unix the helper restricts written file modes, while its Windows branch does not establish a new explicit ACL policy. Treat the files as private regardless of platform.

## Authentication and role boundaries

The pinned Admin security setup validates issuer, audience, expiration, signature and a nonblank subject. Local signing mode allows HS256 with sufficient random key material and cannot be combined with `Authority`. Its lifetime clock skew is zero, unlike the app API's Firebase tolerance.

| Policy | Framework role rule |
| --- | --- |
| Read | `Admin`, `Editor` or `Viewer` |
| Operate | `Admin` |
| Edit | `Admin`, or `Editor` for configured permitted entity types |

The local startup intentionally creates an Admin-role token. Entity-level restrictions still apply to CRUD and are not bypassed merely because the user has that role.

The framework supports explicit bearer calls and a browser session cookie. Its session endpoint can exchange a valid bearer token into an HttpOnly, SameSite Strict cookie scoped to `/admin/v1`, expiring with the token. Cookie-authenticated mutations require `X-Blueshift-Request: 1`. This is separate from the app workspace's memory-only Firebase session.

The Admin audit path is mounted on the named `audit` volume. The framework records mutation attempts/outcomes with safe actor and target identifiers rather than request bodies, tokens or exception messages. Preserve audit storage when restarting or rebuilding. A normal Compose shutdown retains it.

## Catalog, navigation and permissions

`deploy/admin.json` sets product name `AIESummit Admin` and accent color `#176256`. It explicitly allowlists application output and required processing/configuration types. An empty framework allowlist means all discovered persistence types are catalogued, so do not remove the list as a troubleshooting shortcut.

| Menu | Configured entries |
| --- | --- |
| Documents | `UploadedFile`, `OcrPage`, `Document`, `PageImage`, `Image` |
| Configuration | `APIInfo`, `LLMModel`, `LLMStep`, `OcrAgentConfig`, `LLMStepAgentConfig`, `LLMPromptAgentConfig` |
| Operations | `LLMPrompt` and the Agent Operations route `/agents` |

For `UploadedFile`, `OcrPage`, `Document`, `PageImage`, `Image` and `LLMPrompt`, the venture sets `CanCreate`, `CanUpdate` and `CanDelete` to false. These generated/input rows are read-only through generic Admin CRUD. Do not enable their editors merely to reset a processing state.

The configuration types have no equivalent false overrides, so their CRUD access follows the framework's role policy and defaults. Missing permission fields do not default to read-only. Changes to connections, models, step definitions and agent controls can affect processing and provider usage. Keep the distinction visible when explaining what an operator can edit.

CRUD restrictions do not disable every operational endpoint. Agent commands have their own Admin-role policy and behavior. A row's read-only editor is not proof that no operator action can enqueue work or affect processing. Inspect the specific route before offering a recovery action.

Admin catalog metadata is not end-user ownership authorization. `DisplayMetadata(IsHidden = true)` controls presentation, and configured sensitive-property handling provides separate redaction behavior for names such as `AppKey`, `AppSecret` and `FileData`. Neither should be represented as a universal guarantee that every raw property or log path is safe to expose.

New entities need deliberate EF registration, catalog allowlisting, navigation and access settings. A class appearing in the assembly alone is insufficient. Verify both the displayed capabilities and the corresponding API mutation denial when a type should be read-only.

## Operations and telemetry

Open Operations → Agent Operations to inspect the framework's `/agents` view. The configured Admin API uses the agent pack and OpenSearch settings from Compose. Runtime execution records are available through the protected `/admin/v1/agents/executions` route.

The local smoke queries upload runs with `entityType=UploadedFile` and `entityId`, then checks prompt runs correlated by `rootEntityId`. `UploadExecutionContext` establishes that root relationship for application prompts. Look for successful OCR, upload step and prompt execution evidence rather than treating a database row alone as proof that all stages ran.

Anonymous telemetry access must remain denied. The app API's `/health` and public static contract do not grant Admin access. Admin's own OpenAPI and health proxy paths are different from `http://localhost:18080/openapi.json`.

An empty operations view can reflect missing telemetry, a query filter, a stale pack, queue eligibility or no executions. Confirm database state and worker activity before diagnosing it as an authentication problem. A successful Admin login does not prove the worker is processing queues.

## Rebuild and troubleshooting

`deploy/admin.json` is copied into the image, not mounted live. Domain and agent assemblies are also published into it. Changes to any of those require rebuilding/recreating the Admin API image to test their effect. A source-file edit followed only by browser refresh can leave the old catalog and restrictions active.

The root startup remains the supported complete build/start path. For diagnosis, use bounded read-only checks.

```powershell
docker compose ps
docker compose logs --tail 100 blueshift-admin-api blueshift-admin
docker compose config -q
```

Keep resolved configuration and credentials out of logs and copied command output. Do not dump the mounted auth file to prove it exists.

| Symptom | Inspect |
| --- | --- |
| SPA loads, API calls return proxy errors | Admin API container readiness, service name and proxy route |
| Sign-in token is rejected | Expiration, correct token file, matching loaded signing settings and issuer/audience |
| New token still fails after auth settings were replaced | Whether the API process restarted with the current mounted settings |
| Entity is missing | Pack build/path, mapped EF type, allowlist and navigation configuration |
| Generated record cannot be edited | Intentional `EntityAccess` restrictions before assuming a role defect |
| Provider/model change seems ignored | Persisted configuration and operator edits, not just `.env` seeds |
| Telemetry is empty | OpenSearch availability, query filters, pack resolver and actual worker executions |

Do not delete signing settings as the first response to an expired token. Do not expose the Admin API publicly or disable authentication to work around a proxy or role problem. There is no need to add a private local Admin host project.

## Verification expectations

- Validate Compose quietly and build the relevant Docker targets when deployment or pack behavior changes. The application solution build alone does not publish every shared Admin host target.
- Sign in with the generated local token and verify the configured three menus and useful entity names.
- Verify read-only CRUD restrictions on output rows through both UI capabilities and protected API behavior.
- Verify configuration edits are available only to appropriate roles and survive application restart without seed overwrite.
- Confirm anonymous execution queries are denied and authenticated queries show correlated synthetic upload runs.
- Exercise expiration/renewal and logout when changing authentication setup. Do not claim those browser paths were tested merely because the bootstrap produced a token file.
- Preserve audit and application volumes during ordinary verification. Use synthetic inputs for any processing action that can call paid providers.

`scripts/smoke.ps1` verifies protected execution telemetry and a successful live processing chain. It does not comprehensively test catalog layout, every entity permission or Admin browser interactions. State those limits in handoff evidence.

## Sources and reference translation

Current deployment sources are [admin.json](admin.json), [Dockerfile](Dockerfile), [compose.yaml](../compose.yaml) and [start.ps1](../start.ps1). Framework contracts are [AdminOptions.cs](../external/blueshift/Blueshift.Admin.Api/Options/AdminOptions.cs), [AdminSecurity.cs](../external/blueshift/Blueshift.Admin.Api/Hosting/AdminSecurity.cs), [Program.cs](../external/blueshift/Blueshift.Admin.Api/Program.cs), [nginx.conf](../external/blueshift/Blueshift.Admin/nginx.conf) and [bootstrap-admin-auth.ps1](../external/blueshift/scripts/bootstrap-admin-auth.ps1).

The full Horus root guide at `../../horus/AGENTS.md` motivates a separate operator surface over the shared domain. Its ASP.NET Framework Admin and application-specific controller conventions are legacy. Cite8's root guide inspected at commit `903626a9bb4ce471bccc28490b857886d6aa5c78` on Erick's recent `origin/codex/certification-tier-checkout` confirms the modern central Admin/domain-pack pattern. Cite8-specific workspace bundles, skills and operator scripts are not AIESummit features.
