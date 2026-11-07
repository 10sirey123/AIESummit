import { useEffect, useRef, useState, type FormEvent } from 'react';
import { ArrowDownToLine, ArrowUpRight, FileText, Image, UploadCloud } from 'lucide-react';
import { errorMessage, type DocumentResult, type ImageReference, type Session, type Upload, type UploadDetail } from './api';

const labels: Record<Upload['status'], string> = {
  queued: 'Waiting for OCR', splitting: 'Identifying documents', ready: 'Ready', failed: 'Failed',
};
const pending = (upload: Upload) => upload.status === 'queued' || upload.status === 'splitting';
const dateFormat = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' });

export function Workspace({ session }: { session: Session }) {
  const [uploads, setUploads] = useState<Upload[]>([]);
  const [selected, setSelected] = useState<number>();
  const [initial, setInitial] = useState(true);
  const [more, setMore] = useState(false);
  const [listing, setListing] = useState(false);
  const [listError, setListError] = useState('');
  const [file, setFile] = useState<File>();
  const [sending, setSending] = useState(false);
  const [uploadError, setUploadError] = useState('');
  const input = useRef<HTMLInputElement>(null);
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

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!file || sending) return;
    setUploadError('');
    if (!file.size || file.size > 20 * 1024 * 1024) {
      setUploadError('Choose a non-empty file up to 20 MiB.');
      return;
    }
    setSending(true);
    try {
      const { id } = await session.upload(file);
      setSelected(id);
      setFile(undefined);
      if (input.current) input.current.value = '';
    } catch (failure) {
      setUploadError(errorMessage(failure));
    } finally {
      setSending(false);
    }
  }

  return (
    <main className="shell workspace">
      <div className="workspace-heading">
        <h1>Document workspace</h1>
        <p>Upload a file and review the documents AIESummit finds.</p>
      </div>
      <form className="upload-form" onSubmit={submit} aria-labelledby="upload-title">
        <UploadCloud size={26} aria-hidden="true" />
        <div className="upload-copy">
          <h2 id="upload-title">Add a source file</h2>
          <p id="file-hint">PDF, PNG or JPEG · Up to 20 MiB</p>
          {file && <p className="filename">{file.name}</p>}
          {uploadError && <p className="error" role="alert">{uploadError}</p>}
        </div>
        <div className="upload-actions">
          <input ref={input} id="file" name="file" className="file-input" type="file" accept=".pdf,.png,.jpg,.jpeg,application/pdf,image/png,image/jpeg" aria-describedby="file-hint" disabled={sending} onChange={event => { setFile(event.target.files?.[0]); setUploadError(''); }} />
          <label htmlFor="file" className="button secondary file-label">{file ? 'Change file' : 'Choose file'}</label>
          <button className="button primary" disabled={!file || sending}>{sending ? 'Uploading…' : 'Upload file'} <ArrowUpRight size={16} aria-hidden="true" /></button>
        </div>
      </form>
      <div className="workspace-grid">
        <section className="upload-list" aria-labelledby="uploads-title">
          <h2 id="uploads-title" tabIndex={-1} ref={uploadsTitle}>Uploads</h2>
          {initial && <p role="status">Loading uploads…</p>}
          {listError && <><p role="alert" className="error">{listError}</p><button className="button secondary" onClick={() => void load()}>Reload uploads</button></>}
          {!initial && !listError && !uploads.length && <p>Your uploads will appear here.</p>}
          <ul>
            {uploads.map(upload => (
              <li key={upload.id}>
                <button className="upload-row" aria-current={selected === upload.id ? 'true' : undefined} onClick={() => {
                  if (selected === upload.id) revealResult();
                  setSelected(upload.id);
                }}>
                  <FileText size={17} aria-hidden="true" />
                  <span className="row-copy">
                    <strong>{upload.fileName}</strong>
                    <span className="row-meta">{dateFormat.format(new Date(upload.created))} · {labels[upload.status]}</span>
                  </span>
                  <ArrowUpRight className="row-arrow" size={16} aria-hidden="true" />
                </button>
              </li>
            ))}
          </ul>
          {more && <button className="button secondary" disabled={listing} onClick={() => void load(uploads.at(-1)?.id)}>{listing ? 'Loading…' : 'Load older uploads'}</button>}
        </section>
        {selected === undefined ? (
          <section className="results empty-result" aria-labelledby="empty-title">
            <FileText size={30} aria-hidden="true" />
            <h2 id="empty-title">Document results</h2>
            <p>{uploads.length ? 'Select an upload to see its results.' : 'Your Markdown and page ranges will appear here after processing.'}</p>
          </section>
        ) : <Results key={selected} id={selected} session={session} onUpload={updateUpload} titleRef={resultTitle} backToUploads={() => uploadsTitle.current?.focus()} />}
      </div>
    </main>
  );
}

