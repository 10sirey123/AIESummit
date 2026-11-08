# Firebase authentication and upload ownership

The application API accepts Firebase ID tokens. It does not issue its own user JWTs, share Admin credentials with the browser, or use Horus's static API-key and OAuth-client configuration sections. Authentication is configured in [Program.cs](Program.cs), ownership is enforced in [UploadEndpoints.cs](UploadEndpoints.cs), and the browser session flow is in [app.js](wwwroot/app.js).

Read [Session.AGENTS.md](wwwroot/Session.AGENTS.md) for detailed browser behavior and [Admin.AGENTS.md](../deploy/Admin.AGENTS.md) for the separate operator authentication system.

## Two operating modes

The effective emulator flag is the conjunction of two conditions.

```csharp
var emulator = builder.Environment.IsDevelopment()
    && config.GetValue<bool>("Firebase:Emulator");
```

Unsigned emulator tokens are accepted only when the ASP.NET environment is Development and the Firebase emulator setting is enabled. Preserve both gates. Setting `Firebase:Emulator=true` under Production does not activate unsigned-token handling.

| Configuration | Effective mode |
| --- | --- |
| Development and emulator enabled | Local unsigned emulator tokens |
| Development and emulator disabled | Hosted Firebase token validation |
| Production and either emulator value | Hosted Firebase token validation |

The local Compose defaults are Development, emulator enabled, project `demo-aiesummit` and API key `demo-key`. These are local workflow settings. They are not credentials for a real Firebase project.

The emulator listens on `0.0.0.0:9099` inside its container, while Compose publishes it only on `127.0.0.1:19099`. The app API is likewise published on loopback at port 18080. Keep the unsigned-token environment private. An emulator token's signature does not prove identity, and the server does not contact the emulator to confirm the account for each request.

## Server validation contract

`Program.cs` requires the configured project ID and constructs issuer `https://securetoken.google.com/{projectId}`. Outside effective emulator mode, it requires a configured Firebase web API key for the browser. These checks use null-coalescing throws, not a comprehensive nonblank or project-existence validator. Do not claim startup verifies the Firebase project or enabled sign-in methods.

The JWT bearer handler sets the following options explicitly.

| Option | Purpose |
| --- | --- |
| `MapInboundClaims = false` | Keeps the Firebase claim name `sub` unchanged |
| `IncludeErrorDetails = false` | Avoids exposing detailed bearer validation errors to clients |
| `Audience = projectId` | Requires the token's audience to match the configured project |
| `ValidIssuer = issuer` | Requires the expected Firebase issuer |
| `ClockSkew = 30 seconds` | Limits the timestamp validation tolerance |
| Hosted `Authority = issuer` | Uses the Firebase authority and signing-key discovery path |

The handler retains standard token lifetime and hosted signature validation behavior. The application does not explicitly disable expiration validation. Do not replace authority validation with merely decoding a JWT payload.

In emulator mode, the handler sets `RequireSignedTokens = false` and supplies a signature validator that constructs `JsonWebToken` and rejects it unless `jwt.Alg == "none"` and `jwt.IsSigned == false`. This is a deliberately narrow local bypass. It does not mean issuer, audience and lifetime checks should be removed, and it does not permit arbitrary signed algorithms through that custom branch.

The default authorization policy requires an authenticated user and a `sub` claim. `RequireClaim("sub")` is a presence requirement, not an application-specific owner-directory lookup. Endpoints use `FindFirstValue("sub")` as their owner identifier. Preserve the unmodified claim mapping and policy together.

The current app does not add a Firebase Admin SDK account lookup, revocation check, email-verification requirement, role-based user policy or a server-side session store. Do not document those as existing protections. If adding one, define its user-facing behavior and test both local and hosted modes.

## Authorized routes and public configuration

`MapUploadEndpoints` creates the `/api/v1` group with `RequireAuthorization()`. This covers upload creation, listing, detail and image reads. An anonymous request is challenged by authentication before its owner-scoped query runs.

