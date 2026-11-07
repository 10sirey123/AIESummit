import { useEffect, useState, type FormEvent } from 'react';
import { ArrowRight, ArrowUpRight, CheckCircle2, FileSearch, FileText, ListChecks, Plus, ScanText, UploadCloud } from 'lucide-react';
import type { Session, Upload, UploadDetail } from './api';
import { countPdfPages } from './pdf-page';
import { evaluate, tiers, useActiveRules, type Tier } from './playbook-store';
import { reviewFindings, TierBadge } from './review-desk';
import { TargetChips } from './extraction-view';
import type { UploadTargets } from './extraction-meta';

/** The current OCR service accepts at most 30 pages per document; checked in the browser before upload. */
export const maxPages = 30;
export const maxBytes = 20 * 1024 * 1024;
const statusLabel: Record<Upload['status'], string> = { queued: 'Reading pages', splitting: 'Finding documents', ready: 'Ready', failed: 'Failed' };
const when = new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' });

/**
 * Compact upload control for the top of the contracts list. `incoming` receives files dropped anywhere on the page;
 * a chosen file expands into a small card with the page check and Upload/Cancel.
 */
export function UploadDropzone({ sending, error, onUpload, incoming }: {
  sending: boolean; error: string; onUpload: (file: File) => Promise<boolean>; incoming?: { file: File; at: number };
}) {
  const [file, setFile] = useState<File>();
  const [pages, setPages] = useState<number>();
  const [checking, setChecking] = useState(false);
  const [problem, setProblem] = useState('');

  async function choose(next?: File) {
    setFile(next); setPages(undefined); setProblem('');
    if (!next) return;
    if (!/\.(pdf|png|jpe?g)$/i.test(next.name) && !/^(application\/pdf|image\/(png|jpeg))$/.test(next.type)) { setProblem('Choose a PDF, PNG or JPEG file.'); return; }
    if (!next.size || next.size > maxBytes) { setProblem('Choose a non-empty file up to 20 MiB.'); return; }
    if (!/pdf/i.test(next.type) && !/\.pdf$/i.test(next.name)) { setPages(1); return; }
    setChecking(true);
    try {
      const count = await countPdfPages(next);
      setPages(count);
      if (count > maxPages) setProblem(`${count} pages: processing accepts up to ${maxPages}. Split it or choose a shorter contract.`);
    } catch {
      setProblem('This PDF could not be read. It may be damaged or password protected.');
    } finally { setChecking(false); }
  }
  useEffect(() => { if (incoming) void choose(incoming.file); }, [incoming?.at]);
  async function submit(event: FormEvent) {
    event.preventDefault();
    if (file && !problem && !checking && await onUpload(file)) { setFile(undefined); setPages(undefined); }
  }

  return <form className="add-contract" data-open={!!file || undefined} onSubmit={submit} aria-label="Add a contract">
    <input id="file" name="file" className="file-input" type="file" accept=".pdf,.png,.jpg,.jpeg,application/pdf,image/png,image/jpeg" aria-describedby="file-hint" disabled={sending}
      onChange={event => { void choose(event.target.files?.[0]); event.target.value = ''; }} />
    {!file ? <>
      <label htmlFor="file" className="add-contract-button"><Plus size={16} aria-hidden="true" /> Add contract</label>
      <p id="file-hint" className="add-contract-hint">or drop a PDF anywhere · up to {maxPages} pages</p>
    </> : <div className="add-contract-card">
      <p className="add-contract-name"><FileText size={15} aria-hidden="true" /> {file.name}</p>
      <p id="file-hint" className="add-contract-hint">{checking ? 'Counting pages…' : problem ? '' : `${pages ?? '?'} page${pages === 1 ? '' : 's'} · ${(file.size / 1024 / 1024).toFixed(1)} MB · ready`}</p>
      {(problem || error) && <p className="error" role="alert">{problem || error}</p>}
      <div className="add-contract-actions">
        <button className="button primary small" disabled={!!problem || checking || sending}>{sending ? 'Uploading…' : 'Upload'} <ArrowUpRight size={14} aria-hidden="true" /></button>
        <button type="button" className="button secondary small" disabled={sending} onClick={() => void choose(undefined)}>Cancel</button>
      </div>
    </div>}
  </form>;
}

