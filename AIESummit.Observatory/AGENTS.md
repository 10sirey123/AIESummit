# Extraction Observatory

Independent read-only .NET 10 API (18114) and React dashboard (18115). Start from the repository root with `./scripts/start-observatory.ps1`. Uses existing local MongoDB settings; never runs providers or changes attempts. Frontend charting uses Recharts; exact executed DAG interaction uses React Flow; icons use Phosphor.

Compare scored `workflow-run` records within one manifest hash. Latest 200 runs/attempts and at most 200 documents/nodes per selected query are visible. Missing metrics remain unknown. Graphs use exact document membership; historical inferred graphs are explicitly unavailable. LLMStep count is the selected document's executed LLM node count, not the workflow definition count. All measured candidates appear regardless of promotion.

Keep MongoDB credentials server-side. Use existing MongoHistory filters for confirmation isolation. Render stored labels as React text. Preserve the separate approval workbench and unrelated in-progress edits.

Checks: `dotnet build AIESummit.Observatory`, `npm run build` inside `client`, real MongoDB reads, browser desktop/narrow selection and graph inspection. No provider-backed smoke required for this read-only surface.

The default landing page is a global benchmark overview: one bar per scored attempt, grouped by manifest. Bars show total validated TP across all documents/targets. Clicking a bar opens that benchmark with the run selected; All benchmarks returns to the overview. Hash URLs support benchmark deep links and browser back/forward. Zero detections retain a visible minimum-height click target and keyboard-accessible attempt buttons.

Attempt coverage: the Extraction coverage view pivots development observations into document rows and extraction-type columns, joined by documentRunId and exact expectationId. Green means expected-present/found. Pink means expected-present/missed or rejected evidence. Failed, unexpected, absent and unknown outcomes keep explicit text and distinct styling. Document links open the graph.
