import { useEffect, useState } from 'react';
import { ArrowLeft, Check, Link2, X } from 'lucide-react';
import type { PageResult } from './api';
import type { ClauseLink, ClauseLinks, SectionRef, Term } from './contract-links';
import { tiers, type Evaluation, type Tier } from './playbook-store';

const sectionName = (ref?: SectionRef, id?: string) => ref ? `§${ref.id}${ref.title ? ` ${ref.title}` : ''}` : id ? `§${id}` : 'Unnumbered text';

/** Cross-references detected from the text, in both directions. */
export function LinksBlock({ links, onFollow }: { links?: ClauseLinks; onFollow: (link: ClauseLink) => void }) {
  if (!links || (!links.outgoing.length && !links.incoming.length)) return <div className="detail-block">
    <p className="detail-label">Linked clauses</p>
    <p className="detail-muted">{links?.section ? `No cross-references to or from ${sectionName(links.section)} were detected.` : 'No cross-references were detected in this clause.'}</p>
  </div>;
  return <div className="detail-block">
    <p className="detail-label">Linked clauses · detected from the text</p>
    <ul className="link-list">
      {[...links.outgoing, ...links.incoming].map(link => {
        const ref = link.target ?? link.from;
        const missing = !!link.target === false && !link.from && link.key.startsWith('out-');
        return <li key={link.key}>
          <button type="button" className="link-row" disabled={missing} onClick={() => onFollow(link)} title={link.excerpt}>
            <Link2 size={14} aria-hidden="true" />
            <span className="link-copy">
              <span className="link-relation">{link.relation}</span>
              <strong>{sectionName(ref, link.id)}</strong>
              <span className="link-page">{missing ? 'Not found in this document' : `p.${link.page}`}</span>
            </span>
          </button>
        </li>;
      })}
    </ul>
  </div>;
}

/** Defined terms used in a clause, with their definitions. */
export function TermsBlock({ terms, onGo }: { terms?: Term[]; onGo: (term: Term) => void }) {
  if (!terms?.length) return null;
  return <div className="detail-block">
    <p className="detail-label">Defined terms in this clause</p>
    <div className="term-list">{terms.slice(0, 8).map(term => <details key={term.term}>
      <summary>{term.term}</summary>
      <p>{term.definition}</p>
      <button type="button" className="text-button" onClick={() => onGo(term)}>Go to definition · p.{term.page}</button>
    </details>)}</div>
  </div>;
}

/** Shown after following a link: the target clause, its own links, and a way back. */
export function LinkedClausePanel({ title, page, context, relation, origin, links, terms, located, onBack, onFollow, onTermGo }: {
  title: string; page: number; context: string; relation?: string; origin?: string; links?: ClauseLinks; terms?: Term[]; located?: boolean;
  onBack?: () => void; onFollow: (link: ClauseLink) => void; onTermGo: (term: Term) => void;
}) {
  return <div className="finding-detail">
    <div className="finding-detail-head">
      <p className="detail-eyebrow">Linked clause · p.{page}</p>
      {onBack && <button type="button" className="button secondary small" onClick={onBack}><ArrowLeft size={14} aria-hidden="true" /> Back</button>}
    </div>
    <h3>{title}</h3>
    {relation && origin && <p className="detail-muted">{origin} → <strong>{relation}</strong> this clause</p>}
    <div className="detail-block">
      <p className="detail-label">Clause text</p>
      <blockquote className="detail-quote">{context.length > 700 ? `${context.slice(0, 700)}…` : context}</blockquote>
      <p className="evidence-status" role="status">{located === undefined ? 'Finding it on the page…' : located ? `Highlighted in yellow on page ${page}` : 'The clause start could not be located exactly on the page.'}</p>
    </div>
    <LinksBlock links={links} onFollow={onFollow} />
    <TermsBlock terms={terms} onGo={onTermGo} />
  </div>;
}

/** One AI finding: a page section with its exact quoted evidence. */
export type ReviewFinding = { key: string; page: number; index: number; heading: string; type: string; summary: string; quote: string };
export type Decision = { decision: 'accepted' | 'rejected'; reason?: string };

