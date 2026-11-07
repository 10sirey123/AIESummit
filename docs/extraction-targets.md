# Contract extraction targets

Status: proposed product specification, November 7, 2026. These targets describe future extraction and application cards; they are not implemented detections or a completed evaluation.

## Scope and selection

Adopt **25 selected targets: 15 grounded in existing benchmark labels and 10 additional healthcare-specific targets**. Preserve the original project goals below as the baseline. They overlap this selection and are not counted as another 25 targets. Indemnification and broader termination-for-cause coverage remain explicit baseline gaps requiring new annotations.

The selection favors actionable dates, financial exposure, operating restrictions, remedies, and healthcare obligations over exhaustive taxonomy coverage. It combines 11 CUAD categories with 4 MAUD questions. The MAUD subset supports acquisition review, including healthcare acquisitions; it should not be applied indiscriminately to ordinary MSAs or NDAs.

Local inspection covered all prepared JSON sidecars: **510 CUAD contracts, 152 MAUD agreements, and 100 EDGAR reports**. CUAD supplies clause spans; MAUD supplies categorical deal-point answers and excerpts. **EDGAR has no existing detection labels**: all 100 sidecars have `expected_detections: null`. Accordingly, no selected target is represented as EDGAR gold. EDGAR remains useful for ingestion and for testing whether the system distinguishes narrative disclosures from operative contract obligations. An annual-report statement about a contract is a disclosure, not the underlying contract clause.

“Additional” healthcare targets means **not dedicated annotated targets in the inspected CUAD/MAUD label inventories**. It does not mean their subject matter never appears in the source documents or EDGAR narratives. Generic audit, insurance, license, and liability labels do not supply healthcare-specific gold. The healthcare list is a proposed business taxonomy with an initial U.S. focus; jurisdiction and contractual applicability must be recorded rather than assumed.

## Original project targets

The starting scope in [AGENTS.md](../AGENTS.md) and [README.md](../README.md) is contract review for MSAs and NDAs, initially covering indemnity and liability limits, non-compete restrictions, renewal dates, and termination triggers.

| Original target | Required extraction | Relationship to the selected targets |
| --- | --- | --- |
| Indemnification | Indemnifying and protected parties; covered claims/losses; first- versus third-party claims when explicit; defense/control obligations; exclusions; survival; links to caps | Retained baseline requirement. Neither CUAD nor MAUD provides a dedicated general indemnification target. B07/B08 help describe liability but are not indemnity gold. New annotations are needed. |
| Liability limits | Cap amount or formula, currency, measurement period, liable party, exclusions and uncapped categories | B07 and B08 provide partial benchmark support; numerical and party-level fields need additional labels. |
| Non-compete restrictions | Restricted party, activity, geography, duration and exceptions | B05 supplies clause-span support; normalized fields require additional labels. |
| Renewal dates | Initial expiry, renewal mechanism/term, non-renewal notice window and any computable next deadline | B01–B04. A renewal term is not itself a calendar date. |
| Termination triggers | Terminating party, triggering event, notice, cure period, effective date and consequences | B06 covers convenience only. Cause, breach, insolvency, regulatory events and other triggers remain baseline extensions requiring new labels. |

Do not remove indemnification or broader termination triggers merely because the initial datasets cannot score them. Confidentiality is also relevant to NDAs, but is not silently added as a dedicated benchmark-supported target here.

## Fifteen targets from existing benchmark labels

Identifiers are stable product keys. The exact source category/question strings below are the mapping keys in `expected_detections`. Additional card fields are proposed extraction requirements, not fields already annotated by the benchmark.

### CUAD: commercial contract targets

For B01–B11, match `dataset = cuad` and the exact `category`. Each category has an entry in every one of the 510 sidecars. “Positive” counts mean `expected_present: true`, not the number of spans or a model result. Negative entries remain valuable evaluation cases.

