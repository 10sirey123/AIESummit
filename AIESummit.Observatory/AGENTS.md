# Extraction Observatory

Independent read-only .NET 10 API (18114) and React dashboard (18115). Start from the repository root with `./scripts/start-observatory.ps1`. Uses existing local MongoDB settings; never runs providers or changes attempts. Frontend charting uses Recharts; exact executed DAG interaction uses React Flow; icons use Phosphor.

Compare scored `workflow-run` records within one manifest hash. Latest 200 runs/attempts and at most 200 documents/nodes per selected query are visible. Missing metrics remain unknown. Graphs use exact document membership; historical inferred graphs are explicitly unavailable. LLMStep count is the selected document's executed LLM node count, not the workflow definition count. All measured candidates appear regardless of promotion.

Keep MongoDB credentials server-side. Use existing MongoHistory filters for confirmation isolation. Render stored labels as React text. Preserve the separate approval workbench and unrelated in-progress edits.

Checks: `dotnet build AIESummit.Observatory`, `npm run build` inside `client`, real MongoDB reads, browser desktop/narrow selection and graph inspection. No provider-backed smoke required for this read-only surface.

The default landing page is a global benchmark overview: one bar per scored attempt, grouped by manifest. Bars show total validated TP across all documents/targets. Clicking a bar opens that benchmark with the run selected; All benchmarks returns to the overview. Hash URLs support benchmark deep links and browser back/forward. Zero detections retain a visible minimum-height click target and keyboard-accessible attempt buttons.

Attempt coverage: the Extraction coverage view pivots development observations into document rows and extraction-type columns, joined by documentRunId and exact expectationId. Green means expected-present/found. Pink means expected-present/missed or rejected evidence. Failed, unexpected, absent and unknown outcomes keep explicit text and distinct styling. Document links open the graph.

Run discovery refreshes every 15 seconds, with a last-updated timestamp. Unscored workflow runs appear separately as pending; completed scored runs populate the bar charts. Manifest grouping is retained, so changed benchmark manifests appear separately.

Homepage evolution now uses one chronological chart of validated expected items found (micro TP) for 20-document scored evaluations across manifests. It shows raw per-run counts, with manifest differences disclosed, and links each bar to its attempt. Headline and delta derive from first/latest values. One-document diagnostic runs remain excluded.

Historical baseline run configuration may contain `sourceCommitEstimate` with sha, basis, certainty and recordedAt. This additive provenance backfill does not alter scores or graph records. The homepage uses it only after explicit commit metadata, and labels estimates with ≈. Original pre-backfill records are retained under ignored artifacts/commit-backfill.

Coverage comparison has Show previous / Show current toggles. Previous selects the immediately earlier scored evaluation with the same document count, across manifests. Cells match sampleId and exact expectationId; missing matches remain not run. Current document order is preserved. Header labels identify which run is displayed; summary metrics continue to describe the selected current run.

A separate Segmentation benchmark tab displays the user-supplied November 7, 2026 fixed SubQ Preview/Muse Wonder sample, including timings, tokens, per-input medians, boundary limitations and supplied pricing. It is static and separate from MongoDB extraction scoring; no Preview price is inferred from Snap. No source URLs were supplied.
