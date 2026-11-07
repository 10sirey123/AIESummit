# Evidence matching: 50% word overlap

The extraction connector now uses `word-dice-50-v1` as a fallback when the model's quote is not an exact substring of its specified page. This changes evidence anchoring only: target coverage, structured-value validation, field-support links and atomic per-analysis publication still apply.

## Rule

1. Prefer an ordinal exact substring match, preserving the existing occurrence selection.
2. Otherwise tokenize the quote and that page into Unicode letter/number words. Ignore case and punctuation; normalize individual words with Unicode NFKC. Keep repeated-word counts.
3. Slide a contiguous source window containing the same number of words as the quote. If the page has fewer words, use its entire word sequence.
4. Count shared words as a multiset intersection: each word contributes the smaller of its two occurrence counts. Score each window as `2 × shared / (quote word count + window word count)`.
5. Accept the best candidate at **0.50 or higher**, inclusive. Overlapping tied windows are reduced to a deterministic representative at that location. Distinct non-overlapping best locations require the model's occurrence index; an unspecified ambiguous location is rejected.

For example, `alpha beta gamma delta` and `alpha beta other words` share two of four words and score 50%. Repeating one matching word does not fabricate additional matches. This is lexical overlap, not proof of equivalent legal meaning; changing a key date or negation can still leave many words in common.

## Stored evidence and compatibility

- `Quote` remains the **actual source substring**, not an approximation. `snapshot.Markdown[start..end] == Quote` stays true.
- `StartOffset` and `EndOffset` remain inclusive/exclusive UTF-16 offsets into the immutable page snapshot. Matching does not search other pages or documents.
- `ModelQuote` retains the original model text.
- `MatchMethod` is `exact` or `word-dice-50-v1`.
- `MatchScore` records the overlap score; an exact match scores 1.

The owner-scoped extraction response exposes these matching fields. Clients can label approximate matches while continuing to highlight the exact stored source range. Upgrade `004-evidence-word-overlap.sql` adds the fields without resetting prompts or analyses. Legacy evidence defaults to exact/1 with an empty `ModelQuote`.

The prompt still requests faithful source quotes. Its persisted template and existing seeds are not overwritten by this validator change. Existing analyses are not automatically reprocessed or sent to a provider.

## Saved-response recheck

Offline validation of the retained baseline responses, with all other rules unchanged:

| Measurement | Original exact rule | 50% fallback |
| --- | ---: | ---: |
| Accepted findings across the 20 original PDFs | 4 | 8 |
| Findings per PDF | 0.20 | 0.40 |
| Fully accepted PDFs | 3 | 5 |
| Accepted logical-document analyses | 6 of 24 | 9 of 24 |

Four evidence segments in accepted findings used the approximate fallback. One of the 20 original files has no retained analysis response and remains a failed file in the denominator. Other responses still fail target coverage, field/value rules, ambiguity or the overlap threshold.

These are **revalidation results**, not a new inference run or a new gold-accuracy measurement. Original benchmark scores, processing times and token totals remain unchanged. Historical database results were not overwritten. Diagnostic inputs and output are retained under `artifacts/evidence-overlap-recheck/`.

Tests cover the exact 50% boundary, below-threshold rejection, repeated-word accounting, ambiguous locations, Unicode offsets and preservation of exact source quotes. Disposable PostgreSQL checks exercise the additive upgrade twice as part of the retained-data workflow.
