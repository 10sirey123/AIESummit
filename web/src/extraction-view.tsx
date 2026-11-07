import { useEffect, useMemo, useState } from 'react';
import { AlertTriangle, CheckCircle2, ChevronDown, ChevronRight, CircleDashed, Loader2, Quote, RefreshCw, Sparkles, X } from 'lucide-react';
import { errorMessage, type DocumentResult, type ExtractionEvidence, type ExtractionFinding, type ExtractionType, type Session, type TargetOutcome } from './api';
import { headline, orderedTargets, outcomeLabel, outcomeShort, selectAnalyses, targetMeta, valueRows, type DocumentSelection, type TargetMeta, type UploadTargets } from './extraction-meta';
import { loadExtractions, type UploadExtractions } from './extraction-store';

export function TargetIcon({ tag, size = 15, muted, title }: { tag: string; size?: number; muted?: boolean; title?: string }) {
  const meta = targetMeta(tag);
  const Icon = meta.icon;
  return <span className="target-icon" data-muted={muted || undefined} style={{ '--target': meta.color } as React.CSSProperties} title={title}>
    <Icon size={size} aria-hidden="true" />
  </span>;
}

/** One finding in the context of its target and document; `key` is stable across polls. */
export type SelectedFinding = { key: string; tag: string; finding: ExtractionFinding; documentId: number; analysisId: number };
export const findingKey = (finding: ExtractionFinding) => `x-${finding.id}`;

export function flattenFindings(selections: DocumentSelection[], order: TargetMeta[]): SelectedFinding[] {
  const rank = new Map(order.map((item, index) => [item.tag, index]));
  return selections.flatMap(selection => (selection.shown?.targets ?? []).flatMap(target => target.extractions.map(finding => ({
    key: findingKey(finding), tag: target.tag, finding, documentId: selection.documentId, analysisId: selection.shown!.id,
  })))).sort((a, b) => (rank.get(a.tag) ?? 99) - (rank.get(b.tag) ?? 99) || firstPage(a.finding) - firstPage(b.finding));
}
const firstPage = (finding: ExtractionFinding) => Math.min(...finding.evidence.map(item => item.page), 9999);

/** Status of the contract-extraction branch, with the explicit actions the API allows. */
export function ExtractionStatus({ session, uploadId, state, documents }: { session: Session; uploadId: number; state?: UploadExtractions; documents: DocumentResult[] }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const summary = state?.summary;
  async function request(key: string) {
    setBusy(true); setError('');
    try { await session.requestExtractions(uploadId, key); loadExtractions(session, uploadId, true); }
    catch (failure) { setError(errorMessage(failure)); }
    finally { setBusy(false); }
  }
  if (!summary) return <p className="extraction-status" role="status">{state?.error ? <span className="error">{state.error}</span> : <><Loader2 size={14} className="spin" aria-hidden="true" /> Loading extracted terms…</>}</p>;
  const x = summary.extraction;
  const failedDocs = summary.documents.filter(doc => doc.analysis.status === 'failed');
  let content: React.ReactNode;
  if (!summary.availability.available) content = <><AlertTriangle size={14} aria-hidden="true" /> Contract extraction is not enabled on the server.</>;
  else if (x.status === 'waiting_for_documents') content = <><Loader2 size={14} className="spin" aria-hidden="true" /> Waiting for OCR and document splitting…</>;
  else if (x.status === 'analyzing') content = <><Loader2 size={14} className="spin" aria-hidden="true" /> Extracting terms · {x.readyDocuments} of {x.totalDocuments} document{x.totalDocuments === 1 ? '' : 's'} done</>;
  else if (x.status === 'ready') content = <><CheckCircle2 size={14} className="ok" aria-hidden="true" /> Terms extracted from {x.totalDocuments} document{x.totalDocuments === 1 ? '' : 's'}</>;
  else if (x.status === 'not_started') content = <>No extraction has run for this upload yet.</>;
  else content = <><AlertTriangle size={14} aria-hidden="true" /> Extraction {x.status.replaceAll('_', ' ')}{failedDocs.length ? ` · ${failedDocs.length} document${failedDocs.length === 1 ? '' : 's'} failed` : ''}</>;
  const canStart = summary.availability.available && (x.status === 'not_started' || x.status === 'incomplete' || x.status === 'blocked');
  const canRerun = summary.availability.available && !x.canPoll && documents.length > 0 && x.status !== 'not_started';
  return <div className="extraction-status" role="status" data-state={x.status}>
    <span className="extraction-status-text">{content}</span>
    {canStart && <button type="button" className="button primary small" disabled={busy} onClick={() => void request('initial')}><Sparkles size={14} aria-hidden="true" /> {busy ? 'Requesting…' : 'Extract terms'}</button>}
    {canRerun && <button type="button" className="text-button" disabled={busy} title="Creates a new, separate analysis attempt"
      onClick={() => { if (window.confirm('Run a new extraction attempt for this upload? Earlier results stay available as history.')) void request(`rerun-${Date.now()}`); }}>
      <RefreshCw size={13} aria-hidden="true" /> Re-analyze</button>}
    {error && <span className="error">{error}</span>}
  </div>;
}

