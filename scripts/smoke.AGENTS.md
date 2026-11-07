# Provider-backed local smoke verification

Read this guide before running or changing [smoke.ps1](smoke.ps1). The script exercises the running local application through HTTP with a generated PDF, two Firebase emulator accounts, live OCR/LLM processing, and protected Admin telemetry. It is an integration smoke check, not an isolated unit test or browser automation suite.

The complete Horus LLM testing guide distinguishes live provider integration from deterministic factory/mock tests. Apply that distinction here. This script does not select Horus's mock model or use its mock prompt command tags. Current persisted provider/model configuration determines actual processing.

## Prerequisites and side effects

Use the repository's local stack prepared through [start.ps1](../start.ps1). The following surfaces must be usable.

| Dependency | Script expectation |
| --- | --- |
| Application | `http://localhost:18080` |
| Firebase auth emulator | `http://localhost:19099/identitytoolkit.googleapis.com/v1/accounts:` |
| Firebase project/key mode | Local stack with demo key and compatible emulator/API project configuration |
| Worker and queue | Upload/prompt processing active |
| OCR/LLM configuration | Valid persisted credentials, endpoints, step, and model |
| Admin frontend/API | `http://localhost:13088/admin/v1/agents/executions` |
| Admin credential | `artifacts/admin.token.local.txt`, readable and still valid |
| Telemetry | Configured history provider available and execution records retained |

The startup script creates the local Admin settings and token with a requested lifetime of 240 minutes. A stale token can make the telemetry phase fail after document processing has already succeeded. Check credential readiness without printing the token.

The script uses modern PowerShell HTTP features, including `-Form` and `-SkipHttpErrorCheck`. Run it with the PowerShell version used by this repository's scripts, not an older shell that lacks those parameters. Node or a browser is not needed for this script's HTTP assertions.

Every invocation creates new emulator users and an uploaded database record. It also initiates real provider-backed work under the configured runtime. It is not an offline fixture check. Use the intended development environment and synthetic source content.

Existing database configuration can override expectations based on current environment defaults. Bootstrap seeds missing rows and preserves existing values. If a run uses an unexpected model or template, inspect the stored configuration through [Bootstrap.AGENTS.md](../AIESummit.Api/Bootstrap.AGENTS.md) and [SplitStep.AGENTS.md](../AIESummit.Agents/SplitStep.AGENTS.md).

## Running the check

From the repository root, run the current script.

```powershell
./scripts/smoke.ps1
```

The optional parameter controls the document-processing polling deadline.

```powershell
./scripts/smoke.ps1 -TimeoutSeconds 240
```

The default is 180 seconds. Increasing it does not alter the separate 15-second telemetry polling window, cancel old jobs, or change provider request timeouts. A longer wait is useful only when the observed processing time warrants it.

The application and auth origins are hardcoded in the script. There are no origin, hosted-Firebase, provider-mock, or Admin-token parameters. Do not run it against a different deployment and assume it follows the browser's `/firebase-config` response.

## Generated PDF fixture

`New-Fixture` builds a minimal ASCII PDF 1.4 directly using `StringBuilder`. It creates catalog/pages/font objects, three page objects and text streams, object offsets, an xref table, and a trailer. The pages use Helvetica and a 612-by-792 media box.

The temporary path is a GUID-suffixed `aiesummit-...pdf` under the operating system's temporary directory. It does not reuse a user document or require a committed binary fixture.

| Source page | Content and intended grouping |
| --- | --- |
| 1 | Northwind invoice 1842, marked page 1 of 2 and continued on page 2 |
| 2 | Same invoice, marked page 2 of 2 with totals and end-of-invoice text |
| 3 | Birch County Public Library event flyer for a community book fair |

The intended split is one document spanning pages 1–2 and a separate document containing page 3. These cues exercise continuation versus a new document boundary.

The PDF contains text only and no embedded images. A passing run does not establish image extraction, image MIME validation, owner-scoped image download, or browser image rendering. Add separate fixtures for those behaviors when required.

The current text is safe for the minimal PDF writer's string construction. If extending the fixture, handle PDF string escaping and byte-offset correctness deliberately. Arbitrary Unicode, parentheses, or backslashes cannot be inserted blindly into this ASCII/PDF construction while assuming the result remains valid.

