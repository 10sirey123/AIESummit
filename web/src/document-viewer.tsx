import { useEffect, useRef, useState } from 'react';
import Markdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { ArrowDownToLine, Image, MessageSquare, PanelRightClose, PanelRightOpen } from 'lucide-react';
import { errorMessage, type DocumentResult, type ImageReference, type PageResult, type Session } from './api';
import { PdfPage } from './pdf-page';

export function documentPages(doc: DocumentResult) {
  const parts = doc.markdown.split(/<!--\s*page break\s*-->/i);
  const exactPages = parts.length === doc.lastPage - doc.firstPage + 1;
  return parts.map((markdown, index) => ({
    markdown,
    label: exactPages ? `Page ${doc.firstPage + index}` : index === 0 ? pageRange(doc) : 'Page break',
  }));
}

function pageRange(doc: DocumentResult) {
  return doc.firstPage === doc.lastPage ? `Page ${doc.firstPage}` : `Pages ${doc.firstPage}–${doc.lastPage}`;
}

// Only explicit web/mail links are navigable. Never load image URLs from OCR text.
export function documentLink(url: string) {
  return /^(https?:\/\/|mailto:)/i.test(url) ? url : '';
}

export function MarkdownBody({ markdown }: { markdown: string }) {
  return <Markdown remarkPlugins={[remarkGfm]} skipHtml urlTransform={documentLink} components={{
    h1: ({ children }) => <h4>{children}</h4>,
    h2: ({ children }) => <h5>{children}</h5>,
    h3: ({ children }) => <h6>{children}</h6>,
    a: ({ href, children }) => href ? <a href={href} target="_blank" rel="noopener noreferrer">{children}</a> : <span>{children}</span>,
    img: ({ alt }) => <span className="markdown-image-reference">Image reference: {alt || 'Untitled image'}</span>,
    table: ({ children }) => <div className="markdown-table" role="region" aria-label="Document table" tabIndex={0}><table>{children}</table></div>,
  }}>{markdown}</Markdown>;
}

