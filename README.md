# AIESummit

Contract intake and analysis project for legal contract risk and clause auditing.

Initial extraction targets are indemnity and liability limits, non-compete restrictions, renewal dates, and termination triggers in MSAs and NDAs. Extraction evidence (document, page when available, and quoted text) must remain separate from risk interpretations. Model output is not legal advice.

## Current status

The repository contains benchmark preparation tooling and a .NET SDK configuration. The application has not been scaffolded; contract ingestion, extraction, risk analysis, and evaluation are not implemented yet.

The planned application stack is C# and .NET 10 (`net10.0`). `global.json` selects SDK 10.0.403 with `latestFeature` roll-forward. There are no application build or test commands yet.

## Benchmark preparation

Use Python 3.12+; the download utility uses only the standard library. Python is used for dataset preparation, not the application.

```sh
python scripts/download_benchmarks.py
python scripts/download_benchmarks.py --verify-only
```

Downloads are pinned in `corporea/sources.lock.json`. The utility checks upstream sizes and checksums and writes a local verification report to `corporea/manifest.json`. See [dataset documentation](corporea/README.md) for provenance and limitations. Dataset downloads can be large; completion must be checked in the generated report.

## Contributing

This project is hosted publicly at [10sirey123/AIESummit](https://github.com/10sirey123/AIESummit). Read [AGENTS.md](AGENTS.md) before making changes, and keep project documentation current.

Keep raw datasets, generated outputs, private contracts, credentials, and personal data out of Git. Preserve upstream evaluation splits and keep transformations and model outputs separate from raw data. Do not send contract contents to external services without authorization.
