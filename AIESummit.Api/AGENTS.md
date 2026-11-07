# AIESummit.Api

Page-section analysis and the page reader are implemented. See [the current page-analysis guide](../docs/page-analysis.md) for the `OcrPage` LLM subject, Gemini APIInfo, additive schema upgrade, queues, evidence validation and Admin checks; the detailed split/OCR guidance below remains applicable to that original workflow.

This .NET 10 ASP.NET Core application serves the browser client and its Firebase-authenticated upload API. It uses handwritten minimal endpoints, PostgreSQL through EF Core, and BlueShift publishers for background processing. The separate central BlueShift Admin host owns operator configuration and processing inspection.

Read this guide before adding routes, changing public response fields, modifying authentication or changing startup behavior. Keep public contracts, owner scoping and processing publication aligned.

## Directory map and reading routes

| Source or directory | Responsibility | Focused guide |
| --- | --- | --- |
| `Program.cs` | Authentication, service registration, startup and middleware | [Authentication.AGENTS.md](Authentication.AGENTS.md) |
| `UploadEndpoints.cs` | Upload validation, list/detail projections and image reads | [UploadEndpoints.AGENTS.md](UploadEndpoints.AGENTS.md) |
| `Bootstrap.cs` | Initial schema creation and missing configuration seeds | [Bootstrap.AGENTS.md](Bootstrap.AGENTS.md) |
| `ApiSpec` | Hand-authored OpenAPI fragments and composition script | [ApiSpec/AGENTS.md](ApiSpec/AGENTS.md) |
| `ApiSpec/Components` | Public response and request schemas | [Components guide](ApiSpec/Components/AGENTS.md) |
| `ApiSpec/Paths` | HTTP operations, responses and security declarations | [Paths guide](ApiSpec/Paths/AGENTS.md) |
| `wwwroot` | Static client and composed OpenAPI document | [Browser guide](wwwroot/AGENTS.md) |
| `wwwroot/app.js` | Firebase session and upload interaction | [Session guide](wwwroot/Session.AGENTS.md) |

Read the [Domain guide](../AIESummit.Domain/AGENTS.md) for schema changes, the [Agents guide](../AIESummit.Agents/AGENTS.md) for processing changes and the [Admin guide](../deploy/Admin.AGENTS.md) for operator surfaces. A new Admin entity does not automatically become a client API endpoint.

## Current HTTP surface

| Route | Access | Behavior |
| --- | --- | --- |
| `GET /health` | Public | Reports that the API process is serving requests |
| `GET /firebase-config` | Public | Returns browser Firebase configuration and emulator selection |
| Static `wwwroot` files | Public | Serves the browser client and `openapi.json` |
| `POST /api/v1/uploads` | Firebase bearer token | Validates and persists one multipart upload, then returns 202 with its ID and location |
| `GET /api/v1/uploads` | Firebase bearer token | Returns up to 50 owned uploads before an ID cursor |
| `GET /api/v1/uploads/{id}` | Firebase bearer token | Returns an owned upload summary, documents and image references |
| `GET /api/v1/uploads/{id}/images/{imageId}` | Firebase bearer token | Returns owned embedded image bytes |

The `/api/v1` group requires authorization. Health, browser configuration and static files intentionally remain public. Do not place private documents or credentials under `wwwroot`. The current health response does not verify database connectivity, queues, provider credentials or completed processing.

There is no public deletion, prompt-editing, agent-control or generic entity CRUD endpoint. Add new capabilities as explicit contracts with their own ownership and validation rules.

## Host setup and boundaries

`Program.cs` registers `AIESummitDb` with `UseNpgsql`, exposes it as `EFBaseDbContext`, and registers generic `EFBasePersistenceRepository<>` instances. API writes use the application's context and its base save hooks. Bypassing these hooks can change processing publication behavior.

The API loads `AIESummit.Agents` pack services and registers BlueShift with publishers enabled and receivers disabled. It then activates agents to establish their publisher behavior. The queue environment is currently `dev` and the venture prefix is `aiesummit`. Queue enablement and its connection string come from configuration.