| ID / card title | Exact CUAD category | Extract and show on the card | Business use | Positive documents |
| --- | --- | --- | --- | ---: |
| B01 · Effective date | `Effective Date` | Effective date or event; conditions; whether explicit or event-dependent. Keep signing date separate. | Establish when obligations start. | 390 |
| B02 · Initial expiration | `Expiration Date` | Initial expiration date or term formula, anchor date, and qualifications. | Identify the first term boundary. | 413 |
| B03 · Renewal mechanism | `Renewal Term` | Automatic, optional or negotiated renewal; renewal length; exercising party; limits and conditions. | Identify continuing commitments. | 176 |
| B04 · Non-renewal notice | `Notice Period To Terminate Renewal` | Notice duration, units, delivery conditions, recipient and date anchor; computed deadline only when inputs are sufficient. | Avoid unwanted renewal. | 111 |
| B05 · Non-compete | `Non-Compete` | Restricted party, prohibited activities, territory, duration, and linked exceptions. | Review limits on market activity and partnerships. | 119 |
| B06 · Termination for convenience | `Termination For Convenience` | Eligible party, required notice, waiting period, conditions and linked fees. | Understand exit flexibility without cause. | 183 |
| B07 · Liability cap | `Cap On Liability` | Amount/formula, currency, fee lookback, aggregate/per-claim scope, covered party, time bar if present, and exceptions. | Quantify contractual exposure. | 275 |
| B08 · Uncapped liability | `Uncapped Liability` | Expressly uncapped liabilities, affected party, covered breaches and carveouts from a general cap. | Reveal exposure that a headline cap omits. | 111 |
| B09 · Audit rights | `Audit Rights` | Auditor/right holder, records or facilities covered, frequency, notice, cost allocation and remediation. | Plan oversight and operational burden. | 214 |
| B10 · Insurance obligations | `Insurance` | Required policy types, limits, insured party, additional insured requirements, duration and proof obligations. | Check insurance procurement and coverage obligations. | 166 |
| B11 · Assignment restrictions | `Anti-Assignment` | Assigning party, consent/notice requirements, permitted transfers and exceptions. | Review restructuring and transaction flexibility. | 374 |

B08 requires express evidence: failure to find a liability cap does not establish uncapped liability. B11 does not replace CUAD's separate `Change Of Control` category, which is outside this initial selection. Linked exceptions may require evidence outside the originally labeled span; do not claim that span labels score the complete interpretation.

Positive examples: `corporea/benchmarks/cuad/0001.json` for B01–B05, B07–B09 and B11; `0002.json` for B06; `0006.json` for B10. These local payload files are ignored by Git and may not exist in a fresh checkout until preparation.

### MAUD: acquisition and remedy targets

Match `dataset = maud`, `category` and the exact `question` below. All selected rows have `subquestion = <NONE>`. Preserve the original `answer`, `label`, source row and split; the numeric label is question-specific and is not a universal Boolean. A missing question row is unknown, not a negative answer.

| ID / card title | Exact MAUD category and question | Extract and show on the card | Business use and label coverage |
| --- | --- | --- | --- |
| B12 · Change-in-law MAE carveout | Category: `Material Adverse Effect`; question: `Change in law (Y/N)` | Whether change in law is carved out of material adverse effect (MAE); supporting clause; any linked disproportionate-impact qualifier as separately sourced detail. | Understand allocation of legal-change risk in acquisitions. 152 labeled agreements: 151 Yes, 1 No. This is not a generic change-in-law termination right. |
| B13 · Public-health-event MAE carveout | Category: `Material Adverse Effect`; question: `Pandemic or other public health event-Answer (Y/N)` | Whether pandemic/public-health events are carved out of MAE; event scope; separately evidenced exceptions. | Review transaction risk allocation during public-health disruption. 152 labeled agreements: 147 Yes, 5 No. This is not a general force-majeure classifier. |
| B14 · Antitrust efforts standard | Category: `Operating and Efforts Covenant`; question: `General Antitrust Efforts Standard-Answer` | Contractual efforts standard; responsible party; related approval obligations and limits if stated. | Support acquisition approval planning. 152 labeled agreements: 129 Reasonable best efforts, 16 Flat standard, 7 Commercially reasonable efforts. Preserve the upstream category even when a product summary uses plainer language. |
| B15 · Specific performance | Category: `Remedies`; question: `Specific Performance-Answer` | Distinguish entitlement to specific performance from entitlement to seek it; parties, conditions and limitations. | Identify negotiated enforcement remedies. 149 labeled agreements: 135 “entitled to,” 14 “entitled to seek.” Do not infer judicial enforceability. |

