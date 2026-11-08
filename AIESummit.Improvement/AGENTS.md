# Improvement workbench

Local .NET 10 operator application, served only on loopback port 18110. Start with `./scripts/start-improvement.ps1`. Requires the current application stack, existing benchmark fixtures, MongoDB settings and a local `META_API_KEY` for Codex/Muse. Do not print those settings.

`ImprovementService` freezes the current Git tree using a separate index (preserving the user's index/checkout), step graph, public development gold files and dataset manifest. Planning is read-only. A hash binds approval to the proposal and frozen source/dataset/graph. A fresh Codex implementation session begins in a new `codex/improvement/*` worktree only after approval.

Candidate changes are restricted to `improvement/workflow.json`. Never allow candidates to change validators, gold, scoring, target definitions, the runner, dependencies or application services. All new candidate model IDs are Muse. `AIESummit.WorkflowRunner` supplies the native execution contract; read its guide before changing it.

Attempts and transcripts live under ignored `artifacts/improvement`; attempt lifecycle snapshots are also journaled to MongoDB `improvement_attempts` with a durable local outbox. MongoDB history endpoints expose bounded read-only evaluation views for Codex and the UI. Confirmation details are excluded. An interrupted operation is retained as interrupted; do not silently resume inference or clear failures.

`Use as baseline` copies the evaluated workflow into the working tree after verifying that the current baseline belongs to the candidate's graph ancestry, including parent commit/graph identities. A better descendant can skip an unpromoted intermediate; an unrelated changed baseline blocks promotion. It does not merge unrelated source files or change the running application's persisted step selection. Worktrees, branches and all database volumes remain intact.

The UI follows the existing graphite/Geist design system. Treat all filenames, model messages and stored results as untrusted text. Keep statuses explicit: found means validated target presence, not clause-level matching. Unknown usage/labels remain unknown; logical workflow tokens include cached outputs while newly consumed tokens exclude them.

Checks: `dotnet build AIESummit.slnx --nologo`, `dotnet test AIESummit.WorkflowRunner.Tests`, `./scripts/test-extractions.ps1`, `node --check AIESummit.Improvement/wwwroot/app.js`, browser desktop/narrow checks, and native live/cache-only replay. Preserve unrelated source edits and pinned bundles.

## Workbench navigation and UI checks

The UI has Attempt review, Progress, Document coverage and Run history views. A native attempt selector includes saved status. Proposal review leads with the hypothesis, benefits and risks; full change specifications are expandable. New drafts show their parent and offer Cancel without submitting inference. Drafts remain in memory while switching views, not across reloads. Unknown measurements stay unknown, and historical runs without workflow snapshots never fall back to the current graph. Unchanged polling results preserve interactive controls; attempt selection resolves before replacing the displayed identity.

For frontend-only changes, run `node --check AIESummit.Improvement/wwwroot/app.js`, `node --check AIESummit.Improvement/wwwroot/progression.js` and `node --test AIESummit.Improvement/tests/workbench.test.mjs`, then check desktop/narrow layouts and keyboard navigation. UI checks must not generate, approve or promote experiments. The startup script serves published files under `artifacts/improvement/web-host/wwwroot`; source edits alone do not update an already-running published host.

The operator surface intentionally retains Geist/Geist Mono. Its compact type scale uses 12px metadata, 13–14px controls/body, 15px section labels, 20–22px headings/measurements and a 2rem page title (1.75rem on narrow screens). The warm `#e6c88b` status identifies completed-with-failures or interrupted runs; it always accompanies text. Existing chart series colors remain semantic. These local choices may generate advisory differences against the contract-reader design tokens.