The separate worker receives and executes OCR and LLM work. A 202 response acknowledges persisted intake, not completed OCR or splitting. Do not add provider calls inside an upload handler to hide a worker outage. Diagnose persisted state, publication and worker configuration through their respective guides.

The API uses `NullBroadcaster`. The browser follows the established HTTP interaction rather than requiring a local SignalR service. Horus broadcaster flags and entity-only activation switches are not current API configuration options.

## Firebase authentication

The host requires `Firebase:ProjectId` and normally `Firebase:ApiKey`. It uses the project-specific `https://securetoken.google.com/{projectId}` issuer and matching audience. Claim mapping is disabled so application code can use the original `sub` claim. The default authorization policy requires an authenticated principal with that claim.

Hosted mode configures the issuer as the JWT authority. Emulator mode is selected only when the environment is Development and `Firebase:Emulator` is true. In that mode the signature validator accepts unsigned `alg=none` emulator tokens and rejects signed tokens through that branch. Do not relax production validation to make emulator tests pass.

`/firebase-config` exposes browser configuration, including the Firebase web API key. It must never expose Foundry provider keys, APIInfo secrets, database connection strings or queue credentials. Browser Firebase configuration and server provider credentials serve different purposes.

The application does not implement Horus's client-credentials `/oauth/token` flow or require its client certificate tooling. Use Firebase ID tokens and the current session guidance when extending clients.

## Owner scoping and projections

`UploadedFile.OwnerId` comes from the authenticated `sub`. It is not accepted from the multipart body or an arbitrary query parameter. Preserve this assignment when adding alternative intake paths.

List and detail queries include owner predicates. Detail first proves ownership by loading the scoped upload summary, then reads documents and image references for that upload. Image retrieval performs the complete join through `PageImage.Page.Upload.OwnerId` and also constrains the requested upload and image IDs.

Missing and nonowned detail/image resources return 404. Do not load an unrestricted row and accidentally disclose its existence or fields before enforcing ownership. New direct document endpoints must independently prove their upload's ownership rather than relying on a previous client request.

Public responses are explicit projections. `UploadSummary` and `ImageReference` are endpoint-local records, and document detail uses selected fields. Never return a tracked `UploadedFile`, prompt or APIInfo graph as a shortcut. Domain metadata such as `IsHidden` is an Admin presentation hint and does not secure API serialization.

Changing a public field requires reviewing its schema, browser consumers and information exposure. Original upload bytes, credentials and internal prompts are not part of the current public response. Generated Markdown and image references are returned only after the upload ownership check.

## Intake and response behavior

The upload handler accepts one multipart `IFormFile`. It enforces a nonzero size of at most 20 MiB and buffers the accepted file. Kestrel's request limit is that size plus 65,536 bytes for request overhead. Revisit both limits and the buffering strategy if changing intake size or multipart shape.

Content type is inferred from PDF, PNG or JPEG magic bytes. The client-supplied filename extension and MIME type are not authoritative. Signature recognition is not full document parsing, so a file with an accepted header can still fail later during OCR.

Filenames are reduced to their final path component after normalizing backslashes, then truncated to 255 characters. The server stores the detected MIME type and bytes. Invalid length produces a 400 problem response, and an unsupported signature produces 415. Framework request parsing or the server body limit can reject requests before this handler runs.

The upload route disables antiforgery because it uses bearer-token authorization in this client design. Do not copy that decision to a future cookie-authenticated write path without reviewing its authentication model.

List pagination orders IDs descending, filters `ID < before` and takes 50 rows. Status projection returns `failed` when an error exists or the step is in permanent error, `ready` when the step is processed, `splitting` after OCR completes and otherwise `queued`. Preserve this ordering when extending states so a failed record does not appear ready.

Document detail orders by `FirstPage`. Each document receives image references whose one-based page number lies within its inclusive range. Image bytes use the stored image MIME type. Keep this relationship aligned with the Domain page/image model.

## Startup configuration and seeding

Before serving requests, the host runs `Bootstrap.InitializeAsync`. Startup calls `EnsureCreatedAsync`, reads Foundry configuration, begins a transaction and acquires PostgreSQL advisory transaction lock `825833001` before seeding missing configuration.

