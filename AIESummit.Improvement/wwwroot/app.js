const $ = id => document.getElementById(id);
const state = { dashboard: null, selected: 'baseline', attempt: null, matrix: null, document: '', composing: false, busy: false, refreshing: false, selectedRun: null };
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
function renderTabs() {
  const attempts = state.dashboard?.attempts || [];
  $('attempt-tabs').innerHTML = `<button class="tab ${state.selected === 'baseline' && !state.composing ? 'active' : ''}" data-attempt="baseline" ${state.selected === 'baseline' && !state.composing ? 'aria-current="page"' : ''}>Baseline</button>` + attempts.map((a, index) => `<button class="tab ${state.selected === id(a) && !state.composing ? 'active' : ''}" data-attempt="${escape(id(a))}" ${state.selected === id(a) && !state.composing ? 'aria-current="page"' : ''}><span class="tab-count">${String(index + 1).padStart(2, '0')}</span>${escape(a.title || a.name || 'New attempt')}</button>`).join('') + (state.composing ? '<button class="tab active" data-attempt="new" aria-current="page">New hypothesis</button>' : '');
}
function renderGraph(graph, container = $('graph')) {
  if (typeof graph === 'string') { try { graph = JSON.parse(graph); } catch { graph = null; } }
  const nodes = graph?.nodes || graph?.steps || [];
  if (!nodes.length) {
    container.innerHTML = '<p class="muted">The versioned graph will appear here when its snapshot is available.</p>';
    return;
  }
  const edges = graph.edges || nodes.flatMap(n => asArray(n.dependsOn).map(from => ({ from, to: n.key || n.id })));
  container.innerHTML = nodes.map(n => `<div class="graph-node"><strong>${escape(n.name || n.label || n.key || n.id || n.tag)}</strong><small>${escape(n.stepTag || n.tag || n.kind || n.model || '')}</small>${n.targets ? `<small>${escape(asArray(n.targets).length)} targets</small>` : ''}</div>`).join('') + (edges.length ? `<div class="graph-dependencies">${edges.map(e => `${escape(e.from)} → ${escape(e.to)}`).join(' · ')}</div>` : nodes.length > 1 ? '<div class="graph-dependencies">Independent branches · each receives the frozen document input</div>' : '');
}
function block(title, content) {
  if (!content || !asArray(content).length) return '';
  const contents = Array.isArray(content) ? `<ul>${content.map(x => `<li>${escape(typeof x === 'object' ? x.description || x.summary || JSON.stringify(x) : x)}</li>`).join('')}</ul>` : `<p>${escape(typeof content === 'object' ? JSON.stringify(content, null, 2) : content)}</p>`;
  return `<div class="proposal-block"><h3>${escape(title)}</h3>${contents}</div>`;
}
function renderMain() {
  const attempt = state.composing ? null : state.attempt;
  const run = getRun();
  const m = extractMetrics(run);
  $('attempt-heading').textContent = state.composing ? 'Review the evidence. Try one change.' : attempt?.title || attempt?.name || (state.selectedRun ? 'Retained evaluation' : 'Benchmark baseline');
  $('attempt-subtitle').textContent = state.composing ? 'The next proposal starts from the current workflow and recorded history.' : attempt?.instructions || (run ? `${num(m.documents)} benchmark documents · ${num(m.failed)} failed · ${date(run.startedAt)}` : 'No evaluation runs have been recorded yet.');
  const s = state.composing ? 'Draft' : attempt?.status || run?.status || 'No runs';
  $('attempt-status').textContent = statusText(s); $('attempt-status').className = `status ${s}`;
  $('measurements').innerHTML = metric('Validated detections', m.tp == null ? '—' : `${num(m.tp)}${m.positives == null ? '' : ` / ${num(m.positives)}`}`, m.fp == null ? 'Evidence-accepted findings' : `${num(m.fp)} false positives`) + metric('Input tokens', m.input == null && m.knownInput > 0 ? '≥' + num(m.knownInput) : num(m.input), m.input == null ? m.knownInput > 0 ? 'Known subtotal · ' + num(m.unknownUsage) + ' prompt unreported' : 'Breakdown unavailable' : m.newInput == null ? 'Logical workflow usage' : `Logical · ${num(m.newInput)} newly consumed`) + metric('Output tokens', m.output == null && m.knownOutput > 0 ? '≥' + num(m.knownOutput) : num(m.output), m.output == null ? m.knownOutput > 0 ? 'Known subtotal · ' + num(m.unknownUsage) + ' prompt unreported' : 'Breakdown unavailable' : m.newOutput == null ? 'Logical workflow usage' : `Logical · ${num(m.newOutput)} newly consumed`) + metric('Reused steps', num(m.cacheHits), m.cacheHits == null ? m.total == null ? 'Cache provenance unavailable' : `${num(m.total)} reported total tokens` : 'No new provider call');
  $('comparison').innerHTML = attempt?.comparison ? `<div class="comparison-note">${escape(typeof attempt.comparison === 'string' ? attempt.comparison : attempt.comparison.summary || JSON.stringify(attempt.comparison))}</div>` : '';
  const graph = attempt?.graph || attempt?.proposal?.graph || attempt?.parentGraph || run?.graph || run?.workflow || state.dashboard?.graph || state.dashboard?.workflow;
  renderGraph(graph);
  $('workflow-note').textContent = attempt?.graph ? 'Candidate snapshot' : attempt?.parentGraph ? 'Frozen parent snapshot' : 'Current Muse graph; historical runs retain their original models';
  $('parent-workflow').hidden = !attempt?.graph || !attempt?.parentGraph;
  if (attempt?.graph && attempt?.parentGraph) renderGraph(attempt.parentGraph, $('parent-graph'));
  const proposal = attempt?.proposal;
  $('proposal').hidden = !proposal;
  $('proposal').innerHTML = proposal ? typeof proposal === 'string' ? block('Proposal', proposal) : block('Proposed change', proposal.summary) + block('Hypothesis', proposal.hypothesis) + block('Changes', proposal.changes) + block('Expected benefit', proposal.expectedBenefit) + block('Risks & validation', proposal.risks || proposal.validation) : '';
  const logs = attempt?.logs || attempt?.events || [];
  $('execution-details').hidden = !logs.length && !attempt?.error;
  $('execution-log').textContent = asArray(logs).map(x => typeof x === 'string' ? x : `${x.timestamp || x.at || ''} ${x.message || x.text || JSON.stringify(x)}`).join('\n') + (attempt?.error ? `\n${attempt.error}` : '');
  renderActions();
}
function renderActions() {
  const a = state.attempt;
  $('attempt-form').hidden = !state.composing;
  $('action-content').hidden = state.composing;
  if (state.composing) return;
  const awaiting = ['awaiting_approval', 'proposed', 'proposal_ready'].includes(a?.status);
  const complete = ['completed', 'evaluated', 'ready'].includes(a?.status);
  const running = ['planning', 'implementing', 'evaluating', 'approved', 'queued', 'running'].includes(a?.status);
  const title = awaiting ? 'Your review comes first.' : running ? 'The attempt is in progress.' : complete ? 'Review the measured result.' : a?.status === 'failed' ? 'The attempt stopped.' : a?.status === 'promoted' ? 'A new starting point.' : a ? 'Retained for the next attempt.' : 'One hypothesis at a time.';
  const text = awaiting ? 'Read the proposal and workflow. Approval creates an isolated worktree, implements the change, and runs the benchmark.' : running ? 'Progress is saved as it happens. You can inspect other documents while this attempt runs.' : complete ? 'Compare accepted detections and token usage before choosing whether this workflow becomes the next baseline.' : a?.status === 'failed' ? 'The error and execution log are preserved. A new attempt can use this history to choose a better approach.' : a?.status === 'promoted' ? 'This approved workflow is available as the parent of future improvement attempts.' : a ? 'Its proposal, workflow, and results remain part of the optimization history.' : 'Muse studies the code and recorded misses, then proposes an extraction change for your approval.';
  $('action-content').innerHTML = `<h2 id="action-heading">${title}</h2><p>${text}</p>${!a ? '<ul class="review-list"><li>Keep the benchmark and scoring fixed.</li><li>Reuse unchanged step results.</li><li>Preserve the graph and every attempt.</li></ul>' : ''}${a?.error ? `<p class="inline-error">${escape(a.error)}</p>` : ''}${a ? `<dl class="attempt-meta"><dt>Attempt</dt><dd>${escape(id(a)?.slice(0, 12))}</dd>${a.baseCommit ? `<dt>Parent</dt><dd>${escape(a.baseCommit.slice(0, 12))}</dd>` : ''}${a.worktree ? `<dt>Worktree</dt><dd>${escape(a.worktree)}</dd>` : ''}${a.proposalHash ? `<dt>Proposal</dt><dd>${escape(a.proposalHash.slice(0, 12))}</dd>` : ''}</dl>` : ''}<div class="action-stack">${awaiting ? '<button class="primary full-width" data-action="approve">Approve & run</button><button class="secondary full-width" data-action="reject">Reject proposal</button>' : complete ? '<button class="primary full-width" data-action="promote">Use as baseline</button><button class="secondary full-width" data-action="new">Explore another change</button>' : !running ? '<button class="primary full-width" data-action="new">Start a new attempt</button>' : ''}</div><div class="readonly-note"><svg width="13" height="14" viewBox="0 0 14 15" fill="none" aria-hidden="true"><rect x="2" y="6" width="10" height="7" rx="1.5" stroke="currentColor"/><path d="M4 6V4a3 3 0 0 1 6 0v2" stroke="currentColor"/></svg><span>Benchmark labels and scoring stay fixed. Existing database records and cache are retained.</span></div>`;
  $('action-content').querySelectorAll('button').forEach(b => { b.disabled = state.busy; });
}
function renderHistory() {
  const runs = (state.dashboard?.runs || []).filter(r => r.documentsAttempted !== 0 || r.configuration?.attemptId);
  if (!runs.length) { $('history').innerHTML = '<p class="empty-state">No evaluation history yet. Recorded runs will appear here.</p>'; return; }
  $('history').innerHTML = `<table class="history-table"><thead><tr><th scope="col">Run</th><th scope="col">Documents</th><th scope="col">Validated TP</th><th scope="col">False positives</th><th scope="col">Input / output tokens</th><th scope="col">State</th></tr></thead><tbody>${runs.map(r => { const m = extractMetrics(r); return `<tr><td><button class="run-link" data-run="${escape(id(r))}">${escape(r.name || r.title || r.configuration?.attemptTitle || `Run ${String(id(r)).slice(0, 8)}`)}</button><small>${escape(date(r.startedAt))}</small></td><td>${num(m.documents)}${m.failed ? `<small>${num(m.failed)} failed</small>` : ''}</td><td>${num(m.tp)}</td><td>${num(m.fp)}</td><td>${num(m.input)} / ${num(m.output)}${m.input == null && m.total != null ? `<small>${num(m.total)} total reported</small>` : ''}</td><td>${statusBadge(r.status)}</td></tr>`; }).join('')}</tbody></table>`;
}
function renderDocuments() {
  const documents = state.dashboard?.documents || [];
  const current = state.document;
  if (!documents.length) { $('document-select').innerHTML = '<option value="">No benchmark documents</option>'; return; }
  $('document-select').innerHTML = documents.map(d => { const key = d.sourceSha256 || d.id || d.sampleId || d.documentId; return `<option value="${escape(key)}">${escape(d.sampleId || d.id)}${d.filename || d.name ? ` · ${escape(d.filename || d.name)}` : ''}</option>`; }).join('');
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
    $('matrix').innerHTML = '<p class="empty-state">No expected-results snapshot is available for this document. Import its benchmark labels to compare attempts.</p>';
    $('matrix-summary').textContent = ''; return;
  }
  const expected = row => row.expected === true || row.expected === 'present' || row.expectedPresence === true ? 'present' : row.expected === false || row.expected === 'absent' || row.expectedPresence === false ? 'absent' : 'unknown';
  const positives = rows.filter(r => expected(r) === 'present').length;
  const goldHash = data.labelSha256 || data.goldChecksum || data.goldSha256;
  $('matrix-summary').innerHTML = `<span>${escape(data.sampleId || state.document)} · ${num(rows.length)} extraction targets</span><span>${num(positives)} expected present</span>${goldHash ? `<span title="${escape(goldHash)}">Gold ${escape(goldHash.slice(0, 12))}</span>` : ''}<span>Document / target presence comparison</span>`;
  $('matrix').innerHTML = `<table class="coverage-table"><thead><tr><th scope="col">Extraction target</th><th scope="col">Expected</th>${columns.map(c => `<th scope="col" title="${escape(c.runId || c.id || '')}">${escape(c.label || c.title || c.name || String(c.runId || c.id).slice(0, 8))}</th>`).join('')}</tr></thead><tbody>${rows.map(row => { const e = expected(row); return `<tr><td scope="row">${escape(row.name || row.category || row.tag)}${row.tag ? `<span class="target-tag">${escape(row.tag)}</span>` : ''}</td><td class="expected">${e === 'present' ? 'Present' : e === 'absent' ? 'Absent' : 'Unknown'}${e === 'present' && row.goldFindings != null ? `<span class="target-tag">${num(row.goldFindings)} gold ${row.goldFindings === 1 ? 'finding' : 'findings'}</span>` : ''}</td>${columns.map((column, index) => {
    const cells = row.cells || row.observations || row.results || [];
    const cell = Array.isArray(cells) ? cells.find(c => c && typeof c === 'object' && (c.columnId === id(column) || c.runId === id(column) || c.attemptId === id(column))) || cells[index] : cells[id(column)];
    const s = cellStatus(cell, e); const color = ['found', 'detected', 'correct_absence'].includes(s) ? 'good' : ['missed', 'false_positive', 'rejected_evidence'].includes(s) ? 'bad' : 'pending';
    const detail = cell?.error || (cell?.count > 0 ? `${num(cell.count)} accepted ${cell.count === 1 ? 'finding' : 'findings'}` : ['not_analyzed', 'not_found_in_reviewed_text'].includes(cell?.detail) ? '' : cell?.detail?.replaceAll('_', ' '));
    return `<td class="result-cell"><span class="cell-status ${escape(s)}"><i class="legend-dot ${color}"></i>${escape(resultLabels[s] || statusText(s))}</span>${detail ? `<span class="cell-detail">${escape(detail)}</span>` : ''}</td>`;
  }).join('')}</tr>`; }).join('')}</tbody></table>`;
}
async function loadMatrix() {
  if (!state.document) { renderMatrix({ rows: [] }); return; }
  const selected = state.document;
  try { const data = await request(`/api/documents/${encodeURIComponent(selected)}/matrix`); if (state.document === selected) renderMatrix(data); }
  catch (error) { if (state.document === selected) $('matrix').innerHTML = `<p class="empty-state inline-error">${escape(error.message)}</p>`; }
}
async function refresh() {
  if (state.refreshing) return;
  state.refreshing = true;
  try {
    state.dashboard = await request('/api/dashboard');
    const status = state.dashboard.status || {};
    const connected = status.mongoConnected ?? status.connected ?? true;
    $('connection').className = `connection ${connected ? 'connected' : 'disconnected'}`;
    $('connection').innerHTML = `<span class="signal"></span>${escape(connected ? 'MongoDB history connected' : status.error || 'History unavailable')}`;
    if (state.selected !== 'baseline' && !state.composing) state.attempt = await request(`/api/attempts/${encodeURIComponent(state.selected)}`);
    renderTabs(); renderMain(); renderHistory(); renderDocuments(); await loadMatrix();
  } catch (error) {
    $('connection').className = 'connection disconnected'; $('connection').innerHTML = '<span class="signal"></span>Connection unavailable'; showError(error);
  } finally { state.refreshing = false; }
}
function compose() { state.composing = true; clearError(); renderTabs(); renderMain(); $('instructions').focus(); }
$('new-attempt').addEventListener('click', compose);
$('refresh').addEventListener('click', () => { clearError(); refresh(); });
$('document-select').addEventListener('change', event => { state.document = event.target.value; $('matrix').innerHTML = '<p class="empty-state">Loading document history…</p>'; loadMatrix(); });
$('attempt-tabs').addEventListener('click', async event => {
  const button = event.target.closest('[data-attempt]'); if (!button) return;
  if (button.dataset.attempt === 'new') { compose(); return; }
  state.composing = false; state.selected = button.dataset.attempt; state.selectedRun = null; state.attempt = null; clearError();
  try { if (state.selected !== 'baseline') state.attempt = await request(`/api/attempts/${encodeURIComponent(state.selected)}`); renderTabs(); renderMain(); }
  catch (error) { showError(error); }
});
$('attempt-form').addEventListener('submit', async event => {
  event.preventDefault(); if (state.busy) return;
  state.busy = true; $('propose-button').disabled = true; $('propose-button').textContent = 'Starting Muse…'; clearError();
  try {
    const a = await request('/api/attempts', { method: 'POST', body: JSON.stringify({ instructions: $('instructions').value.trim(), parentAttemptId: ['evaluated', 'promoted'].includes(state.attempt?.status) ? state.selected : null }) });
    state.selected = id(a); state.attempt = a; state.composing = false; state.selectedRun = null; $('instructions').value = ''; await refresh();
  } catch (error) { showError(error); }
  finally { state.busy = false; $('propose-button').disabled = false; $('propose-button').textContent = 'Generate proposal'; renderActions(); }
});
$('action-content').addEventListener('click', async event => {
  const button = event.target.closest('[data-action]'); if (!button || state.busy) return;
  const action = button.dataset.action;
  if (action === 'new') { compose(); return; }
  state.busy = true; clearError(); renderActions();
  try {
    await request(`/api/attempts/${encodeURIComponent(state.selected)}/${action}`, { method: 'POST', body: JSON.stringify(action === 'approve' ? { proposalHash: state.attempt.proposalHash } : {}) });
    await refresh();
  } catch (error) { showError(error); }
  finally { state.busy = false; renderActions(); }
});
$('history').addEventListener('click', async event => {
  const button = event.target.closest('[data-run]'); if (!button) return;
  state.selectedRun = button.dataset.run; state.selected = 'baseline'; state.composing = false; state.attempt = null;
  try {
    const detail = await request(`/api/runs/${encodeURIComponent(state.selectedRun)}`);
    const index = state.dashboard.runs.findIndex(r => id(r) === state.selectedRun);
    const run = Array.isArray(detail.runs) ? detail.runs[0] || {} : detail;
    if (index >= 0) {
      const existing = state.dashboard.runs[index];
      state.dashboard.runs[index] = { ...existing, ...run,
        summary: run.summary?.validatedFindings ? run.summary : existing.summary || run.summary,
        workflow: detail.workflow || run.workflow || existing.workflow };
    }
    renderTabs(); renderMain(); $('attempt-heading').scrollIntoView({ behavior: 'auto', block: 'start' });
  } catch (error) { showError(error); }
});
await refresh();
setInterval(() => { if (!document.hidden && !state.busy) refresh(); }, 5000);
