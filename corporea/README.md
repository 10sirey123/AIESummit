# Benchmark datasets

`corporea` is the project owner's chosen directory name. Raw downloads, extracted files, and generated verification reports stay local and are ignored by Git. Only this documentation and `sources.lock.json` are tracked here.

## Provenance and formats

| Dataset | Pinned source | License recorded in source lock | Use and limitations |
| --- | --- | --- | --- |
| CUAD | Hugging Face `theatticusproject/cuad` revision `a3c393f5d103fd0c516374e4fdff676c8176dcb1`; official split archive from The-Atticus-Project/cuad revision `67faa0e6023b04fcaae6cc09497ab00e5d63a2a2` | CC-BY-4.0 | Annotated clause extraction and original contract PDF benchmark. Includes annotations, clause tables, and original PDFs. |
| MAUD | [Zenodo record 7500064](https://doi.org/10.5281/zenodo.7500064), v1 archive | CC-BY-4.0 | Merger agreement understanding. This release does not ship original contract PDFs; its README PDF is documentation. |
| EDGAR-CORPUS | [Zenodo record 5528490](https://doi.org/10.5281/zenodo.5528490), annual archives listed in the lock | CC-BY-4.0 | Annual-report text for ingestion and domain stress testing, not gold clause-risk annotations. This release does not ship original filing PDFs. |

Exact download URLs, byte sizes, upstream checksums, and extraction destinations are recorded in `sources.lock.json`. Retain upstream license and attribution materials with downloaded data. These datasets do not establish complete coverage of the project's intended MSA/NDA clauses or provide legal advice.

## Download and verification

From the repository root with Python 3.12+:

```sh
python scripts/download_benchmarks.py
python scripts/download_benchmarks.py --verify-only
```

The dependency-free utility checks downloaded payload sizes and upstream checksums, extracts archives, and records SHA-256 values and failures in `manifest.json`. Verification-only mode also checks extracted files against archive sizes and CRC values. A `complete: true` report indicates that all locked payloads passed that run; the presence of files alone does not establish completion.

## Evaluation policy

Preserve originals and upstream train/dev/test splits. Store transformations, OCR, model output, and evaluation results separately. Do not train or tune on evaluation splits, and prevent document overlap across splits. Rendered text and synthetic PDFs must never be labeled original documents. Keep quoted extraction evidence separate from risk interpretation.