It creates missing `APIInfo` rows identified by `mistral-ocr` and `openai`, an `LLMModel` tagged `foundry-split`, a step tagged `split-upload`, and initial OCR/step/prompt agent configuration. Existing rows are preserved. Changing environment variables or code defaults does not overwrite operator-edited stored values.

Foundry endpoint and key are read and required even when configuration rows already exist. Treat a startup failure there as configuration evidence rather than assuming credentials are only needed for first launch. Keep provider secrets in deployment configuration and never commit their values into source or documentation.

`EnsureCreatedAsync` is not a migration runner. An existing schema is not upgraded when an entity changes. A retained database needs an explicit schema transition, and startup seeding does not provide that transition. See the persistence and bootstrap guides before modifying storage.

## OpenAPI maintenance

The API uses handwritten C# minimal endpoints and hand-authored OpenAPI fragments. The composition script does not generate C# DTOs, controllers or endpoint implementations.

From the repository root, run `node AIESummit.Api/ApiSpec/build.mjs` after editing the contract. It reads `template.json`, merges top-level JSON entries from immediate `Paths` and `Components` directories, rejects duplicate keys within each collection, and writes `wwwroot/openapi.json`. File discovery is sorted and nonrecursive.

The builder parses JSON and detects duplicate entries. It is not a complete OpenAPI semantic validator and does not prove implementation parity. Review references, required fields, status codes, security and actual response serialization separately. There is no local `npm run build-openapi-json` or generated-controller workflow to copy from Horus.

Update fragments and regenerate the composed document instead of making durable edits only to `wwwroot/openapi.json`. Keep the Firebase bearer security scheme and operation security accurate. If documenting public operations, explicitly account for their access rather than inheriting protected-route assumptions blindly.

## Endpoint extension checklist

1. Define the request, selected response fields, status codes and ownership rule before adding the handler.
2. Place client data routes under the protected group or explicitly establish equivalent authorization. Derive owner identity from claims.
3. Filter by owner in the database query or first load a scoped parent before dependent queries. Validate related IDs through that same scope.
4. Use explicit projections and bounded reads. Keep internal entity state and credentials out of responses.
5. Pass the cancellation token through asynchronous I/O and use the existing scoped context. Review persistence publication for writes that trigger processing.
6. Validate user-controlled input and choose safe public errors. Keep operational detail in diagnostics with appropriate identifiers.
7. Update OpenAPI paths/components, regenerate the composed file and update browser handling when the user flow changes.
8. Verify positive, invalid, unauthenticated, missing-resource and second-account cases. For processing changes, inspect persisted output and worker evidence.
9. Document schema or configuration changes separately from endpoint changes. Do not solve local verification by resetting retained data.

## Build and verification

Build from the repository root with `dotnet build AIESummit.Api/AIESummit.Api.csproj`. Use a solution build when changing shared Domain or agent contracts. Preserve useful compiler diagnostics rather than copying Horus's broad warning suppression.

Verify route behavior with Firebase tokens for two accounts. Check that each can see its own uploads and that another account cannot retrieve detail or image bytes using known IDs. Verify unsupported files, empty uploads, size boundaries and cursor behavior for intake/list changes.

For contract changes, regenerate OpenAPI and compare schemas with actual projected JSON, including casing and nullable fields. A successful JSON merge is not evidence that a newly added endpoint exists.

For the full flow, use [scripts/smoke.AGENTS.md](../scripts/smoke.AGENTS.md). The smoke workflow covers authenticated intake, processing, persisted results and isolation and can call real provider services. API liveness alone does not verify it. Documentation-only changes do not require a live provider run.

The full historical `../../horus/DUTai.Api/AGENTS.md` supplied the separation between API contract and implementation work. Its .NET 9 controllers, generated C# project, OAuth settings, client-certificate harness and legacy configuration bridge are not current AIESummit architecture. Current evidence is [Program.cs](Program.cs), [UploadEndpoints.cs](UploadEndpoints.cs), [Bootstrap.cs](Bootstrap.cs), [AIESummit.Api.csproj](AIESummit.Api.csproj) and [ApiSpec/build.mjs](ApiSpec/build.mjs).
