import { renderProgression } from './progression.js?v=20261108';
import { workflowEdges } from './workflow-view.js';

const $ = id => document.getElementById(id);
const state = { dashboard: null, selected: 'baseline', attempt: null, matrix: null, document: '', composing: false, busy: false, refreshing: false, selectedRun: null, loading: false };
const format = new Intl.NumberFormat('en-US');
const labels = { awaiting_approval: 'Awaiting approval', proposed: 'Awaiting approval', planning: 'Planning', implementing: 'Implementing', evaluating: 'Evaluating', evaluated: 'Evaluated', completed: 'Evaluated', completed_with_failures: 'Completed with failures', ready: 'Ready', failed: 'Failed', promoted: 'Baseline selected', rejected: 'Rejected', queued: 'Queued', approved: 'Approved', running: 'Running' };
const resultLabels = { found: 'Found', detected: 'Found', missed: 'Missed', false_positive: 'False positive', correct_absence: 'Correct absence', failed: 'Processing failed', rejected_evidence: 'Evidence rejected', not_run: 'Not evaluated', not_evaluated: 'Not evaluated', unknown: 'Unscored', uncertain: 'Uncertain', not_found: 'Not found' };
const escape = value => String(value ?? '').replace(/[&<>"']/g, x => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[x]));
const num = value => value == null || !Number.isFinite(Number(value)) ? '—' : format.format(Number(value));
const statusText = value => labels[value] || String(value || 'Unknown').replaceAll('_', ' ');
const date = value => { if (!value) return 'Date unavailable'; const d = new Date(value); return Number.isNaN(d.getTime()) ? String(value) : d.toLocaleString(undefined, { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' }); };
const id = value => value?.id || value?.runId || value?._id;
const asArray = value => value == null ? [] : Array.isArray(value) ? value : [value];

async function request(path, options) {
  const response = await fetch(path, { headers: { 'Content-Type': 'application/json' }, ...options });
  const text = await response.text();
  let body; try { body = text ? JSON.parse(text) : {}; } catch { body = { error: text }; }
  if (!response.ok) throw new Error(body.error || body.detail || body.title || `Request failed (${response.status}). Refresh and try again.`);
  return body;
}
function showError(error) { $('error').textContent = error.message || String(error); $('error').hidden = false; }
function clearError() { $('error').hidden = true; }
function statusBadge(status) { return `<span class="status ${escape(status || '')}">${escape(statusText(status))}</span>`; }
function metric(label, value, note) { return `<div class="measurement"><span class="measurement-label">${escape(label)}</span><strong class="measurement-value">${escape(value)}</strong><span class="measurement-note">${escape(note)}</span></div>`; }
function getRun() {
  const runs = state.dashboard?.runs || [];
  if (state.selectedRun) return runs.find(r => id(r) === state.selectedRun);
  const selected = state.attempt?.runId || state.attempt?.evaluationRunId;
  if (selected) return runs.find(r => id(r) === selected) || state.attempt?.results || state.attempt?.result;
  return state.selected === 'baseline' ? runs.find(r => r.isBaseline || r.configuration?.command === 'import-results') || runs.at(-1) : state.attempt?.results || null;
}
function extractMetrics(run) {
  const s = run?.summary || run?.result || run || {};
  const quality = s.validatedFindings?.micro || s.validated?.micro || s.scores?.micro || s.micro || {};
  const tokens = s.metrics || s.tokens || {};
  return {
    tp: quality.tp ?? s.truePositives ?? s.validatedTruePositives,
    positives: quality.tp != null && quality.fn != null ? quality.tp + quality.fn : s.expectedPositives,
    fp: quality.fp ?? s.falsePositives,
    input: tokens.inputOutputBreakdownAvailable === false ? null : s.logicalInputTokens ?? s.inputTokens ?? s.reportedInputTokens ?? tokens.reportedInputTokens ?? tokens.input,
    output: tokens.inputOutputBreakdownAvailable === false ? null : s.logicalOutputTokens ?? s.outputTokens ?? s.reportedOutputTokens ?? tokens.reportedOutputTokens ?? tokens.output,
    total: s.reportedLlmTokens ?? s.reportedTotalTokens ?? s.totalTokens ?? tokens.reportedTotalTokens,
    newInput: tokens.newlyConsumedInputTokens ?? s.newlyConsumedInputTokens,
    newOutput: tokens.newlyConsumedOutputTokens ?? s.newlyConsumedOutputTokens,
    knownInput: tokens.knownInputTokensSubtotal,
    knownOutput: tokens.knownOutputTokensSubtotal,
    unknownUsage: tokens.unknownUsagePromptCount,
    documents: run?.documentsAttempted ?? s.documentsAttempted,
    failed: run?.documentsFailed ?? s.failedDocuments ?? s.failures,
    cacheHits: s.cacheHits ?? s.reusedNodes ?? tokens.cacheHits
  };
}
// Keep unchanged controls mounted so polling preserves focus and open selectors.
function updateMarkup(element, html) {
  if (element._markup !== html || !element.hasChildNodes()) { element.innerHTML = html; element._markup = html; }
}
function showView(view) {
  document.querySelectorAll('[data-view-panel]').forEach(panel => { panel.hidden = panel.dataset.viewPanel !== view; });
  document.querySelectorAll('[data-view]').forEach(button => {
    if (button.dataset.view === view) button.setAttribute('aria-current', 'page');
    else button.removeAttribute('aria-current');
  });
}
function renderTabs() {
  const attempts = state.dashboard?.attempts || [];
  const options = (state.selectedRun ? '<option value="retained-run" disabled>Recorded run · ' + escape(state.selectedRun.slice(0, 8)) + '</option>' : '') + '<option value="baseline">Current baseline</option>' + attempts.map((a, index) => `<option value="${escape(id(a))}">${index + 1}. ${escape(a.title || a.name || 'Untitled attempt')} · ${escape(statusText(a.status))}</option>`).join('');
  updateMarkup($('attempt-select'), options);
  $('attempt-select').value = state.selectedRun ? 'retained-run' : state.selected;
  $('attempt-select').disabled = state.composing || state.busy || state.loading;
  $('attempt-count').textContent = state.composing ? 'Draft · not submitted' : `${attempts.length} saved ${attempts.length === 1 ? 'attempt' : 'attempts'}`;
}
function renderGraph(graph, container = $('graph')) {
  if (typeof graph === 'string') { try { graph = JSON.parse(graph); } catch { graph = null; } }
  const nodes = graph?.nodes || graph?.steps || [];
  if (!nodes.length) {
    container.innerHTML = '<p class="muted">The versioned graph will appear here when its snapshot is available.</p>';
    return;
  }
  const edges = workflowEdges(graph, nodes);
  container.innerHTML = nodes.map(n => {
    const kind = n.kind === 'detect-pages' ? 'Page detector' : !n.kind || n.kind === 'extract' ? 'Extractor' : n.kind;
    const scope = n.selectedPagesFrom ? 'Selected pages' : n.kind === 'detect-pages' ? 'All pages' : n.scope || 'Whole document';
    const targets = asArray(n.targets);
    return `<div class="graph-node"><strong>${escape(n.name || n.label || n.key || n.id || n.tag)}</strong><small>${escape(n.model || n.stepTag || n.tag || '')}</small><div class="graph-badges"><span>${escape(kind)}</span><span>${escape(scope.replaceAll('-', ' '))}</span>${targets.length ? `<span title="${escape(targets.join(', '))}">${targets.length} ${targets.length === 1 ? 'target' : 'targets'}</span>` : ''}</div>${n.selectedPagesFrom ? `<small>Pages selected by ${escape(n.selectedPagesFrom)}</small>` : ''}</div>`;
  }).join('') + (edges.length ? `<div class="graph-dependencies">${edges.map(e => `${escape(e.from)} → ${escape(e.to)}${e.pageSelection ? ' (page selection)' : ''}`).join(' · ')}</div>` : nodes.length > 1 ? '<div class="graph-dependencies">Independent branches · each receives the frozen document input</div>' : '');
}
function block(title, content) {
  if (!content || !asArray(content).length) return '';
  const contents = Array.isArray(content) ? `<ul>${content.map(x => `<li>${escape(typeof x === 'object' ? x.description || x.summary || JSON.stringify(x) : x)}</li>`).join('')}</ul>` : `<p>${escape(typeof content === 'object' ? JSON.stringify(content, null, 2) : content)}</p>`;
  return `<div class="proposal-block"><h3>${escape(title)}</h3>${contents}</div>`;
}
function renderMain() {
  const fromBaseline = state.composing && !['evaluated', 'promoted'].includes(state.attempt?.status);
  const attempt = fromBaseline ? null : state.attempt;
  const run = fromBaseline ? (state.dashboard?.runs || []).find(r => r.isBaseline || r.configuration?.command === 'import-results') || state.dashboard?.runs?.at(-1) : getRun();
  const m = extractMetrics(run);
  document.querySelector('.work-area').classList.toggle('composing', state.composing);
  $('measurements').hidden = !run;
  document.querySelector('.metric-help').hidden = !run;
  $('attempt-heading').textContent = state.composing ? 'Starting point for your proposal' : attempt?.title || attempt?.name || (state.selectedRun ? 'Retained evaluation' : 'Benchmark baseline');
  $('attempt-subtitle').textContent = state.composing ? 'Review these results while you describe the next change. No attempt has started yet.' : attempt?.instructions || (run ? `${num(m.documents)} benchmark documents · ${num(m.failed)} failed · ${date(run.startedAt)}` : 'No evaluation runs have been recorded yet.');
  const s = state.composing ? 'Draft' : attempt?.status || run?.status || 'No runs';
  $('attempt-status').textContent = statusText(s); $('attempt-status').className = `status ${s}`;
  $('measurements').innerHTML = metric('Expected items found', m.tp == null ? '—' : `${num(m.tp)}${m.positives == null ? '' : ` / ${num(m.positives)}`}`, m.fp == null ? 'Evidence-accepted findings' : `${num(m.fp)} false positives`) + metric('Logical input tokens', m.input == null && m.knownInput > 0 ? '≥' + num(m.knownInput) : num(m.input), m.input == null ? m.knownInput > 0 ? 'Known subtotal · ' + num(m.unknownUsage) + ' prompt unreported' : 'Breakdown unavailable' : m.newInput == null ? 'Logical workflow usage' : `Logical · ${num(m.newInput)} newly consumed`) + metric('Logical output tokens', m.output == null && m.knownOutput > 0 ? '≥' + num(m.knownOutput) : num(m.output), m.output == null ? m.knownOutput > 0 ? 'Known subtotal · ' + num(m.unknownUsage) + ' prompt unreported' : 'Breakdown unavailable' : m.newOutput == null ? 'Logical workflow usage' : `Logical · ${num(m.newOutput)} newly consumed`) + metric('Reused steps', num(m.cacheHits), m.cacheHits == null ? m.total == null ? 'Cache provenance unavailable' : `${num(m.total)} reported total tokens` : 'No new provider call');
  $('comparison').innerHTML = attempt?.comparison ? `<div class="comparison-note">${escape(typeof attempt.comparison === 'string' ? attempt.comparison : attempt.comparison.summary || JSON.stringify(attempt.comparison))}</div>` : '';
  const graph = state.selectedRun && !state.composing ? run?.graph || run?.workflow : attempt?.graph || attempt?.proposal?.graph || attempt?.parentGraph || state.dashboard?.graph || state.dashboard?.workflow;
  renderGraph(graph);
  $('workflow-json').textContent = graph ? JSON.stringify(graph, null, 2) : 'Workflow snapshot unavailable.';
  $('workflow-note').textContent = state.selectedRun && !state.composing ? graph ? 'Recorded run snapshot' : 'No workflow snapshot available for this run' : attempt?.graph ? 'Candidate snapshot' : attempt?.parentGraph ? 'Frozen parent snapshot' : 'Current workflow · historical results may use a different version';
  $('parent-workflow').hidden = !attempt?.graph || !attempt?.parentGraph;
  if (attempt?.graph && attempt?.parentGraph) renderGraph(attempt.parentGraph, $('parent-graph'));
  const proposal = state.composing ? null : attempt?.proposal;
  $('proposal').hidden = !proposal;
  updateMarkup($('proposal'), proposal ? typeof proposal === 'string' ? block('Proposal', proposal) : block('Proposed change', proposal.summary) + block('Hypothesis', proposal.hypothesis) + block('Expected benefit', proposal.expectedBenefit) + block('Risks & validation', proposal.risks || proposal.validation) + (proposal.changes ? `<details class="proposal-changes"><summary>Inspect all proposed changes</summary>${block('Changes', proposal.changes)}</details>` : '') : '');
  const logs = attempt?.logs || attempt?.events || [];
  $('execution-details').hidden = !logs.length && !attempt?.error;
  $('execution-log').textContent = asArray(logs).map(x => typeof x === 'string' ? x : `${x.timestamp || x.at || ''} ${x.message || x.text || JSON.stringify(x)}`).join('\n') + (attempt?.error ? `\n${attempt.error}` : '');
  const overhead = [['Planning', attempt?.planningUsage], ['Implementation', attempt?.implementationUsage]].filter(([, usage]) => usage);
  if (overhead.length) $('execution-log').textContent += '\n\nModel overhead (separate from extraction):\n' + overhead.map(([phase, usage]) => `${phase}: ${num(usage.input_tokens)} input (${num(usage.cached_input_tokens)} cached), ${num(usage.output_tokens)} output`).join('\n');
  renderActions();
}
function renderActions() {
  $('new-attempt').disabled = state.busy || state.loading;
  const a = state.attempt;
  $('attempt-form').hidden = !state.composing;
  $('action-content').hidden = state.composing;
  if (state.composing) {
    const parent = ['evaluated', 'promoted'].includes(a?.status) ? a.title || 'Selected attempt' : 'Current baseline';
    $('compose-parent').textContent = `Starts from: ${parent}`;
    return;
  }
  const awaiting = ['awaiting_approval', 'proposed', 'proposal_ready'].includes(a?.status);
  const complete = ['completed', 'evaluated', 'ready'].includes(a?.status);
  const running = ['planning', 'implementing', 'evaluating', 'approved', 'queued', 'running'].includes(a?.status);
  const title = awaiting ? 'Review the proposal' : running ? 'Attempt in progress' : complete ? 'Choose the next step' : a?.status === 'failed' ? 'The attempt stopped.' : a?.status === 'promoted' ? 'A new starting point.' : a ? 'Retained for the next attempt.' : 'Start with a proposal';
  const text = awaiting ? 'Read the proposal and workflow. Approval creates an isolated worktree, implements the change, and runs the benchmark.' : running ? 'Progress is saved as it happens. You can inspect other documents while this attempt runs.' : complete ? 'Compare accepted detections and token usage before choosing whether this workflow becomes the next baseline.' : a?.status === 'failed' ? 'The error and execution log are preserved. A new attempt can use this history to choose a better approach.' : a?.status === 'promoted' ? 'This approved workflow is available as the parent of future improvement attempts.' : a ? 'Its proposal, workflow, and results remain part of the optimization history.' : 'Muse studies the code and recorded misses, then proposes an extraction change for your approval.';
  updateMarkup($('action-content'), `<h2 id="action-heading">${title}</h2><p>${text}</p>${!a ? '<ol class="review-list"><li><strong>Propose</strong> a focused workflow change.</li><li><strong>Review</strong> and approve the plan.</li><li><strong>Evaluate</strong> on the fixed benchmark.</li><li><strong>Choose</strong> whether to use it as baseline.</li></ol>' : ''}${a?.error ? `<p class="inline-error">${escape(a.error)}</p>` : ''}${a ? `<details class="attempt-technical"><summary>Attempt details</summary><dl class="attempt-meta"><dt>Attempt</dt><dd>${escape(id(a)?.slice(0, 12))}</dd>${a.baseCommit ? `<dt>Parent</dt><dd>${escape(a.baseCommit.slice(0, 12))}</dd>` : ''}${a.worktree ? `<dt>Worktree</dt><dd>${escape(a.worktree)}</dd>` : ''}${a.proposalHash ? `<dt>Proposal</dt><dd>${escape(a.proposalHash.slice(0, 12))}</dd>` : ''}</dl></details>` : ''}<div class="action-stack">${awaiting ? '<button class="primary full-width" data-action="approve">Approve & run</button><button class="secondary full-width" data-action="reject">Reject proposal</button>' : complete ? '<button class="primary full-width" data-action="promote">Use as baseline</button><button class="secondary full-width" data-action="new">Explore another change</button>' : !running ? '<button class="primary full-width" data-action="new">Start a new attempt</button>' : ''}</div><div class="readonly-note"><svg width="13" height="14" viewBox="0 0 14 15" fill="none" aria-hidden="true"><rect x="2" y="6" width="10" height="7" rx="1.5" stroke="currentColor"/><path d="M4 6V4a3 3 0 0 1 6 0v2" stroke="currentColor"/></svg><span>Benchmark labels and scoring stay fixed. Existing database records and cache are retained.</span></div>`);
  $('action-content').querySelectorAll('button').forEach(b => { b.disabled = state.busy || state.loading; });
}
function renderHistory() {
  const runs = (state.dashboard?.runs || []).filter(r => r.documentsAttempted !== 0 || r.configuration?.attemptId);
  if (!runs.length) { $('history').innerHTML = '<p class="empty-state">No evaluation history yet. Recorded runs will appear here.</p>'; return; }
  updateMarkup($('history'), `<table class="history-table"><thead><tr><th scope="col">Run</th><th scope="col">Documents</th><th scope="col">Expected items found</th><th scope="col">False positives</th><th scope="col">Input / output tokens</th><th scope="col">State</th></tr></thead><tbody>${runs.map(r => { const m = extractMetrics(r); return `<tr><td><button class="run-link" data-run="${escape(id(r))}">${escape(r.name || r.title || r.configuration?.attemptTitle || `Run ${String(id(r)).slice(0, 8)}`)}</button><small>${escape(date(r.startedAt))}</small></td><td>${num(m.documents)}${m.failed ? `<small>${num(m.failed)} failed</small>` : ''}</td><td>${num(m.tp)}</td><td>${num(m.fp)}</td><td>${num(m.input)} / ${num(m.output)}${m.input == null && m.total != null ? `<small>${num(m.total)} total reported</small>` : ''}</td><td>${statusBadge(r.status)}</td></tr>`; }).join('')}</tbody></table>`);
}
function renderDocuments() {
  const documents = state.dashboard?.documents || [];
  const current = state.document;
  if (!documents.length) { $('document-select').innerHTML = '<option value="">No benchmark documents</option>'; return; }
  updateMarkup($('document-select'), documents.map(d => { const key = d.sourceSha256 || d.id || d.sampleId || d.documentId; return `<option value="${escape(key)}">${escape(d.sampleId || d.id)}${d.filename || d.name ? ` · ${escape(d.filename || d.name)}` : ''}</option>`; }).join(''));
  state.document = documents.some(d => String(d.sourceSha256 || d.id || d.sampleId || d.documentId) === String(current)) ? current : String(documents[0].sourceSha256 || documents[0].id || documents[0].sampleId || documents[0].documentId);
  $('document-select').value = state.document;
}
function cellStatus(cell, expected) {
  if (!cell) return 'not_run';
  if (typeof cell === 'string') return cell;
  if (cell.status || cell.state) return cell.status || cell.state;
  if (cell.failed) return 'failed';
  if (expected === 'unknown') return 'unknown';
  const found = cell.predicted ?? cell.found ?? cell.detected;
  if (found == null) return 'not_run';
  return expected === 'present' ? found ? 'found' : 'missed' : found ? 'false_positive' : 'correct_absence';
}
function renderMatrix(data) {
  state.matrix = data;
  const columns = data.columns || data.attempts || data.runs || [];
  const rows = data.rows || data.targets || [];
  if (!rows.length) {
    updateMarkup($('matrix'), '<p class="empty-state">No expected-results snapshot is available for this document. Import its benchmark labels to compare attempts.</p>');
    $('matrix-summary').textContent = ''; return;
  }
  const expected = row => row.expected === true || row.expected === 'present' || row.expectedPresence === true ? 'present' : row.expected === false || row.expected === 'absent' || row.expectedPresence === false ? 'absent' : 'unknown';
  const positives = rows.filter(r => expected(r) === 'present').length;
  const goldHash = data.labelSha256 || data.goldChecksum || data.goldSha256;
  $('matrix-summary').innerHTML = `<span>${escape(data.sampleId || state.document)} · ${num(rows.length)} extraction targets</span><span>${num(positives)} expected present</span>${goldHash ? `<span title="${escape(goldHash)}">Gold ${escape(goldHash.slice(0, 12))}</span>` : ''}<span>Document / target presence comparison</span>`;
  updateMarkup($('matrix'), `<table class="coverage-table"><thead><tr><th scope="col">Extraction target</th><th scope="col">Expected</th>${columns.map(c => `<th scope="col" title="${escape(c.runId || c.id || '')}">${escape(c.label || c.title || c.name || String(c.runId || c.id).slice(0, 8))}</th>`).join('')}</tr></thead><tbody>${rows.map(row => { const e = expected(row); return `<tr><th scope="row">${escape(row.name || row.category || row.tag)}${row.tag ? `<span class="target-tag">${escape(row.tag)}</span>` : ''}</th><td class="expected">${e === 'present' ? 'Present' : e === 'absent' ? 'Absent' : 'Unknown'}${e === 'present' && row.goldFindings != null ? `<span class="target-tag">${num(row.goldFindings)} gold ${row.goldFindings === 1 ? 'finding' : 'findings'}</span>` : ''}</td>${columns.map((column, index) => {
    const cells = row.cells || row.observations || row.results || [];
    const cell = Array.isArray(cells) ? cells.find(c => c && typeof c === 'object' && (c.columnId === id(column) || c.runId === id(column) || c.attemptId === id(column))) || cells[index] : cells[id(column)];
    const s = cellStatus(cell, e); const color = ['found', 'detected', 'correct_absence'].includes(s) ? 'good' : ['missed', 'false_positive', 'rejected_evidence'].includes(s) ? 'bad' : 'pending';
    const detail = cell?.error || (cell?.count > 0 ? `${num(cell.count)} accepted ${cell.count === 1 ? 'finding' : 'findings'}` : ['not_analyzed', 'not_found_in_reviewed_text'].includes(cell?.detail) ? '' : cell?.detail?.replaceAll('_', ' '));
    return `<td class="result-cell"><span class="cell-status ${escape(s)}"><i class="legend-dot ${color}"></i>${escape(resultLabels[s] || statusText(s))}</span>${detail ? `<span class="cell-detail">${escape(detail)}</span>` : ''}</td>`;
  }).join('')}</tr>`; }).join('')}</tbody></table>`);
}
async function loadMatrix() {
  if (!state.document) { renderMatrix({ rows: [] }); return; }
  const selected = state.document;
  try { const data = await request(`/api/documents/${encodeURIComponent(selected)}/matrix`); if (state.document === selected) renderMatrix(data); }
  catch (error) { if (state.document === selected) updateMarkup($('matrix'), `<p class="empty-state inline-error">${escape(error.message)}</p>`); }
}
async function refresh() {
  if (state.refreshing || state.loading) return;
  const version = selectionVersion;
  state.refreshing = true;
  try {
    const dashboard = await request('/api/dashboard');
    if (version !== selectionVersion || state.loading) return;
    state.dashboard = dashboard;
    const status = state.dashboard.status || {};
    const connected = status.mongoConnected ?? status.connected ?? true;
    $('connection').className = `connection ${connected ? 'connected' : 'disconnected'}`;
    $('connection').innerHTML = `<span class="signal"></span>${escape(connected ? 'MongoDB history connected' : status.error || 'History unavailable')}`;
    if (state.selected !== 'baseline' && !state.composing) {
      const selected = state.selected;
      const attempt = await request(`/api/attempts/${encodeURIComponent(selected)}`);
      if (version !== selectionVersion || state.loading) return;
      if (state.selected === selected && !state.composing) state.attempt = attempt;
    }
    renderTabs(); renderMain(); renderHistory(); renderDocuments(); renderProgression(state.dashboard); await loadMatrix();
  } catch (error) {
    $('connection').className = 'connection disconnected'; $('connection').innerHTML = '<span class="signal"></span>Connection unavailable'; showError(error);
  } finally { state.refreshing = false; }
}
function compose() { if (state.busy || state.loading) return; ++selectionVersion; state.composing = true; showView('review'); clearError(); renderTabs(); renderMain(); $('instructions').focus(); }
document.querySelector('.view-navigation').addEventListener('click', event => {
  const button = event.target.closest('[data-view]'); if (button) showView(button.dataset.view);
});
$('cancel-attempt').addEventListener('click', () => {
  if (state.busy) return;
  state.composing = false; renderTabs(); renderMain(); $('new-attempt').focus();
});
$('new-attempt').addEventListener('click', compose);
$('refresh').addEventListener('click', () => { clearError(); refresh(); });
$('document-select').addEventListener('change', event => { state.document = event.target.value; $('matrix').innerHTML = '<p class="empty-state">Loading document history…</p>'; loadMatrix(); });
let selectionVersion = 0;
async function selectAttempt(selected) {
  if (state.busy || state.loading) return;
  const version = ++selectionVersion;
  state.loading = true; clearError(); renderTabs(); renderActions();
  $('attempt-count').textContent = 'Loading attempt…';
  try {
    const attempt = selected === 'baseline' ? null : await request(`/api/attempts/${encodeURIComponent(selected)}`);
    if (version !== selectionVersion) return;
    state.composing = false; state.selected = selected; state.selectedRun = null; state.attempt = attempt;
    showView('review'); renderMain();
  } catch (error) { if (version === selectionVersion) showError(error); }
  finally { if (version === selectionVersion) { state.loading = false; renderTabs(); renderActions(); } }
}
$('attempt-select').addEventListener('change', event => selectAttempt(event.target.value));
$('attempt-form').addEventListener('submit', async event => {
  event.preventDefault(); if (state.busy) return;
  state.busy = true; $('propose-button').disabled = true; $('cancel-attempt').disabled = true; $('propose-button').textContent = 'Starting Muse…'; clearError();
  try {
    const a = await request('/api/attempts', { method: 'POST', body: JSON.stringify({ instructions: $('instructions').value.trim(), parentAttemptId: ['evaluated', 'promoted'].includes(state.attempt?.status) ? state.selected : null }) });
    state.selected = id(a); state.attempt = a; state.composing = false; state.selectedRun = null; $('instructions').value = ''; await refresh();
  } catch (error) { showError(error); }
  finally { state.busy = false; $('propose-button').disabled = false; $('cancel-attempt').disabled = false; $('propose-button').textContent = 'Generate proposal'; renderTabs(); renderActions(); }
});
$('action-content').addEventListener('click', async event => {
  const button = event.target.closest('[data-action]'); if (!button || state.busy || state.loading) return;
  const action = button.dataset.action;
  if (action === 'new') { compose(); return; }
  state.busy = true; clearError(); renderActions();
  try {
    await request(`/api/attempts/${encodeURIComponent(state.selected)}/${action}`, { method: 'POST', body: JSON.stringify(action === 'approve' ? { proposalHash: state.attempt.proposalHash } : {}) });
    await refresh();
  } catch (error) { showError(error); }
  finally { state.busy = false; renderTabs(); renderActions(); }
});
$('history').addEventListener('click', async event => {
  const button = event.target.closest('[data-run]'); if (!button || state.busy || state.loading) return;
  const selectedRun = button.dataset.run;
  const version = ++selectionVersion;
  state.loading = true; clearError(); renderTabs(); renderActions();
  try {
    const detail = await request(`/api/runs/${encodeURIComponent(selectedRun)}`);
    if (version !== selectionVersion) return;
    const index = state.dashboard.runs.findIndex(r => id(r) === selectedRun);
    const run = Array.isArray(detail.runs) ? detail.runs[0] || {} : detail;
    if (index >= 0) {
      const existing = state.dashboard.runs[index];
      state.dashboard.runs[index] = { ...existing, ...run,
        summary: run.summary?.validatedFindings ? run.summary : existing.summary || run.summary,
        workflow: detail.workflow || run.workflow || existing.workflow };
    }
    state.selectedRun = selectedRun; state.selected = 'baseline'; state.composing = false; state.attempt = null;
    showView('review'); renderTabs(); renderMain();
    $('attempt-heading').focus();
  } catch (error) { if (version === selectionVersion) showError(error); }
  finally { if (version === selectionVersion) { state.loading = false; renderTabs(); renderActions(); } }
});
$('progression').addEventListener('click', async event => {
  const link = event.target.closest('[data-progression-attempt]'); if (!link) return;
  event.preventDefault();
  await selectAttempt(link.dataset.progressionAttempt);
  if (!state.loading) $('attempt-heading').focus();
});
await refresh();
setInterval(() => { if (!document.hidden && !state.busy) refresh(); }, 5000);
