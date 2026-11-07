// Detects section structure, cross-references and defined terms from OCR page Markdown.
// Everything here is a text heuristic shown as "detected from the text", never as model output.
import type { PageResult } from './api';
import { locate } from './highlight';

export type SectionRef = { id: string; page: number; title: string; line: string; offset: number };
export type ClauseLink = { key: string; id: string; relation: string; target?: SectionRef; from?: SectionRef; page: number; excerpt: string };
export type Term = { term: string; page: number; definition: string };
export type ClauseLinks = { section?: SectionRef; context: string; outgoing: ClauseLink[]; incoming: ClauseLink[] };

const clean = (text: string) => text.replace(/[*_`#>]/g, '').replace(/\s+/g, ' ').trim();
const lineStart = /^\s{0,3}(?:#{1,6}\s*)?(?:\*\*|__)?\s*(?:(?:section|article|clause)\s+|§\s*)?(\d{1,2}(?:\.\d{1,3}){0,3})\.?\)?(?:\*\*|__)?\s+(?:\*\*|__)?([A-Z][^\n]{0,120})/i;
const refPattern = /(?:\b(?:sections?|articles?|clauses?|paragraphs?)\b|§§?)\s*(\d{1,2}(?:\.\d{1,3}){0,3}(?:\([a-z0-9]{1,4}\))*(?:\s*(?:,|and|or|through|to|&)\s*\d{1,2}(?:\.\d{1,3}){0,3}(?:\([a-z0-9]{1,4}\))*)*)/gi;

/** Every numbered section start, in page order; the first occurrence of an id defines it. */
export function sectionIndex(pages: PageResult[]) {
  const starts: SectionRef[] = [];
  const byId = new Map<string, SectionRef>();
  for (const page of pages) {
    let offset = 0;
    for (const line of page.markdown.split('\n')) {
      const match = lineStart.exec(line);
      if (match) {
        const title = clean(match[2]).split(/(?<=[a-z)])\.\s|:\s/)[0].slice(0, 70);
        const ref = { id: match[1], page: page.number, title, line: clean(line).slice(0, 160), offset };
        starts.push(ref);
        if (!byId.has(ref.id)) byId.set(ref.id, ref);
      }
      offset += line.length + 1;
    }
  }
  return { starts, byId };
}
export type SectionIndex = ReturnType<typeof sectionIndex>;

/** The section that contains a position on a page: the last start at or before it, looking back across pages. */
export function sectionAt(index: SectionIndex, page: number, offset: number) {
  let found: SectionRef | undefined;
  for (const start of index.starts) {
    if (start.page < page || (start.page === page && start.offset <= offset)) found = start; else break;
  }
  return found;
}

export function references(text: string) {
  const found: { ids: string[]; index: number; length: number }[] = [];
  for (const match of text.matchAll(refPattern)) {
    const ids = [...new Set((match[1].match(/\d{1,2}(?:\.\d{1,3}){0,3}/g) ?? []))];
    if (ids.length) found.push({ ids, index: match.index ?? 0, length: match[0].length });
  }
  return found;
}

/** Relationship wording, read from the words just before a reference. */
const cues: { pattern: RegExp; out: string; in: string }[] = [
  { pattern: /notwithstanding/g, out: 'Overrides', in: 'Overridden by' },
  { pattern: /subject to/g, out: 'Subject to', in: 'Governs' },
  { pattern: /shall not apply|except|other than|excluding|exclusive of|limitations? (?:in|of|set forth in)/g, out: 'Carves out of', in: 'Has exceptions in' },
  { pattern: /surviv\w*/g, out: 'Survival includes', in: 'Survives under' },
  { pattern: /pursuant to|in accordance with|as (?:set forth|described|provided) in|\bunder\b|defined in/g, out: 'Relies on', in: 'Relied on by' },
];

/** Relationship wording from the nearest cue phrase before a reference, within its sentence. */
export function relation(context: string, index: number) {
  const window = context.slice(Math.max(0, index - 100), index).toLowerCase();
  const before = window.slice(window.lastIndexOf('. ') + 1);
  let best: { at: number; out: string; in: string } | undefined;
  for (const cue of cues) {
    for (const match of before.matchAll(cue.pattern)) {
      const at = (match.index ?? 0) + match[0].length;
      if (!best || at >= best.at) best = { at, out: cue.out, in: cue.in };
    }
  }
  return best ? { out: best.out, in: best.in } : { out: 'Refers to', in: 'Referred to by' };
}

/** The paragraph around a quote on a page; falls back to the quote itself. */
export function clauseContext(markdown: string, quote: string) {
  const range = locate(markdown, quote);
  if (!range) return { text: quote, start: -1 };
  const start = Math.max(0, markdown.lastIndexOf('\n\n', range[0]) + 1);
  const next = markdown.indexOf('\n\n', range[1]);
  return { text: markdown.slice(start, next < 0 ? markdown.length : next).trim(), start };
}

/** Outgoing references from a clause, and other clauses that reference its section. */
export function clauseLinks(pages: PageResult[], index: SectionIndex, pageNumber: number, quote: string): ClauseLinks {
  const page = pages.find(item => item.number === pageNumber);
  if (!page) return { context: quote, outgoing: [], incoming: [] };
  const { text: context, start } = clauseContext(page.markdown, quote);
  const section = start >= 0 ? sectionAt(index, pageNumber, start + Math.min(context.length, 40)) : undefined;
  const outgoing: ClauseLink[] = [];
  const seen = new Set<string>();
  for (const ref of references(context)) {
    for (const id of ref.ids) {
      if (id === section?.id || seen.has(id)) continue;
      seen.add(id);
      outgoing.push({ key: `out-${id}`, id, relation: relation(context, ref.index).out, target: index.byId.get(id), page: index.byId.get(id)?.page ?? pageNumber, excerpt: clean(context.slice(ref.index, ref.index + ref.length)) });
    }
  }
  const incoming: ClauseLink[] = [];
  if (section) {
    const parent = section.id.includes('.') ? section.id.split('.')[0] : undefined;
    const from = new Set<string>();
    for (const other of pages) {
      for (const ref of references(other.markdown)) {
        if (!ref.ids.some(id => id === section.id || (parent && id === parent))) continue;
        if (other.number === pageNumber && start >= 0 && ref.index >= start && ref.index <= start + context.length) continue;
        const source = sectionAt(index, other.number, ref.index);
        const sourceKey = source?.id ?? `p${other.number}-${ref.index}`;
        if (source?.id === section.id || (parent && source?.id === parent) || from.has(sourceKey)) continue;
        from.add(sourceKey);
        const paragraph = other.markdown.slice(Math.max(0, other.markdown.lastIndexOf('\n\n', ref.index) + 1), ref.index + ref.length + 160);
        incoming.push({ key: `in-${sourceKey}`, id: source?.id ?? '', relation: relation(other.markdown, ref.index).in, from: source, page: other.number, excerpt: clean(paragraph).slice(0, 200) });
      }
    }
  }
  return { section, context, outgoing, incoming };
}

const definitionMeans = /["“]([A-Z][A-Za-z0-9&'’\- ]{1,40})["”]\s+(?:shall\s+)?(?:means?|has the meaning|refers? to|includes?)\b([^\n]{0,280})/g;
const definitionInline = /\((?:the\s+|each,?\s+(?:a\s+)?|collectively,?\s+)?["“]([A-Z][A-Za-z0-9&'’\- ]{1,40})["”]\)/g;

/** Terms the contract defines, with the sentence that defines them (first definition wins). */
export function glossary(pages: PageResult[]) {
  const terms = new Map<string, Term>();
  for (const page of pages) {
    const text = page.markdown.replace(/[*_`]/g, '');
    for (const match of text.matchAll(definitionMeans)) {
      const term = match[1].trim();
      if (terms.has(term)) continue;
      const afterTerm = (match.index ?? 0) + match[0].indexOf(match[1]) + match[1].length + 1;
      const rest = text.slice(afterTerm, afterTerm + 400).split(/(?<=\.)\s/)[0];
      terms.set(term, { term, page: page.number, definition: clean(`“${term}”${rest}`) });
    }
    for (const match of text.matchAll(definitionInline)) {
      const term = match[1].trim();
      if (terms.has(term)) continue;
      const at = match.index ?? 0;
      const sentenceStart = Math.max(text.lastIndexOf('. ', at) + 2, text.lastIndexOf('\n', at) + 1, 0);
      const sentenceEnd = text.indexOf('. ', at + match[0].length);
      terms.set(term, { term, page: page.number, definition: clean(text.slice(sentenceStart, sentenceEnd < 0 ? at + match[0].length + 200 : sentenceEnd + 1)) });
    }
  }
  return terms;
}

/** Defined terms that appear in a piece of text, longest first. */
export function termsIn(text: string, terms: Map<string, Term>) {
  const plain = clean(text);
  return [...terms.values()].filter(item => item.term.length >= 3 && new RegExp(`\\b${escapeRegExp(item.term)}\\b`).test(plain)).sort((a, b) => b.term.length - a.term.length);
}

export const escapeRegExp = (value: string) => value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