export function DocumentViewer({ documents, pages, session, uploadId, onAnalysisQueued }: {
  documents: DocumentResult[]; pages: PageResult[]; session: Session; uploadId: number; onAnalysisQueued?: () => void;
}) {
  const [pageNumber, setPageNumber] = useState(pages[0]?.number);
  const [view, setView] = useState<'pdf' | 'markdown'>('pdf');
  const [annotations, setAnnotations] = useState(true);
  const [blob, setBlob] = useState<Blob>();
  const [sourceError, setSourceError] = useState('');
  const [sourceAttempt, setSourceAttempt] = useState(0);
  const [analysisError, setAnalysisError] = useState('');
  const [queuing, setQueuing] = useState(false);
  const reading = useRef<HTMLDivElement>(null);
  const page = pages.find(item => item.number === pageNumber) ?? pages[0];
  const doc = page && documents.find(item => item.firstPage <= page.number && item.lastPage >= page.number);
  const pageIndex = pages.indexOf(page);
  const panelId = `page-panel-${uploadId}`;

  useEffect(() => {
    if (view !== 'pdf' || blob) return;
    const controller = new AbortController();
    setSourceError('');
    void session.source(uploadId, controller.signal).then(result => {
      if (!controller.signal.aborted) setBlob(result);
    }).catch(failure => { if (!controller.signal.aborted) setSourceError(errorMessage(failure)); });
    return () => controller.abort();
  }, [session, uploadId, view, blob, sourceAttempt]);
  useEffect(() => { if (reading.current) reading.current.scrollTop = 0; }, [page?.number, view]);
  if (!page) return null;

  function download() {
    const url = URL.createObjectURL(new Blob([doc?.markdown ?? page.markdown], { type: 'text/markdown;charset=utf-8' }));
    const link = document.createElement('a');
    link.href = url;
    link.download = doc ? `document-${doc.id}.md` : `page-${page.number}.md`;
    link.click();
    window.setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
  async function analyze() {
    setQueuing(true); setAnalysisError('');
    try { await session.analyze(uploadId); onAnalysisQueued?.(); }
    catch (failure) { setAnalysisError(errorMessage(failure)); }
    finally { setQueuing(false); }
  }

  return <div className="document-viewer">
    {documents.length > 0 && <div className="document-picker">
      <label htmlFor={`document-${uploadId}`}>Document</label>
      <select id={`document-${uploadId}`} value={doc?.id ?? ''} onChange={event => {
        const next = documents.find(item => item.id === Number(event.target.value));
        if (next) setPageNumber(next.firstPage);
      }}>
        {!doc && <option value="">Source pages</option>}
        {documents.map(item => <option key={item.id} value={item.id}>{item.title} · {pageRange(item)}</option>)}
      </select>
    </div>}
    <div className="page-navigation" role="group" aria-label="Source page navigation">
      <button className="button secondary" disabled={pageIndex <= 0} onClick={() => setPageNumber(pages[pageIndex - 1].number)}>Previous page</button>
      <label>Page <select aria-label="Source page" value={page.number} onChange={event => setPageNumber(Number(event.target.value))}>
        {pages.map(item => <option key={item.id} value={item.number}>{item.number}</option>)}
      </select> of {pages.length}</label>
      <button className="button secondary" disabled={pageIndex >= pages.length - 1} onClick={() => setPageNumber(pages[pageIndex + 1].number)}>Next page</button>
    </div>
    <div className="reader-toolbar">
      <div className="view-switch" role="tablist" aria-label="Page format">
        {(['pdf', 'markdown'] as const).map(format => <button key={format} type="button" role="tab" id={`${format}-tab-${uploadId}`} aria-selected={view === format}
          aria-controls={panelId} tabIndex={view === format ? 0 : -1} onClick={() => setView(format)}
          onKeyDown={event => {
            if (['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) {
              event.preventDefault();
              const next = event.key === 'Home' ? 'pdf' : event.key === 'End' ? 'markdown' : view === 'pdf' ? 'markdown' : 'pdf';
              setView(next);
              document.getElementById(`${next}-tab-${uploadId}`)?.focus();
            }
          }}>{format === 'pdf' ? 'Original PDF' : 'Markdown'}</button>)}
      </div>
      <button className="reader-icon-button" type="button" aria-expanded={annotations} aria-controls={`annotations-${uploadId}`} onClick={() => setAnnotations(value => !value)}>
        {annotations ? <PanelRightClose size={17} aria-hidden="true" /> : <PanelRightOpen size={17} aria-hidden="true" />}
        <span>{annotations ? 'Hide annotations' : 'Show annotations'}</span>
      </button>
    </div>
    <div className="reader-grid" data-annotations={annotations}>
      <article className="document reading-document" aria-labelledby={`document-title-${uploadId}`}>
        <header className="document-header">
          <h3 id={`document-title-${uploadId}`}>{doc?.title ?? 'Source document'}</h3>
          <p className="page-range" role="status">Page {page.number} · {view === 'pdf' ? 'Original source' : 'OCR Markdown'}</p>
        </header>
        <div role="tabpanel" id={panelId} aria-labelledby={`${view}-tab-${uploadId}`}>
          {view === 'pdf' ? sourceError ? <div className="source-failure"><p className="error" role="alert">{sourceError}</p><button className="button secondary" onClick={() => setSourceAttempt(value => value + 1)}>Retry original file</button></div>
            : blob ? blob.type === 'application/pdf' ? <PdfPage key={page.number} blob={blob} pageNumber={page.number} /> : <OriginalImage blob={blob} />
            : <div className="pdf-stage"><p role="status">Loading original file…</p></div>
          : <div key={page.number} className="markdown-page-scroll" ref={reading} tabIndex={0} role="region" aria-label={`Markdown for page ${page.number}`}>
              <div className="markdown-preview markdown-page"><MarkdownBody markdown={page.markdown} /></div>
            </div>}
        </div>
        <div className="document-actions"><button className="button secondary" onClick={download}><ArrowDownToLine size={16} aria-hidden="true" /> Download Markdown</button></div>
        <details className="page-source"><summary>Markdown source for this page</summary><pre className="markdown-source">{page.markdown}</pre></details>
        {doc?.images.some(image => image.page === page.number) && <section className="extracted-images" aria-label="Extracted images">
          <h4>Extracted images on this page</h4><div className="document-actions">{doc.images.filter(image => image.page === page.number).map(image => <ExtractedImage key={image.id} session={session} uploadId={uploadId} image={image} />)}</div>
        </section>}
      </article>
      <aside key={page.number} className="annotation-panel" id={`annotations-${uploadId}`} aria-labelledby={`annotations-title-${uploadId}`} hidden={!annotations}>
        <h3 id={`annotations-title-${uploadId}`}>Page sections</h3>
        <p className="annotation-context">Page {page.number} · Gemini analysis</p>
        {page.analysisStatus === 'ready' ? <>
          <p className="analysis-status" role="status">Analysis complete · {page.sections.length} sections</p>
          {!page.sections.length && <p className="annotation-empty">No identifiable sections were returned for this page.</p>}
          {page.sections.map((section, index) => <section className="section-finding" key={`${page.id}-${index}`}>
            <h4>{section.heading}</h4><p className="section-classification">{section.classification.replaceAll('_', ' ')}</p>
            <p>{section.summary}</p><details><summary>Source evidence · page {page.number}</summary><blockquote>{section.quote}</blockquote></details>
          </section>)}
          <p className="annotation-future">AI-generated section descriptions, not a legal risk assessment. Check the source evidence.</p>
        </> : <div className="annotation-empty">
          <MessageSquare size={23} aria-hidden="true" />
          <h4>{page.analysisStatus === 'analyzing' ? 'Analyzing this page…' : page.analysisStatus === 'failed' ? 'Analysis needs attention' : 'No section analysis yet'}</h4>
          <p>{page.error ?? (page.analysisStatus === 'analyzing' ? 'The section classifications and summaries will appear automatically.' : 'Analyze this upload with Gemini to identify and summarize the sections on each page.')}</p>
          {page.analysisStatus === 'not_started' && <button className="button secondary" disabled={queuing} onClick={() => void analyze()}>{queuing ? 'Queuing…' : 'Analyze pages with Gemini'}</button>}
        </div>}
        {analysisError && <p role="alert" className="error">{analysisError}</p>}
      </aside>
    </div>
  </div>;
}

function OriginalImage({ blob }: { blob: Blob }) {
  const [url, setUrl] = useState('');
  useEffect(() => { const value = URL.createObjectURL(blob); setUrl(value); return () => URL.revokeObjectURL(value); }, [blob]);
  return <div className="pdf-stage">{url && <img src={url} alt="Original uploaded image" />}</div>;
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