export const typeLabel = (classification: string) => classification.replaceAll('_', ' ');
export const rejectReasons = ['Wrong clause', 'Wrong type', 'Quote is incomplete', 'Not relevant'];

const plainHeading = (heading: string) => heading.replace(/[*_#`]+/g, '').replace(/\s+/g, ' ').trim() || 'Untitled section';

export function reviewFindings(pages: PageResult[]): ReviewFinding[] {
  return pages.filter(page => page.analysisStatus === 'ready').flatMap(page => page.sections.map((section, index) => ({
    key: `${page.id}-${index}`, page: page.number, index, heading: plainHeading(section.heading), type: typeLabel(section.classification), summary: section.summary, quote: section.quote,
  })));
}

export { tiers, type Tier } from './playbook-store';

export function TierBadge({ tier }: { tier: Tier }) {
  return <span className="tier-badge" data-tier={tier}>{tier}</span>;
}

/** Types ordered by how often they occur, most frequent first. */
export function findingTypes(findings: ReviewFinding[]) {
  const counts = new Map<string, number>();
  findings.forEach(finding => counts.set(finding.type, (counts.get(finding.type) ?? 0) + 1));
  return [...counts].sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0])).map(([type, count]) => ({ type, count }));
}

/** Review decisions have no backend yet; they stay in this browser, keyed by upload. */
export function useDecisions(uploadId: number) {
  const storageKey = `clauselens-review-${uploadId}`;
  const [decisions, setDecisions] = useState<Record<string, Decision>>(() => {
    try { return JSON.parse(localStorage.getItem(storageKey) ?? '{}'); } catch { return {}; }
  });
  useEffect(() => {
    try { localStorage.setItem(storageKey, JSON.stringify(decisions)); } catch { /* storage unavailable: keep in memory */ }
  }, [storageKey, decisions]);
  const decide = (key: string, decision?: Decision) => setDecisions(current => {
    const next = { ...current };
    if (decision) next[key] = decision; else delete next[key];
    return next;
  });
  /** Set or clear several decisions in one update (bulk accept and its undo). */
  const decideMany = (keys: string[], decision?: Decision) => setDecisions(current => {
    const next = { ...current };
    for (const key of keys) if (decision) next[key] = decision; else delete next[key];
    return next;
  });
  return [decisions, decide, decideMany] as const;
}

/** Findings in review order: by playbook tier, then by type frequency, then page. */
export function orderFindings(findings: ReviewFinding[], tierOf: (finding: ReviewFinding) => Tier, tag?: string) {
  const typeRank = new Map(findingTypes(findings).map((item, index) => [item.type, index]));
  return findings.filter(item => !tag || item.type === tag).sort((a, b) =>
    tiers.indexOf(tierOf(a)) - tiers.indexOf(tierOf(b)) || typeRank.get(a.type)! - typeRank.get(b.type)! || a.page - b.page || a.index - b.index);
}

