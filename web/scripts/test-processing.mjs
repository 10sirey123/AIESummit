import assert from 'node:assert/strict';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { createServer } from 'vite';

const server = await createServer({ server: { middlewareMode: true, hmr: false }, appType: 'custom' });
try {
  const { processingStages, processingHeadline, ProcessingPanel, ProcessingSummary, serverTime, clock, isProcessing } = await server.ssrLoadModule('/src/processing-view.tsx');
  const upload = { id: 3, fileName: 'msa.pdf', created: '2026-11-07T19:52:39', pageCount: 0, status: 'queued', error: null };
  const page = (number, analysisStatus, sections = []) => ({ id: 100 + number, number, markdown: `Page ${number}`, analysisStatus, error: null, sections });
  const states = detail => processingStages(detail).map(stage => stage.state);

  // OCR running: only reading is live; the locally counted page total is shown.
  const queued = { upload, documents: [], pages: [] };
  assert.deepEqual(states(queued), ['live', 'waiting', 'waiting', 'waiting']);
  assert.equal(processingHeadline(processingStages(queued, 42)), 'Reading 42 pages');
  assert.equal(processingHeadline(processingStages(queued)), 'Reading the file');

  // Split and per-page analysis run together after OCR.
  const pages = [page(1, 'ready', [{ heading: 'Limitation of Liability', classification: 'liability_cap', summary: 'Caps liability.', quote: 'shall not exceed the fees paid' }]), page(2, 'analyzing'), page(3, 'analyzing')];
  const splitting = { upload: { ...upload, status: 'splitting', pageCount: 3 }, documents: [], pages };
  assert.deepEqual(states(splitting), ['done', 'live', 'live', 'waiting']);
  assert.equal(processingHeadline(processingStages(splitting)), 'Finding documents and analyzing pages');
  assert.equal(isProcessing(splitting), true);

  const panel = renderToStaticMarkup(createElement(ProcessingPanel, { detail: splitting, now: serverTime(upload.created) + 75_000, onOpenPage: () => {} }));
  assert.match(panel, /1:15 elapsed/);
  assert.match(panel, /1 of 3 analyzed/);
  assert.match(panel, /Limitation of Liability/);
  assert.match(panel, /liability cap/);
  assert.match(panel, /Open page 2, being analyzed/);
  assert.doesNotMatch(panel, /left</, 'no time estimate before progress has been observed');

  // Finished: analysis complete, documents found, nothing live.
  const done = { upload: { ...upload, status: 'ready', pageCount: 3 }, documents: [{ id: 1, title: 'MSA', firstPage: 1, lastPage: 3, markdown: '', images: [] }], pages: pages.map(item => ({ ...item, analysisStatus: 'ready' })) };
  assert.deepEqual(states(done), ['done', 'done', 'done', 'done']);
  assert.equal(isProcessing(done), false);
  assert.match(renderToStaticMarkup(createElement(ProcessingSummary, { detail: done })), /3 pages · 1 document · 1 section found/);

  // Analysis disabled is reported as not run, not as stuck.
  const off = { ...done, pages: pages.map(item => ({ ...item, analysisStatus: 'not_started', sections: [] })) };
  assert.equal(processingStages(off)[2].state, 'off');

  // Failure before any page was read.
  const failed = { upload: { ...upload, status: 'failed', error: 'OCR failed.' }, documents: [], pages: [] };
  assert.equal(processingStages(failed)[0].state, 'failed');
  assert.equal(processingHeadline(processingStages(failed)), 'Processing stopped');

  assert.equal(serverTime('2026-11-07T19:52:39'), Date.parse('2026-11-07T19:52:39Z'));
  assert.equal(serverTime('2026-11-07T19:52:39+00:00'), Date.parse('2026-11-07T19:52:39Z'));
  assert.equal(clock(65_400), '1:05');

  // Evidence highlighting: tolerant matching, and a <mark> that can span inline formatting.
  const { locate, joinPieces, spansByOwner } = await server.ssrLoadModule('/src/highlight.ts');
  const { MarkdownBody } = await server.ssrLoadModule('/src/document-viewer.tsx');
  const source = 'Each party’s aggregate liability shall not exceed the **Fees** paid in the twelve (12) months.';
  const range = locate(source, "each party's aggregate liability shall not exceed the Fees paid");
  assert.ok(range, 'matches across Markdown syntax and typographic quotes');
  assert.equal(source.slice(range[0], range[1]), 'Each party’s aggregate liability shall not exceed the **Fees** paid');
  assert.equal(locate(source, 'this sentence is not in the page at all'), undefined);
  const marked = renderToStaticMarkup(createElement(MarkdownBody, { markdown: source, highlight: 'exceed the Fees paid in the twelve' }));
  assert.match(marked, /<mark class="evidence-highlight">exceed the <\/mark><strong><mark class="evidence-highlight">Fees<\/mark><\/strong><mark class="evidence-highlight"> paid in the twelve<\/mark>/);
  assert.doesNotMatch(renderToStaticMarkup(createElement(MarkdownBody, { markdown: source, highlight: 'missing quote text here' })), /<mark/);
  assert.doesNotMatch(renderToStaticMarkup(createElement(MarkdownBody, { markdown: 'Plain <b>text</b>', highlight: 'Plain' })), /<b>/, 'raw HTML stays skipped');
  // PDF text items: pieces on the same line join without spaces; a line end adds one.
  const pdf = joinPieces([{ owner: 0, text: 'aggregate liab' }, { owner: 1, text: 'ility shall', gap: false }, { owner: 2, text: 'not exceed', gap: true }]);
  const pdfRange = locate(pdf.text, 'liability shall not exceed');
  assert.deepEqual([...spansByOwner(pdf.runs, pdfRange)], [[0, [10, 14]], [1, [0, 11]], [2, [0, 10]]]);
  // Review desk: findings rail, tags and the evidence-versus-interpretation detail.
  const { reviewFindings, findingTypes, FindingDetail, ReviewRail, orderFindings } = await server.ssrLoadModule('/src/review-desk.tsx');
  const { DocumentViewer } = await server.ssrLoadModule('/src/document-viewer.tsx');
  const store = await server.ssrLoadModule('/src/playbook-store.ts');
  const rules = store.starterState().playbooks.customer.published.rules;
  const tierFor = (type, quote = '', summary = '') => store.evaluate({ type, quote, summary }, rules).tier;
  assert.equal(tierFor('limitation of liability', 'The limitations shall not apply to indemnity'), 'Walk-away', 'condition overrides the default');
  assert.equal(tierFor('limitation of liability', 'shall not exceed the fees paid'), 'Escalate');
  assert.equal(tierFor('indemnity'), 'Escalate');
  assert.equal(tierFor('termination', 'Customer may terminate for convenience'), 'Meets playbook');
  assert.equal(tierFor('payment'), 'Fallback');
  assert.equal(tierFor('payment', 'a late fee of 1.5% per month'), 'Escalate');
  assert.equal(tierFor('parties'), 'Meets playbook');
  assert.equal(tierFor('force majeure'), 'Not tiered');
  assert.equal(store.evaluate({ type: 'termination', quote: 'for convenience', summary: '' }, store.starterState().playbooks.vendor.published.rules).tier, 'Escalate', 'vendor side differs');
  assert.deepEqual(store.coverageGaps([{ type: 'force majeure', quote: '', summary: '' }, { type: 'payment', quote: '', summary: '' }], rules), [{ type: 'force majeure', count: 1 }]);
  // Draft and publish: reviews use the published rules until the draft is published.
  store.playbookStore.reset();
  store.playbookStore.editDraft('customer', list => list.map(rule => rule.id === 'payment' ? { ...rule, tier: 'Escalate' } : rule));
  assert.ok(store.playbookStore.get().playbooks.customer.draft, 'edit creates a draft');
  assert.equal(store.evaluate({ type: 'payment', quote: '', summary: '' }, store.playbookStore.get().playbooks.customer.published.rules).tier, 'Fallback', 'published rules unchanged');
  store.playbookStore.publish('customer', 'Payment escalates');
  const after = store.playbookStore.get().playbooks.customer;
  assert.equal(after.published.version, 2);
  assert.equal(after.draft, undefined);
  assert.equal(store.evaluate({ type: 'payment', quote: '', summary: '' }, after.published.rules).tier, 'Escalate');
  store.playbookStore.restore('customer', 1);
  assert.equal(store.evaluate({ type: 'payment', quote: '', summary: '' }, store.playbookStore.get().playbooks.customer.draft).tier, 'Fallback', 'restore brings back v1 as a draft');
  store.playbookStore.reset();
  const tierOf = finding => store.evaluate(finding, rules).tier;
  const reviewPages = [
    page(1, 'ready', [{ heading: 'Limitation of Liability', classification: 'liability_cap', summary: 'Caps liability at fees paid.', quote: 'shall not exceed the fees paid' }]),
    page(2, 'ready', [{ heading: 'Cap Exceptions', classification: 'liability_cap', summary: 'Carves indemnity out of the cap.', quote: 'shall not apply to' }, { heading: 'Auto-Renewal', classification: 'renewal_term', summary: 'Renews yearly.', quote: 'automatically renew' }]),
    page(3, 'analyzing'),
  ];
  const found = reviewFindings(reviewPages);
  assert.equal(found.length, 3);
  assert.deepEqual(findingTypes(found), [{ type: 'liability cap', count: 2 }, { type: 'renewal term', count: 1 }]);
  const rail = renderToStaticMarkup(createElement(ReviewRail, { findings: found, decisions: { [found[0].key]: { decision: 'rejected', reason: 'Wrong type' } }, selected: found[1].key, tag: 'liability cap', totalPages: 3, currentPage: 2, analyzing: true, tierOf, playbookName: 'Customer-side MSA v1', onOpenPlaybook() {}, onSelect() {}, onTag() {} }));
  assert.match(rail, /data-tier="Escalate"/, 'rail groups findings by playbook tier');
  assert.match(rail, /Tiers from <strong>Customer-side MSA v1<\/strong>/);
  assert.match(rail, />Edit playbook</);
  const mixed = [{ ...found[0], type: 'parties' }, { ...found[1], type: 'limitation of liability', quote: 'uncapped' }, { ...found[2], type: 'renewal term', quote: 'shall automatically renew' }];
  assert.deepEqual(orderFindings(mixed, tierOf).map(item => item.type), ['limitation of liability', 'renewal term', 'parties']);
  const evaluated = renderToStaticMarkup(createElement(FindingDetail, { finding: found[1], findings: found, evaluation: store.evaluate({ type: 'limitation of liability', quote: 'shall not apply to', summary: '' }, rules), onDecide() {}, onSelect() {}, onNext() {}, onClose() {} }));
  assert.match(evaluated, /class="tier-badge" data-tier="Walk-away">Walk-away/);
  assert.match(evaluated, /Playbook · Limitation of liability/);
  assert.match(evaluated, /Text contains “shall not apply”/);
  assert.match(evaluated, /Fallback wording to propose/);
  assert.match(rail, /1 of 3 reviewed/);
  assert.match(rail, /2 of 2/, 'instance position within the active tag');
  assert.match(rail, /Rejected · Wrong type/);
  assert.match(rail, /Cap Exceptions, page 2/);
  assert.doesNotMatch(rail, /Auto-Renewal/, 'tag filter hides other types');
  const detail = renderToStaticMarkup(createElement(FindingDetail, { finding: found[1], findings: found, located: true, onDecide() {}, onSelect() {}, onNext() {}, onClose() {} }));
  assert.match(detail, /Evidence · what the document says/);
  assert.match(detail, /shall not apply to/);
  assert.match(detail, /AI interpretation · why it was flagged/);
  assert.match(detail, /Highlighted in yellow on page 2/);
  assert.match(detail, /Also tagged liability cap/);
  assert.match(detail, />Accept/);
  const desk = renderToStaticMarkup(createElement(DocumentViewer, { initialMode: 'review', documents: [], pages: reviewPages, session: {}, uploadId: 9 }));
  assert.match(desk, /Findings to review/);
  assert.match(desk, /0 of 3 reviewed/);
  // Tags and linked clauses: section index, cross-references with relationship wording, defined terms.
  const links = await server.ssrLoadModule('/src/contract-links.ts');
  const contract = [
    { ...page(11, 'ready'), markdown: '**11.1 Provider Indemnity.** Provider shall defend Customer.\n\n11.2 Customer Indemnity. Customer shall indemnify Provider for claims about Customer Data.' },
    { ...page(12, 'ready'), markdown: '## 12. LIMITATION OF LIABILITY\n\n12.2 Cap. Aggregate liability shall not exceed the Fees paid in the twelve (12) months.\n\n12.3 Exceptions. The limitations in Section 12.2 shall not apply to Customer’s obligations under Section 11.2.\n\n12.4 Survival. Notwithstanding Section 12.2, Sections 11 and 12 survive termination.' },
    { ...page(1, 'ready'), number: 1, markdown: '1.4 “Customer Data” means all data submitted by Customer.\n\n1.8 “Fees” means the subscription fees stated in each Order Form.\n\nNorthwind Inc. (the “Provider”) and Acme LLC (the “Customer”) agree.' },
  ].sort((a, b) => a.number - b.number);
  const sections = links.sectionIndex(contract);
  assert.equal(sections.byId.get('12.3').page, 12);
  assert.equal(sections.byId.get('11.2').title, 'Customer Indemnity');
  const out = links.clauseLinks(contract, sections, 12, 'The limitations in Section 12.2 shall not apply');
  assert.equal(out.section.id, '12.3');
  assert.deepEqual(out.outgoing.map(link => [link.id, link.relation]), [['12.2', 'Carves out of'], ['11.2', 'Relies on']]);
  const cap = links.clauseLinks(contract, sections, 12, 'Aggregate liability shall not exceed the Fees');
  assert.deepEqual(cap.incoming.map(link => [link.from?.id, link.relation]), [['12.3', 'Has exceptions in'], ['12.4', 'Overridden by']]);
  assert.equal(links.relation('Subject to Section 4, the parties', 11).out, 'Subject to');
  const terms = links.glossary(contract);
  assert.match(terms.get('Fees').definition, /^“Fees” means the subscription fees stated in each Order Form\.$/);
  assert.ok(terms.has('Provider') && terms.has('Customer Data'));
  assert.deepEqual(links.termsIn('Aggregate liability shall not exceed the Fees paid by Customer.', terms).map(term => term.term), ['Customer', 'Fees']);
  const underlined = renderToStaticMarkup(createElement(MarkdownBody, { markdown: 'The Fees and Customer Data apply.', terms: ['Fees', 'Customer Data'], onTerm() {} }));
  assert.match(underlined, /<button type="button" class="defined-term"[^>]*>Fees<\/button>/);
  assert.match(underlined, /<button type="button" class="defined-term"[^>]*>Customer Data<\/button>/);
  const both = renderToStaticMarkup(createElement(MarkdownBody, { markdown: 'Alpha clause here. Beta clause there.', highlight: 'Alpha clause here', extra: ['Beta clause there'] }));
  assert.match(both, /class="evidence-highlight">Alpha clause here/);
  assert.match(both, /class="evidence-secondary">Beta clause there/);
  // Key dates: absolute dates, periods, and derived term end / notice deadline.
  const dates = await server.ssrLoadModule('/src/contract-dates.ts');
  const datePages = [{ ...page(1, 'ready'), markdown: 'This Agreement is entered into as of July 1, 2024 (the “Effective Date”).\n\n2.1 Initial Term. The initial term of this Agreement is three (3) years from the Effective Date.\n\n2.3 Renewal. This Agreement shall automatically renew for successive one (1) year terms unless either party gives notice at least ninety (90) days before the end of the term.\n\n4.1 Payment. Invoices are due within thirty (30) days of receipt.' }];
  const found2 = dates.detectDates(datePages);
  const iso = date => date.toISOString().slice(0, 10);
  assert.deepEqual(found2.dates.map(item => [item.kind, iso(item.date), !!item.calculated]), [['Effective date', '2024-07-01', false], ['Notice', '2027-04-02', true], ['Expiration', '2027-07-01', true]]);
  assert.ok(found2.periods.some(item => item.kind === 'Payment' && item.days === 30 && /thirty \(30\) days/.test(item.label)));
  assert.equal(dates.relative(new Date(Date.UTC(2027, 3, 2)), Date.UTC(2027, 2, 3)), 'in 30 days');
  assert.match(dates.icsFile('Northwind MSA', found2.dates.slice(1, 2)), /DTSTART;VALUE=DATE:20270402[\s\S]*SUMMARY:Notice · Northwind MSA/);
  // Negotiation summary: asks exclude rejected and Meets-playbook findings; memo carries positions and wording.
  const summary = await server.ssrLoadModule('/src/summary-view.tsx');
  const askFindings = [
    { key: 'a', page: 12, index: 0, heading: 'Cap', type: 'limitation of liability', summary: '', quote: 'shall not apply to indemnity' },
    { key: 'b', page: 4, index: 0, heading: 'Fees', type: 'payment', summary: '', quote: 'Invoices are due within thirty days' },
    { key: 'c', page: 1, index: 0, heading: 'Parties', type: 'parties', summary: '', quote: 'Acme and Northwind' },
    { key: 'd', page: 7, index: 0, heading: 'Indemnity', type: 'indemnity', summary: '', quote: 'shall defend' },
  ];
  const asks = summary.negotiationAsks(askFindings, rules, { d: { decision: 'rejected', reason: 'Not relevant' }, a: { decision: 'accepted' } });
  assert.deepEqual(asks.map(ask => [ask.finding.key, ask.evaluation.tier]), [['a', 'Walk-away'], ['b', 'Fallback']]);
  const memo = summary.memoMarkdown('msa.pdf', 'Customer-side MSA v1', asks, 2, 4);
  assert.match(memo, /## Walk-away[\s\S]*\*\*Cap\*\* \(p\.12, limitation of liability, accepted\)/);
  assert.match(memo, /Proposed wording: “…shall not exceed the fees paid or payable in the twenty-four \(24\) months/);
  assert.match(memo, /\*\*Fees\*\* \(p\.4, payment, not yet reviewed\)/);
  const summaryHtml = renderToStaticMarkup(createElement(summary.NegotiationSummary, { fileName: 'msa.pdf', playbook: 'Customer-side MSA v1', findings: askFindings, rules, decisions: {}, onOpen() {} }));
  assert.match(summaryHtml, /Copy as email/);
  assert.match(summaryHtml, /3 of 3 asks selected/, 'with no decisions: cap, indemnity and fees are asks; parties meets playbook');
  // Bulk accept is offered per tier group (not for Walk-away) when two or more findings are unreviewed.
  const bulkRail = renderToStaticMarkup(createElement(ReviewRail, { findings: askFindings, decisions: {}, totalPages: 12, currentPage: 1, analyzing: false, tierOf: finding => store.evaluate(finding, rules).tier, onSelect() {}, onTag() {}, onBulkAccept() {} }));
  assert.doesNotMatch(bulkRail, /Accept all/, 'no tier group has two or more unreviewed findings here');
  const many = [...askFindings, { ...askFindings[2], key: 'e', heading: 'Signatures', type: 'signatures' }];
  assert.match(renderToStaticMarkup(createElement(ReviewRail, { findings: many, decisions: {}, totalPages: 12, currentPage: 1, analyzing: false, tierOf: finding => store.evaluate(finding, rules).tier, onSelect() {}, onTag() {}, onBulkAccept() {} })), /Accept all 2/);
  assert.match(renderToStaticMarkup(createElement(ReviewRail, { findings: askFindings, decisions: Object.fromEntries(askFindings.map(item => [item.key, { decision: 'accepted' }])), totalPages: 12, currentPage: 1, analyzing: false, tierOf: finding => store.evaluate(finding, rules).tier, onSelect() {}, onTag() {}, onOpenSummary() {} })), /Review complete/);
  console.log('Passed processing stage, headline, panel, summary, time-format, evidence-highlight, review-desk, playbook, linked-clause, key-date, negotiation-summary and bulk-accept checks.');
} finally {
  await server.close();
}
