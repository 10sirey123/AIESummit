# Verification scripts

The improvement UI starts with `./scripts/start-improvement.ps1`; read [self-improvement](../docs/self-improvement.md). `./scripts/measure-improvement-baseline.ps1` measures the frozen 20-file dataset through the native Muse graph while preserving PostgreSQL/cache; `-CacheOnly` forbids model calls. Run only one measurement at a time and preserve its artifacts. Candidate attempts never modify these scripts or scoring.

`test-extractions.ps1` runs `AIESummit.Extraction.Tests` against uniquely named disposable PostgreSQL and MongoDB containers. The provider-backed tester is `AIESummit.Evaluation`: see [commands and evidence boundaries](../docs/contract-extraction.md) and [instrumentation schema](../docs/evaluation-instrumentation.md). Preserve generated run folders; do not resume dataset downloads for these checks.

This directory contains the local end-to-end acceptance script, `smoke.ps1`. It is not a .NET test project. `AIESummit.slnx` includes application projects, `AIESummit.Configuration`, focused `AIESummit.Tests`, and framework library references. The bundled Blueshift CLI runs through `admin.ps1`.

Read [smoke.AGENTS.md](smoke.AGENTS.md) before changing or running the live acceptance flow, and [../Startup.AGENTS.md](../Startup.AGENTS.md) for startup, shutdown and private local configuration. The guide below separates compilation, static checks, isolated behavior checks and real provider acceptance.

## Execution environment

Use PowerShell 7 for the repository scripts. The smoke uses `Invoke-WebRequest -Form` and `-SkipHttpErrorCheck`, so Windows PowerShell 5.1 is not the intended runtime. Docker and the committed `dependencies/blueshift` bundle are required for the complete stack; private framework access is not needed. Host compilation requires the .NET 10 SDK, and contract/JavaScript checks require Node.js.

Run the documented relative commands from the repository root. `start.ps1` changes to its own script directory and restores the caller's location in `finally`. `smoke.ps1` locates its Admin token through `$PSScriptRoot` and creates its PDF in the OS temporary directory. The smoke's service URLs are fixed local URLs, not derived from the working directory or a generic environment argument.

```powershell
./start.ps1
./scripts/smoke.ps1
```

Startup builds and starts the configured services and generates a four-hour local Admin token. It does not run the smoke automatically. The smoke creates users, uploads a synthetic file and invokes configured live OCR/LLM deployments. It is therefore a mutating acceptance check that may incur provider charges, not a zero-cost health probe.

## Choose checks from the change

| Change | Relevant evidence |
| --- | --- |
| Documentation only | Verify named paths/symbols, links, commands and diff whitespace. No provider call needed |
| Domain properties or mapping | Domain/solution build, finalized EF model inspection and isolated persistence checks. Retained data needs an explicit schema transition |
| OCR or LLM logic | Agent/solution build and focused synthetic behavior cases, followed by live acceptance when the change affects the provider chain |
| HTTP routes or projections | API build, contract regeneration, request/response checks and two-account isolation |
| Authentication/session | Server-mode checks, malformed-token cases and browser signin/refresh/signout scenarios |
| Browser JavaScript or markup/CSS | Syntax check plus actual browser interaction, keyboard and layout checks appropriate to the change |
| Contract fragments | Builder success, generated diff, reference/semantic validation and comparison with actual endpoints |
| Compose, Dockerfile or pack loading | Quiet Compose validation, image builds, startup readiness and relevant runtime behavior |
| Queue recovery | Eligible-state and configuration checks plus a controlled restart/republication scenario |

Do not run the entire live flow reflexively for a prose edit, nor stop at compilation for a change to ownership or persistence. State the verification boundary clearly in the handoff.

## Existing static and build commands

These commands exist today and do not require inventing a new test harness.

```powershell
dotnet build AIESummit.slnx --nologo
docker compose config -q
node AIESummit.Api/ApiSpec/build.mjs
node --check AIESummit.Api/wwwroot/app.js
git diff --check
```

For a bounded compilation check, choose the relevant project.

