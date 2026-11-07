# Configuration from code with Blueshift seeds

The bundled Blueshift CLI runs the framework's `SeedPlanner` and transactional `SeedImporter` against `AIESummitDb`. The small `AIESummit.Configuration` program only exports authored configuration and applies the additive seed-registry schema upgrade; it does not implement a second importer or provider loop. Normal users need no private Blueshift access.

## Use locally

Start the ordinary stack first with `./start.ps1`. From the repository root in PowerShell 7:

```powershell
./scripts/admin.ps1 build
./scripts/admin.ps1 schema
./scripts/admin.ps1 export
./scripts/admin.ps1 validate
./scripts/admin.ps1 plan
```

`schema` adds only the seed identity table/indexes to an already initialized application database. It does not create an empty application schema, synchronize configuration or send messages. API startup also applies this idempotent upgrade under its existing advisory lock. Existing installations can use the explicit command without restarting services.

Inspect the exported `artifacts/seeding/workflow.json` and the plan's `Items` in `artifacts/seeding/plan.json`. Plans name fields, actions and stable identities; they deliberately do not print database values or credentials. `validate` checks manifest structure without connecting to PostgreSQL. `plan` reads the database but does not save rows or register identities. Apply only the reviewed export and plan:

```powershell
$plan = Get-Content artifacts/seeding/plan.json -Raw | ConvertFrom-Json
./scripts/admin.ps1 apply -Preview $plan.plan.PreviewToken
./scripts/admin.ps1 plan
```

The wrapper requires a preview token even for ordinary updates. If the authored manifest, policy or relevant persisted state changes, Blueshift rejects the stale token. Run a fresh plan and review it again. Apply never automatically re-exports or silently generates a replacement token. A subsequent plan should contain only `keep` actions. The first apply may register identities for existing rows without changing their values.

After changing C# definitions, repeat **build → export → validate → plan → apply**. The build creates only the one-off configuration image, not replacements for the running API, worker or Admin containers. The exporter uses the definitions compiled into that image. Changes to subject/metadata processing code still require the ordinary application image deployment.

## Managed fields and operator settings

`WorkflowSeeds.Create()` exports seven records in venture `aiesummit`, set `workflow`:

| Records | Source and behavior |
| --- | --- |
| `split-upload`, `classify-page-sections` | Call the actual `SplitStep.Definition()` and `PageSectionsStep.Definition()`. Export name, description, subject/DTO type, templates, lambda, validation state, temperature, reasoning, parser and multimodal settings. |
| Three model identities | `foundry-split`, `gemini-page-sections`, `meta-muse-wonder`. Always use `keep`, creating defaults if absent and preserving existing provider/name settings. |
| Two API identities | `geminiapi`, `meta`, matched by `Identifier`. Always use `keep`; new rows contain the official endpoint/auth type and are disabled. No keys, secrets or tokens are exported. Azure/OCR connection provisioning stays in bootstrap. |

The wrapper's default step policy is `update`, which updates only listed fields. `-Policy keep` reports differences without changing existing configuration; `-Policy merge` only fills null/empty values. Use the same policy for plan and apply. The model/API records retain their explicit `keep` policy regardless of the step policy.

By default, step `Enabled` and `Model` are omitted, preserving existing selections such as Meta Muse Wonder. A step created by the default export is disabled and unbound; select its model and enable it in Admin, or deliberately export bindings:

```powershell
./scripts/admin.ps1 export -IncludeBindings
./scripts/admin.ps1 plan
```

This adds model references by stable tag and the definitions' enabled settings. With update policy it changes existing bindings too: the current definitions bind splitting to `foundry-split` and page analysis to `gemini-page-sections`. Review this explicitly before applying; it can replace an operator-selected Meta model. Provider APIInfos still require separate credential provisioning/enabling.

The manifest contains no database IDs, audit timestamps, uploads, pages, results or LLMPrompt history. Existing natural-key matches remain operator-owned in the registry. This wrapper exposes no seed deletion/adoption operations. Exported manifests are local review artifacts, not files to edit with secrets or private contract text.

## Persistence and execution boundary

`SeedIdentities` maps framework `SeedIdentity` records. The additive upgrade is `AIESummit.Domain/SeedSchema.cs`. Configuration data seeding does not migrate other application tables.

`SeedImporter` replans and applies through the framework repository gateway inside a serializable database transaction. A failed later write rolls back earlier entity/registry writes. The dedicated CLI host registers no external broadcaster, publishers or queue receiver. It does not run OCR/LLMs, replay old prompts or enqueue existing pages. New submissions use the resulting enabled configuration through the normal worker. Existing prompts retain captured inputs; use a separate deliberate processing action if reanalysis is required.

The one-off Compose service in `deploy/compose.cli.yaml` joins the existing private app network. PostgreSQL remains unpublished. Local operator commands have database privileges, rather than using a Firebase login or Admin browser token. The checked-in connection is the existing local-development credential; hosted environments need separate private connection injection and deployment review. Keys continue to be provisioned through write-only APIInfo Admin updates.

## Verification

```powershell
dotnet test AIESummit.Tests/AIESummit.Tests.csproj
./scripts/admin.ps1 build
./scripts/test-seeding.ps1 -VerifyCli
./scripts/verify-dependencies.ps1
```

The normal test run checks the export contract and skips the PostgreSQL case when its explicit test connection is absent. `test-seeding.ps1` creates its own uniquely named PostgreSQL container/database, exercises registry creation/repeated upgrade, read-only planning, preservation of synthetic credentials and selected models, code-template updates, repeat-apply convergence/timestamps, stale-plan rejection, transactional rollback and optional model bindings. It refuses any database name other than `aiesummit_seed_test` and removes only its own disposable container/volume in `finally`.

With `-VerifyCli`, it additionally uses the built configuration image for actual CLI export, plan, previewed apply and repeat-plan convergence. No live providers or contract data are used. Docker must be running; the CLI verification image must be built first. Ignored local test exports remain under `artifacts/`.

Maintainers build `cli-linux-x64.tar.gz` from the same revision and compatibility patches as the other bundled hosts. Full refreshes use `scripts/package-blueshift.ps1`; the manifest/verifier now cover eight artifacts, including the CLI. No .NET tool installation or private feed is needed for ordinary operation.
