// Playbook: the reviewer's own positions, as ordered rules over finding types and clause text.
// Browser-only until a playbook API exists. Reviews use the published version; edits are a draft.
import { useSyncExternalStore } from 'react';

export const tiers = ['Walk-away', 'Escalate', 'Fallback', 'Meets playbook', 'Not tiered'] as const;
export type Tier = typeof tiers[number];
export const ruleTiers = tiers.filter(tier => tier !== 'Not tiered') as Exclude<Tier, 'Not tiered'>[];
export const signOffs = ['Reviewer', 'Legal counsel', 'General Counsel'] as const;

export type Condition = { id: string; words: string; tier: Tier };
export type Rule = {
  id: string; name: string; types: string; tier: Tier; conditions: Condition[];
  reason: string; preferred: string; fallback: string; signOff: typeof signOffs[number];
};
export type PlaybookVersion = { version: number; at: string; note: string; rules: Rule[] };
export type Playbook = { id: string; name: string; side: 'customer' | 'vendor'; published: PlaybookVersion; history: PlaybookVersion[]; draft?: Rule[] };
export type PlaybookState = { active: string; playbooks: Record<string, Playbook> };
export type Evaluation = { tier: Tier; rule?: Rule; condition?: Condition; matchedWords?: string };
export type Evaluable = { type: string; quote: string; summary: string; heading?: string };

let counter = 0;
export const newId = (prefix: string) => `${prefix}-${Date.now().toString(36)}-${(counter++).toString(36)}`;
const words = (list: string) => list.split(',').map(word => word.trim().toLowerCase()).filter(Boolean);
const rule = (id: string, name: string, types: string, tier: Tier, conditions: [string, Tier][], reason: string, preferred: string, fallback: string, signOff: Rule['signOff']): Rule =>
  ({ id, name, types, tier, conditions: conditions.map(([w, t], i) => ({ id: `${id}-c${i}`, words: w, tier: t })), reason, preferred, fallback, signOff });

function starterRules(side: 'customer' | 'vendor'): Rule[] {
  const customer = side === 'customer';
  return [
    rule('liability', 'Limitation of liability', 'liabilit, limitation', 'Escalate', [['unlimited, uncapped, shall not apply', 'Walk-away']],
      customer ? 'The vendor’s liability should track what we pay, and our own exposure must always be capped.' : 'Our total exposure must be capped at fees received.',
      customer ? 'Mutual cap of at least 12 months of fees, covering all indemnities.' : 'Cap at 12 months of fees received; exclude indirect damages.',
      customer ? '“…shall not exceed the fees paid or payable in the twenty-four (24) months preceding the claim.”' : '“…shall not exceed the fees received in the twelve (12) months preceding the claim.”', 'Legal counsel'),
    rule('indemnity', 'Indemnity', 'indemn', 'Escalate', [['unlimited, uncapped', 'Walk-away']],
      customer ? 'Indemnities shift third-party risk; ours must be narrow and capped.' : 'Indemnities we give must be limited to third-party IP claims.',
      customer ? 'Vendor indemnifies for IP infringement worldwide; our indemnity limited to misuse.' : 'Indemnify only for third-party IP claims, capped.',
      '“…infringe or misappropriate any patent, copyright, trademark or trade secret of a third party.”', 'Legal counsel'),
    rule('noncompete', 'Non-compete and exclusivity', 'compet, exclusiv', 'Walk-away', [],
      'Restrictions on who we can work with limit the business.', 'No non-compete or exclusivity obligations on us.', 'Delete the restriction.', 'General Counsel'),
    rule('termination', 'Termination', 'terminat', 'Escalate', [[customer ? 'convenience' : 'for cause', 'Meets playbook']],
      customer ? 'We need a clean exit without cause.' : 'Customers should not exit at will mid-term.',
      customer ? 'Customer may terminate for convenience on 30–90 days’ notice.' : 'Termination for cause only, with a 30-day cure period.',
      customer ? '“Customer may terminate this Agreement for convenience upon thirty (30) days’ written notice.”' : '“Either party may terminate for material breach not cured within thirty (30) days.”', 'Reviewer'),
    rule('renewal', 'Renewal and notice', 'renew', 'Fallback', [['automatically, auto-renew, ninety, 90', 'Escalate']],
      'Auto-renewals with long notice windows lock us in.', 'No auto-renewal, or notice of 30 days or less.', '“…unless either party gives written notice at least thirty (30) days before the end of the then-current term.”', 'Reviewer'),
    rule('assignment', 'Assignment and change of control', 'assign, change of control', 'Fallback', [['without consent, without the prior', 'Escalate']],
      'We may need to move the contract in a reorganisation or acquisition.', 'Either party may assign to an affiliate or successor without consent.', '“…except to an affiliate or a successor in a merger or sale of substantially all assets.”', 'Reviewer'),
    rule('payment', 'Payment and fees', 'payment, fee, price, invoice', 'Fallback', [['late fee, interest, increase, adjust', 'Escalate']],
      'Price changes and penalties need finance review.', customer ? 'Net 30, no increases during the term.' : 'Net 30; annual increases up to 5%.', '“Fees are fixed for the initial term.”', 'Reviewer'),
    rule('confidentiality', 'Confidentiality', 'confidential', 'Meets playbook', [['perpetual, indefinitely, in perpetuity', 'Fallback']],
      'Standard mutual confidentiality is acceptable.', 'Mutual, 3–5 years after termination; trade secrets as long as protected.', '“…for three (3) years following termination.”', 'Reviewer'),
    rule('law', 'Governing law and disputes', 'governing law, jurisdiction, dispute, arbitrat', 'Fallback', [],
      'Forum affects cost and leverage in a dispute.', 'Our home jurisdiction.', '“…governed by the laws of [our state].”', 'Legal counsel'),
    rule('general', 'Boilerplate and structure', 'part, signature, scope, definition, recital, general, other, notice, entire', 'Meets playbook', [],
      'Structural sections rarely carry risk on their own.', 'No action unless a condition applies.', '', 'Reviewer'),
  ];
}