```powershell
dotnet build AIESummit.Domain/AIESummit.Domain.csproj --nologo
dotnet build AIESummit.Agents/AIESummit.Agents.csproj --nologo
dotnet build AIESummit.Api/AIESummit.Api.csproj --nologo
```

A solution build covers shared application signatures but does not independently publish every shared worker/Admin Docker target. Container changes require their own build/start evidence. Keep compiler warnings visible rather than copying Horus's broad warning and package-audit suppression.

`docker compose config -q` needs the environment variables required by Compose but avoids printing their resolved values. Do not replace it with an unredacted configuration dump in a shared transcript.

The OpenAPI builder changes `AIESummit.Api/wwwroot/openapi.json`. Review that generated diff alongside the source fragments. Its successful exit proves JSON parsing and its limited merge checks, not full OpenAPI semantics or runtime conformance. There is no local npm script or guaranteed Spectral installation to invoke by assumption.

`node --check` checks JavaScript syntax only. It does not execute DOM access, Firebase requests, polling or image rendering. `git diff --check` checks diff whitespace, not the accuracy of claims or safety of example commands.

## Readiness versus acceptance

Use bounded read-only observations before the live smoke.

```powershell
docker compose ps
Invoke-RestMethod http://localhost:18080/health
docker compose logs --tail 100 api worker
```

The API health endpoint means the HTTP process is serving. It does not establish that the worker can reach Service Bus, that provider credentials work, that a real Firebase project accepts signin, or that Admin telemetry is indexed. The smoke provides stronger workflow evidence, while browser and negative cases remain separate.

Inspect logs before increasing timeouts or rerunning the provider flow. A repeated smoke creates additional accounts and upload records. Increasing the polling budget cannot fix a disabled agent, missing APIInfo or permanent processing error.

## What the existing smoke verifies

The script generates a unique suffix and password, creates two Firebase emulator users and explicitly signs in as the owner. It builds a three-page PDF in memory with an invoice spanning pages 1–2 and a separate library event flyer on page 3.

It then performs the following assertions.

1. Multipart upload returns HTTP 202 and a usable `Location`.
2. Anonymous detail access returns 401 and another account's access returns 404.
3. Owner polling reaches `ready` rather than `failed` or the configured deadline.
4. The result contains three pages and two documents spanning 1–2 and 3.
5. Markdown retains expected invoice/flyer phrases and the page-break marker.
6. Owner listing includes the upload while the other account's listing does not.
7. Anonymous Admin execution access returns 401.
8. Authenticated Admin results include successful OCR, LLM step and root-upload-correlated prompt runs.

The processing wait defaults to 180 seconds and polls every three seconds. `-TimeoutSeconds` changes that processing deadline, not every network timeout or the telemetry phase. The telemetry check has a separate 15-second polling window and looks at up to 50 prompt runs. Heavy concurrent activity can affect what that bounded query sees.

```powershell
./scripts/smoke.ps1 -TimeoutSeconds 240
```

Use a larger budget only when processing latency warrants it and record that choice. A deadline is not a strict whole-script wall-clock cap because individual network calls have their own behavior.

## Data, cleanup and credentials

The temporary PDF path uses a unique name in `[IO.Path]::GetTempPath()`. A `finally` block removes that specific file if present. The script does not recursively delete a workspace directory or remove the database records it created.

The two Firebase accounts, uploaded source, pages, documents, prompts, image records when present and execution telemetry remain after the script completes. Failure after account creation can also leave partial test state. Do not describe the smoke as self-cleaning beyond its temporary fixture file.

The script reads `artifacts/admin.token.local.txt` near the telemetry phase. An expired/missing token can cause a late failure after successful paid processing. Renew local setup as needed and check the intended file's availability without printing its contents. Do not replace the token with Firebase credentials or expose it in command arguments.

Use synthetic content. Do not copy clinical/legal examples or real user PDFs from Horus merely because its tests reference them. Raw document content, authorization headers, Firebase tokens and provider connection details should not appear in shared failure reports.

