import { useEffect, useRef, useState } from 'react';
import type { PDFDocumentProxy } from 'pdfjs-dist';
import workerUrl from 'pdfjs-dist/build/pdf.worker.min.mjs?url';

// PDF.js paints exactly one original page into one canvas, with fit-page sizing.
export function PdfPage({ blob, pageNumber }: { blob: Blob; pageNumber: number }) {
  const stage = useRef<HTMLDivElement>(null);
  const canvas = useRef<HTMLCanvasElement>(null);
  const [pdf, setPdf] = useState<PDFDocumentProxy>();
  const [size, setSize] = useState({ width: 0, height: 0 });
  const [error, setError] = useState('');
  const [rendering, setRendering] = useState(true);

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
    setRendering(true); setError('');
    target.width = 0; target.height = 0;
    void (async () => {
      try {
        const page = await pdf.getPage(pageNumber);
        if (disposed) return;
        const natural = page.getViewport({ scale: 1 });
        const scale = Math.max(.01, Math.min((size.width - 24) / natural.width, (size.height - 24) / natural.height));
        const ratio = Math.min(window.devicePixelRatio || 1, 2);
        const viewport = page.getViewport({ scale: scale * ratio });
        target.width = Math.ceil(viewport.width); target.height = Math.ceil(viewport.height);
        target.style.width = `${viewport.width / ratio}px`; target.style.height = `${viewport.height / ratio}px`;
        task = page.render({ canvas: target, canvasContext: target.getContext('2d')!, viewport });
        await task.promise;
        if (!disposed) setRendering(false);
      } catch {
        if (!disposed) { setError('This original PDF page could not be rendered. Try another page or view its Markdown.'); setRendering(false); }
      }
    })();
    return () => { disposed = true; task?.cancel(); };
  }, [pdf, pageNumber, size]);

  return <div className="pdf-stage" ref={stage} aria-busy={!error && rendering}>
    {!error && <canvas ref={canvas} role="img" aria-label={`Original PDF page ${pageNumber}`} style={{ visibility: rendering ? 'hidden' : 'visible' }} />}
    {error ? <p className="error" role="alert">{error}</p> : rendering && <p className="pdf-loading" role="status">Rendering page {pageNumber}…</p>}
  </div>;
}
