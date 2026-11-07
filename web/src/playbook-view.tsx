import { useEffect, useMemo, useRef, useState } from 'react';
import { ArrowDown, ArrowUp, Download, Plus, Trash2, Upload as UploadIcon } from 'lucide-react';
import { errorMessage, type Session } from './api';
import { reviewFindings, TierBadge, type ReviewFinding } from './review-desk';
import { coverageGaps, evaluate, newId, playbookStore, ruleTiers, signOffs, tiers, usePlaybooks, type Rule, type Tier } from './playbook-store';

type ContractFinding = ReviewFinding & { uploadId: number; file: string };
const dateTime = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' });

/** Findings from the owner's analyzed uploads, used to preview a playbook on real contracts. */
function useContractFindings(session: Session) {
  const [state, setState] = useState<{ loading: boolean; error: string; findings: ContractFinding[]; contracts: number }>({ loading: true, error: '', findings: [], contracts: 0 });
  useEffect(() => {
    const controller = new AbortController();
    void (async () => {
      try {
        const ready = (await session.list()).filter(row => row.status === 'ready').slice(0, 25);
        const details = await Promise.all(ready.map(row => session.detail(row.id, controller.signal)));
        if (controller.signal.aborted) return;
        const findings = details.flatMap(detail => reviewFindings(detail.pages).map(item => ({ ...item, uploadId: detail.upload.id, file: detail.upload.fileName })));
        setState({ loading: false, error: '', findings, contracts: details.filter(detail => detail.pages.some(page => page.analysisStatus === 'ready')).length });
      } catch (failure) {
        if (!controller.signal.aborted) setState(current => ({ ...current, loading: false, error: errorMessage(failure) }));
      }
    })();
    return () => controller.abort();
  }, [session]);
  return state;
}