Static files, `/health` and `/firebase-config` are public. Static middleware runs before authentication, and these public endpoints are not assigned the upload group's policy. This lets the browser load sign-in UI and determine how to contact Firebase.

`/firebase-config` returns only `projectId`, `apiKey`, the effective `emulator` flag and `emulatorPort`, defaulting to `19099`. A Firebase web API key is browser configuration and appears in the Firebase REST URL by design. It is not the application's authorization check.

Never extend this response with Gemini keys, provider `APIInfo` secrets, database credentials, Admin signing material or the local Admin bearer token. Do not return the entire configuration tree. The public health endpoint proves only that the API is serving, not that Firebase login or provider processing succeeds.

The upload POST disables antiforgery handling and relies on an explicit bearer header rather than an application authentication cookie. If introducing cookies later, reassess CSRF and browser request protections instead of carrying that setting forward without analysis.

## Ownership is enforced after authentication

On upload creation, the server derives `OwnerId` from `sub`. There is no authoritative owner field accepted from the request body or filename. The resulting ID is a database identifier, not a bearer capability.

| Route | Ownership rule |
| --- | --- |
| `GET /api/v1/uploads` | Filter uploads by current owner before ordering, pagination and projection |
| `GET /api/v1/uploads/{id}` | Filter by both owner and upload ID before projecting. Return 404 if absent |
| `GET /api/v1/uploads/{id}/images/{imageId}` | Require image ID, page/upload relationship and upload owner in the same query |

After detail access establishes ownership of the upload, its page-image and document queries are scoped to that upload ID. Keep that preceding ownership check intact if restructuring the endpoint. A new direct document endpoint would need its own owner traversal.

Apply owner filters to entity queries before `Summaries` builds the response record projection. This is both the established EF translation pattern and the clearest place to audit access. Do not load a broad list and filter it only in the browser.

An authenticated second account receives 404 for another user's upload or image. The list omits that upload. Do not change this to returning the other owner's metadata with a hidden UI button. Metadata such as filenames, document titles and image references is also private user data.

Admin is a separate service with its own operator token and access configuration. It can inspect application rows for operational purposes and does not automatically apply the Firebase end-user filter. A Firebase ID token is not the generated Admin operator token, and the local Admin token must never be placed in the app's session.

## Browser sign-in and refresh

The browser fetches `/firebase-config`, then chooses hosted Identity Toolkit and Secure Token endpoints or emulator URLs under the current hostname and configured emulator port. The emulator URL is constructed with `http://`. This is a local development arrangement, not an HTTPS reverse-proxy configuration recipe.

The authentication form calls Firebase REST signup or password signin using the submit button's operation value, email, password and `returnSecureToken: true`. On success it stores the returned tokens in the module's `session` variable, sets expiry 60 seconds early, resets the form, shows the workspace and loads uploads.

Session tokens are kept in JavaScript memory. They are not persisted to localStorage, sessionStorage or application cookies. Reloading the page loses this app session. This reduces persistence of credentials on the device but does not make them inaccessible to JavaScript running in the page.

The `token()` helper returns the current ID token until its early-expiry timestamp. Then it posts the refresh token using `grant_type=refresh_token`. A shared `refreshInFlight` promise coalesces concurrent refresh requests. After completion it checks that the active session object has not changed before updating the ID token, refresh token and expiry.

API calls attach `Authorization: Bearer ...`. Image fetches also call `token()` and attach the header. Refresh is based on the locally tracked expiry, not an automatic retry after every HTTP 401. A server rejection or failed refresh is reported as an error. There is no implemented forced-signout or reauthentication flow for all token failure cases.

Signout clears session, selected upload and cached list state, increments the request generation, removes rendered results and revokes tracked image object URLs. It does not revoke Firebase refresh tokens remotely or cancel all in-flight network requests. A request already accepted by the API can still finish after browser signout.

List and upload handlers compare session identity before applying results. Detail and image results use the request generation to avoid displaying obsolete data after navigation or signout. These are important guards, not proof that every error callback or concurrent sign-in race has been eliminated. Preserve them and test account switching explicitly when editing the session flow.