function starterPlaybook(side: 'customer' | 'vendor'): Playbook {
  const published = { version: 1, at: new Date().toISOString(), note: 'Starter playbook', rules: starterRules(side) };
  return { id: side, name: side === 'customer' ? 'Customer-side MSA' : 'Vendor-side MSA', side, published, history: [published] };
}
export const starterState = (): PlaybookState => ({ active: 'customer', playbooks: { customer: starterPlaybook('customer'), vendor: starterPlaybook('vendor') } });

/** First matching rule wins; within it, the first condition whose words appear in the clause decides. */
export function evaluate(finding: Evaluable, rules: Rule[]): Evaluation {
  const type = finding.type.toLowerCase();
  const text = `${finding.quote} ${finding.summary}`.toLowerCase();
  for (const item of rules) {
    if (!words(item.types).some(word => type.includes(word))) continue;
    for (const condition of item.conditions) {
      const hit = words(condition.words).find(word => text.includes(word));
      if (hit) return { tier: condition.tier, rule: item, condition, matchedWords: hit };
    }
    return { tier: item.tier, rule: item };
  }
  return { tier: 'Not tiered' };
}

/** Types present in findings that no rule matches. */
export function coverageGaps(findings: Evaluable[], rules: Rule[]) {
  const gaps = new Map<string, number>();
  for (const finding of findings) if (!evaluate(finding, rules).rule) gaps.set(finding.type, (gaps.get(finding.type) ?? 0) + 1);
  return [...gaps].sort((a, b) => b[1] - a[1]).map(([type, count]) => ({ type, count }));
}

/* ---- Shared store (localStorage + subscribers) ---- */
const storageKey = 'clauselens-playbooks-v1';
let state: PlaybookState = load();
const listeners = new Set<() => void>();

function load(): PlaybookState {
  try {
    const saved = JSON.parse(localStorage.getItem(storageKey) ?? 'null');
    if (saved?.playbooks?.customer && saved?.playbooks?.vendor) return saved;
  } catch { /* unavailable or corrupt: fall back to starter */ }
  return starterState();
}
function commit(next: PlaybookState) {
  state = next;
  try { localStorage.setItem(storageKey, JSON.stringify(state)); } catch { /* keep in memory */ }
  listeners.forEach(listener => listener());
}
const update = (id: string, change: (playbook: Playbook) => Playbook) => commit({ ...state, playbooks: { ...state.playbooks, [id]: change(state.playbooks[id]) } });

export const playbookStore = {
  get: () => state,
  subscribe(listener: () => void) { listeners.add(listener); return () => listeners.delete(listener); },
  setActive: (id: string) => commit({ ...state, active: id }),
  editDraft: (id: string, change: (rules: Rule[]) => Rule[]) => update(id, playbook => ({ ...playbook, draft: change(structuredClone(playbook.draft ?? playbook.published.rules)) })),
  discardDraft: (id: string) => update(id, playbook => ({ ...playbook, draft: undefined })),
  publish: (id: string, note: string) => update(id, playbook => {
    if (!playbook.draft) return playbook;
    const published = { version: playbook.published.version + 1, at: new Date().toISOString(), note: note.trim() || 'Updated rules', rules: playbook.draft };
    return { ...playbook, published, history: [published, ...playbook.history].slice(0, 30), draft: undefined };
  }),
  restore: (id: string, version: number) => update(id, playbook => {
    const old = playbook.history.find(item => item.version === version);
    return old ? { ...playbook, draft: structuredClone(old.rules) } : playbook;
  }),
  reset: () => commit(starterState()),
  importJson(text: string) {
    const parsed = JSON.parse(text) as PlaybookState;
    if (!parsed?.playbooks || !Object.values(parsed.playbooks).every(item => Array.isArray(item?.published?.rules))) throw new Error('This file is not a ClauseLens playbook export.');
    commit({ active: parsed.playbooks[parsed.active] ? parsed.active : Object.keys(parsed.playbooks)[0], playbooks: parsed.playbooks });
  },
  exportJson: () => JSON.stringify(state, null, 2),
};

export function usePlaybooks() {
  return useSyncExternalStore(playbookStore.subscribe, playbookStore.get, playbookStore.get);
}

/** The published rules of the active playbook: what reviews use. */
export function useActiveRules() {
  const current = usePlaybooks();
  const playbook = current.playbooks[current.active];
  return { playbook, rules: playbook.published.rules };
}
