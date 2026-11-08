import { useEffect, useMemo, useState } from 'react';
import { ArrowRight } from 'lucide-react';
import { errorMessage, type Session, type Upload, type UploadDetail } from './api';
import { orderedTargets, targetMeta, tierExtraction, uploadTargets, worstTier } from './extraction-meta';
import { useCatalog, useManyExtractions } from './extraction-store';
import { TargetIcon } from './extraction-view';
import { evaluate, useActiveRules, type Tier } from './playbook-store';
import { reviewFindings, TierBadge } from './review-desk';
import { reviewedCount } from './workspace-parts';
import type { OpenRequest } from './workspace';

const maxContracts = 40;

/** Supporting screen: every contract in one list, filterable by Walk-away findings, clause type and unfinished review. */
export function PortfolioView({ session, onOpen }: { session: Session; onOpen: (request: Omit<OpenRequest, 'request'>) => void }) {
  const [uploads, setUploads] = useState<Upload[]>();
  const [details, setDetails] = useState<Map<number, UploadDetail>>(new Map());
  const [error, setError] = useState('');
  const [walkAway, setWalkAway] = useState(false);
  const [unfinished, setUnfinished] = useState(false);
  const [type, setType] = useState('');
  const catalog = useCatalog(session);
  const { playbook, rules } = useActiveRules();

  useEffect(() => {
    const controller = new AbortController();
    void (async () => {
      try {
        const rows = (await session.list()).filter(row => row.status === 'ready').slice(0, maxContracts);
        if (controller.signal.aborted) return;
        setUploads(rows);
        for (let i = 0; i < rows.length; i += 4) {
          const batch = await Promise.all(rows.slice(i, i + 4).map(row => session.detail(row.id, controller.signal).catch(() => undefined)));
          if (controller.signal.aborted) return;
          setDetails(previous => { const next = new Map(previous); batch.forEach(item => item && next.set(item.upload.id, item)); return next; });
        }
      } catch (failure) { if (!controller.signal.aborted) setError(errorMessage(failure)); }
    })();
    return () => controller.abort();
  }, [session]);

  const ids = (uploads ?? []).map(row => row.id);
  const states = useManyExtractions(session, ids);
  const order = useMemo(() => orderedTargets((catalog ?? []).filter(item => item.enabled)), [catalog]);

  const rows = (uploads ?? []).map(upload => {
    const targets = uploadTargets(states.get(upload.id)?.results);
    const extracted = [...targets].flatMap(([tag, entry]) => entry.outcome === 'detected' ? entry.findings.map(finding => ({ tag, key: `x-${finding.id}`, tier: tierExtraction(tag, finding, rules).tier })) : []);
    const detail = details.get(upload.id);
    const pageFindings = detail ? reviewFindings(detail.pages) : [];
    const pageTiers = pageFindings.map(finding => evaluate(finding, rules).tier);
    const all: Tier[] = [...extracted.map(item => item.tier), ...pageTiers];
    const reviewKeys = [...pageFindings.map(item => item.key), ...extracted.map(item => item.key)];
    const reviewed = detail ? reviewedCount(upload.id, reviewKeys) : 0;
    const walk = all.filter(tier => tier === 'Walk-away').length;
    const escalate = all.filter(tier => tier === 'Escalate').length;
    const found = new Set(extracted.map(item => item.tag));
    const extracting = !!states.get(upload.id)?.polling;
    return { upload, found, walk, escalate, worst: worstTier(all), reviewed, total: reviewKeys.length, loaded: !!detail, extracting,
      unfinished: !!detail && reviewed < reviewKeys.length, walkTypes: [...new Set(extracted.filter(item => item.tier === 'Walk-away').map(item => targetMeta(item.tag).short))] };
  });
  const shown = rows.filter(row => (!walkAway || row.walk > 0) && (!unfinished || row.unfinished) && (!type || row.found.has(type)));
  const counts = { walk: rows.filter(row => row.walk > 0).length, unfinished: rows.filter(row => row.unfinished).length, type: type ? rows.filter(row => row.found.has(type)).length : 0 };

  return <main className="shell portfolio">
    <header className="compare-head">
      <h2>Portfolio</h2>
      <p>All {rows.length} processed contracts against the published <strong>{playbook.name} v{playbook.published.version}</strong> playbook. Tiers combine extracted terms and page-review findings; review progress is stored in this browser.</p>
    </header>
    {error && <p className="error" role="alert">{error}</p>}
    {!uploads && !error && <p role="status">Loading contracts…</p>}
    {uploads && <>
      <div className="portfolio-filters" role="group" aria-label="Filter contracts">
        <button type="button" className="filter-pill" aria-pressed={walkAway} data-kind="walk" onClick={() => setWalkAway(value => !value)}>
          <TierBadge tier="Walk-away" /> {counts.walk} with Walk-away findings</button>
        <button type="button" className="filter-pill" aria-pressed={unfinished} onClick={() => setUnfinished(value => !value)}>
          {counts.unfinished} with unfinished review</button>
        <label className="filter-select">Contains
          <select value={type} onChange={event => setType(event.target.value)}>
            <option value="">any clause type</option>
            {order.map(meta => <option key={meta.tag} value={meta.tag}>{meta.name}</option>)}
          </select>
          {type && <span className="detail-muted">{counts.type} of {rows.length}</span>}
        </label>
        {(walkAway || unfinished || type) && <button type="button" className="text-button" onClick={() => { setWalkAway(false); setUnfinished(false); setType(''); }}>Clear</button>}
      </div>

      <div className="compare-table-wrap" role="region" aria-label="Contracts" tabIndex={0}>
        <table className="compare-table portfolio-table">
          <thead><tr>
            <th scope="col">Contract</th><th scope="col">Most serious</th><th scope="col">Walk-away · Escalate</th>
            <th scope="col">Clause types found</th><th scope="col">Review</th><th scope="col"><span className="visually-hidden">Open</span></th>
          </tr></thead>
          <tbody>
            {shown.map(row => <tr key={row.upload.id}>
              <th scope="row"><span className="compare-col-title" title={row.upload.fileName}>{row.upload.fileName}</span>
                <span className="compare-col-meta">{row.upload.pageCount} pages{row.extracting ? ' · extracting…' : ''}</span></th>
              <td><TierBadge tier={row.worst} /></td>
              <td><span className="portfolio-counts"><strong data-zero={row.walk === 0 || undefined}>{row.walk}</strong> · {row.escalate}</span>
                {row.walkTypes.length > 0 && <span className="compare-col-meta">{row.walkTypes.join(', ')}</span>}</td>
              <td><span className="target-chips">{order.filter(meta => row.found.has(meta.tag)).map(meta =>
                <span key={meta.tag} data-active={type === meta.tag || undefined} className="chip-wrap"><TargetIcon tag={meta.tag} size={12} muted={!!type && type !== meta.tag} title={meta.name} /></span>)}
                {!row.found.size && <span className="cell-muted">{row.extracting ? 'Extracting…' : 'None published'}</span>}</span></td>
              <td>{!row.loaded ? <span className="cell-muted">…</span> : row.total === 0 ? <span className="cell-muted">Nothing to review</span>
                : <span className="portfolio-progress" title={`${row.reviewed} of ${row.total} findings reviewed`}>
                  <span className="continue-progress"><span style={{ width: `${(row.reviewed / row.total) * 100}%` }} /></span>
                  <span className="compare-col-meta">{row.reviewed}/{row.total}{row.unfinished ? '' : ' · done'}</span></span>}</td>
              <td><button type="button" className="button secondary small" onClick={() => onOpen({ uploadId: row.upload.id })}>Open <ArrowRight size={13} aria-hidden="true" /></button></td>
            </tr>)}
            {!shown.length && <tr><td colSpan={6} className="cell-muted">No contracts match these filters.</td></tr>}
          </tbody>
        </table>
      </div>
    </>}
  </main>;
}