export function ReviewRail({ findings, decisions, selected, tag, totalPages, currentPage, analyzing, tierOf, playbookName, onOpenPlaybook, onSelect, onTag, onBulkAccept, bulkNotice, onOpenSummary }: {
  findings: ReviewFinding[]; decisions: Record<string, Decision>; selected?: string; tag?: string; totalPages: number; currentPage: number; analyzing: boolean;
  tierOf: (finding: ReviewFinding) => Tier; playbookName?: string; onOpenPlaybook?: () => void;
  onSelect: (finding: ReviewFinding) => void; onTag: (tag?: string) => void;
  /** Accept every listed finding at once; returns nothing, the caller offers undo. */
  onBulkAccept?: (keys: string[], label: string) => void; bulkNotice?: { text: string; undo: () => void }; onOpenSummary?: () => void;
}) {
  const [query, setQuery] = useState('');
  const [allTags, setAllTags] = useState(false);
  const types = findingTypes(findings);
  const reviewed = findings.filter(finding => decisions[finding.key]).length;
  const needle = query.trim().toLowerCase();
  const visible = orderFindings(findings, tierOf, tag).filter(finding => !needle || `${finding.heading} ${finding.type} ${finding.quote} ${finding.summary}`.toLowerCase().includes(needle));
  // Tag rows: most serious tier first, then most frequent; each knows where it appears.
  const tagRows = types.map(item => {
    const members = findings.filter(finding => finding.type === item.type);
    const worst = tiers[Math.min(...members.map(finding => tiers.indexOf(tierOf(finding))))];
    return { ...item, worst, pages: [...new Set(members.map(finding => finding.page))] };
  }).sort((a, b) => tiers.indexOf(a.worst) - tiers.indexOf(b.worst) || b.count - a.count);
  const shownTags = allTags ? tagRows : tagRows.filter((row, index) => index < 6 || row.type === tag);
  const groups = tiers.map(tier => ({ type: tier, items: visible.filter(finding => tierOf(finding) === tier) })).filter(group => group.items.length);
  const instances = tag ? [...visible].sort((a, b) => a.page - b.page || a.index - b.index) : [];
  const at = instances.findIndex(finding => finding.key === selected);
  const position = (page: number) => `${totalPages > 1 ? ((page - 1) / (totalPages - 1)) * 100 : 50}%`;

  return <aside className="review-rail" aria-label="Findings to review">
    <div className="review-progress">
      <div className="review-progress-label"><h3>Findings</h3><span>{reviewed} of {findings.length} reviewed</span></div>
      <div className="review-progress-bar" role="progressbar" aria-label="Findings reviewed" aria-valuemin={0} aria-valuemax={findings.length} aria-valuenow={reviewed}>
        <span style={{ width: findings.length ? `${(reviewed / findings.length) * 100}%` : 0 }} />
      </div>
      {playbookName && <p className="playbook-toggle-row"><span>Tiers from <strong>{playbookName}</strong></span>
        {onOpenPlaybook && <button type="button" className="text-button playbook-toggle" onClick={onOpenPlaybook}>Edit playbook</button>}</p>}
    </div>
    {bulkNotice && <p className="bulk-notice" role="status">{bulkNotice.text} <button type="button" className="text-button" onClick={bulkNotice.undo}>Undo</button></p>}
    {findings.length > 0 && reviewed === findings.length && <div className="review-complete">
      <strong>Review complete</strong>
      <span>All {findings.length} findings have a decision.</span>
      {onOpenSummary && <button type="button" className="button primary small" onClick={onOpenSummary}>Open negotiation summary</button>}
    </div>}

    {tagRows.length > 0 && <div className="tag-block">
      <p className="rail-label"><span>Tags</span>{tag ? <button type="button" className="text-button tag-reset" onClick={() => onTag(undefined)}>Show all findings</button> : <span>{tagRows.length}</span>}</p>
      <ul className="tag-rows">
        {shownTags.map(row => {
          const active = tag === row.type;
          return <li key={row.type} data-active={active || undefined}>
            <button type="button" className="tag-row" aria-pressed={active} data-tier={row.worst} onClick={() => onTag(active ? undefined : row.type)}
              title={`${row.count} finding${row.count === 1 ? '' : 's'} · most serious: ${row.worst}`}>
              <span className="tag-row-top">
                <span className="tag-dot" aria-hidden="true" />
                <span className="tag-name">{row.type}</span>
                <span className="tag-count">{row.count}</span>
              </span>
              <span className="tag-spark" aria-hidden="true">
                {row.pages.map(pageNumber => <span key={pageNumber} style={{ left: position(pageNumber) }} />)}
              </span>
              <span className="visually-hidden">, most serious tier {row.worst}, on page{row.pages.length === 1 ? '' : 's'} {row.pages.join(', ')}</span>
            </button>
            {active && <div className="tag-instances">
              <div className="tag-nav">
                <span>{at >= 0 ? `${at + 1} of ${instances.length}` : `${instances.length} instance${instances.length === 1 ? '' : 's'}`}</span>
                <button type="button" aria-label={`Previous ${tag}`} onClick={() => onSelect(instances[(at - 1 + instances.length) % instances.length])}>←</button>
                <button type="button" aria-label={`Next ${tag}`} onClick={() => onSelect(instances[(at + 1) % instances.length])}>→</button>
              </div>
              <div className="tag-map" aria-label={`Where ${tag} appears, page 1 to ${totalPages}`}>
                <span className="tag-map-here" style={{ left: position(currentPage) }} title={`You are on page ${currentPage}`} />
                {instances.map(finding => <button key={finding.key} type="button" className="tag-map-tick" data-selected={finding.key === selected || undefined}
                  style={{ left: position(finding.page) }} aria-label={`${finding.heading}, page ${finding.page}`} title={`p.${finding.page} · ${finding.heading}`} onClick={() => onSelect(finding)} />)}
              </div>
              <div className="tag-map-scale"><span>p.1</span><span>p.{totalPages}</span></div>
            </div>}
          </li>;
        })}
      </ul>
      {tagRows.length > 6 && <button type="button" className="text-button tag-more" onClick={() => setAllTags(value => !value)}>{allTags ? 'Show fewer tags' : `Show all ${tagRows.length} tags`}</button>}
    </div>}

    {findings.length > 0 && <label className="finding-filter">
      <span className="visually-hidden">Filter findings</span>
      <input type="search" value={query} onChange={event => setQuery(event.target.value)} placeholder="Filter findings by word…" />
    </label>}
    {needle && !visible.length && <p className="rail-empty">No findings match “{query}”.</p>}

    {!findings.length && <p className="rail-empty">{analyzing ? 'Findings appear here as pages are analyzed.' : 'No findings yet. Run page analysis to identify the sections to review.'}</p>}
    {groups.map(group => <div className="finding-group" key={group.type} data-tier={group.type}>
      <div className="tier-head">
        <p className="rail-label tier-label"><span>{group.type}</span><span>{group.items.length}</span></p>
        {onBulkAccept && group.type !== 'Walk-away' && (() => {
          const pending = group.items.filter(finding => !decisions[finding.key]);
          return pending.length > 1 ? <button type="button" className="text-button bulk-accept" onClick={() => onBulkAccept(pending.map(finding => finding.key), `${pending.length} ${group.type}`)}
            title={`Accept the ${pending.length} unreviewed ${group.type} findings shown`}>Accept all {pending.length}</button> : null;
        })()}
      </div>
      <ul>
        {group.items.map(finding => {
          const decision = decisions[finding.key];
          return <li key={finding.key}>
            <button type="button" className="rail-finding" aria-current={finding.key === selected || undefined} data-decision={decision?.decision} onClick={() => onSelect(finding)}>
              <span className="rail-finding-top"><strong>{finding.heading}</strong><span>p.{finding.page}</span></span>
              <span className="rail-finding-type">{finding.type}</span>
              {decision && <span className="rail-finding-decision">{decision.decision === 'accepted' ? 'Accepted' : `Rejected${decision.reason ? ` · ${decision.reason}` : ''}`}</span>}
            </button>
          </li>;
        })}
      </ul>
    </div>)}
  </aside>;
}

