import { useEffect, useRef, useState } from 'react';
import { UploadCloud } from 'lucide-react';
import { errorMessage, type Session, type Upload, type UploadDetail } from './api';
import { DocumentViewer } from './document-viewer';
import { countPdfPages } from './pdf-page';
import { isProcessing, pendingUpload as pending, ProcessingPanel, ProcessingSummary } from './processing-view';
import { UploadCard, UploadDropzone, WorkspaceOverview } from './workspace-parts';
import { loadExtractions, useCatalog, useExtractions, useManyExtractions } from './extraction-store';
import { uploadTargets } from './extraction-meta';

/** Another view (Compare) asking to open an upload, optionally at an extracted finding's evidence. */
export type OpenRequest = { uploadId: number; page?: number; quote?: string; finding?: string; request: number };

const labels: Record<Upload['status'], string> = {
  queued: 'Reading pages', splitting: 'Finding documents', ready: 'Ready', failed: 'Failed',
};

export function Workspace({ session, onOpenPlaybook, openRequest, onUploadsChange }: {
  session: Session; onOpenPlaybook?: () => void; openRequest?: OpenRequest; onUploadsChange?: (uploads: Upload[]) => void;
}) {
  const [uploads, setUploads] = useState<Upload[]>([]);
  const [selected, setSelected] = useState<number>();
  const [initial, setInitial] = useState(true);
  const [more, setMore] = useState(false);
  const [listing, setListing] = useState(false);
  const [listError, setListError] = useState('');
  const [sending, setSending] = useState(false);
  const [dragging, setDragging] = useState(false);
  const [dropped, setDropped] = useState<{ file: File; at: number }>();
  const [uploadError, setUploadError] = useState('');
  const [focus, setFocus] = useState<OpenRequest>();
  useEffect(() => { if (openRequest) { setSelected(openRequest.uploadId); setFocus(openRequest); } }, [openRequest?.request]);
  const readyIds = uploads.filter(row => row.status === 'ready').map(row => row.id).slice(0, 40);
  const listExtractions = useManyExtractions(session, readyIds);
  useEffect(() => { onUploadsChange?.(uploads); }, [uploads]);
  const resultTitle = useRef<HTMLHeadingElement>(null);
  const uploadsTitle = useRef<HTMLHeadingElement>(null);

  function revealResult() {
    if (window.matchMedia('(max-width: 690px)').matches) resultTitle.current?.focus();
  }

  useEffect(() => {
    if (selected !== undefined) revealResult();
  }, [selected]);

  async function load(before?: number) {
    setListing(true);
    setListError('');
    try {
      const rows = await session.list(before);
      setUploads(previous => {
        const all = new Map(previous.map(row => [row.id, row]));
        rows.forEach(row => all.set(row.id, row));
        return [...all.values()].sort((a, b) => b.id - a.id);
      });
      setMore(rows.length === 50);
    } catch (failure) {
      setListError(errorMessage(failure));
    } finally {
      setInitial(false);
      setListing(false);
    }
  }

  useEffect(() => { void load(); }, [session]);

  function updateUpload(upload: Upload) {
    setUploads(previous => [upload, ...previous.filter(row => row.id !== upload.id)].sort((a, b) => b.id - a.id));
  }

  const hasPending = uploads.some(row => row.id !== selected && pending(row));
  useEffect(() => {
    if (!hasPending) return;
    let stopped = false;
    let inFlight = false;
    const interval = window.setInterval(async () => {
      if (inFlight) return;
      inFlight = true;
      try {
        const rows = await session.list();
        if (!stopped) {
          const latest = new Map(rows.map(row => [row.id, row]));
          setUploads(previous => previous.map(row => pending(row) && row.id !== selected ? latest.get(row.id) ?? row : row));
        }
      } catch {
        // Keep the last confirmed status and retry on the next tick.
      } finally {
        inFlight = false;
      }
    }, 4000);
    return () => { stopped = true; window.clearInterval(interval); };
  }, [session, hasPending, selected]);

  async function upload(file: File) {
    if (sending) return false;
    setUploadError('');
    setSending(true);
    try {
      const { id } = await session.upload(file);
      setSelected(id);
      void load();
      return true;
    } catch (failure) {
      setUploadError(errorMessage(failure));
      return false;
    } finally {
      setSending(false);
    }
  }

  return (
    <main className="shell workspace"
      onDragOver={event => { if (event.dataTransfer.types.includes('Files')) { event.preventDefault(); setDragging(true); } }}
      onDragLeave={event => { if (!event.currentTarget.contains(event.relatedTarget as Node)) setDragging(false); }}
      onDrop={event => { event.preventDefault(); setDragging(false); const file = event.dataTransfer.files?.[0]; if (file) setDropped({ file, at: Date.now() }); }}>
      {dragging && <div className="drop-overlay" aria-hidden="true"><UploadCloud size={28} /> Drop to add contract</div>}
      <div className="workspace-grid">
        <section className="upload-list" aria-labelledby="uploads-title">
          <div className="upload-list-head">
            <h2 id="uploads-title" tabIndex={-1} ref={uploadsTitle}>Contracts</h2>
            {uploads.length > 0 && <span>{uploads.length}</span>}
          </div>
          <UploadDropzone sending={sending} error={uploadError} onUpload={upload} incoming={dropped} />
          {initial && <p role="status">Loading uploads…</p>}
          {listError && <><p role="alert" className="error">{listError}</p><button className="button secondary" onClick={() => void load()}>Reload uploads</button></>}
          {!initial && !listError && !uploads.length && <p className="upload-empty">Uploaded contracts appear here.</p>}
          <ul>
            {uploads.map(item => (
              <li key={item.id}>
                <UploadCard upload={item} selected={selected === item.id} targets={uploadTargets(listExtractions.get(item.id)?.results)} onOpen={() => {
                  if (selected === item.id) revealResult();
                  setSelected(item.id);
                }} />
              </li>
            ))}
          </ul>
          {more && <button className="button secondary" disabled={listing} onClick={() => void load(uploads.at(-1)?.id)}>{listing ? 'Loading…' : 'Load older uploads'}</button>}
        </section>
        {selected === undefined ? (
          <WorkspaceOverview session={session} uploads={uploads} onOpen={setSelected} onOpenPlaybook={onOpenPlaybook} />
        ) : <Results key={selected} id={selected} session={session} focus={focus?.uploadId === selected ? focus : undefined} onOpenPlaybook={onOpenPlaybook} onUpload={updateUpload} titleRef={resultTitle} backToUploads={() => uploadsTitle.current?.focus()} />}
      </div>
    </main>
  );
}

