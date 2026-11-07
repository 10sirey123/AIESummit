import { useEffect, useMemo, useState } from 'react';
import { ArrowUpRight, Check, Loader2 } from 'lucide-react';
import { errorMessage, type Session, type Upload } from './api';
import { headline, orderedTargets, outcomeShort, targetMeta, uploadTargets, valueRows, type UploadTargets } from './extraction-meta';
import { useCatalog, useManyExtractions } from './extraction-store';
import { TargetIcon } from './extraction-view';
import type { OpenRequest } from './workspace';

const maxColumns = 6;

/** Compare extracted terms across contracts: which types they share, and the values side by side. */
export function CompareView({ session, onOpen }: { session: Session; onOpen: (request: Omit<OpenRequest, 'request'>) => void }) {
  const [uploads, setUploads] = useState<Upload[]>();
  const [error, setError] = useState('');
  const [chosen, setChosen] = useState<number[]>();
  const [hidden, setHidden] = useState<Set<string>>(new Set());
  const [focusTag, setFocusTag] = useState<string>();
  const [onlyDiffer, setOnlyDiffer] = useState(false);
  const catalog = useCatalog(session);

  useEffect(() => {
    let live = true;
    session.list().then(rows => { if (live) setUploads(rows.filter(row => row.status === 'ready')); }).catch(failure => { if (live) setError(errorMessage(failure)); });
    return () => { live = false; };
  }, [session]);
  const ready = uploads ?? [];
  const selected = chosen ?? ready.slice(0, 4).map(row => row.id);
  const states = useManyExtractions(session, ready.slice(0, 40).map(row => row.id));
  const columns = selected.map(id => ({ upload: ready.find(row => row.id === id)!, state: states.get(id) })).filter(column => column.upload)
    .map(column => ({ ...column, targets: uploadTargets(column.state?.results) }));
  const order = useMemo(() => orderedTargets((catalog ?? []).filter(type => type.enabled)), [catalog]);
  const rows = order.filter(meta => !hidden.has(meta.tag)).filter(meta => !onlyDiffer || !sameEverywhere(meta.tag, columns.map(column => column.targets)));
  const common = order.filter(meta => columns.length > 1 && columns.every(column => column.targets.get(meta.tag)?.outcome === 'detected'));

  function toggle(id: number) {
    setChosen(current => {
      const base = current ?? selected;
      return base.includes(id) ? base.filter(item => item !== id) : base.length >= maxColumns ? base : [...base, id];
    });
  }

  return <main className="shell compare">
    <header className="compare-head">
      <div>
        <h2>Compare contracts</h2>
        <p>Extracted terms side by side. Each term type keeps its icon and color everywhere in ClauseLens.</p>
      </div>
    </header>
    {error && <p className="error" role="alert">{error}</p>}
    {!uploads && !error && <p role="status">Loading contracts…</p>}
    {uploads && !ready.length && <p>No processed contracts yet. Upload contracts in the Contracts view first.</p>}
    {ready.length > 0 && <>
      <section className="compare-controls" aria-label="Choose contracts">
        <p className="detail-label">Contracts · up to {maxColumns}</p>
        <div className="contract-pills">
          {ready.map(row => {
            const on = selected.includes(row.id);
            return <button key={row.id} type="button" className="contract-pill" aria-pressed={on} onClick={() => toggle(row.id)} disabled={!on && selected.length >= maxColumns}>
              {on && <Check size={13} aria-hidden="true" />}{row.fileName}
            </button>;
          })}
        </div>
        <p className="detail-label">Term types</p>
        <div className="type-pills">
          {order.map(meta => <button key={meta.tag} type="button" className="type-pill" aria-pressed={!hidden.has(meta.tag)} style={{ '--target': meta.color } as React.CSSProperties}
            onClick={() => setHidden(current => { const next = new Set(current); if (next.has(meta.tag)) next.delete(meta.tag); else next.add(meta.tag); return next; })}>
            <TargetIcon tag={meta.tag} size={13} muted={hidden.has(meta.tag)} />{meta.short}
          </button>)}
          <label className="differ-toggle"><input type="checkbox" checked={onlyDiffer} onChange={event => setOnlyDiffer(event.target.checked)} /> Only rows that differ</label>
        </div>
      </section>

      {columns.length > 1 && <p className="compare-common">
        <strong>In common ({common.length}):</strong> {common.length ? common.map(meta => <span key={meta.tag} className="common-item"><TargetIcon tag={meta.tag} size={12} /> {meta.short}</span>) : 'no term type was found in every selected contract.'}
      </p>}

      {columns.length > 0 && <div className="compare-table-wrap" role="region" aria-label="Comparison matrix" tabIndex={0}>
        <table className="compare-table">
          <thead><tr>
            <th scope="col">Term</th>
            {columns.map(column => <th key={column.upload.id} scope="col">
              <span className="compare-col-title" title={column.upload.fileName}>{column.upload.fileName}</span>
              <span className="compare-col-meta">{column.state?.loading ? 'Loading…' : column.state?.summary?.extraction.canPoll ? 'Extracting…' : `${[...column.targets.values()].filter(item => item.outcome === 'detected').length} types found`}</span>
            </th>)}
          </tr></thead>
          <tbody>
            {rows.map(meta => {
              const count = columns.filter(column => column.targets.get(meta.tag)?.outcome === 'detected').length;
              return <tr key={meta.tag} data-focus={focusTag === meta.tag || undefined} style={{ '--target': meta.color } as React.CSSProperties}>
                <th scope="row">
                  <button type="button" className="compare-row-head" aria-pressed={focusTag === meta.tag} onClick={() => setFocusTag(focusTag === meta.tag ? undefined : meta.tag)}>
                    <TargetIcon tag={meta.tag} /> <span>{meta.name}</span><span className="compare-row-count">{count}/{columns.length}</span>
                  </button>
                </th>
                {columns.map(column => <td key={column.upload.id}><Cell tag={meta.tag} targets={column.targets} polling={!!column.state?.polling || !!column.state?.loading}
                  onClick={() => setFocusTag(meta.tag)} /></td>)}
              </tr>;
            })}
          </tbody>
        </table>
      </div>}

      {focusTag && <SideBySide tag={focusTag} columns={columns} onOpen={onOpen} onClose={() => setFocusTag(undefined)} />}
    </>}
  </main>;
}