export function UploadCard({ upload, selected, onOpen, targets }: { upload: Upload; selected: boolean; onOpen: () => void; targets?: UploadTargets }) {
  const created = Date.parse(/[zZ]|[+-]\d\d:?\d\d$/.test(upload.created) ? upload.created : `${upload.created}Z`);
  return <button className="upload-card" aria-current={selected ? 'true' : undefined} onClick={onOpen}>
    <span className="upload-card-top">
      <strong>{upload.fileName}</strong>
      <span className="status-pill" data-status={upload.status}>{statusLabel[upload.status]}</span>
    </span>
    <span className="upload-card-meta">
      {upload.pageCount ? `${upload.pageCount} page${upload.pageCount === 1 ? '' : 's'} · ` : ''}{Number.isNaN(created) ? '' : when.format(created)}
    </span>
    <TargetChips targets={targets} />
    {upload.status === 'failed' && <span className="upload-card-error">{upload.error ?? 'Processing failed.'}</span>}
  </button>;
}

function reviewedCount(uploadId: number, keys: string[]) {
  try {
    const decisions = JSON.parse(localStorage.getItem(`clauselens-review-${uploadId}`) ?? '{}') as Record<string, unknown>;
    return keys.filter(key => key in decisions).length;
  } catch { return 0; }
}

/** The empty results area: continue the latest ready review, or learn how it works. */
export function WorkspaceOverview({ session, uploads, onOpen, onOpenPlaybook }: {
  session: Session; uploads: Upload[]; onOpen: (id: number) => void; onOpenPlaybook?: () => void;
}) {
  const latest = uploads.find(upload => upload.status === 'ready');
  const [detail, setDetail] = useState<UploadDetail>();
  const { playbook, rules } = useActiveRules();
  useEffect(() => {
    if (!latest) return;
    const controller = new AbortController();
    void session.detail(latest.id, controller.signal).then(setDetail).catch(() => undefined);
    return () => controller.abort();
  }, [session, latest?.id]);
  const findings = detail ? reviewFindings(detail.pages) : [];
  const counts = tiers.map(tier => ({ tier, n: findings.filter(item => evaluate(item, rules).tier === tier).length })).filter(item => item.n) as { tier: Tier; n: number }[];
  const reviewed = latest ? reviewedCount(latest.id, findings.map(item => item.key)) : 0;
  const ready = uploads.filter(upload => upload.status === 'ready').length;
  const failed = uploads.filter(upload => upload.status === 'failed').length;

  return <section className="results overview" aria-labelledby="overview-title">
    {latest ? <div className="continue-card">
      <p className="detail-label">Continue reviewing</p>
      <h2 id="overview-title">{latest.fileName}</h2>
      <p className="continue-meta">{latest.pageCount} pages · {detail ? `${findings.length} findings · ${reviewed} of ${findings.length} reviewed` : 'Loading findings…'}</p>
      {findings.length > 0 && <div className="continue-progress" role="progressbar" aria-label="Findings reviewed" aria-valuemin={0} aria-valuemax={findings.length} aria-valuenow={reviewed}><span style={{ width: `${(reviewed / findings.length) * 100}%` }} /></div>}
      {counts.length > 0 && <ul className="continue-tiers" aria-label={`Findings by tier in ${playbook.name}`}>
        {counts.map(item => <li key={item.tier}><TierBadge tier={item.tier} /><span>{item.n}</span></li>)}
      </ul>}
      <div className="continue-actions">
        <button type="button" className="button primary" onClick={() => onOpen(latest.id)}>Open review <ArrowRight size={16} aria-hidden="true" /></button>
        {onOpenPlaybook && <button type="button" className="button secondary" onClick={onOpenPlaybook}>Playbook: {playbook.name} v{playbook.published.version}</button>}
      </div>
    </div> : <div className="continue-card">
      <p className="detail-label">Get started</p>
      <h2 id="overview-title">{uploads.length ? 'No contract is ready yet' : 'Review your first contract'}</h2>
      <p className="continue-meta">{uploads.length ? 'Processing results appear here as soon as an upload is ready.' : 'Upload an MSA or NDA. ClauseLens reads every page, finds the clauses that matter and checks them against your playbook.'}</p>
    </div>}

    <ol className="how-it-works" aria-label="How ClauseLens works">
      <li><ScanText size={18} aria-hidden="true" /><strong>Read</strong><span>Every page is transcribed, with page numbers kept for evidence.</span></li>
      <li><FileSearch size={18} aria-hidden="true" /><strong>Find</strong><span>Sections are classified and summarized, each with an exact quote.</span></li>
      <li><ListChecks size={18} aria-hidden="true" /><strong>Check</strong><span>Your playbook tiers every finding: walk-away, escalate or fallback.</span></li>
      <li><CheckCircle2 size={18} aria-hidden="true" /><strong>Review</strong><span>Accept or reject each finding with the evidence highlighted.</span></li>
    </ol>
    {uploads.length > 0 && <p className="overview-totals">{uploads.length} upload{uploads.length === 1 ? '' : 's'} · {ready} ready{failed ? ` · ${failed} failed` : ''}</p>}
  </section>;
}
