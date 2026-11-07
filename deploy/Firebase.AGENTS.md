# Firebase authentication configuration

Read this guide when configuring local sign-in, diagnosing token rejection, or switching the application to hosted Firebase authentication. The application uses Firebase REST calls in the browser and JWT bearer validation in the API. Admin authentication is separate and covered in [Admin.AGENTS.md](Admin.AGENTS.md).

The current implementation is defined by [firebase.json](firebase.json), [Firebase.Dockerfile](Firebase.Dockerfile), [compose.yaml](../compose.yaml), [Program.cs](../AIESummit.Api/Program.cs), and [app.js](../AIESummit.Api/wwwroot/app.js). Do not infer that a Firebase SDK, Admin SDK, service-account file, or Horus JWT configuration section is installed merely because the application uses Firebase identity.

## Local emulator container

The Firebase image uses `node:22-alpine` and installs `firebase-tools@14.12.1` globally. It copies `firebase.json` into `/app`. The Dockerfile-specific [ignore file](Firebase.Dockerfile.dockerignore) excludes every context entry except `firebase.json`, keeping unrelated deployment files out of this build context.

The image's default command starts only the authentication emulator for project `demo-aiesummit`. Compose supplies the effective command, adding import/export behavior.

```text
firebase emulators:start --only auth --project demo-aiesummit --import=/data --export-on-exit=/data
```

No Java runtime is installed in this Dockerfile. This is an auth-only container. Do not copy instructions for a Java-dependent emulator into this image or expand `--only auth` without reviewing the additional services' runtime requirements and image dependencies.

`firebase.json` binds the auth emulator to `0.0.0.0:9099` inside the container, disables the emulator UI, and enables single-project mode. Compose publishes only `127.0.0.1:19099:9099` on the host. An absent graphical emulator UI is expected configuration, not evidence of a startup failure.

The container listens on all interfaces within its network namespace so the published port can reach it. That does not make the host binding public. Preserve the loopback host binding for this development setup.

## Project and port consistency

| Location | Local value or meaning |
| --- | --- |
| Emulator command project | `demo-aiesummit` |
| Compose default `FIREBASE_PROJECT_ID` | `demo-aiesummit` |
| Compose default `FIREBASE_API_KEY` | `demo-key` |
| Container auth port | 9099 |
| Host auth port | 19099 |
| `/firebase-config` default emulator port | `19099` |

The local project ID must agree across emulator-issued tokens and API audience/issuer validation. Changing `FIREBASE_PROJECT_ID` alone does not change the emulator command's fixed project argument. Keep the effective values aligned when deliberately changing the local project.

The API reads `Firebase:EmulatorPort` when producing browser configuration, but the checked-in Compose environment does not explicitly forward a custom value. A host-port change therefore needs coordinated configuration rather than only changing the Compose port mapping.

## Browser routing and session handling

On page load, the browser fetches `/firebase-config` from the application origin. That response contains project ID, web API key, effective emulator mode, and emulator port. It does not contain a service-account credential, password, provider key, or Admin token.

When emulator mode is enabled, the browser constructs its auth base from `http://`, `location.hostname`, and the configured emulator port. Sign-up/sign-in calls use the emulator's `identitytoolkit.googleapis.com` path. Refresh calls use its `securetoken.googleapis.com` path.

`location.hostname` is intentional browser-side routing. It does not resolve Docker's `firebase` service name. A browser using `localhost:18080` contacts `localhost:19099`. If the app is opened under another hostname, that hostname must resolve to the machine exposing the auth port. A remote device cannot reach the development emulator through a host port bound only to loopback.

The emulator URL is explicitly HTTP. Serving the application over HTTPS while leaving emulator auth on HTTP can create a browser mixed-content problem. Do not solve a hosted sign-in problem by enabling emulator mode or broadly exposing its port.

The browser posts email and password with `returnSecureToken=true`, then retains the resulting session only in memory. It sends the ID token as `Authorization: Bearer ...` to the upload API. It refreshes an expiring token through the selected token endpoint, coalescing concurrent refresh requests and checking that the session has not changed before applying the result.

The stored expiry is shortened by 60 seconds to refresh before expiration. Sign-out clears the in-memory session, upload/document UI state, request sequence, and browser image object URLs. It does not delete the Firebase account or erase persisted uploads. Reloading the page loses the in-memory application session and requires sign-in again.

## API emulator guard and token validation

The API's effective emulator flag is the conjunction of two conditions.

```csharp
builder.Environment.IsDevelopment()
    && config.GetValue<bool>("Firebase:Emulator")
```

Setting `FIREBASE_EMULATOR=true` in Compose only enables emulator acceptance when the API environment is also Development. Outside Development, the effective flag is false and the API uses hosted authority validation. The `/firebase-config` response exposes that effective flag, so browser routing follows the same decision.

In either mode, the API sets audience to the configured project ID and issuer to `https://securetoken.google.com/{projectId}`. Claim remapping is disabled, error details are suppressed, and clock skew is 30 seconds. The default authorization policy requires an authenticated caller with a `sub` claim.

In emulator mode, signature requirements are relaxed specifically for an unsigned JWT whose algorithm is `none`. The custom validator rejects a signed token or a different algorithm. It does not turn the API into an accept-any-token endpoint. Project, issuer, subject, and the remaining configured validation still matter.

Outside emulator mode, `options.Authority` points at the Firebase secure-token issuer. Hosted tokens must satisfy the normal bearer validation path. Do not remove issuer/audience checks, enable unsigned acceptance globally, or weaken the Development guard to work around a mismatch.

