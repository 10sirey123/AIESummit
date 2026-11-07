# Ten-target contract extraction baseline

Measured November 7, 2026 against the local Docker stack and live configured providers. This is a deliberately naive, single-extraction-step engineering baseline, not a production-readiness claim.

## Collection and execution

**20 original CUAD PDFs, 119 pages, 200 document/target presence decisions.** Select from the official test split, 1–10 pages per PDF, ordered by SHA-256 of `aiesummit-extraction-v1/<sample_id>`, with engineering preflight `0190` explicitly excluded. Gold labels were used for scoring, not sent to the model. Raw sources and upstream split files were preserved. No downloads were resumed.

The preflight exposed abbreviated quotes; a generic instruction to copy short contiguous quotes was added before the measured configuration was frozen. The final set excludes that preflight document. Nineteen measured files were retained from the initial batch and the next eligible hash-ranked file (`0487`) replaced the preflight file. No other selection changes were based on labels or results.

Workflow: original PDF → owner-scoped upload → Mistral OCR (or existing native cache) → native document splitting → one native `extract-contract-targets` prompt per logical document. The independent per-page summaries were explicitly skipped. The 20 PDFs produced **24 logical-document analyses**.

| Measurement | Result |
| --- | --- |
| Fully accepted source files | 3/20 (15%) |
| Rejected source files | 17/20 |
| Accepted logical-document analyses | 6/24 |
| Sum of per-file wall times | 301.44 seconds |
| Median per-file wall time | 15.95 seconds |
| Reported LLM tokens | 199,373 |
| Used configured model | `gpt-4.1-mini`, framework provider `OpenAI` |
| Immutable provider model revision | Not exposed; recorded as unknown |
| Input/output token breakdown | Not populated by the configured adapter; unavailable |
| OCR tokens / monetary cost | Unavailable / not estimated |

Wall times include upload, queueing, OCR, splitting, extraction and polling. The sum is not a sum of pure provider latencies. Reported token counts include rejected responses; they are native counters rather than billing verification. These figures cover the final 20-file set and exclude extra engineering preflight/superseded-selection work.

| Native step | Prompts | API attempts | Reported tokens |
| --- | ---: | ---: | ---: |
| `split-upload` | 20 | 20 | 75,252 |
| `extract-contract-targets` | 24 | 24 | 124,121 |

Extraction system-prompt SHA-256: `5FB89BF3222B652EC49F128E2942890D1B6CCA798A7E833EE1E4E9165F97B219`. Pinned per-analysis definitions, source hashes, input/schema hashes, prompt IDs and model identifiers are retained in the local result files.

## Presence comparison

**Model-reported classifications are not accepted findings.** The model can name the right target while supplying an invalid quote, value or response shape. The application rejects the entire logical-document response when validation fails.

| View (all 200 decisions) | TP | FP | FN | TN | Precision | Recall | F1 | Accuracy |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Diagnostic model-reported presence | 46 | 16 | 12 | 126 | 74.2% | 79.3% | 76.7% | 86.0% |
| Published, evidence-validated findings | 3 | 1 | 55 | 141 | 75.0% | 5.2% | 9.7% | 72.0% |

Uncertain, not-applicable, missing and unpublished results count as no detection in the presence comparison. Failed analyses are included, not excluded from the validated score. Some PDFs contain multiple logical documents, so an accepted analysis can coexist with a failed analysis within the same source file. These differences explain why there are more accepted analyses than fully accepted files.

| Target | Gold positives | Model TP / FP / FN | Model precision | Model recall | Model F1 |
| --- | ---: | --- | ---: | ---: | ---: |
| Effective Date | 14 | 13 / 4 / 1 | 76.5% | 92.9% | 83.9% |
| Expiration Date | 15 | 12 / 1 / 3 | 92.3% | 80.0% | 85.7% |
| Renewal Term | 3 | 3 / 1 / 0 | 75.0% | 100.0% | 85.7% |
| Notice Period To Terminate Renewal | 3 | 3 / 2 / 0 | 60.0% | 100.0% | 75.0% |
| Non-Compete | 0 | 0 / 0 / 0 | — | — | — |
| Termination For Convenience | 5 | 5 / 6 / 0 | 45.5% | 100.0% | 62.5% |
| Cap On Liability | 6 | 3 / 1 / 3 | 75.0% | 50.0% | 60.0% |
| Uncapped Liability | 3 | 0 / 1 / 3 | 0.0% | 0.0% | 0.0% |
| Audit Rights | 5 | 3 / 0 / 2 | 100.0% | 60.0% | 75.0% |
| Insurance | 4 | 4 / 0 / 0 | 100.0% | 100.0% | 100.0% |

There are no gold-positive non-compete examples in this fixed subset, so its recall is undefined. Insurance has only a few positives; perfect performance here is not broad coverage. No claim is made about healthcare target performance or long-document generalization.

