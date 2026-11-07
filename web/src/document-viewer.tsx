import { useEffect, useMemo, useRef, useState } from 'react';
import Markdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { ArrowDownToLine, BookOpen, ChevronLeft, ChevronRight, Image, MessageSquare, PanelRightClose, PanelRightOpen, X } from 'lucide-react';
import { errorMessage, type DocumentResult, type ExtractionEvidence, type ExtractionType, type ImageReference, type PageResult, type Session } from './api';
import { PdfPage } from './pdf-page';
import { rehypeEvidence, rehypeTerms } from './highlight';
import { clauseContext, clauseLinks, glossary, sectionIndex, termsIn, type ClauseLink, type Term } from './contract-links';
import { FindingDetail, LinkedClausePanel, orderFindings, ReviewRail, reviewFindings, useDecisions, type ReviewFinding } from './review-desk';
import { evaluate, useActiveRules } from './playbook-store';
import { KeyDates, NegotiationSummary } from './summary-view';
import { ExtractionDetail, ExtractionHint, ExtractionRail, ExtractionStatus, flattenFindings, type SelectedFinding } from './extraction-view';
import { orderedTargets, selectAnalyses } from './extraction-meta';
import type { UploadExtractions } from './extraction-store';

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

export function MarkdownBody({ markdown, highlight, extra, terms, onTerm }: {
  markdown: string; highlight?: string; extra?: string[]; terms?: string[]; onTerm?: (term: string) => void;
}) {
  return <Markdown remarkPlugins={[remarkGfm]} rehypePlugins={[[rehypeEvidence, { quote: highlight, extra }], [rehypeTerms, { terms }]]} skipHtml urlTransform={documentLink} components={{
    dfn: ({ node, children }) => {
      const term = String(node?.properties?.dataTerm ?? '');
      return onTerm && term ? <button type="button" className="defined-term" title="Show definition" onClick={() => onTerm(term)}>{children}</button> : <dfn>{children}</dfn>;
    },
    h1: ({ children }) => <h4>{children}</h4>,
    h2: ({ children }) => <h5>{children}</h5>,
    h3: ({ children }) => <h6>{children}</h6>,
    a: ({ href, children }) => href ? <a href={href} target="_blank" rel="noopener noreferrer">{children}</a> : <span>{children}</span>,
    img: ({ alt }) => <span className="markdown-image-reference">Image reference: {alt || 'Untitled image'}</span>,
    table: ({ children }) => <div className="markdown-table" role="region" aria-label="Document table" tabIndex={0}><table>{children}</table></div>,
  }}>{markdown}</Markdown>;
}