export function FindingDetail({ finding, findings, decision, located, rejectRequest, links, terms, evaluation, onDecide, onSelect, onNext, onClose, onFollow, onTermGo }: {
  finding: ReviewFinding; findings: ReviewFinding[]; decision?: Decision; located?: boolean; rejectRequest?: number;
  links?: ClauseLinks; terms?: Term[]; evaluation?: Evaluation;
  onDecide: (decision?: Decision) => void; onSelect: (finding: ReviewFinding) => void; onNext: () => void; onClose: () => void;
  onFollow?: (link: ClauseLink) => void; onTermGo?: (term: Term) => void;
}) {
  const [rejecting, setRejecting] = useState(false);
  useEffect(() => setRejecting(false), [finding.key]);
  useEffect(() => { if (rejectRequest && !decision) setRejecting(true); }, [rejectRequest]);
  const others = findings.filter(item => item.type === finding.type && item.key !== finding.key);

  return <div className="finding-detail">
    <div className="finding-detail-head">
      {evaluation ? <TierBadge tier={evaluation.tier} /> : <p className="detail-eyebrow">p.{finding.page} · {finding.type}</p>}
      <button type="button" className="detail-close" aria-label="Close finding" onClick={onClose}><X size={15} aria-hidden="true" /></button>
    </div>
    <h3>{finding.heading}</h3>
    {evaluation && <p className="detail-muted">p.{finding.page} · {finding.type}</p>}

    <div className="detail-block">
      <p className="detail-label">Evidence · what the document says</p>
      <blockquote className="detail-quote">{finding.quote}</blockquote>
      <p className="evidence-status" role="status">{located === undefined ? 'Finding it on the page…' : located ? `Highlighted in yellow on page ${finding.page}` : 'The exact quote could not be located on the page. Compare it with the source.'}</p>
    </div>

    <div className="detail-block">
      <p className="detail-label">AI interpretation · why it was flagged</p>
      <p className="detail-summary">{finding.summary}</p>
    </div>

    {evaluation && <PlaybookBlock evaluation={evaluation} />}

    {onFollow && <LinksBlock links={links} onFollow={onFollow} />}
    {onTermGo && <TermsBlock terms={terms} onGo={onTermGo} />}

    {others.length > 0 && <div className="detail-block">
      <p className="detail-label">Also tagged {finding.type}</p>
      <div className="detail-others">{others.slice(0, 12).map(item => <button key={item.key} type="button" onClick={() => onSelect(item)} title={item.heading}>p.{item.page}</button>)}</div>
    </div>}

    <div className="detail-block">
      {decision ? <div className="detail-decided">
        <span data-decision={decision.decision}>{decision.decision === 'accepted' ? <><Check size={14} aria-hidden="true" /> Accepted</> : <><X size={14} aria-hidden="true" /> Rejected{decision.reason ? `: ${decision.reason}` : ''}</>}</span>
        <button type="button" className="button secondary small" onClick={() => onDecide(undefined)}>Undo</button>
        <button type="button" className="button secondary small" onClick={onNext}>Next finding <kbd>J</kbd></button>
      </div> : rejecting ? <div className="detail-reasons">
        <p>What’s wrong with this finding?</p>
        <div>{rejectReasons.map(reason => <button key={reason} type="button" className="button secondary small" onClick={() => { onDecide({ decision: 'rejected', reason }); setRejecting(false); }}>{reason}</button>)}</div>
        <button type="button" className="text-button" onClick={() => setRejecting(false)}>Cancel</button>
      </div> : <div className="detail-actions">
        <button type="button" className="button primary" onClick={() => onDecide({ decision: 'accepted' })}>Accept <kbd>A</kbd></button>
        <button type="button" className="button secondary" onClick={() => setRejecting(true)}>Reject <kbd>R</kbd></button>
      </div>}
      <p className="detail-note">Decisions are saved in this browser. Keys: J / K next and previous, A accept, R reject.</p>
    </div>
  </div>;
}

