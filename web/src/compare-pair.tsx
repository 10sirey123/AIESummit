import { useState } from 'react';
import { ArrowLeftRight, ArrowUpRight } from 'lucide-react';
import type { ExtractionFinding, Upload } from './api';
import { headline, outcomeShort, tierExtraction, uploadTargets, valueRows, worstTier, type TargetMeta, type UploadTargets } from './extraction-meta';
import type { UploadExtractions } from './extraction-store';
import { TargetIcon } from './extraction-view';
import { tiers, type Rule, type Tier } from './playbook-store';
import { TierBadge } from './review-desk';
import type { OpenRequest } from './workspace';

type Side = { upload?: Upload; targets: UploadTargets; state?: UploadExtractions };
type Diff = 'same' | 'differs' | 'only-a' | 'only-b' | 'neither';
const diffLabel: Record<Diff, string> = { same: 'Same', differs: 'Differs', 'only-a': 'Only yours', 'only-b': 'Only theirs', neither: 'Neither' };

/** Two contracts side by side, one row per clause type, each side tiered by the active playbook. */
export function ComparePair({ ready, states, order, rules, playbookName, onOpen }: {
  ready: Upload[]; states: Map<number, UploadExtractions | undefined>; order: TargetMeta[]; rules: Rule[]; playbookName: string;
  onOpen: (request: Omit<OpenRequest, 'request'>) => void;
}) {
  const [aId, setA] = useState<number | undefined>(ready[1]?.id ?? ready[0]?.id);
  const [bId, setB] = useState<number | undefined>(ready[0]?.id !== aId ? ready[0]?.id : ready[1]?.id);
  const [onlyDiff, setOnlyDiff] = useState(false);
  const side = (id?: number): Side => { const upload = ready.find(row => row.id === id); const state = id === undefined ? undefined : states.get(id); return { upload, state, targets: uploadTargets(state?.results) }; };
  const a = side(aId), b = side(bId);
  const rows = order.map(meta => ({ meta, diff: difference(meta.tag, a.targets, b.targets) })).filter(row => !onlyDiff || (row.diff !== 'same' && row.diff !== 'neither'));
  const sideTiers = (s: Side) => [...s.targets].flatMap(([tag, entry]) => entry.outcome === 'detected' ? entry.findings.map(finding => tierExtraction(tag, finding, rules).tier) : []);

  if (ready.length < 2) return <p>Upload and process at least two contracts to compare them side by side.</p>;
  return <section className="pair" aria-label="Two-contract comparison">
    <div className="pair-pickers">
      <Picker label="Yours" value={aId} ready={ready} onChange={setA} />
      <button type="button" className="icon-button" aria-label="Swap contracts" title="Swap" onClick={() => { setA(bId); setB(aId); }}><ArrowLeftRight size={16} aria-hidden="true" /></button>
      <Picker label="Theirs" value={bId} ready={ready} onChange={setB} />
      <label className="differ-toggle"><input type="checkbox" checked={onlyDiff} onChange={event => setOnlyDiff(event.target.checked)} /> Only differences</label>
    </div>
    <p className="pair-playbook">Tiers from the published <strong>{playbookName}</strong> playbook. Types with no matching rule show “Not tiered”.</p>

    <div className="pair-grid pair-head">
      <span />
      {[a, b].map((s, index) => <div key={index} className="pair-col-head">
        <span className="pair-side-label">{index === 0 ? 'Yours' : 'Theirs'}</span>
        <strong title={s.upload?.fileName}>{s.upload?.fileName ?? 'Choose a contract'}</strong>
        <TierSummary list={sideTiers(s)} loading={!!s.state?.loading || !!s.state?.polling} />
      </div>)}
    </div>
    {rows.map(({ meta, diff }) => <div key={meta.tag} className="pair-grid pair-row" data-diff={diff} style={{ '--target': meta.color } as React.CSSProperties}>
      <div className="pair-type">
        <TargetIcon tag={meta.tag} /><span>{meta.name}</span>
        <span className="diff-badge" data-diff={diff}>{diffLabel[diff]}</span>
      </div>
      {[a, b].map((s, index) => <PairCell key={index} label={index === 0 ? "Yours" : "Theirs"} tag={meta.tag} side={s} rules={rules} onOpen={onOpen} />)}
    </div>)}
    {!rows.length && <p className="detail-muted">No differences between these two contracts for the extracted clause types.</p>}
  </section>;
}