function sameEverywhere(tag: string, all: UploadTargets[]) {
  const values = all.map(targets => {
    const entry = targets.get(tag);
    return entry?.outcome === 'detected' ? entry.findings.map(finding => headline(tag, finding)).sort().join('|') : entry?.outcome ?? 'none';
  });
  return values.every(value => value === values[0]);
}

function Cell({ tag, targets, polling, onClick }: { tag: string; targets: UploadTargets; polling: boolean; onClick: () => void }) {
  const entry = targets.get(tag);
  if (!entry) return polling ? <span className="cell-muted"><Loader2 size={13} className="spin" aria-hidden="true" /></span> : <span className="cell-muted">—</span>;
  if (entry.outcome !== 'detected') return <span className="cell-muted" data-outcome={entry.outcome} title={entry.explanation ?? undefined}>{outcomeShort[entry.outcome]}</span>;
  return <button type="button" className="cell-found" onClick={onClick}>
    {entry.findings.slice(0, 2).map(finding => <span key={finding.id} className="cell-value">{headline(tag, finding)}</span>)}
    {entry.findings.length > 2 && <span className="cell-more">+{entry.findings.length - 2} more</span>}
  </button>;
}

function SideBySide({ tag, columns, onOpen, onClose }: {
  tag: string; columns: { upload: Upload; targets: UploadTargets }[]; onOpen: (request: Omit<OpenRequest, 'request'>) => void; onClose: () => void;
}) {
  const meta = targetMeta(tag);
  return <section className="side-by-side" style={{ '--target': meta.color } as React.CSSProperties} aria-labelledby="sbs-title">
    <div className="side-head">
      <h3 id="sbs-title"><TargetIcon tag={tag} size={16} /> {meta.name}</h3>
      <button type="button" className="text-button" onClick={onClose}>Close</button>
    </div>
    <div className="side-grid" style={{ gridTemplateColumns: `repeat(${columns.length}, minmax(240px, 1fr))` }}>
      {columns.map(column => {
        const entry = column.targets.get(tag);
        return <article key={column.upload.id} className="side-col">
          <p className="side-col-title" title={column.upload.fileName}>{column.upload.fileName}</p>
          {!entry ? <p className="detail-muted">No result yet.</p>
            : entry.outcome !== 'detected' ? <p className="detail-muted"><strong>{outcomeShort[entry.outcome]}.</strong> {entry.explanation}</p>
            : entry.findings.map(finding => {
              const primary = finding.evidence.find(item => item.role === 'primary') ?? finding.evidence[0];
              return <div key={finding.id} className="side-finding">
                <p className="side-finding-title">{finding.title}</p>
                <dl className="value-table">{valueRows(finding.values).map(row => <div key={row.key} className="value-row"><dt>{row.label}</dt><dd>{row.text}</dd></div>)}</dl>
                {primary && <blockquote className="side-quote">{primary.quote.length > 260 ? `${primary.quote.slice(0, 260)}…` : primary.quote}</blockquote>}
                {primary && <button type="button" className="button secondary small" onClick={() => onOpen({ uploadId: column.upload.id, page: primary.page, quote: primary.quote, finding: `x-${finding.id}` })}>
                  Open on page {primary.page} <ArrowUpRight size={13} aria-hidden="true" /></button>}
              </div>;
            })}
        </article>;
      })}
    </div>
  </section>;
}
