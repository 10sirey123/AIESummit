import assert from 'node:assert/strict';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { createServer } from 'vite';

const server = await createServer({ server: { middlewareMode: true, hmr: false }, appType: 'custom' });
const originalNow = Date.now;
let now = 100_000;
const timers = new Map();
let nextTimer = 0;
globalThis.window = {
  setTimeout(callback) { timers.set(++nextTimer, callback); return nextTimer; },
  clearTimeout(id) { timers.delete(id); },
};
Date.now = () => now;
const settled = () => new Promise(resolve => setImmediate(resolve));
try {
  const { loadExtractions, resetExtractions } = await server.ssrLoadModule('/src/extraction-store.ts');
  const { ExtractionStatus } = await server.ssrLoadModule('/src/extraction-view.tsx');
  let calls = 0;
  let status = 'waiting_for_documents';
  let canPoll = true;
  let fail = false;
  const summary = () => ({ ocrStatus: 'ready', splitStatus: 'analyzing', availability: { available: true }, documents: [],
    extraction: { status, canPoll, totalDocuments: 0 } });
  const session = { analysisSummary: async () => { calls++; if (fail) throw new Error('Disconnected'); return summary(); },
    extractions: async () => ({ analyses: [] }) };

  const text = renderToStaticMarkup(createElement(ExtractionStatus, { session, uploadId: 1, documents: [], state: { summary: summary() } }));
  assert.match(text, /OCR complete/);
  assert.doesNotMatch(text, /Waiting for OCR/);
  const reading = summary(); reading.ocrStatus = 'processing';
  assert.match(renderToStaticMarkup(createElement(ExtractionStatus, { session, uploadId: 1, documents: [], state: { summary: reading } })), /Reading source pages/);

  loadExtractions(session, 1);
  await settled();
  assert.equal(timers.size, 1, 'keep polling while splitting is queued or running');
  status = 'analyzing';
  let callback = [...timers.values()][0]; timers.clear(); callback();
  await settled();
  assert.equal(calls, 2, 'poll through the OCR-to-extraction transition');
  assert.equal(timers.size, 1);
  status = 'ready'; canPoll = false;
  callback = [...timers.values()][0]; timers.clear(); callback();
  await settled();
  assert.equal(timers.size, 0, 'stop at completed extraction');
  loadExtractions(session, 1); await settled();
  assert.equal(calls, 3, 'deduplicate recently fetched results');
  now += 6000;
  loadExtractions(session, 1); await settled();
  assert.equal(calls, 4, 'reopening revalidates older cached results');
  fail = true;
  loadExtractions(session, 1, true); await settled();
  fail = false;
  loadExtractions(session, 1); await settled();
  assert.equal(calls, 6, 'reopening recovers after a failed fetch even with cached results');
  resetExtractions();
  assert.equal(timers.size, 0);
  console.log('Passed extraction progress labels, polling transitions, cache revalidation and failed-fetch recovery checks.');
} finally {
  Date.now = originalNow;
  delete globalThis.window;
  await server.close();
}