function Results({ id, session, onUpload, titleRef, backToUploads, onOpenPlaybook, focus }: {
  id: number;
  focus?: OpenRequest;
  session: Session;
  onOpenPlaybook?: () => void;
  onUpload: (upload: Upload) => void;
  titleRef: React.Ref<HTMLHeadingElement>;
  backToUploads: () => void;
}) {
  const [result, setResult] = useState<UploadDetail>();
  const [error, setError] = useState('');
  const [attempt, setAttempt] = useState(0);
  const [sourcePages, setSourcePages] = useState<number>();
  const [focusPage, setFocusPage] = useState<{ page: number; request: number; quote?: string; finding?: string }>();
  useEffect(() => { if (focus?.page) setFocusPage({ page: focus.page, quote: focus.quote, finding: focus.finding, request: focus.request }); }, [focus?.request]);
  const extractions = useExtractions(session, id);
  const catalog = useCatalog(session);
  const wasPending = useRef(false);
  const [watched, setWatched] = useState(false);
  const onLatestUpload = useRef(onUpload);
  onLatestUpload.current = onUpload;

  // While OCR runs the API has no page count yet; count pages in the original file locally.
  const countSource = !!result && pending(result.upload) && !result.upload.pageCount && sourcePages === undefined;
  useEffect(() => {
    if (!countSource) return;
    const controller = new AbortController();
    void session.source(id, controller.signal)
      .then(blob => blob.type === 'application/pdf' ? countPdfPages(blob) : 1)
      .then(count => { if (!controller.signal.aborted) setSourcePages(count); })
      .catch(() => { if (!controller.signal.aborted) setSourcePages(0); });
    return () => controller.abort();
  }, [session, id, countSource]);
  useEffect(() => { if (result && isProcessing(result)) setWatched(true); }, [result]);

  useEffect(() => {
    const controller = new AbortController();
    let timer: number;
    async function load() {
      try {
        const detail = await session.detail(id, controller.signal);
        if (controller.signal.aborted) return;
        setError('');
        setResult(detail);
        onLatestUpload.current(detail.upload);
        // OCR/splitting just finished: refresh the extraction branch so its polling picks up the new documents.
        if (wasPending.current && !pending(detail.upload)) loadExtractions(session, id, true);
        wasPending.current = pending(detail.upload);
        if (pending(detail.upload) || detail.pages.some(page => page.analysisStatus === 'analyzing')) timer = window.setTimeout(load, 4000);
      } catch (failure) {
        if (!controller.signal.aborted) setError(errorMessage(failure));
      }
    }
    void load();
    return () => { controller.abort(); window.clearTimeout(timer); };
  }, [session, id, attempt]);

  return (
    <section className="results" aria-labelledby="result-title">
      <h2 id="result-title" tabIndex={-1} ref={titleRef}>{result?.upload.fileName ?? 'Document results'}</h2>
      <button className="text-button back-to-uploads" onClick={backToUploads}>Back to uploads</button>
      {error && <><p role="alert" className="error">{error}</p><button className="button secondary" onClick={() => { setError(''); setAttempt(value => value + 1); }}>Try again</button></>}
      {!result && !error && <p role="status">Loading upload…</p>}
      {result && <>
        {isProcessing(result) || result.upload.status === 'failed' || watched
          ? (isProcessing(result) || result.upload.status === 'failed'
            ? <ProcessingPanel detail={result} sourcePages={sourcePages} compact={result.pages.length > 0} onOpenPage={(page, quote) => setFocusPage(previous => ({ page, quote, request: (previous?.request ?? 0) + 1 }))} />
            : <ProcessingSummary detail={result} />)
          : <p className="result-status" role="status" data-status={result.upload.status}>{labels[result.upload.status]}</p>}
        {result.upload.error && <p className="error">{result.upload.error}</p>}
        {result.upload.status === 'ready' && !result.documents.length && <p>No documents were returned for this upload.</p>}
        {result.pages.length > 0 && <DocumentViewer documents={result.documents} pages={result.pages} session={session} uploadId={id} focusPage={focusPage} fileName={result.upload.fileName} onOpenPlaybook={onOpenPlaybook} extractions={extractions} catalog={catalog} onAnalysisQueued={() => setAttempt(value => value + 1)} />}
      </>}
    </section>
  );
}
