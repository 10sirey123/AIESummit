# Benchmark corpora

Sources verified on **2026-11-07**. Folder name `corporea` follows the project owner's requested spelling.

See the [extraction target specification](../docs/extraction-targets.md) for the original project scope, 15 selected CUAD/MAUD targets and 10 proposed healthcare targets. It distinguishes existing gold labels from new annotation needs; EDGAR supplies no gold clause detections.

## Prepared document/label pairs

The working benchmark collection is `corporea/benchmarks/`, with three corpus folders:

| Folder | Documents | PDF source | Label content |
| --- | ---: | --- | --- |
| `benchmarks/cuad/` | 510 | Byte-for-byte copies of original contract PDFs | All 41 upstream clause categories per document, including absent categories and exact answer spans |
| `benchmarks/maud/` | 152 | PDFs rendered from original agreement TXT files | Original `main` deal-point questions, categorical answers, evidence excerpts, and upstream row/split references |
| `benchmarks/edgar-corpus/` | 100 | PDFs rendered from parsed annual-report sections | Filing metadata; expected detections explicitly **unlabeled** |

Each folder contains `0001.pdf`, `0001.json`, `0001.txt`, and so on. The JSON is the labeling sidecar; the TXT is the reference text. `benchmarks/manifest.json` indexes the prepared pairs. IDs are stable for the pinned sources and selection policy. Original filenames, source paths, hashes, and PDF provenance are retained in each sidecar.

No contract analysis model or external service is invoked during preparation. No application test suite is created. The labels are upstream annotations, not new risk judgments. EDGAR has no upstream clause labels: `annotation_status` is `unlabeled` and `expected_detections` is **null**, which must never be interpreted as an empty or negative gold label set.

Downloads were intentionally stopped at the owner's request. CUAD and MAUD are complete. Eight EDGAR annual archives are verified and extracted: **1993, 1994, 1995, 1998, 2004, 2005, 2017, 2019**, containing **50,637 JSON reports**. Twenty incomplete archive transfers remain available for resuming if requested. The 100 prepared EDGAR documents are selected from the eight completed years, with equal year quotas (13 for the first four years, 12 for the others), ordered within each year by SHA-256 of the original filename; empty reports are skipped. They are a sample, not the complete EDGAR-CORPUS release.

### Prepare again without downloading

```powershell
python -m pip install -r scripts/benchmark-requirements.txt
python scripts/prepare_benchmarks.py
```

Preparation reuses identical existing pairs and refuses to silently overwrite differing labels or documents. `--dataset cuad`, `--dataset maud`, or `--dataset edgar-corpus` restricts preparation; `--limit N` caps that corpus's output. The default is all 510 CUAD, all 152 MAUD, and 100 EDGAR reports. The script requires ReportLab and pypdf; the application remains .NET 10.

### Label sidecar conventions

- `schema_version`, `dataset`, and `sample_id` identify the record.
- `document.file`, `document.sha256`, `document.pages`, and `document.origin` identify the paired PDF. Origin is `original_pdf` for CUAD and `rendered_from_text` for MAUD/EDGAR.
- `source` retains the upstream document identity, relative path (from `corporea`), checksum, license, and relevant split/filing metadata.
- `reference_text` identifies the companion TXT and its checksum. CUAD answer `start`/`end` positions are zero-based Unicode code-point offsets with an exclusive end into this TXT, **not PDF offsets or page coordinates**. All 13,823 original answer spans are checked against the source text during preparation.
- CUAD `expected_detections` holds `category`, `question`, `expected_present`, `answers`, and the upstream question ID. A gold category marked absent is different from EDGAR's unknown labels. The original train/test split is retained.
- MAUD `expected_detections` holds questions/subquestions, category, original answer/label, evidence excerpt, and source CSV row/split. Only the **20,623 original `main` rows** are attached to full documents. Abridged contexts and synthetic rare-answer rows are retained in raw data but excluded from full-document gold sidecars. Upstream excerpts may contain original page hints or formatting differences; offsets are null and no mapping to the newly rendered PDF's pages is claimed.
- Rendered PDFs reflow source text into readable pages and identify themselves in the footer. The TXT preserves the reference text; PDF pagination and layout do not reproduce the original filing. Treat these separately from original-PDF OCR benchmarks.

The full-release downloader lock still lists all 28 EDGAR years. Its local `manifest.json` reports 754 verified downloads out of 774 and `complete: false`; the full-lock `--verify-only` command therefore returns nonzero for the intentionally missing archives. Use `benchmarks/manifest.json` for the prepared working collection. Do not resume bulk downloads during routine preparation or verification.

## Availability and PDF coverage

| Dataset | Pinned release | Input formats | Original input PDFs | Benchmark role |
| --- | --- | --- | --- | --- |
| CUAD | v1, Hugging Face revision `a3c393f5d103fd0c516374e4fdff676c8176dcb1` plus authors' GitHub train/test archive | PDF, TXT, JSON, CSV, XLSX | **510 contract PDFs** | Expert clause extraction labels across 41 categories |
| MAUD | v1, Zenodo record `7500064` | TXT and CSV; documentation PDF separately | **None in this release** | Merger agreement understanding; 152 agreements and 92 questions |
| EDGAR-CORPUS | Zenodo record `5528490`, all 28 annual archives, 1993–2020 | Structured JSON | **None in this release** | Annual-report ingestion and financial-domain stress testing |

All three datasets are publicly downloadable. They do **not** all include original PDF inputs. CUAD supplies the original-PDF benchmark. MAUD's README PDF is documentation, not a contract; EDGAR-CORPUS contains parsed annual-report text rather than filing PDFs. The prepared MAUD/EDGAR pairs described above include derived text-rendered PDFs, explicitly distinguished from originals.

The pinned CUAD mirror contains all 510 contract PDFs and its complete master annotation files, but only 200 standalone contract TXT files (plus a separate README TXT). Use the annotated contexts in `CUAD_v1.json` and the authors' split archive for text evaluation; do not assume a standalone TXT exists for every PDF. Preserve original filenames and use an explicit mapping when matching PDFs to annotation titles. Several paths exceed 260 characters; Windows tooling must support long paths. The bootstrap utility handles this explicitly.

## Local layout

```text
corporea/
  README.md                 # tracked catalog
  sources.lock.json         # tracked source versions, URLs, sizes, checksums
  manifest.json             # ignored generated verification inventory
  benchmarks/
    manifest.json           # prepared working collection index
    cuad/                   # 0001.pdf, 0001.json, 0001.txt, ...
    maud/                   # same numbered pairing convention
    edgar-corpus/           # 100 unlabeled report/metadata pairs
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

Downloader recovery checks run with `python -m unittest discover -s scripts -p test_download_benchmarks.py -v`. These use a local server to verify that prematurely ended responses retain resumable bytes and that full responses safely replace partial files when the server ignores a requested range.

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
