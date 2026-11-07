// Detects dates and time periods in OCR page Markdown, and derives term end / notice deadline when the
// contract states an effective date, a term length and a notice period. Text heuristics, labelled as such.
import type { PageResult } from './api';

export type DateKind = 'Effective date' | 'Expiration' | 'Renewal' | 'Notice' | 'Termination' | 'Payment' | 'Cure period' | 'Term' | 'Other';
export type DetectedDate = { key: string; kind: DateKind; date: Date; page: number; sentence: string; calculated?: string };
export type DetectedPeriod = { key: string; kind: DateKind; days: number; label: string; page: number; sentence: string };

const months = ['january', 'february', 'march', 'april', 'may', 'june', 'july', 'august', 'september', 'october', 'november', 'december'];
const monthPattern = '(january|february|march|april|may|june|july|august|september|october|november|december|jan|feb|mar|apr|jun|jul|aug|sep|sept|oct|nov|dec)\\.?';
const absolute = new RegExp([
  `\\b${monthPattern}\\s+(\\d{1,2})(?:st|nd|rd|th)?,?\\s+(\\d{4})\\b`,
  `\\b(\\d{1,2})(?:st|nd|rd|th)?\\s+(?:day\\s+of\\s+)?${monthPattern},?\\s+(\\d{4})\\b`,
  '\\b(\\d{4})-(\\d{2})-(\\d{2})\\b',
  '\\b(\\d{1,2})/(\\d{1,2})/(\\d{4})\\b',
].join('|'), 'gi');
const periodPattern = /\b(?:([a-z-]+)\s*\()?(\d{1,3})\)?\s*(business\s+|calendar\s+)?(days?|months?|years?)\b/gi;

