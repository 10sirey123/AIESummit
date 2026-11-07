# Project guidance

## Purpose and technology
- Build a contract intake and analysis system for legal contract risk and clause auditing.
- Initial extraction targets: indemnity and liability limits, non-compete restrictions, renewal dates, and termination triggers in MSAs and NDAs.
- Use C# and .NET 10 (`net10.0`) for the application. The installed .NET 10 SDK is 10.0.403. No application has been scaffolded yet.
- Keep extraction evidence (document, page when available, and quoted text) separate from risk interpretations. Do not invent missing clauses or treat model output as legal advice.

## Keep this file current
- Read this file before changing the project.
- Update this file in the same change whenever architecture, commands, dependencies, project layout, benchmark policy, or development workflow changes.
- Before finishing work, check that these instructions and the README describe the actual repository. Record unfinished work honestly; do not describe planned features as implemented.

## Repository layout and commands
- Public GitHub repository: `https://github.com/10sirey123/AIESummit`; `origin` points to this repository.
- `README.md`: project status, setup, and contribution guidance.
- `corporea/`: benchmark datasets (spelling requested by the project owner).
- `corporea/sources.lock.json`: pinned upstream downloads and integrity metadata.
- `corporea/manifest.json`: generated local download and archive verification report.
- `corporea/README.md`: dataset provenance, format availability, and benchmark limitations.
- `scripts/download_benchmarks.py`: dependency-free Python 3.12+ bootstrap utility; Python is used only for dataset preparation, not the application stack.
- Download and verify: `python scripts/download_benchmarks.py`.
- Recheck local payloads without downloading: `python scripts/download_benchmarks.py --verify-only`.
- Build/test commands will be added when the .NET solution exists. Do not claim a .NET build was run before then.

## Benchmark policy
- Preserve original downloaded datasets and upstream train/dev/test splits. Keep transformations, OCR, model outputs, and evaluation results separate from raw files.
- CUAD is the initial annotated clause extraction and original-PDF benchmark. MAUD covers merger agreement understanding. EDGAR-CORPUS provides annual-report text for ingestion and domain stress testing, not gold clause-risk annotations.
- MAUD and EDGAR-CORPUS do not ship original contract/filing PDFs in these releases. Documentation PDFs do not count as input documents. Never label rendered text or synthetic PDFs as originals.
- Keep dataset provenance, licenses, versions, checksums, and limitations documented. Do not train or tune on evaluation splits. MAUD's upstream splits share contract names; preserve them for published-benchmark comparisons, but create a separately documented document-held-out protocol for unseen-contract evaluation. Prevent document overlap in that protocol.
- Raw downloads, extracted corpora, and generated reports are local and ignored by Git. Track download tooling, source locks, and dataset documentation.
- Preserve long original filenames; several CUAD paths exceed the legacy Windows 260-character limit. Use tools that support long paths when counting or opening these files.
- Do not place real private contracts, credentials, or personal data in Git. Do not send contract contents to an external service without authorization.
