// Locates a model-quoted evidence string inside page text, tolerating Markdown syntax,
// whitespace and typographic-quote differences. Used by both the Markdown and PDF views.

const ignored = /[*_`#>|~\\]/;
const fold = (c: string) => c === '’' || c === '‘' ? "'" : c === '“' || c === '”' ? '"' : c === '–' || c === '—' ? '-' : c.toLowerCase();

/** Normalizes a string; `owner[i]` is the index in the source of normalized char i. */
export function normalize(source: string) {
  let text = '';
  const owner: number[] = [];
  for (let i = 0; i < source.length; i++) {
    const c = source[i];
    if (ignored.test(c)) continue;
    if (/\s/.test(c)) {
      if (text && !text.endsWith(' ')) { text += ' '; owner.push(i); }
      continue;
    }
    text += fold(c); owner.push(i);
  }
  return { text, owner };
}

/** Returns the [start, end) range of `quote` in `source`, in source indices, or undefined. */
export function locate(source: string, quote: string): [number, number] | undefined {
  const haystack = normalize(source);
  const needle = normalize(quote).text.trim();
  if (needle.length < 3) return undefined;
  let start = haystack.text.indexOf(needle);
  let length = needle.length;
  // Fall back to the longest matching prefix (at least 40 chars) when the tail differs slightly.
  for (let cut = needle.length - 10; start < 0 && cut >= 40; cut -= 10) {
    start = haystack.text.indexOf(needle.slice(0, cut));
    length = cut;
  }
  if (start < 0) return undefined;
  const end = Math.min(start + length, haystack.owner.length) - 1;
  return [haystack.owner[start], haystack.owner[end] + 1];
}

type Run = { owner: unknown; offset: number } | null;

/** Builds one searchable string from ordered text pieces, keeping where each char came from. */
export function joinPieces<T>(pieces: { owner: T; text: string; gap?: boolean }[]) {
  let text = '';
  const runs: Run[] = [];
  for (const piece of pieces) {
    const gap = piece.gap ?? true;
    if (gap && text && !/\s$/.test(text) && piece.text && !/^\s/.test(piece.text)) { text += ' '; runs.push(null); }
    for (let i = 0; i < piece.text.length; i++) { text += piece.text[i]; runs.push({ owner: piece.owner, offset: i }); }
  }
  return { text, runs: runs as ({ owner: T; offset: number } | null)[] };
}

/** For a located range, returns each owner with the [from, to) char offsets it contributes. */
export function spansByOwner<T>(runs: ({ owner: T; offset: number } | null)[], [start, end]: [number, number]) {
  const spans = new Map<T, [number, number]>();
  for (let i = start; i < end; i++) {
    const run = runs[i];
    if (!run) continue;
    const span = spans.get(run.owner);
    if (span) span[1] = run.offset + 1; else spans.set(run.owner, [run.offset, run.offset + 1]);
  }
  return spans;
}

/* Minimal hast shapes, so the plugin needs no extra type packages. */
type HastText = { type: 'text'; value: string };
type HastElement = { type: 'element'; tagName: string; properties?: Record<string, unknown>; children: HastNode[] };
type HastNode = HastText | HastElement | { type: string; children?: HastNode[] };

type TextSlot = { node: HastText; parent: { children: HastNode[] }; inside: string[] };
function textSlots(tree: HastNode) {
  const texts: TextSlot[] = [];
  const walk = (node: HastNode, inside: string[]) => {
    for (const child of ('children' in node ? node.children ?? [] : [])) {
      if (child.type === 'text') texts.push({ node: child as HastText, parent: node as { children: HastNode[] }, inside });
      else walk(child, child.type === 'element' ? [...inside, (child as HastElement).tagName] : inside);
    }
  };
  walk(tree, []);
  return texts;
}

function wrapQuote(tree: HastNode, quote: string, className: string) {
  const texts = textSlots(tree).filter(item => !item.inside.includes('mark'));
  const { text, runs } = joinPieces(texts.map(item => ({ owner: item, text: item.node.value })));
  const range = locate(text, quote);
  if (!range) return;
  for (const [item, [from, to]] of spansByOwner(runs, range)) {
    const { node, parent } = item;
    const index = parent.children.indexOf(node);
    if (index < 0) continue;
    const parts: HastNode[] = [];
    if (from > 0) parts.push({ type: 'text', value: node.value.slice(0, from) });
    parts.push({ type: 'element', tagName: 'mark', properties: { className: [className] }, children: [{ type: 'text', value: node.value.slice(from, to) }] });
    if (to < node.value.length) parts.push({ type: 'text', value: node.value.slice(to) });
    parent.children.splice(index, 1, ...parts);
  }
}

/**
 * rehype plugin: wraps the first match of `quote` in <mark class="evidence-highlight"> and each of `extra`
 * in <mark class="evidence-secondary">. Creates text-only elements, never raw HTML.
 */
export function rehypeEvidence({ quote, extra = [] }: { quote?: string; extra?: string[] }) {
  return (tree: HastNode) => {
    if (quote) wrapQuote(tree, quote, 'evidence-highlight');
    for (const other of extra) if (other && other !== quote) wrapQuote(tree, other, 'evidence-secondary');
  };
}

/** rehype plugin: wraps occurrences of defined terms in <dfn data-term>, rendered as buttons by the viewer. */
export function rehypeTerms({ terms = [] }: { terms?: string[] }) {
  return (tree: HastNode) => {
    const usable = terms.filter(term => term.length >= 3).sort((a, b) => b.length - a.length);
    if (!usable.length) return;
    const pattern = new RegExp(`\\b(${usable.map(term => term.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('|')})\\b`, 'g');
    for (const { node, parent, inside } of textSlots(tree)) {
      if (inside.some(tag => tag === 'a' || tag === 'code' || tag === 'dfn' || /^h[1-6]$/.test(tag))) continue;
      const parts: HastNode[] = [];
      let last = 0;
      for (const match of node.value.matchAll(pattern)) {
        const at = match.index ?? 0;
        if (at > last) parts.push({ type: 'text', value: node.value.slice(last, at) });
        parts.push({ type: 'element', tagName: 'dfn', properties: { dataTerm: match[1] }, children: [{ type: 'text', value: match[1] }] });
        last = at + match[1].length;
      }
      if (!parts.length) continue;
      if (last < node.value.length) parts.push({ type: 'text', value: node.value.slice(last) });
      const index = parent.children.indexOf(node);
      if (index >= 0) parent.children.splice(index, 1, ...parts);
    }
  };
}
