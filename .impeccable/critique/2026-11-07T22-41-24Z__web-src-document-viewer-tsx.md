---
target: contract reader, Extracted terms mode
total_score: 25
max_score: 40
na_heuristics: 
p0_count: 1
p1_count: 3
target_identity: "file:/Users/mmanjon/Documents/NewCo/NewCo/2. Projects/Hackathon/AIESummit-ui/web/src/document-viewer.tsx"
target_fingerprint: "sha256:d2fd70e3b91b82ef4f0d898896981592e57a0bf758604704a570c15be2dee2d3"
target_path: /Users/mmanjon/Documents/NewCo/NewCo/2. Projects/Hackathon/AIESummit-ui/web/src/document-viewer.tsx
timestamp: 2026-11-07T22-41-24Z
slug: web-src-document-viewer-tsx
---
Method: dual-agent (A: design review · B: detector + browser). Assessed before the collapsible-list/real-estate change; that change resolved: 4-column layout, full-width status row, mobile brand overlap.

## Design Health Score
| # | Heuristic | Score | Key Issue |
|---|---|---|---|
| 1 | Visibility of System Status | 3 | Mobile: selecting a term gives no visible response (detail ~1,600px below) |
| 2 | Match System / Real World | 3 | Pipeline jargon: "approximate · 74% overlap", "primary", "snapshot #32", "analysis #5" |
| 3 | User Control and Freedom | 2 | No confirm/dispute on extracted terms |
| 4 | Consistency and Standards | 2 | "Liability cap" vs "Liability Limits"; tiers only in Page review; mode tabs lack roving focus |
| 5 | Error Prevention | 3 | Re-analyze confirm; not-found caveat |
| 6 | Recognition Rather Than Recall | 3 | 10-colour key is a recall burden |
| 7 | Flexibility and Efficiency | 2 | j/k hidden; no PDF zoom/find/copy-quote |
| 8 | Aesthetic and Minimalist Design | 2 | Colour chrome outranks contract; repeated titles |
| 9 | Error Recovery | 3 | Explicit, recoverable failures |
| 10 | Help and Documentation | 2 | No shortcut legend; "approximate" unexplained |
| Total | | 25/40 | Acceptable |

## Design Specificity Verdict
Authored evidence chain (value → p.N chip → exact quote → verified against snapshot); generic three-pane reader around it; 10-hue palette off Modern Dark. Detector: CLI 0 on target files, 7 in web/src (6 side-tab styles.css:852,863,883,929,931,962; Arial summary-view.tsx:55 FP). Browser: 14 PDF / 24 Markdown / 27 Compare — undersized text 10.5–11.5px, line-length 99–104ch in Markdown, skipped heading h3→h5, low contrast 3.1:1 (#5b5d60, opacity .65 "Neither" rows). FPs: Geist (brief), Arial (Word export), neutral quote rules.

## Priority Issues
1. [P0] Clause illegible in default PDF view (fit-page ~4–6px body text). Fix: fit-width + scroll to highlight band + zoom on selection. /impeccable layout
2. [P1] No reviewer decision on extracted terms. Fix: Confirm/Flag (A/R), rail progress, hand-off to Negotiation summary. /impeccable shape
3. [P1] 10-hue palette breaks Modern Dark; coloured side borders. Fix: graphite icons, bright/meta for found/not-found, single selection accent. /impeccable quieter
4. [P1] Mixed trust signals: approximate vs verified, raw "**10", highlight bleeding into 10.3. Fix: separate claims, strip markdown, trim highlight, explain approximate. /impeccable clarify
5. [P2] Tiny text/targets (10.5px, 17px chips) and mobile flow (toolbar fills screen, nested rail scroll, no scroll to detail). Fix: 12px/24px floors, bottom sheet, modes select. /impeccable adapt

## Persona Red Flags
Alex: hidden j/k, no A/R here, no find/copy-quote, ~20 tab stops, no next-not-found. Sam: 10.5px grey chips, 17px targets, mode tabs no roving/tabpanel, no focus/announcement on select, colour-only identity, 6px PDF highlight. Morgan: can't read clause in PDF, can't mark checked, taxonomy mismatch, 74% unexplained, no playbook consequence for not-found, headline drops carve-out.

## Minor Observations
Redundant titles; long agreement title per page; "Not found" wraps names; Download Markdown full button; .reader-grid defined 3x; mode switch drops selection; could preselect first finding.

## Questions to Consider
Why is the work surface the narrowest column — margin notes instead? What is the reviewer's verdict and where does it live? Does a 10-colour key earn its place?