export function PlaybookView({ session, onOpenContracts }: { session: Session; onOpenContracts: () => void }) {
  const store = usePlaybooks();
  const [editingId, setEditingId] = useState(store.active);
  const playbook = store.playbooks[editingId] ?? store.playbooks[store.active];
  const rules = playbook.draft ?? playbook.published.rules;
  const [selectedId, setSelectedId] = useState<string | undefined>(rules[0]?.id);
  const [note, setNote] = useState('');
  const [message, setMessage] = useState('');
  const fileInput = useRef<HTMLInputElement>(null);
  const contracts = useContractFindings(session);
  const selected = rules.find(rule => rule.id === selectedId) ?? rules[0];
  const types = useMemo(() => [...new Set(contracts.findings.map(item => item.type))].sort(), [contracts.findings]);

  const tierCounts = (ruleSet: Rule[]) => Object.fromEntries(tiers.map(tier => [tier, contracts.findings.filter(item => evaluate(item, ruleSet).tier === tier).length])) as Record<Tier, number>;
  const published = tierCounts(playbook.published.rules);
  const draft = playbook.draft ? tierCounts(playbook.draft) : undefined;
  const changes = playbook.draft ? contracts.findings.map(item => ({ item, from: evaluate(item, playbook.published.rules).tier, to: evaluate(item, playbook.draft!).tier })).filter(change => change.from !== change.to) : [];
  const gaps = coverageGaps(contracts.findings, rules);
  const matches = (rule: Rule) => contracts.findings.filter(item => evaluate(item, rules).rule?.id === rule.id).length;

  const edit = (change: (rules: Rule[]) => Rule[]) => playbookStore.editDraft(playbook.id, change);
  const editRule = (id: string, change: Partial<Rule>) => edit(list => list.map(rule => rule.id === id ? { ...rule, ...change } : rule));
  function addRule(type?: string) {
    const id = newId('rule');
    edit(list => [...list.filter(rule => rule.id !== 'general'), { id, name: type ? type.replace(/^\w/, c => c.toUpperCase()) : 'New rule', types: type ?? '', tier: 'Escalate', conditions: [], reason: '', preferred: '', fallback: '', signOff: 'Reviewer' }, ...list.filter(rule => rule.id === 'general')]);
    setSelectedId(id);
  }
  function move(id: string, by: number) {
    edit(list => { const at = list.findIndex(rule => rule.id === id); const next = [...list]; const [item] = next.splice(at, 1); next.splice(Math.max(0, Math.min(next.length, at + by)), 0, item); return next; });
  }
  function exportFile() {
    const url = URL.createObjectURL(new Blob([playbookStore.exportJson()], { type: 'application/json' }));
    const link = document.createElement('a');
    link.href = url; link.download = 'clauselens-playbooks.json'; link.click();
    window.setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
  async function importFile(file?: File) {
    if (!file) return;
    try { playbookStore.importJson(await file.text()); setMessage('Playbooks imported.'); }
    catch (failure) { setMessage(errorMessage(failure)); }
    if (fileInput.current) fileInput.current.value = '';
  }

  return <main className="shell playbook-page">
    <div className="playbook-heading">
      <div>
        <h1>Playbook</h1>
        <p>Your negotiating positions. Reviews tier every finding against the <strong>published</strong> version of the active playbook. Saved in this browser; export to share.</p>
      </div>
      <div className="playbook-file-actions">
        <button type="button" className="button secondary small" onClick={exportFile}><Download size={15} aria-hidden="true" /> Export</button>
        <input ref={fileInput} type="file" accept="application/json,.json" className="file-input" id="playbook-import" onChange={event => void importFile(event.target.files?.[0])} />
        <label htmlFor="playbook-import" className="button secondary small file-label"><UploadIcon size={15} aria-hidden="true" /> Import</label>
        <button type="button" className="button secondary small" onClick={() => { if (window.confirm('Replace both playbooks with the starter playbooks? Your rules and history will be lost.')) playbookStore.reset(); }}>Reset to starter</button>
      </div>
    </div>
    {message && <p role="status" className="playbook-message">{message}</p>}

    <div className="playbook-tabs" role="tablist" aria-label="Playbooks">
      {Object.values(store.playbooks).map(item => <button key={item.id} type="button" role="tab" aria-selected={item.id === playbook.id} onClick={() => { setEditingId(item.id); setSelectedId((item.draft ?? item.published.rules)[0]?.id); }}>
        {item.name}{item.id === store.active && <span className="active-mark">Used for reviews</span>}
      </button>)}
      {playbook.id !== store.active && <button type="button" className="button secondary small" onClick={() => playbookStore.setActive(playbook.id)}>Use {playbook.name} for reviews</button>}
    </div>

    <section className="publish-bar" data-draft={!!playbook.draft || undefined} aria-label="Version">
      <div>
        <strong>Published v{playbook.published.version}</strong>
        <span> · {dateTime.format(new Date(playbook.published.at))} · {playbook.published.note}</span>
        {playbook.draft && <span className="draft-mark"> · Unpublished draft{changes.length ? ` · would change ${changes.length} finding${changes.length === 1 ? '' : 's'}` : ''}</span>}
      </div>
      {playbook.draft && <div className="publish-actions">
        <label className="visually-hidden" htmlFor="publish-note">Change note</label>
        <input id="publish-note" value={note} onChange={event => setNote(event.target.value)} placeholder="What changed? (optional)" />
        <button type="button" className="button primary small" onClick={() => { playbookStore.publish(playbook.id, note); setNote(''); setMessage(`Published v${playbook.published.version + 1}. New reviews use it now.`); }}>Publish v{playbook.published.version + 1}</button>
        <button type="button" className="button secondary small" onClick={() => playbookStore.discardDraft(playbook.id)}>Discard draft</button>
      </div>}
    </section>

    <div className="playbook-grid">
      <aside className="rule-list" aria-label="Rules">
        <p className="rail-label"><span>Rules · first match wins</span><span>{rules.length}</span></p>
        <ol>
          {rules.map((rule, index) => <li key={rule.id}>
            <button type="button" className="rule-row" aria-current={rule.id === selected?.id || undefined} onClick={() => setSelectedId(rule.id)}>
              <span className="rule-row-top"><strong>{index + 1}. {rule.name}</strong><TierBadge tier={rule.tier} /></span>
              <span className="rule-row-meta">{rule.types || 'No types yet'}{contracts.findings.length ? ` · ${matches(rule)} findings` : ''}</span>
            </button>
          </li>)}
        </ol>
        <button type="button" className="button secondary small" onClick={() => addRule()}><Plus size={15} aria-hidden="true" /> Add rule</button>
        {gaps.length > 0 && <div className="gap-list">
          <p className="rail-label"><span>Coverage gaps</span><span>{gaps.length}</span></p>
          <p className="detail-muted">Types in your contracts that no rule covers. They review as Not tiered.</p>
          <ul>{gaps.map(gap => <li key={gap.type}><span>{gap.type} <span>×{gap.count}</span></span><button type="button" className="text-button" onClick={() => addRule(gap.type)}>Create rule</button></li>)}</ul>
        </div>}
        <details className="history">
          <summary>Version history · {playbook.history.length}</summary>
          <ul>{playbook.history.map(version => <li key={version.version}>
            <span><strong>v{version.version}</strong> {version.note}<br /><span className="detail-muted">{dateTime.format(new Date(version.at))}</span></span>
            {version.version !== playbook.published.version && <button type="button" className="text-button" onClick={() => playbookStore.restore(playbook.id, version.version)}>Restore as draft</button>}
          </li>)}</ul>
        </details>
      </aside>

      {selected ? <section className="rule-editor" aria-label={`Rule: ${selected.name}`}>
        <div className="rule-editor-head">
          <label className="field grow"><span>Rule name</span><input value={selected.name} onChange={event => editRule(selected.id, { name: event.target.value })} /></label>
          <div className="rule-order">
            <button type="button" aria-label="Move rule up" onClick={() => move(selected.id, -1)}><ArrowUp size={15} aria-hidden="true" /></button>
            <button type="button" aria-label="Move rule down" onClick={() => move(selected.id, 1)}><ArrowDown size={15} aria-hidden="true" /></button>
            <button type="button" aria-label="Delete rule" onClick={() => { edit(list => list.filter(rule => rule.id !== selected.id)); setSelectedId(undefined); }}><Trash2 size={15} aria-hidden="true" /></button>
          </div>
        </div>

        <label className="field"><span>Applies to finding types containing (comma-separated)</span>
          <input value={selected.types} onChange={event => editRule(selected.id, { types: event.target.value })} placeholder="e.g. liabilit, limitation" /></label>
        {types.length > 0 && <div className="type-picks" aria-label="Types found in your contracts">
          {types.map(type => <button key={type} type="button" className="tag-chip" onClick={() => editRule(selected.id, { types: [...new Set([...selected.types.split(',').map(word => word.trim()).filter(Boolean), type])].join(', ') })}>+ {type}</button>)}
        </div>}

        <fieldset className="tier-ladder">
          <legend>Default tier when this rule matches</legend>
          {ruleTiers.map(tier => <label key={tier} data-tier={tier} data-checked={selected.tier === tier || undefined}>
            <input type="radio" name={`tier-${selected.id}`} checked={selected.tier === tier} onChange={() => editRule(selected.id, { tier })} />{tier}
          </label>)}
        </fieldset>

        <div className="conditions">
          <p className="field-label">Conditions · checked in order, the first match sets the tier</p>
          {!selected.conditions.length && <p className="detail-muted">No conditions: every matching finding gets the default tier.</p>}
          {selected.conditions.map(condition => <div className="condition-row" key={condition.id}>
            <span>If the clause contains any of</span>
            <input aria-label="Words, comma-separated" value={condition.words} onChange={event => editRule(selected.id, { conditions: selected.conditions.map(item => item.id === condition.id ? { ...item, words: event.target.value } : item) })} placeholder="e.g. unlimited, uncapped" />
            <span>→</span>
            <select aria-label="Tier" value={condition.tier} onChange={event => editRule(selected.id, { conditions: selected.conditions.map(item => item.id === condition.id ? { ...item, tier: event.target.value as Tier } : item) })}>
              {ruleTiers.map(tier => <option key={tier} value={tier}>{tier}</option>)}
            </select>
            <button type="button" aria-label="Remove condition" onClick={() => editRule(selected.id, { conditions: selected.conditions.filter(item => item.id !== condition.id) })}><Trash2 size={14} aria-hidden="true" /></button>
          </div>)}
          <button type="button" className="text-button" onClick={() => editRule(selected.id, { conditions: [...selected.conditions, { id: newId('cond'), words: '', tier: 'Walk-away' }] })}>+ Add condition</button>
        </div>

        <label className="field"><span>Why (shown to reviewers)</span><textarea rows={2} value={selected.reason} onChange={event => editRule(selected.id, { reason: event.target.value })} /></label>
        <label className="field"><span>Preferred position</span><textarea rows={2} value={selected.preferred} onChange={event => editRule(selected.id, { preferred: event.target.value })} /></label>
        <label className="field"><span>Fallback wording to propose</span><textarea rows={3} value={selected.fallback} onChange={event => editRule(selected.id, { fallback: event.target.value })} /></label>
        <label className="field narrow"><span>Who signs off</span>
          <select value={selected.signOff} onChange={event => editRule(selected.id, { signOff: event.target.value as Rule['signOff'] })}>{signOffs.map(item => <option key={item}>{item}</option>)}</select></label>
      </section> : <section className="rule-editor"><p className="detail-muted">Select or add a rule.</p></section>}

      <aside className="impact-panel" aria-label="Impact on your contracts">
        <p className="rail-label"><span>Your contracts</span><span>{contracts.contracts} analyzed</span></p>
        {contracts.loading ? <p role="status" className="detail-muted">Loading findings from your uploads…</p>
          : contracts.error ? <p role="alert" className="error">{contracts.error}</p>
          : !contracts.findings.length ? <p className="detail-muted">No analyzed contracts yet. <button type="button" className="text-button" onClick={onOpenContracts}>Upload one</button> to preview this playbook on real findings.</p>
          : <>
            <table className="impact-table">
              <thead><tr><th scope="col">Tier</th><th scope="col">Published</th>{draft && <th scope="col">Draft</th>}</tr></thead>
              <tbody>{tiers.map(tier => <tr key={tier} data-tier={tier}><th scope="row" className="tier-label">{tier}</th><td>{published[tier]}</td>
                {draft && <td>{draft[tier]}{draft[tier] !== published[tier] && <span className="delta"> ({draft[tier] > published[tier] ? '+' : ''}{draft[tier] - published[tier]})</span>}</td>}</tr>)}</tbody>
            </table>
            <p className="detail-muted">{contracts.findings.length} findings across {contracts.contracts} contract{contracts.contracts === 1 ? '' : 's'}.</p>
            {playbook.draft && <div className="change-list">
              <p className="field-label">If you publish · {changes.length} change{changes.length === 1 ? '' : 's'}</p>
              {!changes.length && <p className="detail-muted">The draft does not change any current finding’s tier.</p>}
              <ul>{changes.slice(0, 14).map(change => <li key={`${change.item.uploadId}-${change.item.key}`}>
                <span className="change-what">{change.item.heading} <span>· {change.item.file} p.{change.item.page}</span></span>
                <span className="change-tiers"><TierBadge tier={change.from} /> → <TierBadge tier={change.to} /></span>
              </li>)}</ul>
              {changes.length > 14 && <p className="detail-muted">and {changes.length - 14} more</p>}
            </div>}
          </>}
      </aside>
    </div>
  </main>;
}