Store optional local diagnostic artifacts under ignored `artifacts/` with clear task-specific names. Being ignored does not make sensitive output harmless. Prefer IDs, states, exception categories and redacted summaries over complete request/response dumps.

Do not delete retained database rows or volumes as incidental cleanup. A future cleanup utility would need explicit selection, relationship handling and safety checks. Current scripts provide no such general-purpose cleanup command.

## Coverage the smoke does not provide

- Hosted Firebase signin, signature-key discovery, revocation or all malformed-token cases.
- Browser rendering, keyboard accessibility, token-refresh races or signout during image requests.
- Every invalid file format, size/binding error, malformed provider response or image decoding failure.
- Exhaustive range validation, persistence mapping, transaction rollback and nullable-prompt save cases.
- Emulator restart recovery, duplicate delivery behavior or exactly-once provider execution.
- A database migration path, because the app currently initializes schemas with `EnsureCreated`.
- Every Admin CRUD restriction, menu layout, role or expired-session browser behavior.

Do not report a successful `dotnet test` command with no discovered AIESummit tests as unit-test coverage. Framework tests require a separate authorized source checkout and are not included in this binary bundle. Do not edit or enroll all of them automatically when the task is local documentation or application behavior.

## Adding focused tests in future work

No test-framework dependency is prescribed by the current repository. When meaningful implementation work warrants tests, choose a compatible test project and explain the addition. The following is a proposed structure of evidence, not an existing suite.

Use deterministic synthetic cases for range validation, response shape handling, state transitions and frontend request coordination. Use isolated PostgreSQL contexts for relational constraints and save hooks when in-memory substitutes would miss provider behavior. Use fake transport/provider boundaries for most error paths so a malformed-output test does not require a paid call.

Keep a smaller live-provider acceptance path for endpoint/deployment compatibility and complete OCR-to-document processing. A mocked success cannot prove real authentication, deployment naming or provider payload compatibility. Conversely, a live success cannot replace systematic invalid-input cases.

For persistence tests, inspect results through a fresh context and preserve the framework base save hooks. For queue tests, distinguish enqueue acceptance, execution eligibility and durable completion. For owner tests, use two subjects and verify the API, not only a hidden UI element.

Prefer disposable, explicitly scoped test resources. Do not point a new fixture helper at an operator's existing database by inheriting an unknown connection string. Introduce a reviewed baseline/upgrade workflow before claiming schema-migration acceptance.

## Script maintenance and reporting

Keep the fixture deterministic except for collision-avoiding identities. Assertions should verify meaningful output rather than mirror the implementation line for line. If provider wording varies, preserve the business invariant without weakening checks to “response exists.”

Maintain `$ErrorActionPreference = 'Stop'`, specific assertion failures and `finally` cleanup. When invoking native tools from a new PowerShell script, inspect exit codes explicitly where needed. Avoid dumping entire token-bearing objects when debugging a failing HTTP request.

Report the exact checks run and their outcomes. For a live run, include the synthetic upload ID and observed stage results without credentials or document bodies. Identify separately whether browser checks, negative cases and container rebuilds were performed. If a failure is caused by unavailable infrastructure or expired operator auth, say so rather than presenting it as either a confirmed code defect or a successful acceptance run.

## Sources and legacy translation

Current evidence is [smoke.ps1](smoke.ps1), [start.ps1](../start.ps1), [AIESummit.slnx](../AIESummit.slnx), [build.mjs](../AIESummit.Api/ApiSpec/build.mjs), [Program.cs](../AIESummit.Api/Program.cs) and [compose.yaml](../compose.yaml).

The complete Horus reference `../../horus/Voiston.Agents.Tests/AGENTS.md` motivates separating credential-dependent integration checks from focused logic tests. Its NUnit/Moq dependencies, .NET 9 target, EF6 helpers, clinical test areas and environment variables are not current AIESummit infrastructure. This guide documents the actual PowerShell acceptance path and labels future test design explicitly.

## Benchmark preparation

