# Configuration operator program

Workflow exports include `extract-contract-targets` and ten `ExtractionType` seeds. Catalog records use Keep policy; default step exports preserve model bindings and enabled flags. Startup inserts missing extraction records only. See [contract extraction](../docs/contract-extraction.md).

Read [configuration seeding](../docs/configuration-seeding.md). This .NET 10 executable references the existing Agents/Domain projects to export the real step definitions as a framework `SeedManifest`. It also exposes only the additive seed-registry schema upgrade. Generic validate/plan/apply operations belong to the bundled Blueshift CLI, not this executable.

Keep an explicit field allowlist. Never export credentials, audit IDs, source documents, prompts, results or environment secrets. Model/API records use keep policy; default step exports omit model bindings/enabled flags. Optional bindings must remain explicit. New unbound steps stay disabled. Do not register agent publishers, receivers or live provider calls here.

Use `scripts/admin.ps1 build|export|validate|plan|apply|schema`. Apply requires a reviewed preview token and must consume the same exported manifest; never regenerate it during apply. `scripts/test-seeding.ps1 -VerifyCli` checks PostgreSQL and actual CLI integration in a disposable container. Preserve the production/local application database during tests.
