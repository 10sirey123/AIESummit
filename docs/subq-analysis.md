# SubQ Preview through native BlueShift

The bundled `10.0.0-aiesummit.cc69aa4.subq1` framework adds provider **SubQ** (enum value 6). Existing provider numbers and Azure, Gemini and Meta behavior are preserved. `deploy/patches/subq-provider.patch` adds factory/worker routing and a provider-specific subclass of the shared Responses adapter. It uses `Authorization: Bearer` at `https://api.subq.ai/v1/responses`, strict `text.format` JSON schema, native model identity and usage, and the existing completed-output and evidence checks. Audio transcription is unsupported. No separate application processing loop is used.

All packages and Linux hosts are rebuilt together from the pinned source plus the four checksummed patches. Patches apply sequentially in filename order. Normal consumers need no private framework checkout. No database schema upgrade is required.

## Configure

Bootstrap creates missing `APIInfo(subq)` with a disabled empty key and `LLMModel` with tag/name `subq-preview`, provider SubQ. It preserves existing configuration and application step selections.

Put `SUBQ_API_KEY` in the ignored `.env`, then explicitly import it through authenticated, write-only Admin credentials:

```powershell
./start.ps1
./scripts/configure-subq.ps1 -ImportLocalKey
```

Alternatively supply `-KeyFile C:\private\subq-key.txt`. Runtime workers resolve persisted `APIInfo`, never the environment key. After rotating an existing key, restart the worker to clear its credential cache. Keys remain excluded from generic reads and output.

To select SubQ on the native page-classification step:

```powershell
./scripts/configure-subq.ps1 -ImportLocalKey -SelectPageStep
docker compose restart worker
```

This selects model `subq-preview`, enables `classify-page-sections`, and sets reasoning effort to `low`. **Use low reasoning for structured LLMSteps.** Live testing with `none` returned explanatory prose before JSON despite strict schema; native parsing correctly rejected it. Do not strip arbitrary text to bypass validation. Low reasoning passed the simple structured page test; broader task quality requires separate evaluation.

SubQ must enable Preview access for the organization/key. A valid key can still return `403 model_not_allowed`; `GET https://api.subq.ai/v1/models` lists permitted models. During setup an initial denial was followed by successful requests with the key in `.env`.

## Verify

```powershell
dotnet test AIESummit.Tests/AIESummit.Tests.csproj
./scripts/verify-dependencies.ps1
./scripts/smoke.ps1 -VerifyPageSections -ContractFixture -ExpectedPageModel subq-preview -TimeoutSeconds 240
```

The seven SubQ transport cases cover bearer authentication, strict schema, usage/provider identity, incomplete-output rejection, and rejection of unexpected destinations before credentials are sent. They intercept HTTP and do not establish live access.

The live smoke checks synthetic uploads, ownership isolation, source fidelity, OCR/splitting, all three page results with exact evidence, actual used model/provider, and successful correlated queue execution telemetry. Synthetic accounts, records and telemetry remain; no private contracts are used.

November 7, 2026: the isolated native test used a disabled manual-only step **5**, `subq-connection-test`, and the normal `LLMPromptFactory` → `LlmPromptWorker` → `PageSectionsResult` path. Upload **38**, page **205**, prompt **138** completed with SubQ/subq-preview, one API attempt, 482 total tokens and validated payment evidence. The preceding `none` experiment (upload 37, prompt 137) remains terminal and retained. The isolated test registered no publishers and committed only terminal work, so it cannot be picked up as pending production processing. Its diagnostic harness and output are under ignored `artifacts/subq-smoke*`.

Official protocol references: [Responses](https://docs.subq.ai/endpoints/responses-api/), [structured outputs](https://docs.subq.ai/using-the-api/structured-outputs/), and [models/access](https://docs.subq.ai/using-the-api/models/).

The full queue-backed smoke also passed November 7, 2026: upload **39**, pages **206–208**, SubQ prompts **142, 141, 139**, each processed in one provider attempt. It verified actual SubQ model usage, validated exact source quotes, original-source byte fidelity, owner isolation, OCR/split ranges and authenticated telemetry. Page LLMStep **2** is currently enabled with model **4** and reasoning `low`; splitting and extraction retain their existing model selections. Logs are retained under ignored `artifacts/subq-full-smoke.log`.

Validation: solution build passed; focused tests reported **13 passed, 1 skipped** (the opt-in disposable PostgreSQL seed test); all eight dependency checksums passed; Docker targets rebuilt and the stack started; quiet Compose validation and diff whitespace checks passed. These synthetic checks establish working integration, not benchmark extraction accuracy or support for every SubQ modality.