`Firebase:ProjectId` is required by a null check. `Firebase:ApiKey` is also required outside emulator mode, while emulator mode permits a missing key to fall back to `demo-key`. These source checks do not perform a complete nonblank or hosted-project configuration validation.

## Hosted Firebase selection

For the current Compose configuration, switching the application deliberately to hosted Firebase requires setting all four relevant selections consistently.

```text
FIREBASE_EMULATOR=false
FIREBASE_PROJECT_ID=<hosted-project-id>
FIREBASE_API_KEY=<hosted-project-web-api-key>
AIESUMMIT_API_ENVIRONMENT=Production
```

The placeholders above are illustrative, not real configuration. Supply values through the authorized environment/secret workflow and recreate the API container so it receives them. Preserve the unrelated required database, queue, and OCR/LLM settings.

The chosen Firebase project must have the application's email/password sign-in method enabled through the provider's configuration. This repository does not create the hosted Firebase project, enable providers, configure all hosted application/domain policies, or migrate users. Treat those as external setup work and verify them in the intended project when performing a hosted rollout.

With emulator mode off, the browser uses Google's hosted Identity Toolkit and Secure Token HTTPS endpoints. A demo API key or local emulator account will not become a hosted identity merely because the API environment changed.

The Firebase service remains part of this Compose file even if the application is configured to use hosted authentication. Its presence alone does not determine the application's effective auth mode. Check `/firebase-config` and API settings instead of assuming every running stack uses local accounts.

Switching Firebase project or emulator mode does not migrate application ownership. `UploadedFile.OwnerId` stores the subject from the token that created it. Existing uploads remain associated with those stored subject IDs. A newly created account with the same email is not guaranteed to have the same subject. Do not rewrite ownership IDs as an incidental configuration fix.

## Account persistence and restart behavior

Compose mounts the named `firebase` volume at `/data` and starts the emulator with import and export-on-exit options. Normal named-volume retention allows local emulator data to be reimported after a later start when it has been exported successfully.

Export-on-exit depends on the process receiving and completing its shutdown handling. Abrupt termination, host failure, or a failed export can lose changes made since the last persisted export. A volume existing on disk does not prove every in-memory account change was saved.

Ordinary container rebuilds should preserve that volume. Do not remove it to fix a password error or token mismatch. Removing the volume is an account-data reset and can strand application records whose owner IDs refer to old accounts.

The emulator's local accounts are distinct from hosted Firebase accounts. There is no automatic account migration or synchronization in this code. Password recovery, account administration, and production account lifecycle are not implemented by this local startup script.

## Bounded troubleshooting

First determine whether the failure occurs while fetching browser configuration, authenticating with Firebase, refreshing a token, or calling the upload API. Each has a different source of evidence.

| Symptom | First checks |
| --- | --- |
| Sign-in cannot reach the local endpoint | Firebase container state, host port 19099, browser hostname, and effective emulator flag |
| Browser contacts hosted endpoints unexpectedly | API environment and Development-plus-emulator guard |
| Local sign-up succeeds but API returns 401 | Token project/issuer, API project ID, subject claim, effective mode, and token freshness |
| Hosted sign-up/sign-in fails | Intended project/web key, email/password provider setup, and provider error code |
| Sign-in works but old uploads are absent | Current subject versus stored ownership and whether project/account identity changed |
| Accounts disappear after restart | Export completion, volume retention, import path, and whether the same Compose project/volume is used |
| App loads but auth is not yet ready | Firebase service startup logs, since no readiness dependency guarantees auth availability |

Use bounded container diagnostics from the repository root.

```powershell
docker compose ps firebase api
docker compose logs --tail 60 --since 10m firebase
docker compose logs --tail 60 --since 10m api
```

Inspect browser network status and sanitized provider error codes when needed. Do not copy request passwords, ID tokens, refresh tokens, full authorization headers, or account exports into logs or task notes. A web API key is delivered to the client by design, but there is still no need to reproduce configuration values in routine diagnostic output.

Avoid weakening authentication to make a test pass. Correct the project, environment, endpoint, or account mismatch instead. Admin sign-in failures belong to the separate Admin credentials and policy path.

## Verification scope

The existing [smoke script](../scripts/smoke.ps1) is explicitly local. It hardcodes the application origin `http://localhost:18080`, auth emulator origin `http://localhost:19099`, and demo API key. It creates fresh synthetic users and exercises sign-up/sign-in plus application ownership checks. It is not a hosted Firebase test or a browser interaction test.

Do not run that script unchanged and claim it verifies hosted authentication. A hosted check needs deliberately selected test accounts/project configuration and separate handling of account creation and cleanup. The local script's temporary-file cleanup does not imply it deletes all accounts and uploaded database records it creates.

For configuration changes, verify effective `/firebase-config` values, the expected browser endpoint selection, successful sign-in, token-protected upload access, rejection without authentication, and isolation between two subjects. For refresh changes, exercise expiration, concurrent refresh, and sign-out while a refresh is pending.

For emulator persistence changes, create a synthetic account, stop gracefully, inspect successful export, restart with the same volume, and verify sign-in. Describe abrupt-stop behavior separately if tested. For environment-guard changes, verify unsigned emulator tokens are rejected outside the intended Development mode.

## Reference boundary

The complete Horus API configuration guide was reviewed. Its centralized configuration sections, printed configuration tables, `[LogMask]` behavior, and `Runtime:EntityOnly` option are not implemented by this application's Firebase setup. Current behavior comes from the source files linked above and [.env.example](../.env.example).

Keep Firebase configuration changes scoped to this application and deployment. Do not modify framework authentication or unrelated Admin settings to solve an application identity issue.