## Configuring hosted Firebase

Enable Email/Password sign-in in the intended Firebase project, then configure these values for the API process.

```text
FIREBASE_EMULATOR=false
AIESUMMIT_API_ENVIRONMENT=Production
FIREBASE_PROJECT_ID=<intended project ID>
FIREBASE_API_KEY=<that project's Firebase web API key>
```

These are Compose input variable names. Compose maps them to `Firebase__Emulator`, `ASPNETCORE_ENVIRONMENT`, `Firebase__ProjectId` and `Firebase__ApiKey`. For direct host execution, use the corresponding ASP.NET configuration keys or environment variables instead of assuming the Compose aliases are automatically read by application code.

The project ID determines issuer/audience validation. The web API key selects the browser's Firebase REST project context. They must describe the same intended project. Changing those variables does not create a Firebase project, enable the provider or migrate emulator accounts.

The local smoke script is hardcoded to the emulator signup/signin URL and demo key. It is not a hosted-Firebase acceptance test. Do not silently retarget it to a real project and create accounts as though that were the same disposable local check.

## Verification matrix

Build and check JavaScript syntax from the repository root after implementation changes.

```powershell
dotnet build AIESummit.Api/AIESummit.Api.csproj --nologo
node --check AIESummit.Api/wwwroot/app.js
docker compose config -q
```

Use synthetic users and files for integration checks. The existing `scripts/smoke.ps1` creates two emulator accounts, signs in, verifies anonymous upload-detail 401, cross-account detail 404 and isolated upload lists, then exercises paid OCR/LLM processing and protected Admin telemetry. Read [smoke.AGENTS.md](../scripts/smoke.AGENTS.md) before running it.

The following are additional proposed acceptance cases. They are not claims of completed testing or existing automated coverage.

| Case | Expected boundary |
| --- | --- |
| Missing or malformed bearer token | Reject access to protected routes |
| Wrong issuer or audience | Reject even if token shape is otherwise valid |
| Expired token beyond the configured skew | Reject |
| Missing `sub` | Deny through authentication or authorization, without granting an owner context |
| Emulator `alg` other than `none`, or signed token in the custom emulator branch | Reject |
| Unsigned emulator token under Production | Reject through hosted validation |
| Development with emulator disabled | Use hosted authority validation |
| Other account's image ID paired with either upload ID | Return 404 unless the requested relationship and owner both match |
| Signout during list/detail/image request | Do not repopulate the previous account's result data |
| Concurrent requests after token expiry | Share refresh work and preserve the current session identity |
| Refresh failure or rejected API token | Display a safe error without exposing tokens |

Do not demand a specific 401 versus 403 for every malformed-claim case without observing whether token authentication or the later policy rejected it. The security requirement is denied access and no data disclosure. The existing smoke's anonymous and other-owner expectations are specific and should remain stable.

For hosted acceptance, verify a genuine token from the intended project and a token from a different project, plus normal expiry/refresh behavior. A `/health` response or successful configuration fetch is insufficient evidence of hosted login.

## Diagnostics and source references

Inspect configured mode, environment and project identity without dumping tokens or passwords. Keep bearer values out of command examples, screenshots and logs. `IncludeErrorDetails=false` intentionally limits client diagnostics. Investigate server-side validation categories and Firebase response codes while redacting credential material.

Application sources are [Program.cs](Program.cs), [UploadEndpoints.cs](UploadEndpoints.cs), [app.js](wwwroot/app.js), [firebase.json](../deploy/firebase.json), [compose.yaml](../compose.yaml) and [smoke.ps1](../scripts/smoke.ps1).

The full historical reference is `../../horus/DUTai.Api/Configuration/AGENTS.md`. Its separation of project configuration and startup validation motivates this guide, but AIESummit does not use its centralized `Config` sections, printed configuration table, `[LogMask]` attributes, static API keys, OAuth clients or `Runtime:EntityOnly` switch. Current authentication behavior comes from the explicit ASP.NET setup described above.
