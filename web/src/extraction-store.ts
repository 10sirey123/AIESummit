import { useEffect, useSyncExternalStore } from 'react';
import { errorMessage, type AnalysisSummary, type ExtractionResults, type ExtractionType, type Session } from './api';

/**
 * One in-memory cache of extraction results per upload, shared by the contract list chips, the reader and the compare view.
 * Polling follows `summary.extraction.canPoll` with a client deadline; nothing is persisted locally.
 */
export type UploadExtractions = { results?: ExtractionResults; summary?: AnalysisSummary; error?: string; loading: boolean; polling: boolean };

type Entry = UploadExtractions & { session?: Session; timer?: number; deadline?: number; controller?: AbortController };
const entries = new Map<number, Entry>();
const listeners = new Set<() => void>();
let version = 0;
let catalog: { session: Session; types?: ExtractionType[]; promise?: Promise<void> } | undefined;

function emit() { version++; listeners.forEach(listener => listener()); }
function subscribe(listener: () => void) { listeners.add(listener); return () => listeners.delete(listener); }
const pollEvery = 5000;
const pollFor = 20 * 60 * 1000;

async function fetchOnce(session: Session, id: number) {
  const entry = entries.get(id) ?? { loading: true, polling: false };
  entry.session = session;
  entry.controller?.abort();
  const controller = new AbortController();
  entry.controller = controller;
  entries.set(id, { ...entry, loading: !entry.results });
  emit();
  try {
    const [summary, results] = await Promise.all([session.analysisSummary(id, controller.signal), session.extractions(id, controller.signal)]);
    if (controller.signal.aborted) return;
    const current = entries.get(id)!;
    const keepPolling = summary.extraction.canPoll && Date.now() < (current.deadline ?? Date.now() + pollFor);
    entries.set(id, { ...current, summary, results, error: undefined, loading: false, polling: keepPolling,
      deadline: keepPolling ? current.deadline ?? Date.now() + pollFor : undefined });
    emit();
    if (keepPolling) {
      window.clearTimeout(current.timer);
      entries.get(id)!.timer = window.setTimeout(() => void fetchOnce(session, id), pollEvery);
    }
  } catch (failure) {
    if (controller.signal.aborted) return;
    const current = entries.get(id)!;
    entries.set(id, { ...current, error: errorMessage(failure), loading: false, polling: false });
    emit();
  }
}

/** Load (once) the results for an upload; `refresh` forces a new fetch and restarts polling. */
export function loadExtractions(session: Session, id: number, refresh = false) {
  const entry = entries.get(id);
  if (entry && entry.session === session && !refresh && (entry.results || entry.loading || entry.polling)) return;
  if (refresh && entry) { window.clearTimeout(entry.timer); entry.deadline = undefined; }
  void fetchOnce(session, id);
}

export function useExtractions(session: Session, id: number | undefined, enabled = true) {
  useSyncExternalStore(subscribe, () => version);
  useEffect(() => { if (id !== undefined && enabled) loadExtractions(session, id); }, [session, id, enabled]);
  return id === undefined ? undefined : entries.get(id);
}

/** Snapshot of several uploads at once (compare view and list chips). */
export function useManyExtractions(session: Session, ids: number[]) {
  useSyncExternalStore(subscribe, () => version);
  const key = ids.join(',');
  useEffect(() => { ids.forEach(id => loadExtractions(session, id)); }, [session, key]);
  return new Map(ids.map(id => [id, entries.get(id)]));
}

export function useCatalog(session: Session) {
  useSyncExternalStore(subscribe, () => version);
  useEffect(() => {
    if (catalog?.session === session && (catalog.types || catalog.promise)) return;
    const current: NonNullable<typeof catalog> = { session };
    catalog = current;
    current.promise = session.extractionTypes().then(types => { current.types = types; }).catch(() => { current.types = []; }).finally(() => { current.promise = undefined; emit(); });
  }, [session]);
  return catalog?.session === session ? catalog.types : undefined;
}

/** Forget everything (sign-out). */
export function resetExtractions() {
  entries.forEach(entry => { entry.controller?.abort(); window.clearTimeout(entry.timer); });
  entries.clear();
  catalog = undefined;
  emit();
}