export function DocumentViewer({ documents, pages, session, uploadId, onAnalysisQueued, focusPage, onOpenPlaybook, fileName, extractions, catalog, initialMode = 'extractions' }: {
  documents: DocumentResult[]; pages: PageResult[]; session: Session; uploadId: number; onAnalysisQueued?: () => void;
  /** A page another view asked to open; `request` changes on every ask so repeated clicks still jump. `finding` opens an extracted term. */
  focusPage?: { page: number; request: number; quote?: string; finding?: string };
  extractions?: UploadExtractions; catalog?: ExtractionType[];
  initialMode?: 'extractions' | 'review' | 'summary' | 'dates';
  onOpenPlaybook?: () => void;
  fileName?: string;
}) {
  const [pageNumber, setPageNumber] = useState(pages[0]?.number);
  const [evidence, setEvidence] = useState<{ page: number; key: string; quote: string }>();
  const [located, setLocated] = useState<boolean>();
  const [tag, setTag] = useState<string>();
  const [rejectRequest, setRejectRequest] = useState(0);
  const [decisions, decide, decideMany] = useDecisions(uploadId);
  const [mode, setMode] = useState<'extractions' | 'review' | 'summary' | 'dates'>(initialMode);
  const selections = useMemo(() => selectAnalyses(extractions?.results), [extractions?.results]);
  const extracted = useMemo(() => flattenFindings(selections, orderedTargets(catalog ?? [])), [selections, catalog]);
  const [xSelected, setXSelected] = useState<string>();
  const [xEvidence, setXEvidence] = useState<number>();
  const xItem = extracted.find(item => item.key === xSelected);
  const [bulkNotice, setBulkNotice] = useState<{ text: string; undo: () => void }>();
  const findings = reviewFindings(pages);
  const { playbook: activePlaybook, rules } = useActiveRules();
  const tierOf = (finding: ReviewFinding) => evaluate(finding, rules).tier;
  const ordered = orderFindings(findings, tierOf, tag);
  const selected = findings.find(item => item.key === evidence?.key);
  const index = useMemo(() => sectionIndex(pages), [pages]);
  const definedTerms = useMemo(() => glossary(pages), [pages]);
  const [linkView, setLinkView] = useState<{ key: string; title: string; page: number; quote: string; context: string; relation?: string; origin?: string; back?: string }>();
  const [termCard, setTermCard] = useState<Term>();
  const selectedLinks = useMemo(() => selected ? clauseLinks(pages, index, selected.page, selected.quote) : undefined, [selected?.key, pages, index]);
  const root = useRef<HTMLDivElement>(null);
  function selectFinding(finding: ReviewFinding) {
    setPageNumber(finding.page);
    setLocated(undefined);
    setEvidence({ page: finding.page, key: finding.key, quote: finding.quote });
  }
  function showExtraction(item: SelectedFinding, index = 0) {
    const evidence: ExtractionEvidence | undefined = item.finding.evidence[index];
    setXSelected(item.key);
    setXEvidence(evidence ? index : undefined);
    if (!evidence) return;
    setPageNumber(evidence.page);
    setLocated(undefined);
    setEvidence({ page: evidence.page, key: `${item.key}-${index}`, quote: evidence.quote });
  }
  function step(by: number) {
    if (mode === 'extractions') {
      if (!extracted.length) return;
      const at = extracted.findIndex(item => item.key === xSelected);
      showExtraction(extracted[at < 0 ? 0 : (at + by + extracted.length) % extracted.length]);
      return;
    }
    if (!ordered.length) return;
    const at = ordered.findIndex(item => item.key === selected?.key);
    selectFinding(ordered[at < 0 ? 0 : (at + by + ordered.length) % ordered.length]);
  }
  useEffect(() => {
    if (!focusPage || !pages.some(item => item.number === focusPage.page)) return;
    const target = focusPage.finding ? extracted.find(item => item.key === focusPage.finding) : undefined;
    if (target) {
      setMode('extractions');
      showExtraction(target, Math.max(0, target.finding.evidence.findIndex(item => item.page === focusPage.page && (!focusPage.quote || item.quote === focusPage.quote))));
      root.current?.scrollIntoView({ behavior: 'auto', block: 'start' });
      return;
    }
    setPageNumber(focusPage.page);
    if (focusPage.quote) {
      const match = findings.find(item => item.page === focusPage.page && item.quote === focusPage.quote);
      if (match) selectFinding(match);
      else { setEvidence({ page: focusPage.page, key: `focus-${focusPage.request}`, quote: focusPage.quote }); setLocated(undefined); }
    }
    root.current?.scrollIntoView({ behavior: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth', block: 'start' });
  }, [focusPage?.request, !!extracted.length]);
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
  const shownEvidence = evidence && page && evidence.page === page.number ? evidence : undefined;
  const linkViewLinks = useMemo(() => linkView ? clauseLinks(pages, index, linkView.page, linkView.quote) : undefined, [linkView?.key, pages, index]);
  const termsOnPage = useMemo(() => page ? termsIn(page.markdown, definedTerms).map(item => item.term) : [], [page?.number, page?.markdown, definedTerms]);
  const tagExtras = mode === 'extractions'
    ? (xItem && page ? xItem.finding.evidence.filter((item, index) => item.page === page.number && index !== xEvidence).map(item => item.quote) : [])
    : tag && page ? findings.filter(item => item.type === tag && item.page === page.number && item.key !== shownEvidence?.key).map(item => item.quote) : [];
  useEffect(() => { if (reading.current) reading.current.scrollTop = 0; }, [page?.number, view]);
  // In the Markdown view, bring the highlighted evidence into view and report whether it was found.
  useEffect(() => {
    if (view !== 'markdown' || !shownEvidence) return;
    const mark = reading.current?.querySelector('mark.evidence-highlight');
    setLocated(!!mark);
    if (mark && reading.current) {
      const box = reading.current.getBoundingClientRect(), target = mark.getBoundingClientRect();
      reading.current.scrollTop += target.top - box.top - box.height / 3;
    }
  }, [view, shownEvidence?.key, page?.number]);
  if (!page) return null;

  function selectEvidence(key: string, quote: string) {
    if (evidence?.key === key) { setEvidence(undefined); return; }
    const finding = findings.find(item => item.key === key);
    if (finding) selectFinding(finding);
    else { setLocated(undefined); setEvidence({ page: page.number, key, quote }); }
  }
  function follow(link: ClauseLink, origin: string, back?: string) {
    const ref = link.target ?? link.from;
    const target = pages.find(item => item.number === link.page);
    if (!target || (!ref && !link.excerpt)) return;
    const quote = ref ? ref.line : link.excerpt;
    const key = `link-${link.key}-${Date.now()}`;
    setLinkView({ key, page: link.page, quote, context: clauseContext(target.markdown, quote).text, relation: link.relation, origin,
      title: ref ? `§${ref.id}${ref.title ? ` ${ref.title}` : ''}` : `Page ${link.page}`, back });
    setPageNumber(link.page);
    setLocated(undefined);
    setEvidence({ page: link.page, key, quote });
  }
  function goToDefinition(term: Term) {
    setTermCard(term);
    setPageNumber(term.page);
    setLocated(undefined);
    setEvidence({ page: term.page, key: `term-${term.term}`, quote: term.definition.replace(/^“[^”]+”\s*/, '') || term.definition });
  }
  function onDeskKey(event: React.KeyboardEvent) {
    const target = event.target as HTMLElement;
    if (event.metaKey || event.ctrlKey || event.altKey || /^(INPUT|SELECT|TEXTAREA)$/.test(target.tagName) || target.getAttribute('role') === 'tab') return;
    const key = event.key.toLowerCase();
    if (key === 'j') step(1);
    else if (key === 'k') step(-1);
    else if (mode !== 'review') return;
    else if (key === 'a' && selected && !decisions[selected.key]) decide(selected.key, { decision: 'accepted' });
    else if (key === 'r' && selected && !decisions[selected.key]) setRejectRequest(value => value + 1);
    else return;
    event.preventDefault();
  }

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

  return <div className="document-viewer" ref={root} onKeyDown={onDeskKey}>
    <div className="desk-toolbar">
      <div className="mode-switch" role="tablist" aria-label="Review mode">
        {([['extractions', 'Extracted terms'], ['review', 'Page review'], ['summary', 'Negotiation summary'], ['dates', 'Key dates']] as const).map(([value, label]) =>
          <button key={value} type="button" role="tab" aria-selected={mode === value} onClick={() => setMode(value)}>{label}</button>)}
      </div>
      {(mode === 'review' || mode === 'extractions') && <>
        {documents.length > 1 && <div className="document-picker compact">
          <label htmlFor={`document-${uploadId}`} className="visually-hidden">Document</label>
          <select id={`document-${uploadId}`} value={doc?.id ?? ''} onChange={event => {
            const next = documents.find(item => item.id === Number(event.target.value));
            if (next) setPageNumber(next.firstPage);
          }}>
            {!doc && <option value="">Source pages</option>}
            {documents.map(item => <option key={item.id} value={item.id}>{item.title} · {pageRange(item)}</option>)}
          </select>
        </div>}
        <div className="page-navigation compact" role="group" aria-label="Source page navigation">
          <button className="icon-button" aria-label="Previous page" disabled={pageIndex <= 0} onClick={() => setPageNumber(pages[pageIndex - 1].number)}><ChevronLeft size={16} aria-hidden="true" /></button>
          <label><span className="visually-hidden">Page</span><select aria-label="Source page" value={page.number} onChange={event => setPageNumber(Number(event.target.value))}>
            {pages.map(item => <option key={item.id} value={item.number}>p.{item.number}</option>)}
          </select><span className="page-of">of {pages.length}</span></label>
          <button className="icon-button" aria-label="Next page" disabled={pageIndex >= pages.length - 1} onClick={() => setPageNumber(pages[pageIndex + 1].number)}><ChevronRight size={16} aria-hidden="true" /></button>
        </div>
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
        <button className="reader-icon-button" type="button" aria-expanded={annotations} aria-controls={`annotations-${uploadId}`} onClick={() => setAnnotations(value => !value)}
          title={annotations ? 'Hide the finding panel' : 'Show the finding panel'}>
          {annotations ? <PanelRightClose size={17} aria-hidden="true" /> : <PanelRightOpen size={17} aria-hidden="true" />}
          <span className="visually-hidden">{annotations ? 'Hide annotations' : 'Show annotations'}</span>
        </button>
      </>}
    </div>
    {mode === 'summary' ? <NegotiationSummary fileName={fileName ?? doc?.title ?? 'Contract'} playbook={`${activePlaybook.name} v${activePlaybook.published.version}`}
      findings={findings} rules={rules} decisions={decisions} onOpen={finding => { setMode('review'); selectFinding(finding); }} />
    : mode === 'dates' ? <KeyDates fileName={fileName ?? doc?.title ?? 'Contract'} pages={pages} onOpen={(pageNumber, sentence) => {
      setMode('review'); setPageNumber(pageNumber); setLocated(undefined); setEvidence({ page: pageNumber, key: `date-${Date.now()}`, quote: sentence });
    }} />
    : <>{mode === 'extractions' && <ExtractionStatus session={session} uploadId={uploadId} state={extractions} documents={documents} />}
    <div className="reader-grid" data-annotations={annotations}>
      {mode === 'extractions' ? <ExtractionRail catalog={catalog} selections={selections} documents={documents} selected={xSelected} onSelect={item => showExtraction(item)} />
      : <ReviewRail findings={findings} decisions={decisions} selected={selected?.key} tag={tag} totalPages={pages.length} currentPage={page.number}
        analyzing={pages.some(item => item.analysisStatus === 'analyzing')} tierOf={tierOf} playbookName={`${activePlaybook.name} v${activePlaybook.published.version}`} onOpenPlaybook={onOpenPlaybook} onSelect={selectFinding} onTag={next => {
          setTag(next);
          const first = next && findings.find(item => item.type === next);
          if (first && selected?.type !== next) selectFinding(first);
        }} onBulkAccept={(keys, label) => {
          decideMany(keys, { decision: 'accepted' });
          setBulkNotice({ text: `Accepted ${label} findings.`, undo: () => { decideMany(keys, undefined); setBulkNotice(undefined); } });
        }} bulkNotice={bulkNotice} onOpenSummary={() => setMode('summary')} />}
      <article className="document reading-document" aria-labelledby={`document-title-${uploadId}`}>
        <header className="document-header">
          <h3 id={`document-title-${uploadId}`}>{doc?.title ?? 'Source document'}</h3>
          <p className="page-range" role="status">Page {page.number} · {view === 'pdf' ? 'Original source' : 'OCR Markdown'}</p>
        </header>
        {termCard && <div className="term-card" role="note">
          <BookOpen size={16} aria-hidden="true" />
          <div className="term-card-copy">
            <p className="detail-label">Defined term · p.{termCard.page}</p>
            <p>{termCard.definition}</p>
            {termCard.page !== page.number || shownEvidence?.key !== `term-${termCard.term}`
              ? <button type="button" className="text-button" onClick={() => goToDefinition(termCard)}>Go to definition</button> : null}
          </div>
          <button type="button" className="detail-close" aria-label="Close definition" onClick={() => setTermCard(undefined)}><X size={15} aria-hidden="true" /></button>
        </div>}
        <div role="tabpanel" id={panelId} aria-labelledby={`${view}-tab-${uploadId}`}>
          {view === 'pdf' ? sourceError ? <div className="source-failure"><p className="error" role="alert">{sourceError}</p><button className="button secondary" onClick={() => setSourceAttempt(value => value + 1)}>Retry original file</button></div>
            : blob ? blob.type === 'application/pdf' ? <PdfPage key={page.number} blob={blob} pageNumber={page.number} highlight={shownEvidence?.quote} secondary={tagExtras} onHighlight={setLocated} /> : <OriginalImage blob={blob} />
            : <div className="pdf-stage"><p role="status">Loading original file…</p></div>
          : <div key={page.number} className="markdown-page-scroll" ref={reading} tabIndex={0} role="region" aria-label={`Markdown for page ${page.number}`}>
              <div className="markdown-preview markdown-page"><MarkdownBody markdown={page.markdown} highlight={shownEvidence?.quote} extra={tagExtras} terms={termsOnPage} onTerm={term => setTermCard(definedTerms.get(term))} /></div>
            </div>}
        </div>
        <div className="document-actions"><button className="button secondary" onClick={download}><ArrowDownToLine size={16} aria-hidden="true" /> Download Markdown</button></div>
        <details className="page-source"><summary>Markdown source for this page</summary><pre className="markdown-source">{page.markdown}</pre></details>
        {doc?.images.some(image => image.page === page.number) && <section className="extracted-images" aria-label="Extracted images">
          <h4>Extracted images on this page</h4><div className="document-actions">{doc.images.filter(image => image.page === page.number).map(image => <ExtractedImage key={image.id} session={session} uploadId={uploadId} image={image} />)}</div>
        </section>}
      </article>
      {mode === 'extractions' ? <aside className="annotation-panel" id={`annotations-${uploadId}`} aria-label="Extracted term" hidden={!annotations}>
        {xItem ? <ExtractionDetail session={session} uploadId={uploadId} item={xItem} selection={selections.find(item => item.documentId === xItem.documentId)} documents={documents}
          activeEvidence={xEvidence} located={shownEvidence?.key === `${xItem.key}-${xEvidence}` ? located : undefined}
          onEvidence={(_, index) => showExtraction(xItem, index)} onClose={() => { setXSelected(undefined); setEvidence(undefined); }} />
          : <ExtractionHint count={extracted.length} />}
      </aside>
      : selected && selected.page === page.number ? <aside className="annotation-panel" id={`annotations-${uploadId}`} aria-label="Selected finding" hidden={!annotations}>
        <FindingDetail finding={selected} findings={findings} decision={decisions[selected.key]} located={located} rejectRequest={rejectRequest}
          onDecide={decision => decide(selected.key, decision)} onSelect={selectFinding} onNext={() => step(1)} onClose={() => setEvidence(undefined)}
          evaluation={evaluate(selected, rules)} links={selectedLinks} terms={selectedLinks ? termsIn(selectedLinks.context, definedTerms) : []}
          onFollow={link => follow(link, `§${selectedLinks?.section?.id ?? ''} ${selected.heading}`.trim(), selected.key)} onTermGo={goToDefinition} />
      </aside> : linkView && shownEvidence?.key === linkView.key ? <aside className="annotation-panel" id={`annotations-${uploadId}`} aria-label="Linked clause" hidden={!annotations}>
        <LinkedClausePanel title={linkView.title} page={linkView.page} context={linkView.context} relation={linkView.relation} origin={linkView.origin}
          links={linkViewLinks} terms={termsIn(linkView.context, definedTerms)} located={located}
          onBack={linkView.back ? () => { const back = findings.find(item => item.key === linkView.back); if (back) selectFinding(back); } : undefined}
          onFollow={link => follow(link, linkView.title, linkView.back)} onTermGo={goToDefinition} />
      </aside> : <aside key={page.number} className="annotation-panel" id={`annotations-${uploadId}`} aria-labelledby={`annotations-title-${uploadId}`} hidden={!annotations}>
        <h3 id={`annotations-title-${uploadId}`}>Page sections</h3>
        <p className="annotation-context">Page {page.number} · AI analysis</p>
        {page.analysisStatus === 'ready' ? <>
          <p className="analysis-status" role="status">Analysis complete · {page.sections.length} sections</p>
          {!page.sections.length && <p className="annotation-empty">No identifiable sections were returned for this page.</p>}
          {shownEvidence && <p className="evidence-status" role="status">{located === undefined ? 'Finding the evidence on this page…'
            : located ? `Highlighted on page ${page.number}` : 'The exact quote could not be located on this page. Check the source evidence below.'}</p>}
          {page.sections.map((section, index) => {
            const key = `${page.id}-${index}`;
            const active = shownEvidence?.key === key;
            return <section className="section-finding" key={key} data-selected={active || undefined}>
              <button type="button" className="section-select" aria-pressed={active} onClick={() => selectEvidence(key, section.quote)}>
                <span className="section-select-title">{section.heading}</span>
                <span className="section-select-hint">{active ? 'Highlighted · click to clear' : 'Review and show in document'}</span>
              </button>
              <p className="section-classification">{section.classification.replaceAll('_', ' ')}</p>
              <p>{section.summary}</p><details><summary>Source evidence · page {page.number}</summary><blockquote>{section.quote}</blockquote></details>
            </section>;
          })}
          <p className="annotation-future">AI-generated section descriptions, not a legal risk assessment. Check the source evidence.</p>
        </> : <div className="annotation-empty">
          <MessageSquare size={23} aria-hidden="true" />
          <h4>{page.analysisStatus === 'analyzing' ? 'Analyzing this page…' : page.analysisStatus === 'failed' ? 'Analysis needs attention' : 'No section analysis yet'}</h4>
          <p>{page.error ?? (page.analysisStatus === 'analyzing' ? 'The section classifications and summaries will appear automatically.' : 'Analyze this upload to identify and summarize the sections on each page.')}</p>
          {page.analysisStatus === 'not_started' && <button className="button secondary" disabled={queuing} onClick={() => void analyze()}>{queuing ? 'Queuing…' : 'Analyze pages'}</button>}
        </div>}
        {analysisError && <p role="alert" className="error">{analysisError}</p>}
      </aside>}
    </div></>}
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
