# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Stack

React and TypeScript for the ClauseLens workspace in `web/`. ASP.NET Core, Firebase, PostgreSQL, and BlueShift provide the existing service.

## Users

In-house contract reviewers: legal and contract managers who review inbound agreements (initially MSAs and NDAs) against their company's own playbook before negotiating or signing. They work through one contract at a time, need to find and confirm specific terms quickly, and must be able to show where each fact came from.

## Product Purpose

ClauseLens (repository name AIESummit) turns an uploaded contract into reviewable, source-anchored facts. It reads every page, extracts contract terms (effective date, expiration, renewal, non-renewal notice, termination for convenience, non-compete, liability cap, uncapped liability, audit rights, insurance obligations), and tiers them with the reviewer's playbook. Success means a reviewer reaches a confident negotiation position faster without losing traceability to the original text.

## Positioning

Evidence-first, no AI verdicts. Every extracted term is anchored to an exact quote on the original page, verified against an immutable page snapshot, and openable in place. Risk tiers come only from the reviewer's own playbook rules, never from the model. A found clause is a fact, not a judgement.

## Operating Context

The reviewer signs in, uploads a PDF or image (up to 30 pages), watches OCR, document splitting and analysis progress, then opens a focused review with Findings, Source, Summary and Key dates tabs. Findings pairs a list and detail; Source pairs one original PDF page or OCR Markdown with its evidence detail. They accept or reject page findings, follow cross-references and defined terms, compare two contracts clause by clause, check a portfolio list, and export a negotiation summary or key dates.

The product is designed for real reviewers beyond the AIE Code Summit hackathon event; durability and day-to-day usefulness outrank demo theatrics.

## Capabilities and Constraints

- Extraction covers ten CUAD-derived targets (B01–B10). Additional benchmark and healthcare targets, indemnification and termination for cause are specified in [docs/extraction-targets.md](docs/extraction-targets.md) but not implemented.
- Outcomes are found, not found in reviewed text, uncertain, not applicable, or not analyzed. "Not found" is not proof of absence; a failed or unpublished analysis is not a negative result.
- Evidence offsets refer to page Markdown snapshots, not PDF coordinates; PDF highlighting is text-matched. Approximate (≥50% word-overlap) evidence matches are labelled as such.
- Playbooks, review decisions and tiers are stored in the browser until a playbook/review API exists; they are reviewer settings, not model output.
- Uploads, results and evidence are owner-scoped; another user's data is never visible.
- UI work is front-end only: changes stay in `web/`; backend behaviour is consumed through the documented API ([docs/client-analysis-api.md](docs/client-analysis-api.md)).

## Brand Commitments

The visible name is "ClauseLens (AIESummit)". The product brief, logo and visual authority are in [docs/ui.md](docs/ui.md); the recorded system is [DESIGN.md](DESIGN.md).

## Evidence on Hand

Public CUAD benchmark contracts (CC BY 4.0) under `corporea/benchmarks/cuad/` are the demonstration material. There are no customers, testimonials, accuracy claims or pricing; the recorded baseline in [docs/extraction-baseline-results.md](docs/extraction-baseline-results.md) is the only measured evidence. Do not fabricate any of these.

## Product Principles

1. Every fact shows its source: a value without openable evidence is not shown as a finding.
2. The reviewer decides: the model extracts, the playbook tiers, the person accepts or rejects.
3. Uncertainty is visible: pending, failed, approximate and not-found states are stated plainly, never hidden or reworded as absence.
4. The contract is the work surface: overview screens support getting into a contract, they are not the product.