function Results({ id, session, onUpload, titleRef, backToUploads }: {
  id: number;
  session: Session;
  onUpload: (upload: Upload) => void;
  titleRef: React.Ref<HTMLHeadingElement>;
  backToUploads: () => void;
}) {
  const [result, setResult] = useState<UploadDetail>();
  const [error, setError] = useState('');
  const [attempt, setAttempt] = useState(0);
  const onLatestUpload = useRef(onUpload);
  onLatestUpload.current = onUpload;

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
        if (pending(detail.upload)) timer = window.setTimeout(load, 4000);
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
        <p className="result-status" role="status" data-status={result.upload.status}>{labels[result.upload.status]}</p>
        {result.upload.error && <p className="error">{result.upload.error}</p>}
        {pending(result.upload) && <p>Processing this upload. Results will appear here automatically.</p>}
        {result.upload.status === 'ready' && !result.documents.length && <p>No documents were returned for this upload.</p>}
        {result.documents.map(doc => <Document key={doc.id} doc={doc} session={session} uploadId={id} />)}
      </>}
    </section>
  );
}

function Document({ doc, session, uploadId }: { doc: DocumentResult; session: Session; uploadId: number }) {
  return (
    <article className="document">
      <h3>{doc.title}</h3>
      <p className="page-range">{doc.firstPage === doc.lastPage ? `Page ${doc.firstPage}` : `Pages ${doc.firstPage}–${doc.lastPage}`}</p>
      <pre aria-label="Extracted Markdown">{doc.markdown}</pre>
      <div className="document-actions">
        <button className="button secondary" onClick={() => {
          const url = URL.createObjectURL(new Blob([doc.markdown], { type: 'text/markdown;charset=utf-8' }));
          const link = document.createElement('a');
          link.href = url;
          link.download = `document-${doc.id}.md`;
          link.click();
          window.setTimeout(() => URL.revokeObjectURL(url), 1000);
        }}><ArrowDownToLine size={16} aria-hidden="true" /> Download Markdown</button>
        {doc.images.map(image => <ExtractedImage key={image.id} session={session} uploadId={uploadId} image={image} />)}
      </div>
    </article>
  );
}

function ExtractedImage({ session, uploadId, image }: { session: Session; uploadId: number; image: ImageReference }) {
  const [src, setSrc] = useState('');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const request = useRef<AbortController>(null);
  const url = useRef('');
  useEffect(() => () => {
    request.current?.abort();
    if (url.current) URL.revokeObjectURL(url.current);
  }, []);

  async function load() {
    const controller = new AbortController();
    request.current = controller;
    setBusy(true);
    setError('');
    try {
      const blob = await session.image(uploadId, image.id, controller.signal);
      if (controller.signal.aborted) return;
      url.current = URL.createObjectURL(blob);
      setSrc(url.current);
    } catch (failure) {
      if (!controller.signal.aborted) setError(errorMessage(failure));
    } finally {
      if (!controller.signal.aborted) setBusy(false);
    }
  }

  return src ? <figure><img src={src} alt={`Extracted image from page ${image.page}`} /><figcaption>Page {image.page} · {image.reference}</figcaption></figure> : (
    <div><button className="button secondary" disabled={busy} onClick={() => void load()}><Image size={16} aria-hidden="true" /> {busy ? 'Loading image…' : `View image from page ${image.page}`}</button>{error && <p role="alert" className="error">{error}</p>}</div>
  );
}