/** Why the finding has its tier: the playbook rule, the condition that fired, and what to propose. */
export function PlaybookBlock({ evaluation }: { evaluation: Evaluation }) {
  const [copied, setCopied] = useState(false);
  const { rule, condition, matchedWords, tier } = evaluation;
  if (!rule) return <div className="detail-block">
    <p className="detail-label">Playbook</p>
    <p className="detail-muted">No rule in your playbook covers this type yet, so it is <strong>Not tiered</strong>. Add one from the playbook’s coverage gaps.</p>
  </div>;
  return <div className="detail-block playbook-block">
    <p className="detail-label">Playbook · {rule.name}</p>
    <p className="detail-summary">{condition ? <>Text contains “{matchedWords}”, so this rule sets <strong>{tier}</strong>.</> : <>This rule’s default tier is <strong>{tier}</strong>.</>} Sign-off: {rule.signOff}.</p>
    {rule.reason && <p className="detail-muted">Why: {rule.reason}</p>}
    {rule.preferred && <p className="playbook-preferred"><span>Preferred position</span>{rule.preferred}</p>}
    {rule.fallback && tier !== 'Meets playbook' && <div className="playbook-fallback">
      <span>Fallback wording to propose</span>
      <p>{rule.fallback}</p>
      <button type="button" className="button secondary small" onClick={() => { void navigator.clipboard?.writeText(rule.fallback).then(() => setCopied(true)).catch(() => setCopied(false)); }}>{copied ? 'Copied' : 'Copy wording'}</button>
    </div>}
  </div>;
}
