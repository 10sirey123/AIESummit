const escape = value => String(value ?? '').replace(/[&<>"']/g, x => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[x]));
const format = new Intl.NumberFormat('en-US');
const number = value => typeof value === 'number' && Number.isFinite(value) ? value : null;
const display = value => value == null ? 'Unavailable' : format.format(value);
const runId = run => run?.runId || run?.id || run?._id;

function measures(run) {
  const summary = run?.summary || run?.results || run || {};
  const quality = summary.validatedFindings?.micro || {};
  const usage = summary.metrics || {};
  return {
    tp: number(quality.tp), fp: number(quality.fp), fn: number(quality.fn), tn: number(quality.tn),
    documents: number(run?.documentsAttempted ?? summary.documentsAttempted),
    total: usage.tokenTotalsAvailable === false ? null : number(usage.reportedTotalTokens ?? summary.reportedLlmTokens),
    input: usage.inputOutputBreakdownAvailable === false ? null : number(usage.reportedInputTokens),
    output: usage.inputOutputBreakdownAvailable === false ? null : number(usage.reportedOutputTokens),
    knownInput: number(usage.knownInputTokensSubtotal), knownOutput: number(usage.knownOutputTokensSubtotal),
    cacheHits: number(usage.cacheHits), failed: number(run?.documentsFailed ?? summary.failedDocuments),
    workflowHash: summary.workflowHash || run?.configuration?.workflowHash,
    benchmarkHash: summary.benchmarkHash || summary.datasetHash || run?.configuration?.benchmarkHash,
    unknownUsage: number(usage.unknownUsagePromptCount)
  };
}
function usable(run) {
  const m = measures(run);
  return ['completed', 'completed_with_failures'].includes(run?.status)
    && run?.configuration?.command === 'workflow-run' && m.documents > 0 && m.tp != null && m.fp != null
    && (m.total > 0 || m.input > 0 || m.knownInput > 0 || m.cacheHits > 0);
}
function decisions(m) { return [m.tp, m.fp, m.fn, m.tn].every(v => v != null) ? m.tp + m.fp + m.fn + m.tn : null; }
function dateValue(run) { const value = Date.parse(run?.startedAt || run?.completedAt || ''); return Number.isFinite(value) ? value : 0; }

// The graph uses measured native benchmark runs. Failed planning/infrastructure jobs and
// retrospective GPT imports are deliberately absent; provider/validation failures remain scored.
export function progressionPoints(dashboard) {
  const runs = dashboard?.runs || [];
  const attempts = dashboard?.attempts || [];
  const linked = new Set(attempts.map(a => a.runId).filter(Boolean));
  const native = runs.filter(usable).sort((a, b) => dateValue(a) - dateValue(b));
  const selected = native.find(r => runId(r) === dashboard?.chartBaselineRunId)
    || native.find(r => r.isBaseline)
    || native.find(r => !linked.has(runId(r)));
  if (!selected) return [];
  const reference = measures(selected);
  const comparable = run => {
    const m = measures(run);
    return m.documents === reference.documents && decisions(m) === decisions(reference)
      && !(reference.benchmarkHash && m.benchmarkHash && reference.benchmarkHash !== m.benchmarkHash);
  };
  const baseline = (dashboard?.chartBaselineRunId || selected.isBaseline && !linked.has(runId(selected))) ? selected
    : native.find(r => !linked.has(runId(r)) && comparable(r) && measures(r).total != null)
      || native.find(r => !linked.has(runId(r)) && comparable(r)) || selected;
  const points = [{ id: runId(baseline), attemptId: 'baseline', label: 'Baseline', shortLabel: 'B',
    title: 'Original Muse benchmark', promoted: true, current: !!baseline.isBaseline, ...measures(baseline) }];
  const seen = new Set([runId(baseline)]);
  const candidates = attempts.filter(a => a.runId && ['evaluated', 'promoted', 'rejected'].includes(a.status))
    .map(a => ({ attempt: a, run: runs.find(r => runId(r) === a.runId) }))
    .filter(({ run }) => run && usable(run) && comparable(run))
    .sort((a, b) => dateValue(a.run) - dateValue(b.run));
  for (const { attempt, run } of candidates) {
    if (seen.has(runId(run))) continue;
    seen.add(runId(run));
    points.push({ id: runId(run), attemptId: attempt.id, label: `Attempt ${points.length}`, shortLabel: String(points.length),
      title: attempt.title || 'Extraction experiment', promoted: attempt.status === 'promoted', current: !!run.isBaseline,
      ...measures(run) });
  }
  return points;
}

function segments(points, value, x, y) {
  const chunks = []; let chunk = [];
  for (let i = 0; i < points.length; i++) {
    const v = value(points[i]);
    if (v == null) { if (chunk.length) chunks.push(chunk); chunk = []; }
    else chunk.push(`${x(i).toFixed(2)},${y(v).toFixed(2)}`);
  }
  if (chunk.length) chunks.push(chunk);
  return chunks.filter(c => c.length > 1).map(c => `<polyline points="${c.join(' ')}" fill="none"/>`).join('');
}
function plot(points, series, kind, maximum) {
  const width = 600; const height = 245;
  const left = kind === 'tokens' ? 67 : 42; const top = 20; const right = 20; const bottom = 42;
  const actual = series.flatMap(s => points.map(s.value)).filter(v => v != null);
  if (!actual.length) return '<p class="chart-unavailable">Token usage is unavailable. Missing counters are never shown as zero.</p>';
  const high = maximum || Math.max(1, ...actual) * 1.12;
  const x = i => points.length === 1 ? (left + width - right) / 2 : left + i * (width - left - right) / (points.length - 1);
  const y = value => height - bottom - value / high * (height - top - bottom);
  const ticks = Array.from({ length: 5 }, (_, i) => high * i / 4);
  const tick = value => kind === 'tokens' ? value >= 1000 ? `${Math.round(value / 1000)}k` : String(Math.round(value)) : String(Math.round(value));
  return `<svg class="progression-svg" viewBox="0 0 ${width} ${height}" role="img" aria-label="${kind === 'quality' ? 'Expected items found and false positives' : 'Logical total, input and output tokens'} across benchmark evaluations"><g class="chart-grid">${ticks.map(value => `<line x1="${left}" x2="${width - right}" y1="${y(value)}" y2="${y(value)}"/><text x="${left - 10}" y="${y(value) + 4}" text-anchor="end">${tick(value)}</text>`).join('')}</g>${points.map((p, i) => `<text class="chart-x-label" x="${x(i)}" y="${height - 14}" text-anchor="middle">${escape(p.shortLabel)}</text>`).join('')}${series.map(s => `<g class="chart-series ${s.className}">${segments(points, s.value, x, y)}${points.map((p, i) => { const v = s.value(p); return v == null ? '' : `<a href="#main" data-progression-attempt="${escape(p.attemptId)}" aria-label="${escape(`${p.label}: ${s.label} ${display(v)}. Open attempt.`)}"><title>${escape(`${p.label} · ${s.label}: ${display(v)}\n${p.title}`)}</title><circle class="chart-point-hit" cx="${x(i)}" cy="${y(v)}" r="13"/><circle cx="${x(i)}" cy="${y(v)}" r="4.5"/></a>`; }).join('')}</g>`).join('')}</svg>`;
}
function tokenText(value, subtotal) { return value != null ? display(value) : subtotal > 0 ? `≥ ${display(subtotal)} (partial)` : 'Unavailable'; }

export function renderProgression(dashboard) {
  const target = document.getElementById('progression'); if (!target) return;
  const valuesOpen = !!target.querySelector('.progression-values')?.open;
  const points = progressionPoints(dashboard);
  const signature = JSON.stringify(points);
  if (target._points === signature) return;
  target._points = signature;
  document.getElementById('progression-count').textContent = points.length ? `${points.length} measured ${points.length === 1 ? 'evaluation' : 'evaluations'}` : '';
  if (!points.length) { target.innerHTML = '<p class="empty-state">Completed Muse benchmark attempts will appear automatically. Historical GPT results and planning failures remain in the Run history view.</p>'; return; }
  let champion = points[0].tp;
  const withChampion = points.map(p => { if (p.promoted) champion = p.tp; return { ...p, champion }; });
  const positives = points[0].tp + points[0].fn;
  const quality = [{ label: 'Selected baseline detections', className: 'quality-champion', value: p => p.champion },
    { label: 'Expected items found', className: 'quality-found', value: p => p.tp },
    { label: 'False positives', className: 'quality-false', value: p => p.fp }];
  const tokens = [{ label: 'Total tokens', className: 'tokens-total', value: p => p.total },
    { label: 'Input tokens', className: 'tokens-input', value: p => p.input },
    { label: 'Output tokens', className: 'tokens-output', value: p => p.output }];
  const qualityLegend = '<span class="quality-found">Expected items found</span><span class="quality-false">False positives</span><span class="quality-champion">Selected baseline</span>';
  const tokenLegend = '<span class="tokens-total">Total</span><span class="tokens-input">Input</span><span class="tokens-output">Output</span>';
  target.innerHTML = `<div class="progression-charts"><figure class="progression-figure"><figcaption><h3>Extraction quality</h3><span>${display(positives)} known positive targets · higher detections, lower false positives</span></figcaption>${plot(withChampion, quality, 'quality', Math.max(positives, ...points.map(p => p.fp)) + 2)}<div class="chart-legend">${qualityLegend}</div></figure><figure class="progression-figure"><figcaption><h3>Logical token usage</h3><span>Includes reused responses · lower is better</span></figcaption>${plot(points, tokens, 'tokens')}<div class="chart-legend">${tokenLegend}</div></figure></div><p class="progression-note">${points.length === 1 ? 'The baseline is recorded. Completed benchmark attempts will add real points here automatically. ' : ''}B is the original Muse baseline. Numbered points are measured attempts, including regressions. The dashed line follows promoted workflows; it is not an invented best score. Missing input/output counters leave gaps.</p><details class="progression-values"${valuesOpen ? ' open' : ''}><summary>Inspect measured values and run identities</summary><div class="table-scroll"><table class="history-table"><thead><tr><th scope="col">Evaluation</th><th scope="col">Expected items found / ${display(positives)}</th><th scope="col">False positives</th><th scope="col">Total tokens</th><th scope="col">Input tokens</th><th scope="col">Output tokens</th><th scope="col">Reused steps</th></tr></thead><tbody>${points.map(p => `<tr><td><a href="#main" data-progression-attempt="${escape(p.attemptId)}">${escape(p.label)}${p.current ? ' · current baseline' : p.promoted && p.attemptId !== 'baseline' ? ' · promoted' : ''}</a><small>${escape(p.title)}</small><small>${escape(p.id)}</small></td><td>${display(p.tp)}</td><td>${display(p.fp)}</td><td>${display(p.total)}</td><td>${tokenText(p.input, p.knownInput)}</td><td>${tokenText(p.output, p.knownOutput)}</td><td>${display(p.cacheHits)}</td></tr>`).join('')}</tbody></table></div></details>`;
}
