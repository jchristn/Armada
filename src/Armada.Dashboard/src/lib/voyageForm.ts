import type { SelectedPlaybook, VoyageCreateRequest } from '../types/models';

/**
 * Pure logic of the Create Voyage form, shared by the dashboard page (pages/VoyageCreate.tsx) and the mobile app.
 */

/** One mission row of the form. */
export interface VoyageMissionRow {
  title: string;
  description: string;
  priority: number;
  /** Stable row identity, so a child's parent survives other rows being removed. */
  key?: string;
  /** The key of an earlier row this mission builds on; it starts after that mission finishes, on its result. */
  parentKey?: string;
  /** Branch relationship to the parent; '' applies the default (first child continues, later children fork). */
  branchStrategy?: '' | 'ContinueParent' | 'ForkFromParent';
  /** Files or directories this mission may change, one per line or comma-separated. */
  ownedPaths?: string;
}

/** Everything the form collects. `landingMode` '' inherits (vessel, then the global setting). */
export interface VoyageFormState {
  title: string;
  description: string;
  vesselId: string;
  pipeline: string;
  landingMode: string;
  selectedPlaybooks: SelectedPlaybook[];
  missions: VoyageMissionRow[];
}

/** The priority a new mission row starts with (and the fallback for 0). */
export const DEFAULT_MISSION_PRIORITY = 100;

let rowSequence = 0;

export function newVoyageMissionRow(): VoyageMissionRow {
  rowSequence++;
  return { title: '', description: '', priority: DEFAULT_MISSION_PRIORITY, key: 'm' + rowSequence };
}

/** Rows after removing one: children of the removed row lose their parent instead of pointing at nothing. */
export function removeVoyageMissionRow(rows: VoyageMissionRow[], index: number): VoyageMissionRow[] {
  const removed = rows[index]?.key;
  return rows.filter((_, i) => i !== index).map(row => removed && row.parentKey === removed ? { ...row, parentKey: '', branchStrategy: '' } : row);
}

/** Split an owned-paths field on newlines and commas, trimmed, without blanks or duplicates. */
export function parseOwnedPaths(value: string | undefined): string[] {
  return [...new Set((value ?? '').split(/[\n,]/).map(p => p.trim()).filter(Boolean))];
}

export function emptyVoyageForm(): VoyageFormState {
  return { title: '', description: '', vesselId: '', pipeline: '', landingMode: '', selectedPlaybooks: [], missions: [newVoyageMissionRow()] };
}

/**
 * The first validation problem (English source text, translated by the caller), or null when the form can be sent.
 */
export function validateVoyageForm(form: VoyageFormState): string | null {
  if (!form.title.trim()) return 'Voyage title is required.';
  if (!form.vesselId) return 'Please select a vessel.';
  if (!form.missions.some((m) => m.title.trim())) return 'At least one mission with a title is required.';
  return null;
}

/**
 * The createVoyage payload: rows without a title are dropped, a mission without a description uses its title,
 * and the optional fields (pipeline, playbooks, landing mode) are sent only when set.
 */
export function buildVoyageCreateRequest(form: VoyageFormState): VoyageCreateRequest {
  const kept = form.missions.filter((m) => m.title.trim());
  // Keys are sent only when a kept row builds on another kept, earlier row; the server rejects anything else.
  const keptKeys = new Set<string>();
  const parentOf = new Map<VoyageMissionRow, string>();
  for (const m of kept) {
    if (m.parentKey && keptKeys.has(m.parentKey)) parentOf.set(m, m.parentKey);
    if (m.key) keptKeys.add(m.key);
  }
  const referenced = new Set(parentOf.values());
  const missions = kept.map((m) => {
    const owned = parseOwnedPaths(m.ownedPaths);
    const parentKey = parentOf.get(m);
    return {
      title: m.title.trim(),
      description: m.description.trim() || m.title.trim(),
      vesselId: form.vesselId,
      priority: m.priority || DEFAULT_MISSION_PRIORITY,
      ...(m.key && referenced.has(m.key) ? { key: m.key } : {}),
      ...(parentKey ? { parentKey } : {}),
      ...(parentKey && m.branchStrategy ? { branchStrategy: m.branchStrategy } : {}),
      ...(owned.length > 0 ? { ownedPaths: owned } : {}),
    };
  });
  return {
    title: form.title.trim(),
    description: form.description.trim() || undefined,
    vesselId: form.vesselId,
    ...(form.pipeline ? { pipeline: form.pipeline } : {}),
    missions,
    ...(form.selectedPlaybooks.length > 0 ? { selectedPlaybooks: form.selectedPlaybooks } : {}),
    ...(form.landingMode ? { landingMode: form.landingMode } : {}),
  };
}
