import { Ban, BellRing, CalendarCheck, CalendarX, ClipboardCheck, DoorOpen, RefreshCw, ShieldAlert, ShieldCheck, Tag, Umbrella, type LucideIcon } from 'lucide-react';
import type { DocumentAnalysis, ExtractionFinding, ExtractionResults, ExtractionType, ExtractionValues, TargetOutcome } from './api';

/** Visual identity of each extraction target: the same icon and color everywhere (rail, contract list, compare matrix). */
export type TargetMeta = { tag: string; name: string; short: string; icon: LucideIcon; color: string; order: number; primary: string[] };

const known: Omit<TargetMeta, 'order'>[] = [
  { tag: 'agreement-effective-date', name: 'Effective date', short: 'Effective', icon: CalendarCheck, color: '#5ec8ff', primary: ['date', 'dateExpression'] },
  { tag: 'agreement-expiration', name: 'Initial expiration', short: 'Expiration', icon: CalendarX, color: '#ff8a65', primary: ['date', 'dateExpression', 'term'] },
  { tag: 'renewal-term', name: 'Renewal mechanism', short: 'Renewal', icon: RefreshCw, color: '#66d9a0', primary: ['mechanism', 'term'] },
  { tag: 'non-renewal-notice', name: 'Non-renewal notice', short: 'Notice', icon: BellRing, color: '#f5c451', primary: ['term', 'conditions'] },
  { tag: 'termination-convenience', name: 'Termination for convenience', short: 'Termination', icon: DoorOpen, color: '#a78bfa', primary: ['term', 'mechanism'] },
  { tag: 'non-compete', name: 'Non-compete', short: 'Non-compete', icon: Ban, color: '#f472b6', primary: ['term', 'geography', 'activity'] },
  { tag: 'liability-cap', name: 'Liability cap', short: 'Liability cap', icon: ShieldCheck, color: '#60a5fa', primary: ['money', 'formula'] },
  { tag: 'uncapped-liability', name: 'Uncapped liability', short: 'Uncapped', icon: ShieldAlert, color: '#f87171', primary: ['activity', 'exclusions'] },
  { tag: 'audit-rights', name: 'Audit rights', short: 'Audit', icon: ClipboardCheck, color: '#c6e05a', primary: ['activity', 'term', 'mechanism'] },
  { tag: 'insurance-obligations', name: 'Insurance obligations', short: 'Insurance', icon: Umbrella, color: '#2dd4bf', primary: ['money', 'activity'] },
];
const byTag = new Map(known.map((item, order) => [item.tag, { ...item, order }]));

export function targetMeta(tag: string, name?: string): TargetMeta {
  return byTag.get(tag) ?? { tag, name: name ?? tag.replaceAll('-', ' '), short: name ?? tag, icon: Tag, color: '#9ca3af', order: 100, primary: [] };
}

/** Catalog order for display: known targets first in their fixed order, then any new ones alphabetically. */
export function orderedTargets(types: Pick<ExtractionType, 'tag' | 'name'>[]) {
  return types.map(type => ({ ...targetMeta(type.tag, type.name), name: byTag.has(type.tag) ? targetMeta(type.tag).name : type.name }))
    .sort((a, b) => a.order - b.order || a.name.localeCompare(b.name));
}

export const allKnownTargets = () => known.map(item => targetMeta(item.tag));

export const outcomeLabel: Record<TargetOutcome, string> = {
  detected: 'Found', not_found_in_reviewed_text: 'Not found in reviewed text', uncertain: 'Needs review', not_applicable: 'Not applicable', not_analyzed: 'Not analyzed',
};
export const outcomeShort: Record<TargetOutcome, string> = {
  detected: 'Found', not_found_in_reviewed_text: 'Not found', uncertain: 'Uncertain', not_applicable: 'N/A', not_analyzed: 'Pending',
};

const unitNames: Record<string, [string, string]> = {
  calendar_days: ['calendar day', 'calendar days'], business_days: ['business day', 'business days'], days: ['day', 'days'], months: ['month', 'months'], years: ['year', 'years'],
};

