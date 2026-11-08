import { useEffect, useRef, useState } from 'react';
import { Maximize2, MoveHorizontal, ZoomIn, ZoomOut } from 'lucide-react';
import type { PDFDocumentProxy } from 'pdfjs-dist';
import workerUrl from 'pdfjs-dist/build/pdf.worker.min.mjs?url';
import { joinPieces, locate, spansByOwner } from './highlight';

/** Reads only the page count of an in-memory PDF; nothing is rendered. */
export async function countPdfPages(blob: Blob) {
  const lib = await import('pdfjs-dist');
  lib.GlobalWorkerOptions.workerSrc = workerUrl;
  const task = lib.getDocument({ data: new Uint8Array(await blob.arrayBuffer()) });
  try { return (await task.promise).numPages; }
  finally { void task.destroy(); }
}

type Box = { left: number; top: number; width: number; height: number; secondary?: boolean };
type TextItem = { str: string; transform: number[]; width: number; height: number; hasEOL?: boolean };

// PDF.js paints exactly one original page into one canvas. With evidence selected it fits the page width and
// scrolls to the highlight so the clause is readable; otherwise it fits the whole page. The reader can zoom.
// An optional evidence quote is located in the page's text layer and outlined over the canvas.
type Fit = 'auto' | 'page' | 'width';
export function PdfPage({ blob, pageNumber, highlight, secondary, onHighlight }: {
  blob: Blob; pageNumber: number; highlight?: string; secondary?: string[]; onHighlight?: (found: boolean) => void;
}) {
  const secondaryKey = (secondary ?? []).join('\u0000');
  const stage = useRef<HTMLDivElement>(null);
  const canvas = useRef<HTMLCanvasElement>(null);
  const [pdf, setPdf] = useState<PDFDocumentProxy>();
  const [size, setSize] = useState({ width: 0, height: 0 });
  const [frame, setFrame] = useState({ width: 0, height: 0 });
  const [boxes, setBoxes] = useState<Box[]>([]);
  const [error, setError] = useState('');
  const [rendering, setRendering] = useState(true);
  const [fit, setFit] = useState<Fit>('auto');
  const [zoom, setZoom] = useState(1);
  const mode = fit === 'auto' ? (highlight ? 'width' : 'page') : fit;
  const reportHighlight = useRef(onHighlight);
  reportHighlight.current = onHighlight;

  useEffect(() => {
    let disposed = false;
    let task: ReturnType<typeof import('pdfjs-dist')['getDocument']> | undefined;
    setError(''); setPdf(undefined);
    void (async () => {
      try {
        const lib = await import('pdfjs-dist');
        lib.GlobalWorkerOptions.workerSrc = workerUrl;
        const data = new Uint8Array(await blob.arrayBuffer());
        if (disposed) return;
        task = lib.getDocument({ data });
        const document = await task.promise;
        if (!disposed) setPdf(document);
      } catch {
        if (!disposed) setError('This PDF could not be displayed. It may be damaged or password protected.');
      }
    })();
    return () => { disposed = true; void task?.destroy(); };
  }, [blob]);

  useEffect(() => {
    const element = stage.current;
    if (!element) return;
    const observer = new ResizeObserver(([entry]) => setSize({ width: entry.contentRect.width, height: entry.contentRect.height }));
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  useEffect(() => {
    if (!pdf || !canvas.current || !size.width || !size.height) return;
    let disposed = false;
    let task: ReturnType<Awaited<ReturnType<PDFDocumentProxy['getPage']>>['render']> | undefined;
    const target = canvas.current;
    setRendering(true); setError(''); setBoxes([]);
    target.width = 0; target.height = 0;
    void (async () => {
      try {
        const page = await pdf.getPage(pageNumber);
        if (disposed) return;
        const natural = page.getViewport({ scale: 1 });
        const widthFit = (size.width - 24) / natural.width;
        const scale = Math.max(.01, (mode === 'width' ? widthFit : Math.min(widthFit, (size.height - 24) / natural.height)) * zoom);
        const ratio = Math.min(window.devicePixelRatio || 1, 2);
        const viewport = page.getViewport({ scale: scale * ratio });
        target.width = Math.ceil(viewport.width); target.height = Math.ceil(viewport.height);
        target.style.width = `${viewport.width / ratio}px`; target.style.height = `${viewport.height / ratio}px`;
        setFrame({ width: viewport.width / ratio, height: viewport.height / ratio });
        task = page.render({ canvas: target, canvasContext: target.getContext('2d')!, viewport });
        await task.promise;
        if (disposed) return;
        setRendering(false);
        const others = secondaryKey ? secondaryKey.split('\u0000') : [];
        if (!highlight && !others.length) return;
        const css = page.getViewport({ scale });
        const items = (await page.getTextContent()).items.filter((item): item is TextItem & typeof item => 'str' in item) as TextItem[];
        if (disposed) return;
        const { text, runs } = joinPieces(items.map((item, index) => ({ owner: index, text: item.str, gap: index > 0 && !!items[index - 1].hasEOL })));
        const boxesFor = (quote: string, isSecondary: boolean) => {
          const range = locate(text, quote);
          if (!range) return [];
          const found: Box[] = [];
          for (const [index, [from, to]] of spansByOwner(runs, range)) {
            const item = items[index];
            if (!item.str.length) continue;
            const [, , c, d, e, f] = item.transform;
            const height = item.height || Math.hypot(c, d);
            const x0 = e + item.width * (from / item.str.length), x1 = e + item.width * (to / item.str.length);
            const [ax, ay] = css.convertToViewportPoint(x0, f - height * .22);
            const [bx, by] = css.convertToViewportPoint(x1, f + height * .88);
            found.push({ left: Math.min(ax, bx) - 1, top: Math.min(ay, by), width: Math.abs(bx - ax) + 2, height: Math.abs(by - ay), secondary: isSecondary });
          }
          return found;
        };
        const primary = highlight ? boxesFor(highlight, false) : [];
        setBoxes([...others.filter(quote => quote !== highlight).flatMap(quote => boxesFor(quote, true)), ...primary]);
        // Bring the clause into view: a third of the way down the stage, horizontally centred when zoomed.
        if (primary.length && stage.current) {
          const top = Math.min(...primary.map(box => box.top)), left = Math.min(...primary.map(box => box.left));
          stage.current.scrollTop = Math.max(0, top + 12 - stage.current.clientHeight / 3);
          if (zoom > 1) stage.current.scrollLeft = Math.max(0, left - 24);
        }
        if (highlight) reportHighlight.current?.(primary.length > 0);
      } catch {
        if (!disposed) { setError('This original PDF page could not be rendered. Try another page or view its Markdown.'); setRendering(false); }
      }
    })();
    return () => { disposed = true; task?.cancel(); };
  }, [pdf, pageNumber, size, highlight, secondaryKey, mode, zoom]);

  const step = (by: number) => setZoom(value => Math.min(3, Math.max(.5, Math.round((value + by) * 4) / 4)));
  return <div className="pdf-viewer">
    <div className="pdf-tools" role="toolbar" aria-label="Page zoom">
      <button type="button" aria-pressed={mode === 'page' && zoom === 1} title="Fit the whole page" onClick={() => { setFit('page'); setZoom(1); }}><Maximize2 size={14} aria-hidden="true" /><span className="visually-hidden">Fit page</span></button>
      <button type="button" aria-pressed={mode === 'width' && zoom === 1} title="Fit the page width" onClick={() => { setFit('width'); setZoom(1); }}><MoveHorizontal size={14} aria-hidden="true" /><span className="visually-hidden">Fit width</span></button>
      <button type="button" title="Zoom out" disabled={zoom <= .5} onClick={() => step(-.25)}><ZoomOut size={14} aria-hidden="true" /><span className="visually-hidden">Zoom out</span></button>
      <span className="pdf-zoom" aria-live="polite">{Math.round(zoom * 100)}%</span>
      <button type="button" title="Zoom in" disabled={zoom >= 3} onClick={() => step(.25)}><ZoomIn size={14} aria-hidden="true" /><span className="visually-hidden">Zoom in</span></button>
    </div>
  <div className="pdf-stage" ref={stage} aria-busy={!error && rendering} data-fit={mode} data-zoomed={zoom > 1 || undefined}>
    {!error && <div className="pdf-canvas-wrap" style={{ width: frame.width || undefined, height: frame.height || undefined }}>
      <canvas ref={canvas} role="img" aria-label={`Original PDF page ${pageNumber}`} style={{ visibility: rendering ? 'hidden' : 'visible' }} />
      {!rendering && boxes.map((box, index) => <span key={index} className="pdf-evidence-box" data-secondary={box.secondary || undefined} aria-hidden="true" style={{ left: box.left, top: box.top, width: box.width, height: box.height }} />)}
    </div>}
    {error ? <p className="error" role="alert">{error}</p> : rendering && <p className="pdf-loading" role="status">Rendering page {pageNumber}…</p>}
  </div>
  </div>;
}