function Picker({ label, value, ready, onChange }: { label: string; value?: number; ready: Upload[]; onChange: (id: number) => void }) {
  return <label className="pair-picker"><span>{label}</span>
    <select value={value ?? ''} onChange={event => onChange(Number(event.target.value))}>
      {ready.map(row => <option key={row.id} value={row.id}>{row.fileName}</option>)}
    </select>
  </label>;
}

function TierSummary({ list, loading }: { list: Tier[]; loading: boolean }) {
  if (loading && !list.length) return <span className="detail-muted">Loading…</span>;
  const counts = tiers.filter(tier => tier !== 'Not tiered').map(tier => ({ tier, n: list.filter(item => item === tier).length })).filter(item => item.n);
  return <span className="pair-tiers">{counts.length ? counts.map(item => <span key={item.tier}><TierBadge tier={item.tier} /> {item.n}</span>) : <span className="detail-muted">No tiered findings</span>}</span>;
}

function PairCell({ label, tag, side, rules, onOpen }: { label: string; tag: string; side: Side; rules: Rule[]; onOpen: (request: Omit<OpenRequest, 'request'>) => void }) {
  const entry = side.targets.get(tag);
  if (!side.upload) return <div className="pair-cell" data-side={label} />;
  if (!entry) return <div className="pair-cell pair-empty" data-side={label}>{side.state?.loading || side.state?.polling ? 'Loading…' : 'No result'}</div>;
  if (entry.outcome !== 'detected') return <div className="pair-cell pair-empty" data-side={label} data-outcome={entry.outcome} title={entry.explanation ?? undefined}>{outcomeShort[entry.outcome]}</div>;
  const evaluations = entry.findings.map(finding => tierExtraction(tag, finding, rules));
  return <div className="pair-cell" data-side={label} data-tier={worstTier(evaluations.map(item => item.tier))}>
    {entry.findings.map((finding, index) => <Finding key={finding.id} tag={tag} finding={finding} evaluation={evaluations[index]} upload={side.upload!} onOpen={onOpen} />)}
  </div>;
}

function Finding({ tag, finding, evaluation, upload, onOpen }: {
  tag: string; finding: ExtractionFinding; evaluation: ReturnType<typeof tierExtraction>; upload: Upload; onOpen: (request: Omit<OpenRequest, 'request'>) => void;
}) {
  const primary = finding.evidence.find(item => item.role === 'primary') ?? finding.evidence[0];
  const rows = valueRows(finding.values);
  return <div className="pair-finding">
    <div className="pair-finding-top">
      <strong className="pair-value">{headline(tag, finding)}</strong>
      <TierBadge tier={evaluation.tier} />
    </div>
    {rows.length > 1 && <dl className="pair-values">{rows.slice(0, 4).map(row => <div key={row.key}><dt>{row.label}</dt><dd>{row.text}</dd></div>)}</dl>}
    {evaluation.rule && <p className="pair-rule">{evaluation.rule.name}{evaluation.matchedWords ? ` · matched “${evaluation.matchedWords}”` : ''}{evaluation.tier !== 'Meets playbook' && evaluation.rule.preferred ? ` · Preferred: ${evaluation.rule.preferred}` : ''}</p>}
    {primary && <button type="button" className="text-button pair-open" onClick={() => onOpen({ uploadId: upload.id, page: primary.page, quote: primary.quote, finding: `x-${finding.id}` })}>
      Evidence p.{primary.page} <ArrowUpRight size={12} aria-hidden="true" /></button>}
  </div>;
}

function difference(tag: string, a: UploadTargets, b: UploadTargets): Diff {
  const ea = a.get(tag), eb = b.get(tag);
  const fa = ea?.outcome === 'detected', fb = eb?.outcome === 'detected';
  if (!fa && !fb) return 'neither';
  if (fa && !fb) return 'only-a';
  if (!fa && fb) return 'only-b';
  const key = (entry: typeof ea) => entry!.findings.map(finding => headline(tag, finding).toLowerCase()).sort().join('|');
  return key(ea) === key(eb) ? 'same' : 'differs';
}