## Authentication and intake assertions

Each invocation creates a GUID suffix and a random password in memory. It signs up two accounts, with `owner-...@example.test` and `other-...@example.test` addresses. The same generated password is used for these two synthetic accounts.

The script separately signs the owner back in through `signInWithPassword`. Owner requests use that sign-in ID token. Other-user requests use the second account's sign-up token. Thus the run exercises two sign-ups and one explicit sign-in, not sign-in for both users or token refresh.

The owner uploads the generated file as multipart form data to `POST /api/v1/uploads`. The script requires HTTP 202. It constructs the detail URL from the response's Location header and uses that URL for subsequent checks.

It then requests that detail URL without authentication and requires HTTP 401. It requests it with the other account's bearer token and requires HTTP 404. The latter checks that another user cannot read the owner's upload through the detail route.

The script uses the Location header operationally but does not independently assert every aspect of its format or compare it with the accepted-upload JSON body. Do not describe the run as a complete HTTP response-schema/header validation suite.

## Document polling and result assertions

The polling loop reads the detail response with the owner token. It throws immediately when the projected upload status is `failed`, including the upload ID and generic API error in the exception. It exits successfully from the loop when status becomes `ready`.

Otherwise it sleeps for three seconds before checking the deadline. The default deadline is set to 180 seconds after the pre-poll authentication/isolation checks. Network request durations and the final sleep can make wall-clock runtime longer than that parameter. The parameter is not a hard process execution limit.

If the loop expires without `ready`, the script throws with the upload ID and last observed status. That stops this verification attempt. It does not cancel queued messages, running provider requests, or later database updates.

A ready response must satisfy all current result assertions.

- `upload.pageCount` equals 3.
- Exactly two documents are returned.
- The first document covers pages 1 through 2.
- The second document covers page 3 through 3.
- The first document Markdown contains `INVOICE 1842` and the literal page-break marker `<!-- page break -->`.
- The second document Markdown contains `COMMUNITY BOOK FAIR`.

These content checks use PowerShell's `-notmatch` matching semantics. They do not compare complete Markdown byte-for-byte, validate exact titles, inspect all OCR page content, or assert the complete structured response schema.

The expected ranges match [SplitResult.ConnectTo](../AIESummit.Agents/SplitStep.cs), which validates a complete ordered partition and joins page Markdown with the separator. A failed smoke assertion can originate in OCR text extraction, model boundary selection, stored step configuration, response application, or response projection. Inspect that chain before changing the expected result.

## List isolation

After the document assertions, the script reads the upload list as each user. It requires the new upload ID to appear in the owner's list and not appear in the other user's list.

These are fresh synthetic accounts, so the current upload should appear in the owner's newest page. The check does not exercise pagination through more than 50 uploads, arbitrary cursor handling, deleted uploads, or simultaneous uploads for the same account.

The detail and list isolation checks are distinct. Preserve both when refactoring the script, because a protected detail route does not prove that a list projection uses the same owner predicate.

## Admin telemetry assertions

The script reads the local Admin token from a path relative to `$PSScriptRoot`, trims it, and sends it in a bearer header. It requires an anonymous request to `/admin/v1/agents/executions` to return 401 before issuing authorized queries.

It creates a new deadline 15 seconds in the future and polls once per second for successful recorded runs. This window is independent of `TimeoutSeconds` and starts after the upload/list assertions.

The two queries are exact current script behavior.

```text
/admin/v1/agents/executions?entityType=UploadedFile&entityId=<upload-id>
/admin/v1/agents/executions?agentName=LlmPromptAgent&take=50
```

From the upload query, it finds a run named `OcrAgent` with state `Succeeded` and a run whose status field is `LLMStepStatus` with state `Succeeded`. From the prompt query, it finds a successful run with `rootEntityId` equal to the upload ID.

The prompt assertion does not independently test `rootEntityType`, `stepTag`, exact prompt ID, or retry lineage. The step assertion uses the status field rather than an exact closed-generic agent name. Report these actual checks rather than claiming a complete correlation contract was tested.

The prompt query examines a recent bounded set of at most 50 returned items. Under substantial unrelated activity, the relevant record can fall outside that result even when processing succeeded. The script does not inspect all availability/boundary metadata or perform a paginated exhaustive history search.

