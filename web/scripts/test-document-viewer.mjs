import assert from 'node:assert/strict';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { createServer } from 'vite';

const server = await createServer({ server: { middlewareMode: true, hmr: false }, appType: 'custom' });
try {
  const { MarkdownBody, DocumentViewer, documentPages, documentLink } = await server.ssrLoadModule('/src/document-viewer.tsx');
  const render = markdown => renderToStaticMarkup(createElement(MarkdownBody, { markdown }));
  const html = render('# Agreement\n\n**Supplier** must:\n\n- Give notice\n\n| Term | Value |\n| --- | --- |\n| Notice | 30 days |');
  assert.match(html, /<h4>Agreement<\/h4>/);
  assert.match(html, /<strong>Supplier<\/strong>/);
  assert.match(html, /<li>Give notice<\/li>/);
  assert.match(html, /<table>/);
  assert.match(html, /<td>30 days<\/td>/);
  const untrusted = render('<script>alert(1)</script>\n\n<iframe src="https://example.invalid"></iframe>\n\n[unsafe](javascript:alert%281%29)\n\n![remote](https://example.invalid/tracker.png)\n\n[safe](https://example.com)');
  assert.doesNotMatch(untrusted, /<script|<iframe|<img|href="javascript:|src="https:/i);
  assert.match(untrusted, /rel="noopener noreferrer"/);
  assert.match(untrusted, /href="https:\/\/example.com"/);
  for (const url of ['javascript:alert(1)', 'data:text/html,test', '//example.com', '/api/v1/uploads', 'vbscript:test']) assert.equal(documentLink(url), '');
  const doc = { id: 1, title: 'Synthetic MSA', firstPage: 4, lastPage: 5, markdown: 'First\n<!-- page break -->\nSecond', images: [] };
  assert.deepEqual(documentPages(doc).map(page => page.label), ['Page 4', 'Page 5']);
  assert.deepEqual(documentPages({ ...doc, lastPage: 7 }).map(page => page.label), ['Pages 4–7', 'Page break']);
  const pages = [{id: 8, number: 4, markdown: 'Only this page', analysisStatus:'not_started', sections:[], error:null}];
  const viewer = renderToStaticMarkup(createElement(DocumentViewer, { initialMode: 'review', documents: [doc], pages, session: {}, uploadId: 7 }));
  assert.match(viewer, /No section analysis yet/);
  assert.match(viewer, /Original PDF/);
  assert.match(viewer, /role="tabpanel"/);
  assert.match(viewer, /Source page navigation/);
  const completed = renderToStaticMarkup(createElement(DocumentViewer, { initialMode: 'review',documents:[doc], pages:[{...pages[0],analysisStatus:'ready', sections:[{heading:'Term',classification:'renewal',summary:'A factual summary',quote:'Evidence from this page'}]}],session:{},uploadId:7}));
  assert.match(completed,/A factual summary/);
  assert.match(completed,/Evidence from this page/);
  assert.equal(renderToStaticMarkup(createElement(DocumentViewer, { initialMode: 'review', documents: [], pages:[], session: {}, uploadId: 7 })), '');
  console.log('Passed Markdown formatting, unsafe-content handling, page provenance and viewer empty-state checks.');
} finally {
  await server.close();
}