`prepare_benchmarks.py` builds the requested offline collection under `../corporea/benchmarks/`, with separate `cuad`, `maud`, and `edgar-corpus` folders. Each document has a numbered PDF, JSON sidecar, and reference TXT. Install dependencies from `benchmark-requirements.txt`; then run `python scripts/prepare_benchmarks.py`. It prepares all 510 CUAD and 152 MAUD documents and a deterministic, year-balanced sample of 100 already-downloaded EDGAR reports. It does not download, call models, or create application tests. Preserve gold provenance, exact reference-text spans, null EDGAR labels, and the distinction between original and rendered PDFs. See `../corporea/README.md` for the schema and source-selection details.

`download_benchmarks.py` is the existing dependency-free Python 3.12+ utility for pinned benchmark downloads and integrity verification. Run `python scripts/download_benchmarks.py` or `python scripts/download_benchmarks.py --verify-only` from the root. Preserve its source lock, raw data and upstream splits; see `../corporea/README.md` and root benchmark policy. Python is for dataset preparation only.

`test_download_benchmarks.py` checks recovery from prematurely ended responses and safe replacement when a server ignores a byte range. Run `python -m unittest discover -s scripts -p test_download_benchmarks.py -v`. It uses a local HTTP server and temporary synthetic bytes; it needs no provider credentials or external network access. Incomplete transfers must retain their partial bytes for subsequent range requests. Only discard a partial file when its full expected length fails checksum verification or its length exceeds the expected size.

## Bundled framework maintenance

Run `./scripts/verify-dependencies.ps1` to verify all eight committed Blueshift artifacts against `dependencies/blueshift/manifest.json`; startup runs this too. Only maintainers need `./scripts/package-blueshift.ps1 -SourcePath <separate-checkout>` and private source access to refresh binaries. The pinned-source container build is `deploy/Blueshift.Bundle.Dockerfile`. See `../dependencies/blueshift/README.md` for versioning, provenance and validation. Normal builds consume the local NuGet feed and prebuilt Linux hosts/site without a submodule.

## Native page analysis checks

Meta support: `configure-meta.ps1` imports `META_API_KEY` only with explicit `-ImportLocalKey` (or reads `-KeyFile`), persists write-only `APIInfo(meta)`, and selects `meta-muse-wonder` on the existing page step. `smoke.ps1 -VerifyPageSections -ExpectedPageModel muse-wonder-1.0 -ContractFixture` additionally resolves all used-model links and requires the Meta provider. Read [Meta configuration](../docs/meta-analysis.md); five focused tests now include an intercepted Meta transport contract. Runtime keys never come directly from environment variables.

`configure-gemini.ps1` explicitly imports a private key file or supplied local key into write-only Admin APIInfo, configures the Gemini model and enables the native page LLMStep. Runtime agents never read a Gemini environment key. `smoke.ps1 -VerifyPageSections -KeepTestSession -ContractFixture` exercises synthetic MSA/NDA pages, original source fidelity/ownership, all page classifications, exact source quotes and Admin runs. It retains test data; optional synthetic login details go only to ignored artifacts. `dotnet test AIESummit.Tests/AIESummit.Tests.csproj` checks section validation and EF prompt mapping. See `../docs/page-analysis.md`.

## Configuration seeding

See [the implemented seeding workflow](../docs/configuration-seeding.md). `AIESummit.Configuration` exports the existing step definitions; the bundled Blueshift CLI performs validation, read-only planning and previewed transactional application. `SeedIdentities` and the idempotent `SeedSchema` upgrade support registry provenance. Default exports preserve model/enabled selections and all credentials. No publishers or provider calls run in the seed host. The one-off Docker configuration image and isolated PostgreSQL tests are documented there.

## MongoDB CLI diagnostics

`mongodb-probe.js` runs read-only in the official mongosh with `--nodb --quiet --file scripts/mongodb-probe.js`. It loads credentials from local configuration without printing them or placing the URI in process arguments, verifies the primary, reports the server-observed source IP and counts the latest test run. Use the evaluator `mongo-check` for the actual writer and `replay` for retained telemetry. See [the instrumentation guide](../docs/evaluation-instrumentation.md#connectivity-checks-and-the-resolved-tls-failure) for the resolved destination-dependent NAT/access-list issue.
