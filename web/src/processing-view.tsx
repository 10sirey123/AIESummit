import { useEffect, useRef, useState } from 'react';
import { Check, CircleAlert } from 'lucide-react';
import type { PageResult, Upload, UploadDetail } from './api';

export type StageState = 'done' | 'live' | 'waiting' | 'failed' | 'off';
export type Stage = { key: string; label: string; detail: string; state: StageState };
export type Finding = { key: string; page: number; heading: string; classification: string; quote: string };

const stateText: Record<StageState, string> = {
  done: 'Done', live: 'In progress', waiting: 'Waiting', failed: 'Stopped', off: 'Not run',
};

export const pendingUpload = (upload: Upload) => upload.status === 'queued' || upload.status === 'splitting';
export const isProcessing = (detail: UploadDetail) =>
  pendingUpload(detail.upload) || detail.pages.some(page => page.analysisStatus === 'analyzing');

function analysisCounts(pages: PageResult[]) {
  const count = (status: PageResult['analysisStatus']) => pages.filter(page => page.analysisStatus === status).length;
  return { total: pages.length, ready: count('ready'), failed: count('failed'), analyzing: count('analyzing'), notStarted: count('not_started') };
}

const plural = (n: number, word: string) => `${n} ${word}${n === 1 ? '' : 's'}`;

/** Derives every stage from data the API actually returns; nothing is simulated. */
export function processingStages(detail: UploadDetail, sourcePages?: number): Stage[] {
  const { upload, pages, documents } = detail;
  const total = upload.pageCount || pages.length || sourcePages || 0;
  const a = analysisCounts(pages);
  const read: Stage = pages.length
    ? { key: 'read', label: 'Read pages', detail: `${plural(pages.length, 'page')} transcribed`, state: 'done' }
    : upload.status === 'failed'
      ? { key: 'read', label: 'Read pages', detail: 'The pages could not be read', state: 'failed' }
      : { key: 'read', label: 'Read pages', detail: total ? `Transcribing ${plural(total, 'page')}` : 'Transcribing the file', state: 'live' };
  const split: Stage = upload.status === 'ready'
    ? { key: 'split', label: 'Find documents', detail: documents.length ? `${plural(documents.length, 'document')} found` : 'No separate documents', state: 'done' }
    : upload.status === 'splitting'
      ? { key: 'split', label: 'Find documents', detail: 'Finding where each document starts and ends', state: 'live' }
      : upload.status === 'failed' && pages.length
        ? { key: 'split', label: 'Find documents', detail: 'Documents could not be identified', state: 'failed' }
        : { key: 'split', label: 'Find documents', detail: 'Starts when pages are read', state: 'waiting' };
  const analyze: Stage = !a.total
    ? { key: 'analyze', label: 'Analyze pages', detail: 'Starts when pages are read', state: 'waiting' }
    : a.analyzing
      ? { key: 'analyze', label: 'Analyze pages', detail: `${a.ready} of ${a.total} pages analyzed`, state: 'live' }
      : a.ready + a.failed === a.total
        ? { key: 'analyze', label: 'Analyze pages', detail: a.failed ? `${a.ready} analyzed · ${a.failed} need attention` : `${plural(a.ready, 'page')} analyzed`, state: a.ready ? 'done' : 'failed' }
        : { key: 'analyze', label: 'Analyze pages', detail: 'Not run for this upload', state: 'off' };
  const finished = !isProcessing(detail) && upload.status === 'ready';
  const ready: Stage = { key: 'ready', label: 'Ready to review', detail: finished ? 'Every page is available' : upload.status === 'failed' ? 'Processing stopped' : 'Waiting for the steps above', state: finished ? 'done' : upload.status === 'failed' ? 'failed' : 'waiting' };
  return [read, split, analyze, ready];
}

export function processingHeadline(stages: Stage[]) {
  const live = (key: string) => stages.find(stage => stage.key === key)?.state === 'live';
  const analyze = stages.find(stage => stage.key === 'analyze')!;
  if (stages.some(stage => stage.state === 'failed') && !stages.some(stage => stage.state === 'live')) return 'Processing stopped';
  if (live('read')) return stages[0].detail.replace('Transcribing', 'Reading');
  if (live('split') && live('analyze')) return 'Finding documents and analyzing pages';
  if (live('analyze')) return `Analyzing pages · ${analyze.detail.replace(' pages analyzed', '')}`;
  if (live('split')) return 'Finding documents';
  return 'Ready to review';
}

