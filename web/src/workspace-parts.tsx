import { useEffect, useState, type FormEvent } from 'react';
import { ArrowUpRight, FileText, Plus } from 'lucide-react';
import type { Upload } from './api';
import { countPdfPages } from './pdf-page';
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
      <strong title={upload.fileName}>{upload.fileName}</strong>
      <span className="status-pill" data-status={upload.status}>{statusLabel[upload.status]}</span>
    </span>
    <span className="upload-card-meta">
      {upload.pageCount ? `${upload.pageCount} page${upload.pageCount === 1 ? '' : 's'} · ` : ''}{Number.isNaN(created) ? '' : when.format(created)}
    </span>
    <TargetChips targets={targets} />
    {upload.status === 'failed' && <span className="upload-card-error" title={upload.error ?? undefined}>{upload.error ?? 'Processing failed.'}</span>}
  </button>;
}

export function reviewedCount(uploadId: number, keys: string[]) {
  try {
    const decisions = JSON.parse(localStorage.getItem(`clauselens-review-${uploadId}`) ?? '{}') as Record<string, unknown>;
    return keys.filter(key => key in decisions).length;
  } catch { return 0; }
}
