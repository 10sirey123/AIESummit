# Benchmark corpora

Sources verified on **2026-11-07**. Folder name `corporea` follows the project owner's requested spelling.

## Availability and PDF coverage

| Dataset | Pinned release | Input formats | Original input PDFs | Benchmark role |
| --- | --- | --- | --- | --- |
| CUAD | v1, Hugging Face revision `a3c393f5d103fd0c516374e4fdff676c8176dcb1` plus authors' GitHub train/test archive | PDF, TXT, JSON, CSV, XLSX | **510 contract PDFs** | Expert clause extraction labels across 41 categories |
| MAUD | v1, Zenodo record `7500064` | TXT and CSV; documentation PDF separately | **None in this release** | Merger agreement understanding; 152 agreements and 92 questions |
| EDGAR-CORPUS | Zenodo record `5528490`, all 28 annual archives, 1993–2020 | Structured JSON | **None in this release** | Annual-report ingestion and financial-domain stress testing |

All three datasets are publicly downloadable. They do **not** all include original PDF inputs. CUAD supplies the PDF benchmark. MAUD's README PDF is documentation, not a contract; EDGAR-CORPUS contains parsed annual-report text rather than filing PDFs. No text has been converted to PDF or represented as an original document.

The pinned CUAD mirror contains all 510 contract PDFs and its complete master annotation files, but only 200 standalone contract TXT files (plus a separate README TXT). Use the annotated contexts in `CUAD_v1.json` and the authors' split archive for text evaluation; do not assume a standalone TXT exists for every PDF. Preserve original filenames and use an explicit mapping when matching PDFs to annotation titles. Several paths exceed 260 characters; Windows tooling must support long paths. The bootstrap utility handles this explicitly.

## Local layout

```text
corporea/
  README.md                 # tracked catalog
  sources.lock.json         # tracked source versions, URLs, sizes, checksums
  manifest.json             # ignored generated verification inventory
  cuad/
    raw/CUAD_v1/            # source PDFs, available text, labels, documentation
    archives/official-data.zip
    official-splits/         # authors' train/test data
  maud/
    archives/maud_v1.zip
    raw/                    # original archive contents
    documentation/          # MAUD v1 README.pdf
  edgar-corpus/
    archives/               # 1993.zip through 2020.zip
    raw/                    # original archive contents
```

Run `python scripts/download_benchmarks.py` from the repository root to reproduce the download. Downloads are pinned in `sources.lock.json`; no account or API token is needed to fetch these payloads. Original downloads are verified against upstream Git blob SHA-1, LFS SHA-256, or Zenodo MD5 as applicable, with local SHA-256 values recorded in the generated manifest. Archive extraction verifies CRC integrity. Recheck extracted files and download checksums using `python scripts/download_benchmarks.py --verify-only`.

The manifest's `complete` field reports whether every pinned payload passed. Partial downloads use `.part` filenames and are never reported as complete. Extraction completion markers are local bookkeeping, not dataset inputs. Do not count them, documentation PDFs, or archives as benchmark documents.

## Sources, attribution, and licenses

- **CUAD:** The Atticus Project; Dan Hendrycks, Collin Burns, Anya Chen, and Spencer Ball. *CUAD: An Expert-Annotated NLP Dataset for Legal Contract Review* (2021). [Publisher](https://www.atticusprojectai.org/cuad/), [original data mirror](https://huggingface.co/datasets/theatticusproject/cuad), [authors' code and split archive](https://github.com/The-Atticus-Project/cuad), [paper](https://arxiv.org/abs/2103.06268). Dataset license: **CC BY 4.0**.
- **MAUD:** The Atticus Project; Steven H. Wang et al. *MAUD: An Expert-Annotated Legal NLP Dataset for Merger Agreement Understanding* (2023). [Publisher](https://www.atticusprojectai.org/maud/), [versioned dataset](https://doi.org/10.5281/zenodo.7500064), [authors' repository](https://github.com/The-Atticus-Project/maud), [paper](https://arxiv.org/abs/2301.00876). Dataset license: **CC BY 4.0**.
- **EDGAR-CORPUS:** Lefteris Loukas, Manos Fergadiotis, Ion Androutsopoulos, and Prodromos Malakasiotis. *EDGAR-CORPUS: Billions of Tokens Make The World Go Round* (2021). [Versioned dataset](https://doi.org/10.5281/zenodo.5528490), [authors' Hugging Face mirror](https://huggingface.co/datasets/eloukas/edgar-corpus), [paper](https://aclanthology.org/2021.econlp-1.2/). Downloaded Zenodo release declares **CC BY 4.0**. The Hugging Face card labels its mirror Apache 2.0; this project uses the Zenodo release and records that release's license rather than treating those declarations as interchangeable.

Retain attribution and upstream notices when redistributing data or derivatives. Dataset-release licensing is distinct from the authors' software licensing.

## Evaluation boundaries

- Keep original train/dev/test splits for comparisons with published benchmarks. **MAUD's supplied splits share 152 contract names across train, dev, and test**; they are not an unseen-document evaluation. Define a separate, clearly named document-held-out protocol when measuring generalization to new contracts, while retaining the original files unchanged. Where no evaluation split exists, define and record document-level splits before tuning models; deduplicate across splits.
- Clause annotations are evidence for extraction evaluation, not a universal legal-risk policy. Define a separate risk rubric for indemnity limits, non-competes, renewal dates, and termination triggers.
- CUAD covers diverse commercial contracts; MAUD covers M&A agreements. Neither should be presented as representative coverage of all MSAs and NDAs without a separate evaluation.
- EDGAR-CORPUS is a corpus of 10-K annual reports, not an annotated collection of standalone financial contracts. Use it for ingestion and domain coverage; it does not supply gold labels for contract-risk scoring.
- Preserve raw data. Store OCR, parsed outputs, derived fixtures, model predictions, and evaluation results outside raw directories. Any later rendered PDFs must be clearly labeled as derived and evaluated separately from original-PDF extraction.