/** Left rail: every target with its outcome, expanding to its findings. */
export function ExtractionRail({ catalog, selections, documents, selected, onSelect }: {
  catalog?: ExtractionType[]; selections: DocumentSelection[]; documents: DocumentResult[]; selected?: string; onSelect: (item: SelectedFinding) => void;
}) {
  const [open, setOpen] = useState<Record<string, boolean>>({});
  const [filter, setFilter] = useState<'all' | 'found'>('all');
  const order = useMemo(() => orderedTargets(catalogTargets(catalog, selections)), [catalog, selections]);
  const findings = useMemo(() => flattenFindings(selections, order), [selections, order]);
  const multipleDocs = selections.length > 1;
  const docTitle = (id: number) => documents.find(doc => doc.id === id)?.title ?? `Document ${id}`;
  const outcomes = useMemo(() => {
    const map = new Map<string, TargetOutcome>();
    for (const selection of selections) for (const target of selection.shown?.targets ?? []) {
      const previous = map.get(target.tag);
      if (!previous || target.outcome === 'detected' || (previous !== 'detected' && target.outcome === 'uncertain')) map.set(target.tag, target.outcome);
    }
    return map;
  }, [selections]);
  const explanations = (tag: string) => selections.flatMap(selection => (selection.shown?.targets ?? []).filter(target => target.tag === tag && target.explanation)
    .map(target => ({ doc: selection.documentId, text: target.explanation! })));
  const found = order.filter(item => outcomes.get(item.tag) === 'detected').length;
  const failed = selections.filter(selection => !selection.shown && selection.latest.status === 'failed');
  const pending = selections.some(selection => !selection.shown && selection.latest.status === 'analyzing');
  const visible = filter === 'found' ? order.filter(item => outcomes.get(item.tag) === 'detected') : order;

  return <nav className="review-rail extraction-rail" aria-label="Extracted terms">
    <div className="rail-head">
      <p className="detail-label">Extracted terms</p>
      <p className="rail-count">{found} of {order.length} types found · {findings.length} finding{findings.length === 1 ? '' : 's'}</p>
      <div className="rail-filter" role="group" aria-label="Show">
        {(['all', 'found'] as const).map(value => <button key={value} type="button" aria-pressed={filter === value} onClick={() => setFilter(value)}>{value === 'all' ? 'All types' : 'Found only'}</button>)}
      </div>
    </div>
    {failed.length > 0 && <div className="rail-failure" role="alert">
      <p><strong>No findings were published{multipleDocs ? ` for ${failed.length} document${failed.length === 1 ? '' : 's'}` : ''}.</strong> The model's response was rejected by validation, so this does not mean the clauses are absent.</p>
      {failed.map(selection => <p key={selection.documentId} className="rail-failure-reason">{multipleDocs ? `${docTitle(selection.documentId)}: ` : ''}{selection.latest.error ?? 'Extraction failed.'} <span>(attempt #{selection.latest.id}{selection.attempts > 1 ? ` of ${selection.attempts}` : ''})</span></p>)}
    </div>}
    {pending && !findings.length && <p className="detail-muted"><Loader2 size={13} className="spin" aria-hidden="true" /> Results appear here when extraction completes.</p>}
    <ul className="target-rows">
      {visible.map(meta => {
        const outcome = outcomes.get(meta.tag) ?? 'not_analyzed';
        const unpublished = outcome === 'not_analyzed' && failed.length > 0 && !pending;
        const items = findings.filter(item => item.tag === meta.tag);
        const isOpen = open[meta.tag] ?? (items.some(item => item.key === selected));
        const notes = explanations(meta.tag);
        return <li key={meta.tag} data-open={isOpen || undefined}>
          <button type="button" className="target-row" data-outcome={outcome} aria-expanded={isOpen}
            onClick={() => {
              setOpen(state => ({ ...state, [meta.tag]: !isOpen }));
              if (!isOpen && items.length && !items.some(item => item.key === selected)) onSelect(items[0]);
            }}>
            <TargetIcon tag={meta.tag} muted={outcome !== 'detected'} />
            <span className="target-row-name">{meta.name}</span>
            {items.length ? <span className="target-count" style={{ '--target': meta.color } as React.CSSProperties}>{items.length}</span>
              : <span className="outcome-badge" data-outcome={unpublished ? 'failed' : outcome}>{unpublished ? 'Not published' : outcomeShort[outcome]}</span>}
            {isOpen ? <ChevronDown size={14} aria-hidden="true" /> : <ChevronRight size={14} aria-hidden="true" />}
          </button>
          {isOpen && <div className="target-findings">
            {items.map(item => <button key={item.key} type="button" className="target-finding" aria-current={item.key === selected || undefined} onClick={() => onSelect(item)}
              style={{ '--target': meta.color } as React.CSSProperties}>
              <span className="target-finding-value">{headline(item.tag, item.finding)}</span>
              <span className="target-finding-meta">{multipleDocs ? `${docTitle(item.documentId)} · ` : ''}p.{[...new Set(item.finding.evidence.map(e => e.page))].join(', ')}</span>
            </button>)}
            {!items.length && (notes.length ? notes.map((note, index) => <p key={index} className="target-explanation">{multipleDocs ? <strong>{docTitle(note.doc)}: </strong> : null}{note.text}</p>)
              : <p className="target-explanation">{unpublished ? 'No result was published because the analysis failed validation.' : `${outcomeLabel[outcome]}.`}</p>)}
            {outcome === 'not_found_in_reviewed_text' && <p className="target-caveat">Not finding a clause is not proof it is absent.</p>}
          </div>}
        </li>;
      })}
    </ul>
  </nav>;
}

function catalogTargets(catalog: ExtractionType[] | undefined, selections: DocumentSelection[]) {
  const tags = new Map<string, { tag: string; name: string }>();
  for (const type of catalog ?? []) if (type.enabled) tags.set(type.tag, type);
  for (const selection of selections) for (const target of selection.shown?.targets ?? []) if (!tags.has(target.tag)) tags.set(target.tag, { tag: target.tag, name: targetMeta(target.tag).name });
  return [...tags.values()];
}

/** Right panel: one finding with its structured values and exact source evidence. */
export function ExtractionDetail({ session, uploadId, item, selection, documents, activeEvidence, located, onEvidence, onClose }: {
  session: Session; uploadId: number; item: SelectedFinding; selection?: DocumentSelection; documents: DocumentResult[];
  activeEvidence?: number; located?: boolean; onEvidence: (evidence: ExtractionEvidence, index: number) => void; onClose: () => void;
}) {
  const meta = targetMeta(item.tag);
  const { finding } = item;
  const rows = valueRows(finding.values);
  const doc = documents.find(entry => entry.id === item.documentId);
  const evidenceFor = (fields: string[]) => finding.evidence.map((evidence, index) => ({ evidence, index })).filter(entry => entry.evidence.supportedFields.some(field => fields.includes(field)));
  const active = activeEvidence !== undefined ? finding.evidence[activeEvidence] : undefined;
  const verify = useSnapshotCheck(session, uploadId, active);

  return <div className="finding-detail extraction-detail" style={{ '--target': meta.color } as React.CSSProperties}>
    <div className="finding-detail-head">
      <p className="detail-eyebrow extraction-eyebrow"><TargetIcon tag={item.tag} size={14} /> {meta.name}</p>
      <button type="button" className="detail-close" aria-label="Close finding" onClick={onClose}><X size={15} aria-hidden="true" /></button>
    </div>
    <h3>{finding.title}</h3>
    {finding.summary && <p className="extraction-summary">{finding.summary}</p>}
    {selection?.fallback && <p className="fallback-note"><AlertTriangle size={13} aria-hidden="true" /> The latest attempt (#{selection.latest.id}) failed{selection.latest.error ? `: ${selection.latest.error}` : ''}. Showing earlier successful analysis #{selection.shown?.id}.</p>}

    <div className="detail-block">
      <p className="detail-label">Extracted values</p>
      {finding.schemaVersion !== 1 && <p className="detail-muted">Value schema v{finding.schemaVersion} is not recognised by this client; values are shown as raw data.</p>}
      {rows.length ? <dl className="value-table">
        {rows.map(row => {
          const support = evidenceFor(row.fields);
          return <div key={row.key} className="value-row" data-key={row.key}>
            <dt>{row.label}</dt>
            <dd>{row.text}{support.length > 0 && <span className="value-support">{support.map(entry =>
              <button key={entry.index} type="button" className="page-chip" aria-pressed={activeEvidence === entry.index} title={`Show supporting evidence on page ${entry.evidence.page}`}
                onClick={() => onEvidence(entry.evidence, entry.index)}>p.{entry.evidence.page}</button>)}</span>}</dd>
          </div>;
        })}
      </dl> : <p className="detail-muted">Clause detected; no structured values were stated.</p>}
    </div>

    <div className="detail-block">
      <p className="detail-label">Source evidence · {finding.evidence.length} segment{finding.evidence.length === 1 ? '' : 's'}</p>
      <ol className="evidence-list">
        {finding.evidence.map((evidence, index) => <li key={index} data-active={activeEvidence === index || undefined}>
          <button type="button" className="evidence-button" aria-pressed={activeEvidence === index} onClick={() => onEvidence(evidence, index)}>
            <span className="evidence-meta">
              <Quote size={12} aria-hidden="true" />
              <span>Page {evidence.page}</span>
              <span className="role-badge" data-role={evidence.role}>{evidence.role}</span>
              {evidence.matchMethod && evidence.matchMethod !== 'exact' && <span className="match-badge" title={evidence.modelQuote ?? undefined}>
                approximate{evidence.matchScore !== null ? ` · ${Math.round(evidence.matchScore * 100)}% overlap` : ''}</span>}
            </span>
            <blockquote>{evidence.quote.length > 420 ? `${evidence.quote.slice(0, 420)}…` : evidence.quote}</blockquote>
            {evidence.supportedFields.length > 0 && <span className="evidence-fields">Supports: {evidence.supportedFields.join(', ')}</span>}
          </button>
          {activeEvidence === index && <p className="evidence-status" role="status">
            {located === undefined ? 'Finding it on the page…' : located ? `Highlighted on page ${evidence.page}` : 'Shown on the page; the exact text could not be highlighted in this view.'}
            {verify && <> · <span data-ok={verify.ok}>{verify.text}</span></>}
          </p>}
        </li>)}
      </ol>
    </div>
    <p className="annotation-future">AI-extracted · {doc ? `${doc.title} · ` : ''}analysis #{item.analysisId} · review state: {finding.reviewState}. A found clause is a fact, not a risk rating or legal advice.</p>
  </div>;
}

/** Confirms the evidence offsets against the immutable page snapshot the analysis used. */
function useSnapshotCheck(session: Session, uploadId: number, evidence?: ExtractionEvidence) {
  const [result, setResult] = useState<{ ok: boolean; text: string }>();
  useEffect(() => {
    setResult(undefined);
    if (!evidence) return;
    const controller = new AbortController();
    void session.snapshot(uploadId, evidence.pageTextRevisionId, controller.signal).then(snapshot => {
      if (controller.signal.aborted) return;
      const exact = snapshot.contentHash === evidence.contentHash && snapshot.markdown.slice(evidence.startOffset, evidence.endOffset) === evidence.quote;
      setResult(exact ? { ok: true, text: `verified against snapshot #${snapshot.id}` } : { ok: false, text: 'snapshot text does not match these offsets' });
    }).catch(() => { if (!controller.signal.aborted) setResult(undefined); });
    return () => controller.abort();
  }, [session, uploadId, evidence?.pageTextRevisionId, evidence?.startOffset, evidence?.endOffset]);
  return result;
}

/** Empty right panel in extraction mode. */
export function ExtractionHint({ count }: { count: number }) {
  return <div className="annotation-empty">
    <CircleDashed size={23} aria-hidden="true" />
    <h4>{count ? 'Select an extracted term' : 'No extracted terms yet'}</h4>
    <p>{count ? 'Pick a finding on the left to see its structured values and highlight the source text on the page.' : 'When extraction completes, dates, notice periods, caps and other terms appear on the left with their evidence.'}</p>
  </div>;
}

/** Compact row of colored icons for the targets found in one upload (contract list). */
export function TargetChips({ targets, max = 10 }: { targets?: UploadTargets; max?: number }) {
  if (!targets) return null;
  const found = orderedTargets([...targets].filter(([, value]) => value.outcome === 'detected').map(([tag]) => ({ tag, name: targetMeta(tag).name })));
  if (!found.length) return null;
  return <span className="target-chips" aria-label={`Found: ${found.map(item => item.name).join(', ')}`}>
    {found.slice(0, max).map(item => <TargetIcon key={item.tag} tag={item.tag} size={12} title={`${item.name} · ${targets.get(item.tag)!.findings.length} finding(s)`} />)}
  </span>;
}

export { selectAnalyses };