const clean = (text: string) => text.replace(/[*_`#>|]/g, '').replace(/\s+/g, ' ').trim();
const monthIndex = (name: string) => months.findIndex(month => month.startsWith(name.toLowerCase().replace('.', '').slice(0, 3)));

function sentences(markdown: string) {
  return clean(markdown).split(/(?<=[.;])\s+(?=[A-Z(“"0-9])/).filter(item => item.length > 12);
}

function classify(sentence: string): DateKind {
  const text = sentence.toLowerCase();
  if (/effective date|dated as of|made and entered into|as of the date/.test(text)) return 'Effective date';
  if (/renew/.test(text)) return 'Renewal';
  if (/cure|remedy/.test(text)) return 'Cure period';
  if (/notice|notify/.test(text) && /terminat|renew|non-renew/.test(text)) return 'Notice';
  if (/terminat/.test(text)) return 'Termination';
  if (/expire|expiration|end of the term/.test(text)) return 'Expiration';
  if (/pay|invoice|fee|due/.test(text)) return 'Payment';
  if (/\bterm\b/.test(text)) return 'Term';
  return 'Other';
}

/** For a specific date, the cue word closest before it wins (e.g. "effective … shall terminate on <date>" is the end). */
const dateCues: [RegExp, DateKind][] = [
  [/effective|as of|entered into|dated/g, 'Effective date'], [/terminat|expire|expiration|until|through/g, 'Expiration'],
  [/renew/g, 'Renewal'], [/notice|notify/g, 'Notice'], [/pay|invoice|due/g, 'Payment'],
];
function nearestKind(text: string): DateKind {
  let best: { at: number; kind: DateKind } | undefined;
  for (const [pattern, kind] of dateCues) for (const match of text.matchAll(pattern)) {
    const at = (match.index ?? 0) + match[0].length;
    if (!best || at > best.at) best = { at, kind };
  }
  return best?.kind ?? 'Other';
}

function parseDate(match: RegExpMatchArray): Date | undefined {
  const [, m1, d1, y1, d2, m2, y2, iy, im, id, um, ud, uy] = match;
  let year: number, month: number, day: number;
  if (m1) { month = monthIndex(m1); day = +d1; year = +y1; }
  else if (m2) { month = monthIndex(m2); day = +d2; year = +y2; }
  else if (iy) { year = +iy; month = +im - 1; day = +id; }
  else { year = +uy; month = +um - 1; day = +ud; }
  if (month < 0 || month > 11 || day < 1 || day > 31 || year < 1980 || year > 2100) return undefined;
  const date = new Date(Date.UTC(year, month, day));
  return date.getUTCDate() === day ? date : undefined;
}

function toDays(amount: number, unit: string) {
  return /year/i.test(unit) ? amount * 365 : /month/i.test(unit) ? amount * 30 : amount;
}
const addDays = (date: Date, days: number) => new Date(date.getTime() + days * 86_400_000);
const addMonths = (date: Date, count: number) => new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth() + count, date.getUTCDate()));

export function detectDates(pages: PageResult[]) {
  const dates: DetectedDate[] = [];
  const periods: DetectedPeriod[] = [];
  let n = 0;
  for (const page of pages) {
    for (const sentence of sentences(page.markdown)) {
      const kind = classify(sentence);
      for (const match of sentence.matchAll(absolute)) {
        const date = parseDate(match);
        // Classify by the words just before this date, so "effective … and shall terminate on <date>" reads as the end.
        const near = sentence.slice(Math.max(0, (match.index ?? 0) - 70), (match.index ?? 0)).toLowerCase();
        const nearKind = nearestKind(near);
        const dateKind = nearKind !== 'Other' ? nearKind : kind;
        if (date && !dates.some(item => item.date.getTime() === date.getTime() && item.kind === dateKind)) dates.push({ key: `d${n++}`, kind: dateKind, date, page: page.number, sentence });
      }
      if (kind === 'Other' && !/within|after|before|prior|notice|term|period/i.test(sentence)) continue;
      for (const match of sentence.matchAll(periodPattern)) {
        const amount = +match[2];
        if (!amount || amount > 120) continue;
        const label = `${match[1] && /^[a-z-]+$/i.test(match[1]) ? `${match[1]} (${amount})` : amount} ${match[3] ?? ''}${match[4]}`.replace(/\s+/g, ' ');
        if (!periods.some(item => item.sentence === sentence && item.label === label)) periods.push({ key: `p${n++}`, kind, days: toDays(amount, match[4]), label, page: page.number, sentence });
      }
    }
  }
  // Derived dates: term end from effective date + term length; notice deadline from a renewal notice period.
  const effective = dates.find(item => item.kind === 'Effective date');
  const term = periods.find(item => /initial term|term of this agreement|shall (?:continue|remain) in (?:full )?(?:force|effect)/i.test(item.sentence) && !/renew/i.test(item.sentence) && item.days >= 180)
    ?? periods.find(item => item.kind === 'Term' && item.days >= 180)
    ?? periods.find(item => item.kind === 'Renewal' && item.days >= 365);
  if (effective && term) {
    const unitMonths = /year/i.test(term.label) ? Math.round(term.days / 365) * 12 : /month/i.test(term.label) ? Math.round(term.days / 30) : 0;
    const end = unitMonths ? addMonths(effective.date, unitMonths) : addDays(effective.date, term.days);
    dates.push({ key: `d${n++}`, kind: 'Expiration', date: end, page: term.page, sentence: term.sentence, calculated: `Effective date + ${term.label}` });
    const notice = periods.find(item => (item.kind === 'Renewal' || item.kind === 'Notice') && item.days < 365);
    if (notice) dates.push({ key: `d${n++}`, kind: 'Notice', date: addDays(end, -notice.days), page: notice.page, sentence: notice.sentence, calculated: `Term end − ${notice.label}` });
  }
  dates.sort((a, b) => a.date.getTime() - b.date.getTime());
  return { dates, periods };
}

const dayMs = 86_400_000;
export function relative(date: Date, now = Date.now()) {
  const days = Math.round((date.getTime() - now) / dayMs);
  if (days === 0) return 'today';
  const abs = Math.abs(days);
  const span = abs >= 730 ? `${Math.round(abs / 365)} years` : abs >= 60 ? `${Math.round(abs / 30)} months` : `${abs} day${abs === 1 ? '' : 's'}`;
  return days > 0 ? `in ${span}` : `${span} ago`;
}

/** An all-day iCalendar file for the given dates. */
export function icsFile(title: string, items: DetectedDate[]) {
  const stamp = new Date().toISOString().replace(/[-:]/g, '').replace(/\.\d+Z$/, 'Z');
  const day = (date: Date) => date.toISOString().slice(0, 10).replace(/-/g, '');
  const escape = (text: string) => text.replace(/[\\,;]/g, match => `\\${match}`).replace(/\n/g, '\\n');
  const events = items.map(item => [
    'BEGIN:VEVENT', `UID:${item.key}-${day(item.date)}@clauselens`, `DTSTAMP:${stamp}`,
    `DTSTART;VALUE=DATE:${day(item.date)}`, `DTEND;VALUE=DATE:${day(addDays(item.date, 1))}`,
    `SUMMARY:${escape(`${item.kind} · ${title}`)}`,
    `DESCRIPTION:${escape(`${item.calculated ? `Calculated: ${item.calculated}. ` : ''}Page ${item.page}: ${item.sentence.slice(0, 300)}`)}`,
    'END:VEVENT'].join('\r\n'));
  return ['BEGIN:VCALENDAR', 'VERSION:2.0', 'PRODID:-//ClauseLens//Key dates//EN', ...events, 'END:VCALENDAR'].join('\r\n');
}
