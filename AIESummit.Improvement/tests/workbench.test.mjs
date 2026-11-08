import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';

const source = await readFile(new URL('../wwwroot/app.js', import.meta.url), 'utf8');
const markup = await readFile(new URL('../wwwroot/index.html', import.meta.url), 'utf8');

function fixture(fetch) {
  const elements = new Map();
  function element(key) {
    if (!elements.has(key)) elements.set(key, {
      innerHTML: '', textContent: '', value: '', hidden: false, disabled: false,
      classList: { toggle() {} }, addEventListener() {}, focus() {},
      querySelectorAll: () => [], hasChildNodes() { return !!this.innerHTML; }
    });
    return elements.get(key);
  }
  const context = vm.createContext({
    fetch, Intl, Date, console,
    document: { getElementById: element, querySelector: element, querySelectorAll: () => [] },
    workflowEdges: () => [], renderProgression() {}
  });
  const executable = source.replace(/^import .*;\r?\n/gm, '').replace(/await refresh\(\);\s*setInterval[\s\S]*$/, '');
  vm.runInContext(executable + '\nthis.ui = { state, selectAttempt, refresh, renderMain, renderMatrix, updateMarkup };', context);
  context.ui.state.dashboard = { attempts: [], runs: [], documents: [], graph: { nodes: [{ key: 'current-workflow' }] } };
  return { ...context.ui, element };
}
const response = body => ({ ok: true, text: async () => JSON.stringify(body) });

test('HTML provides every directly bound control and unique IDs', () => {
  const ids = [...markup.matchAll(/\bid="([^"]+)"/g)].map(match => match[1]);
  assert.equal(new Set(ids).size, ids.length);
  for (const [, id] of source.matchAll(/\$\('([^']+)'\)\.addEventListener/g)) assert.ok(ids.includes(id), id);
});

test('loading another attempt keeps the prior identity until its details arrive', async () => {
  let resolve;
  const ui = fixture(() => new Promise(done => { resolve = done; }));
  ui.state.selected = 'old'; ui.state.attempt = { id: 'old', status: 'awaiting_approval' };
  const pending = ui.selectAttempt('next');
  assert.equal(ui.state.selected, 'old');
  assert.equal(ui.state.attempt.id, 'old');
  assert.equal(ui.element('new-attempt').disabled, true);
  resolve(response({ id: 'next', status: 'rejected' }));
  await pending;
  assert.equal(ui.state.selected, 'next');
  assert.equal(ui.state.attempt.id, 'next');
  assert.equal(ui.state.loading, false);
});

test('failed selection preserves the previous attempt and restores controls', async () => {
  const ui = fixture(async () => { throw new Error('Connection unavailable'); });
  ui.state.selected = 'old'; ui.state.attempt = { id: 'old' };
  await ui.selectAttempt('next');
  assert.equal(ui.state.selected, 'old');
  assert.equal(ui.state.attempt.id, 'old');
  assert.equal(ui.element('attempt-select').disabled, false);
  assert.match(ui.element('error').textContent, /Connection unavailable/);
});

test('a delayed poll cannot replace a newly selected attempt', async () => {
  let resolvePoll;
  const ui = fixture(path => path === '/api/dashboard'
    ? new Promise(done => { resolvePoll = done; })
    : Promise.resolve(response({ id: 'next', status: 'rejected' })));
  const polling = ui.refresh();
  await ui.selectAttempt('next');
  resolvePoll(response({ attempts: [], runs: [], documents: [] }));
  await polling;
  assert.equal(ui.state.selected, 'next');
  assert.equal(ui.state.attempt.id, 'next');
});

test('historical results never borrow the current workflow', () => {
  const ui = fixture();
  ui.state.selectedRun = 'historical';
  ui.state.dashboard.runs = [{ id: 'historical', summary: {} }];
  ui.renderMain();
  assert.doesNotMatch(ui.element('graph').innerHTML, /current-workflow/);
  assert.match(ui.element('workflow-note').textContent, /No workflow snapshot/);
});

test('unchanged polling preserves control markup and loading states recover', () => {
  const ui = fixture();
  const node = ui.element('matrix');
  let writes = 0, html = '';
  Object.defineProperty(node, 'innerHTML', { get: () => html, set: value => { html = value; writes++; } });
  ui.updateMarkup(node, '<table>results</table>');
  ui.updateMarkup(node, '<table>results</table>');
  assert.equal(writes, 1);
  ui.updateMarkup(node, '<p>Loading</p>');
  ui.updateMarkup(node, '<table>results</table>');
  assert.equal(writes, 3);
  assert.match(html, /results/);
});