export function pageFindings(pages: PageResult[]): Finding[] {
  return pages.filter(page => page.analysisStatus === 'ready').flatMap(page => page.sections.map((section, index) => ({
    key: `${page.id}-${index}`, page: page.number, heading: section.heading, classification: section.classification.replaceAll('_', ' '), quote: section.quote,
  })));
}

/** Server times may arrive without a zone designator; they are UTC. */
export function serverTime(value: string) {
  const time = Date.parse(/[zZ]|[+-]\d\d:?\d\d$/.test(value) ? value : `${value}Z`);
  return Number.isNaN(time) ? undefined : time;
}

export function clock(ms: number) {
  const seconds = Math.max(0, Math.floor(ms / 1000));
  const minutes = Math.floor(seconds / 60);
  return `${minutes}:${String(seconds % 60).padStart(2, '0')}`;
}

function useNow(active: boolean, fixed?: number) {
  const [now, setNow] = useState(() => fixed ?? Date.now());
  useEffect(() => {
    if (fixed !== undefined || !active) return;
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [active, fixed]);
  return fixed ?? now;
}

export function ProcessingPanel({ detail, sourcePages, onOpenPage, now: fixedNow, compact = false }: {
  detail: UploadDetail; sourcePages?: number; onOpenPage?: (page: number, quote?: string) => void; now?: number;
  /** Once pages are readable, keep the panel to one strip so the document stays in view. */
  compact?: boolean;
}) {
  const processing = isProcessing(detail);
  const now = useNow(processing, fixedNow);
  const stages = processingStages(detail, sourcePages);
  const headline = processingHeadline(stages);
  const findings = pageFindings(detail.pages);
  const counts = analysisCounts(detail.pages);
  const total = detail.upload.pageCount || detail.pages.length || sourcePages || 0;
  const started = serverTime(detail.upload.created);
  const elapsed = started !== undefined && now - started >= 0 && now - started < 6 * 3600_000 ? now - started : undefined;

  // Order findings by when they first appeared, newest first.
  const seen = useRef(new Map<string, number>());
  findings.forEach(finding => { if (!seen.current.has(finding.key)) seen.current.set(finding.key, seen.current.size); });
  const recent = [...findings].sort((x, y) => (seen.current.get(y.key)! - seen.current.get(x.key)!) || x.page - y.page).slice(0, 5);

  // Estimate the remaining analysis time from the observed rate only.
  const samples = useRef<{ time: number; ready: number }[]>([]);
  useEffect(() => {
    if (!counts.analyzing) { samples.current = []; return; }
    const last = samples.current.at(-1);
    if (!last || last.ready !== counts.ready) samples.current.push({ time: Date.now(), ready: counts.ready });
  }, [counts.ready, counts.analyzing]);
  let remaining: number | undefined;
  const first = samples.current[0], latest = samples.current.at(-1);
  if (counts.analyzing && first && latest && latest.ready > first.ready) {
    const rate = (latest.ready - first.ready) / (latest.time - first.time);
    remaining = (counts.total - counts.ready - counts.failed) / rate;
  }

  const tiles = Array.from({ length: Math.min(total, 100) }, (_, index) => {
    const page = detail.pages.find(item => item.number === index + 1);
    const state = !page ? 'waiting' : page.analysisStatus === 'ready' ? 'analyzed' : page.analysisStatus === 'analyzing' ? 'analyzing' : page.analysisStatus === 'failed' ? 'failed' : 'read';
    const label = { waiting: 'being read', read: 'transcribed', analyzing: 'being analyzed', analyzed: 'analyzed', failed: 'analysis needs attention' }[state];
    return { number: index + 1, state, label, available: !!page };
  });

  return <section className="processing-panel" aria-labelledby={`processing-title-${detail.upload.id}`} data-processing={processing} data-compact={compact || undefined}>
    <div className="processing-head">
      <div>
        <h3 id={`processing-title-${detail.upload.id}`} className="processing-eyebrow">{processing ? 'Processing' : detail.upload.status === 'failed' ? 'Processing stopped' : 'Processed'}</h3>
        <p className="processing-headline" role="status">{headline}</p>
      </div>
      <p className="processing-clock">
        {elapsed !== undefined && processing && <span>{clock(elapsed)} elapsed</span>}
        {remaining !== undefined && <span>about {Math.max(5, Math.round(remaining / 5000) * 5)} s left</span>}
      </p>
    </div>

    <ol className="stage-list">
      {stages.map(stage => <li key={stage.key} data-state={stage.state}>
        <span className="stage-mark" aria-hidden="true">{stage.state === 'done' ? <Check size={13} /> : stage.state === 'failed' ? <CircleAlert size={13} /> : null}</span>
        <span className="stage-copy">
          <strong>{stage.label}</strong>
          <span>{stage.detail}</span>
        </span>
        <span className="visually-hidden">{stateText[stage.state]}</span>
      </li>)}
    </ol>

    <ProcessingDetails compact={compact} summary={`Page progress and findings · ${counts.ready} of ${counts.total} pages analyzed · ${plural(findings.length, 'section')}`}>
    <div className="processing-body">
    {tiles.length > 0 && <div className="page-tiles-block">
      <div className="page-tiles-head">
        <h4>Pages</h4>
        <span>{detail.pages.length ? `${counts.ready} of ${counts.total} analyzed` : `${total} found · transcribing`}</span>
      </div>
      <ol className="page-tiles" aria-label="Page progress">
        {tiles.map(tile => <li key={tile.number}>
          {tile.available && onOpenPage
            ? <button type="button" className="page-tile" data-state={tile.state} aria-label={`Open page ${tile.number}, ${tile.label}`} title={`Page ${tile.number} · ${tile.label}`} onClick={() => onOpenPage(tile.number)}><span>{tile.number}</span></button>
            : <span className="page-tile" data-state={tile.state} title={`Page ${tile.number} · ${tile.label}`}><span>{tile.number}</span><span className="visually-hidden">, {tile.label}</span></span>}
        </li>)}
      </ol>
      <ul className="tile-legend" aria-label="Legend">
        <li data-state="waiting">Being read</li>
        <li data-state="read">Transcribed</li>
        <li data-state="analyzing">Being analyzed</li>
        <li data-state="analyzed">Analyzed</li>
      </ul>
    </div>}

    <div className="findings-block">
      <div className="page-tiles-head">
        <h4>Found so far</h4>
        <span>{findings.length ? plural(findings.length, 'section') : ''}</span>
      </div>
      {!findings.length ? <p className="findings-empty">{detail.pages.length
        ? 'Sections appear here as each page is analyzed, while the rest are still in progress.'
        : 'Pages become readable below as soon as they are transcribed. Sections appear here as each page is analyzed.'}</p>
        : <ul className="findings-list">
          {recent.map(finding => <li key={finding.key}>
            <button type="button" className="finding-row" onClick={() => onOpenPage?.(finding.page, finding.quote)} disabled={!onOpenPage} title="Open the page with this evidence highlighted">
              <span className="finding-top"><strong>{finding.heading}</strong><span className="finding-page">p.{finding.page}</span></span>
              <span className="finding-class">{finding.classification}</span>
              <span className="finding-quote">“{finding.quote.length > 140 ? `${finding.quote.slice(0, 140)}…` : finding.quote}”</span>
            </button>
          </li>)}
          {findings.length > recent.length && <li className="findings-more">and {findings.length - recent.length} more in the page sidebar</li>}
        </ul>}
    </div>
    </div>
    </ProcessingDetails>
  </section>;
}

function ProcessingDetails({ compact, summary, children }: { compact: boolean; summary: string; children: React.ReactNode }) {
  return compact ? <details className="processing-details"><summary>{summary}</summary>{children}</details> : <>{children}</>;
}

/** One-line record shown once processing has finished. */
export function ProcessingSummary({ detail }: { detail: UploadDetail }) {
  const findings = pageFindings(detail.pages).length;
  const parts = [plural(detail.pages.length, 'page'), plural(detail.documents.length, 'document')];
  if (detail.pages.some(page => page.analysisStatus !== 'not_started')) parts.push(`${plural(findings, 'section')} found`);
  return <p className="processing-summary"><Check size={14} aria-hidden="true" /> Processed · {parts.join(' · ')}</p>;
}