All four mappings have examples in `corporea/benchmarks/maud/0001.json`. The counts describe original `main` rows attached to full documents, excluding edited/synthetic material. Related MAUD questions about disproportionate impact or other qualifiers are not automatically included in these four scored targets.

## Ten additional healthcare targets

H01–H10 have no dedicated gold labels in the inspected collection. Each requires a separate annotation guide and authorized healthcare examples before accuracy claims. The proposed fields and business uses below are product choices, not assertions that every agreement must contain each term. Generic compliance language alone is insufficient for a positive finding of a specific operational obligation.

| ID / card title | Intended users and agreement types | Required classification/extraction | Business relevance and boundary from existing labels |
| --- | --- | --- | --- |
| H01 · PHI and business-associate obligations | Providers, payers, pharma service teams; BAAs and health-data service agreements | Explicit covered-entity/business-associate roles; permitted PHI uses; safeguards; incident/breach reporting trigger and contractual deadline; subcontractor flow-down; return/destruction terms. | Assign data-handling responsibilities and incident workflows. Generic confidentiality or audit clauses do not establish a BAA or adequate PHI protection. [HHS business-associate guidance](https://www.hhs.gov/hipaa/for-professionals/privacy/guidance/business-associates/index.html) supports the domain rationale. |
| H02 · Secondary use of clinical data | Pharma research teams, providers, payers and analytics vendors; research/data-use agreements | Permitted research, analytics, AI training or commercialization purposes; data form (identifiable, limited data set, de-identified as stated); authorization/waiver references; re-identification restrictions; onward sharing and withdrawal handling. | Determine what reuse the agreement permits and which dependencies need review. Separate from generic IP ownership or a license grant; do not infer lawful authorization from a commercial license. [HHS research guidance](https://www.hhs.gov/hipaa/for-professionals/special-topics/research/index.html) explains the privacy context. |
| H03 · Safety-event reporting and pharmacovigilance | Pharma sponsors, trial sites, CROs and commercial partners; clinical, licensing and safety-data agreements | Event types; collecting, assessing and reporting parties; contractual clock start, deadline and units; escalation recipients; follow-up and safety-data exchange duties. | Prevent responsibility gaps between partners and support safety operations. Separate from general notice clauses and MAUD pandemic carveouts. Contractual and regulatory reporting clocks must be stored separately. [FDA IND safety-reporting overview](https://www.fda.gov/drugs/investigational-new-drug-application-ind/ind-application-reporting-ind-safety-reports) supports this distinction. |
| H04 · Manufacturing quality and recall allocation | Pharma quality, supply and procurement teams; contract manufacturing and quality agreements | Batch release authority; deviations and corrective/preventive actions; change approval; inspection notification; recall initiation, cooperation and cost allocation when stated. | Establish accountable owners for quality events and supply disruption. Generic audit, insurance and damages labels do not describe these manufacturing duties. FDA discusses allocation of manufacturing activities through [quality agreements](https://www.fda.gov/regulatory-information/search-fda-guidance-documents/contract-manufacturing-arrangements-drugs-quality-agreements-guidance-industry). |
| H05 · Trial-related injury payment | Pharma sponsors, research hospitals and trial sites; clinical trial agreements | Who pays for trial-related injury care; causation conditions; exclusions; claim procedure; payer-of-last-resort wording; linked insurance and indemnity provisions. | Clarify participant-care funding and sponsor/site financial responsibilities. This is a proposed specialized extraction beyond generic insurance or liability caps; it does not assess patient entitlement under law. |
| H06 · Claims payment and recoupment | Providers, payers and revenue-cycle teams; network/provider reimbursement agreements | Reimbursement methodology and incorporated fee schedule/version; clean-claim definition; filing/payment deadlines; denial and appeal windows; overpayment recovery/recoupment conditions and limits. | Make revenue terms and dispute deadlines visible. Separate from generic price restrictions, which do not capture healthcare claims administration. Do not invent rates in an unavailable fee schedule. |
| H07 · Value-based payment and quality gates | Payers, providers, ACOs and pharma market-access teams; shared-savings, capitation and outcomes-based agreements | Attributed population/product; cost benchmark; risk adjustment references; quality metrics/gates; measurement period; upside/downside sharing; stop-loss limits; reconciliation and data responsibilities. | Explain how performance affects payment and financial exposure. Generic revenue sharing is insufficient to describe healthcare outcome and quality conditions. [CMS value-based care guidance](https://www.cms.gov/newsroom/fact-sheets/value-based-care-state-medicaid-directors-letter) describes shared savings linked to quality targets. |
| H08 · Patient continuity after network exit | Providers, payers and care-delivery partners; network and clinical service agreements | Eligible patient groups; ongoing treatment obligations; transition period; notice recipients/deadlines; referral/record handoff; payment terms during transition. | Plan continued care and network changes. Generic post-termination services are broader and provide no dedicated patient-continuity gold; do not import a statutory continuation period into an agreement that does not state it. |
| H09 · Credentialing and exclusion screening | Providers, payers, pharma and healthcare staffing vendors; provider, investigator and service agreements | Licensure/credential representations; responsible screening party; named exclusion/debarment lists; screening frequency; notification duties; suspension/removal triggers. | Identify who maintains provider/vendor eligibility and responds to status changes. Separate from a generic compliance representation or audit right. [HHS-OIG's exclusions overview](https://oig.hhs.gov/exclusions/) supports the exclusion-screening domain; do not infer that a counterparty actually passed screening. |
| H10 · Drug rebates and government-pricing cooperation | Pharma, payers, PBMs and distribution partners; rebate, formulary and distribution agreements | Covered products/programs; rebate/discount formulas; utilization data and submission windows; reconciliation; government-price reporting cooperation; named AMP/best-price/340B provisions and duplicate-discount controls if present. | Expose net-price commitments and reporting dependencies. These drug-program fields go beyond generic price restrictions or most-favored-nation clauses. [CMS's Medicaid Drug Rebate Program overview](https://www.medicaid.gov/medicaid/prescription-drugs/medicaid-drug-rebate-program) establishes the program context; do not calculate statutory liability from incomplete contract data. |

The healthcare rationale is informed by the linked public agency sources, reviewed November 7, 2026. Those sources do not prescribe this taxonomy, validate the model, or establish that a particular contract complies with law. H05, H06 and H08 are proposed business extractions, not summaries of a cited legal mandate. Regulatory obligations can differ from negotiated terms and require separate review.

## Card behavior and evidence requirements

Each selected target defines a card type. For each analyzed document, create a target result and allow multiple findings per target when different parties, clauses, schedules or amendments contain different terms. Related cards should link to one another rather than merge liability caps, indemnity, insurance and specialized healthcare obligations into one conclusion.

Every finding must retain:

- **Identity and context:** target ID, upload and split-document identity, document version, document type, relevant party/role, and whether the source is an operative agreement, amendment, exhibit or narrative disclosure.
- **Evidence:** verbatim source quote; source document and section; original upload page when verified; split-document page when available; source-text offsets only when genuinely mapped. Keep OCR text distinct from dataset reference text. MAUD/EDGAR rendered page numbers are derived pagination, not original filing pages.
- **Structured values:** target-specific facts, raw wording and normalized values with units/currency; separate inferred/computed values from explicit values. Preserve conditions, exceptions, cross-references and survival language. Unresolved schedules and definitions are dependencies, not permission to guess.
- **Review state:** `detected`, `not_found_in_reviewed_text`, `uncertain`, `not_applicable`, or `not_analyzed`, with a reason and reviewed coverage. Applicability requires context; a missing acquisition clause in an NDA need not generate an alarm. Unknown benchmark labels remain separate from these application states.
- **Provenance:** extraction/model and taxonomy version; analysis time; reviewed page range; human corrections where applicable. A confidence score, if used, is not a substitute for evidence or measured accuracy.

The primary card view should show title, party, concise factual result, important date/amount, status and an evidence link that opens the document at the supporting passage. Expanded content should expose conditions, other quotes and unresolved dependencies. A completed review can show a compact “Not found in reviewed text” result; unreadable or unprocessed pages must produce uncertainty rather than an assertion of absence. Multiple conflicting clauses should be flagged for review; do not silently choose which amendment controls.

Do not display “high risk” merely because a clause exists. Any later risk assessment must be a separate record with a documented business policy, applicability, rationale and evidence references. The extraction itself is not legal advice.

For dates, keep explicit and computed values separate. For example, an illustrative expiration of December 31, 2027 with notice at least 90 calendar days before expiration gives an arithmetic date of October 2, 2027. Mark it as computed and retain the assumptions; receipt rules, holidays, time zones and contract-specific counting provisions can affect the operative deadline.

## Benchmark and acceptance boundaries

1. **Freeze mappings before evaluation.** B01–B11 use exact CUAD category keys; B12–B15 use the exact MAUD category/question keys above. Preserve upstream splits and source IDs. Do not relabel test examples to improve scores or tune on evaluation splits.
2. **Score what is labeled.** CUAD supports presence/absence and answer-span extraction for selected categories. Report presence precision/recall/F1 and text-span exact match/overlap separately. MAUD supports question-specific categorical answers: report per-question accuracy and macro-F1 with class support. B12/B13 are heavily imbalanced, so headline accuracy alone is misleading. The excerpt is evidence, not a verified PDF bounding box.
3. **Separate enriched fields.** Party attribution, normalized amounts/dates, complete exceptions and computed deadlines are generally not independently labeled here. Annotate and measure them separately; do not treat a correct clause span as proof of a correct card.
4. **Keep EDGAR unlabeled.** Use all three corpora for ingestion and source-type handling. EDGAR results can be manually reviewed for disclosure/contract confusion, but cannot enter clause accuracy or absence scores until separately annotated. Filing metadata such as CIK/year is not a clause detection.
5. **Create new gold for healthcare and baseline gaps.** Establish inclusion/exclusion rules and examples for H01–H10, indemnification and termination for cause. Use authorized public, synthetic or permissioned documents; record provenance and synthetic status. Include absent, ambiguous, cross-referenced, multi-party and amendment cases. Healthcare subject-matter reviewers should adjudicate disputed labels.
6. **Hold documents out.** Preserve MAUD's original splits for published comparisons, but use a separately documented document-held-out evaluation for new-contract performance because upstream splits share contract names. Keep the same agreement, related amendments and duplicates out of opposite sides of the held-out split. Do not send private contracts to external services without authorization.
7. **Gate application acceptance on evidence.** A useful card must identify the correct document and party, show a supported fact, preserve material conditions, and navigate to verified evidence. Test unknown/absent states and failed OCR as well as positive findings. Set numerical launch thresholds after a reviewed pilot; no benchmark results or readiness claims are made by this document.

## Local source trail

- [Benchmark catalog](../corporea/README.md): provenance, licenses, split limitations, PDF origin and sidecar semantics.
- [Source lock](../corporea/sources.lock.json): pinned upstream releases and integrity metadata.
- `corporea/benchmarks/cuad/*.json`: all 510 prepared sidecars inspected for category inventory and selected positive counts.
- `corporea/benchmarks/maud/*.json`: all 152 prepared sidecars inspected for exact questions, subquestions and selected answer distributions.
- `corporea/benchmarks/edgar-corpus/*.json`: all 100 prepared sidecars inspected; no gold detection list.
- [Preparation utility](../scripts/prepare_benchmarks.py): transformation and source-row retention, including MAUD `main` filtering. These local TXT files are reference text, not guaranteed OCR outputs.

No datasets were downloaded, modified or newly annotated for this proposal. No model inference or application implementation was performed.
