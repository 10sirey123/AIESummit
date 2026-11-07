# Legal Contract Risk & Clause Auditing

This project will ingest contracts and extract evidence for reviewing indemnity and liability limits, non-compete restrictions, renewal dates, and termination triggers. The initial business focus is master service agreements (MSAs) and NDAs.

The application will use **C# and .NET 10**. This repository currently contains benchmark data preparation and project guidance; the application has not been scaffolded.

## Getting started

- Install a stable .NET 10 SDK. `global.json` selects SDK 10.0.403 with forward roll to later stable .NET 10 feature bands.
- Use Python 3.12 or later for the dependency-free dataset preparation utility.
- Read [AGENTS.md](AGENTS.md) for working conventions and the requirement to maintain these instructions alongside project changes.

```powershell
python scripts/download_benchmarks.py
python scripts/download_benchmarks.py --verify-only
```

The first command downloads all pinned CUAD, MAUD, and EDGAR-CORPUS files into `corporea/` and extracts the archives. Allow approximately 11.2 GB for downloads plus space for extracted data. Existing valid downloads are reused; interrupted downloads resume when the server supports byte ranges. The second command checks payload checksums and extracted file integrity without network access. Either command returns a nonzero exit code if verification fails and writes a local `corporea/manifest.json` report. Download concurrency can be adjusted with `--workers` (default: 6).

See [the benchmark catalog](corporea/README.md) for original-PDF availability, sources, licenses, and evaluation limitations. Dataset payloads are excluded from Git; download tooling, documentation, and source pins are tracked.

No .NET build or test command exists until the application is created.