/** A calendar date shown without timezone conversion ("2027-12-31" stays 31 Dec 2027 everywhere). */
export function formatDate(value: string) {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (!match) return value;
  const date = new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3])));
  return new Intl.DateTimeFormat(undefined, { day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' }).format(date);
}

export function formatTerm(duration?: number, unit?: string) {
  if (duration === undefined || duration === null) return '';
  const names = unit ? unitNames[unit] : undefined;
  return `${duration} ${names ? names[duration === 1 ? 0 : 1] : unit ?? ''}`.trim();
}

export function formatMoney(amount?: number, currency?: string) {
  if (amount === undefined || amount === null) return '';
  if (currency && /^[A-Z]{3}$/.test(currency)) {
    try { return new Intl.NumberFormat(undefined, { style: 'currency', currency, maximumFractionDigits: 2 }).format(amount); } catch { /* unknown code */ }
  }
  return `${new Intl.NumberFormat().format(amount)}${currency ? ` ${currency}` : ''}`;
}

/** One displayed structured value and the raw fields it comes from (so evidence links can name them). */
export type ValueRow = { key: string; label: string; text: string; fields: string[] };

export function valueRows(values: ExtractionValues): ValueRow[] {
  const rows: ValueRow[] = [];
  const used = new Set<string>();
  const add = (key: string, label: string, text: string, fields: string[]) => {
    fields.forEach(field => used.add(field));
    if (text) rows.push({ key, label, text, fields });
  };
  const str = (key: string) => typeof values[key] === 'string' ? values[key] as string : '';
  add('date', 'Date', values.date ? formatDate(values.date) : '', ['date']);
  add('dateExpression', 'Date as written', str('dateExpression'), ['dateExpression']);
  add('term', 'Term', formatTerm(values.duration, values.unit), ['duration', 'unit']);
  add('money', 'Amount', formatMoney(values.amount, values.currency), ['amount', 'currency']);
  add('formula', 'Formula', str('formula'), ['formula']);
  add('mechanism', 'Mechanism', str('mechanism'), ['mechanism']);
  add('activity', 'Scope / activity', str('activity'), ['activity']);
  add('geography', 'Geography', str('geography'), ['geography']);
  add('exclusions', 'Exclusions', str('exclusions'), ['exclusions']);
  add('party', 'Party', str('party'), ['party']);
  add('conditions', 'Conditions', str('conditions'), ['conditions']);
  for (const [key, value] of Object.entries(values)) {
    if (used.has(key) || value === null || value === undefined || value === '') continue;
    rows.push({ key, label: key, text: typeof value === 'object' ? JSON.stringify(value) : String(value), fields: [key] });
  }
  return rows;
}

/** The short fact for a card or a comparison cell, e.g. "31 Dec 2027" or "90 calendar days". */
export function headline(tag: string, finding: ExtractionFinding) {
  const rows = valueRows(finding.values);
  const primary = targetMeta(tag).primary;
  const row = primary.map(key => rows.find(item => item.key === key)).find(Boolean) ?? rows.find(item => item.key !== 'party' && item.key !== 'conditions');
  const text = row?.text ?? finding.title;
  return text.length > 90 ? `${text.slice(0, 88)}…` : text;
}

/** Per current document: the latest attempt and the analysis to display (the latest, or the last successful one if the latest failed). */
export type DocumentSelection = { documentId: number; latest: DocumentAnalysis; shown?: DocumentAnalysis; fallback: boolean; attempts: number };

export function selectAnalyses(results?: ExtractionResults): DocumentSelection[] {
  if (!results) return [];
  const groups = new Map<number, DocumentAnalysis[]>();
  for (const analysis of results.analyses) if (analysis.isCurrent) groups.set(analysis.documentId, [...(groups.get(analysis.documentId) ?? []), analysis]);
  return [...groups].map(([documentId, list]) => {
    const sorted = [...list].sort((a, b) => b.id - a.id);
    const latest = sorted[0];
    const success = sorted.find(item => item.status === 'ready');
    const shown = latest.status === 'ready' ? latest : success;
    return { documentId, latest, shown, fallback: !!shown && shown !== latest, attempts: sorted.length };
  });
}

/** Detected targets across an upload's displayed analyses, with their findings (used by the list chips and the compare view). */
export type UploadTargets = Map<string, { outcome: TargetOutcome; findings: ExtractionFinding[]; explanation: string | null }>;

const outcomeRank: Record<TargetOutcome, number> = { detected: 0, uncertain: 1, not_applicable: 2, not_found_in_reviewed_text: 3, not_analyzed: 4 };

export function uploadTargets(results?: ExtractionResults): UploadTargets {
  const map: UploadTargets = new Map();
  for (const selection of selectAnalyses(results)) {
    for (const target of selection.shown?.targets ?? []) {
      const current = map.get(target.tag);
      if (!current) { map.set(target.tag, { outcome: target.outcome, findings: [...target.extractions], explanation: target.explanation }); continue; }
      current.findings.push(...target.extractions);
      if (outcomeRank[target.outcome] < outcomeRank[current.outcome]) { current.outcome = target.outcome; current.explanation = target.explanation; }
    }
  }
  return map;
}