A telemetry failure after a ready document does not establish that OCR failed. It can reflect an expired Admin token, missing provider history, indexing/emission timing, environment mismatch, missing root enrichment, or a bounded-query limitation. Follow [Telemetry.AGENTS.md](../AIESummit.Agents/Telemetry.AGENTS.md) to distinguish these cases.

## Cleanup and retained state

The `finally` block removes only the generated temporary PDF, using its literal path when it exists. This runs after success or a thrown failure once the try/finally body has been entered.

The script does not delete either Firebase account, the upload, source bytes, OCR pages, logical documents, images returned by providers, prompts, queue messages, logs, or audit history. It does not stop containers or remove volumes. Repeated runs accumulate development data.

Do not silently add broad cleanup or a database reset to make repeat runs look isolated. If a cleanup feature is requested, scope it to the synthetic run's identifiers and account for background work still using those rows. A timeout does not prove such work has stopped.

Keep password, ID tokens, refresh tokens, and the Admin token out of diagnostic output. Retain safe upload/execution identifiers when investigating a failed run. The temporary fixture is synthetic, but provider and authentication responses can contain sensitive operational data.

## Failure triage

| Failure phase | Evidence to inspect first |
| --- | --- |
| Sign-up/sign-in | Firebase service readiness, local project consistency, hardcoded origins |
| Upload request | API readiness, authentication, multipart/body validation, response status |
| Anonymous/other-user detail assertion | Endpoint authorization and owner query |
| Status remains queued | Worker activation, queue delivery, OCR eligibility and persisted OCR APIInfo |
| Status remains splitting | Prompt planning, stored split step/model, prompt execution and connection state |
| Ready but wrong documents | Returned ranges/text, OCR pages, effective prompt configuration |
| List isolation assertion | Owner IDs and the upload-list query |
| Admin 401 with bearer token | Local token lifetime and Admin authentication setup |
| Missing success telemetry | History-provider availability, root correlation, query bounds, recorded timestamps |

Use bounded container log tails and exact upload/prompt/execution IDs. Do not dump `.env`, full container environments, raw provider payloads, or authorization headers. Read the existing evidence before rerunning, because another invocation creates more accounts and initiates more provider work.

When reporting a failure, state the phase, upload ID when known, last observed status, and the narrow evidence supporting the diagnosis. Do not call a test timeout a cancellation or claim a provider error from an unrelated telemetry failure.

## What this script does not cover

It does not drive the browser, test refresh/sign-out races, validate responsive layouts, inspect object-URL revocation, or click an image. It does not test hosted Firebase, every Admin role, expired/malformed JWTs, token refresh, or all cross-user routes.

It does not systematically test empty/oversized/unsupported uploads, invalid OCR indexes, base64 failures, unsupported image MIME types, malformed metadata, missing ranges, retries, duplicate processing, concurrent writes, or failure rollback. Its text-only fixture also does not exercise extracted-image storage/download semantics.

It does not prove the current source definition matches stored runtime settings, benchmark provider performance, or assert deterministic model behavior across provider/model changes. The observed split is a concrete expected integration result, not a substitute for deterministic validation tests.

For a behavior change, add or run focused tests that directly exercise that risk. Static JSON/unit-level tests are useful for metadata validation and replacement rules. Controlled OCR fixtures are useful for parser branches. Browser checks are needed for frontend interactions. Keep live-provider smoke evidence distinct from those checks.

## Changing and reporting the smoke check

Preserve the fixture's clear document boundary, synthetic identities, both owner-isolation checks, and separate telemetry deadline unless the task deliberately changes them. If changing endpoints, compare [UploadEndpoints.cs](../AIESummit.Api/UploadEndpoints.cs), [OpenAPI paths](../AIESummit.Api/ApiSpec/Paths/uploads.json), and the frontend contract.

Validate the script syntax and run the intended integration path when its dependencies are available and execution is authorized. Report the actual outcome and remaining coverage gaps. Reading the script, building projects, or seeing the stack healthy does not count as executing this smoke test.

The success message summarizes completed assertions only after the telemetry checks pass. A truthful report can name the tested upload, expected ranges 1–2 and 3, owner isolation, and successful retained agent runs. Do not extend that report to browser verification, image coverage, or negative cases that the script did not exercise.