## Rejected responses

| Validation reason | Logical-document analyses |
| --- | ---: |
| Evidence missing or ambiguous. | 11 |
| Currency must be a three-letter code. | 1 |
| Invalid evidence role or supported field. | 2 |
| Response must include each requested target exactly once. | 1 |
| Field not allowed for this target. | 2 |
| Every structured value requires field-level evidence. | 1 |

The main bottleneck is publishing reliable evidence, not merely classifying clause presence. Missing/ambiguous exact quotes, unsupported field assignments, malformed currency and incomplete target coverage prevented acceptance. Validation deliberately remained strict; the run did not replace missing quotes with fuzzy matches, discard failed documents, or automatically retry until favorable results appeared.

## Per-file results

| CUAD ID | Pages | Upload ID | Result | Seconds | Reported tokens |
| --- | ---: | ---: | --- | ---: | ---: |
| `0201` | 8 | 15 | failed | 16.76 | 11,858 |
| `0429` | 10 | 16 | failed | 19.76 | 15,964 |
| `0295` | 10 | 17 | failed | 9.11 | 8,183 |
| `0261` | 6 | 18 | failed | 15.19 | 8,431 |
| `0279` | 4 | 19 | ready | 10.70 | 4,859 |
| `0415` | 2 | 20 | ready | 9.16 | 3,735 |
| `0136` | 8 | 21 | failed | 16.69 | 15,738 |
| `0084` | 1 | 22 | failed | 18.20 | 3,472 |
| `0185` | 2 | 23 | failed | 9.13 | 4,312 |
| `0226` | 6 | 24 | failed | 16.70 | 13,550 |
| `0150` | 5 | 25 | failed | 16.71 | 11,324 |
| `0437` | 6 | 26 | failed | 16.72 | 14,597 |
| `0108` | 3 | 27 | ready | 13.71 | 8,566 |
| `0339` | 7 | 28 | failed | 15.21 | 13,066 |
| `0481` | 4 | 29 | failed | 7.63 | 3,988 |
| `0239` | 9 | 30 | failed | 18.23 | 14,464 |
| `0277` | 10 | 31 | failed | 21.26 | 12,516 |
| `0209` | 9 | 32 | failed | 22.75 | 14,506 |
| `0356` | 8 | 33 | failed | 13.66 | 12,455 |
| `0487` | 1 | 34 | failed | 14.16 | 3,789 |

## Reproduction and artifacts

- Frozen selection: `artifacts/extraction-evaluation/manifest-final.json`.
- Final per-file results, accepted findings, rejected-analysis diagnostics and native metrics: `artifacts/extraction-evaluation/final/`.
- Aggregate comparison: `artifacts/extraction-evaluation/final/scores.json`.
- Full MongoDB command outbox: `artifacts/extraction-evaluation/final/telemetry-outbox.ndjson`.
- Aggregate instrumentation run ID: `ead9272caae140fcaf21df00b749b74c` (retrospective import of the final measured set).
- Earlier preflight, initial batch and enrichment folders are retained separately; do not combine their totals with this final-set comparison.

These generated files are local and ignored by Git. Preserve the entire folders. Re-score without inference using:

```powershell
dotnet run --project AIESummit.Evaluation -- score artifacts/extraction-evaluation/manifest-final.json artifacts/extraction-evaluation/final
```

The original runner did not yet collect failure diagnostics directly. The owner-scoped API was used after the run to recover retained metrics and diagnostic outcomes for those same uploads, without repeating inference or changing their measured times. Later CLI versions now collect these diagnostics on failures. The MongoDB importer labels its events as retrospective; their timestamps are not substituted for original processing times.

## Instrumentation and verification status

PostgreSQL and disposable MongoDB tests pass, including repeatable upgrades, seeds preserving edits, immutable evidence snapshots, real split-connector history preservation, graph records, content exclusion and replay without duplicate events. Owner-scoped GET checks returned anonymous 401 and other-owner 404; repeated initial extraction requests reused existing analyses. Solution builds and the seed acceptance checks passed.

The configured Atlas primary currently rejects TLS handshakes (`TLSV1_ALERT_INTERNAL_ERROR`) while both secondary-node TLS handshakes succeed. The application process egress IP matched the supplied allowlist, and the official driver was supplied the local URI unchanged. No Atlas access settings were modified. **Remote primary ping and writes are not verified; the complete telemetry is queued locally for replay.** Successful disposable MongoDB writes do not establish Atlas write access.

No official CUAD character-offset scoring or normalized structured-value accuracy is claimed. OCR Markdown differs from reference text; any retained lexical evidence overlap is descriptive only. No new gold labels were invented. See [backend details](contract-extraction.md) and [MongoDB consumer contract](evaluation-instrumentation.md).
