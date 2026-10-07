import type {
  Captain,
  FleetRecommendationApplyRequest,
  VesselImportFleetRecommendation,
} from '../types/models';

/**
 * Pure logic of the vessel import wizard, shared by the dashboard (components/vessels/import) and the mobile app:
 * pasted-path parsing, the fleet categorization options, and the editable fleet recommendation drafts.
 */

/** Poll interval for background discovery, import, and fleet categorization, in milliseconds. */
export const IMPORT_POLL_MS = 2000;

/** Split pasted text into trimmed, non-empty, de-duplicated lines. */
export function parsePastedPaths(text: string): string[] {
  const seen = new Set<string>();
  const out: string[] = [];
  for (const raw of text.split(/\r?\n/)) {
    const line = raw.trim().replace(/^["']|["']$/g, '');
    if (!line || seen.has(line)) continue;
    seen.add(line);
    out.push(line);
  }
  return out;
}

/** Validate the optional max-depth field: null when empty, otherwise the number and whether it is out of range (1-16). */
export function parseMaxDepth(raw: string): { value: number | null; invalid: boolean } {
  const value = raw.trim() === '' ? null : Number(raw);
  const invalid = value !== null && (!Number.isInteger(value) || value < 1 || value > 16);
  return { value, invalid };
}

/** Landing modes offered as the import default (English labels; translate when rendering). */
export const IMPORT_LANDING_MODES: Array<{ value: string; label: string }> = [
  { value: '', label: 'Default (use global setting)' },
  { value: 'LocalMerge', label: 'Local Merge' },
  { value: 'MergeAndPush', label: 'Merge and Push' },
  { value: 'PullRequest', label: 'Pull Request' },
  { value: 'MergeQueue', label: 'Merge Queue' },
  { value: 'None', label: 'None' },
];

/** Fleet categorization choices made on the Review step. */
export interface CategorizationOptions {
  enabled: boolean;
  captainId: string | null;
  prompt: string;
  applyAutomatically: boolean;
}

export const EMPTY_CATEGORIZATION: CategorizationOptions = { enabled: false, captainId: null, prompt: '', applyAutomatically: false };

/** Captain states that can take a categorization job right away. */
export function isCaptainAvailable(captain: Captain): boolean {
  return captain.state === 'Idle';
}

/** Validation errors for the categorization options (English source strings; translate when rendering). */
export function validateCategorization(options: CategorizationOptions): { captain?: string; prompt?: string } {
  const errors: { captain?: string; prompt?: string } = {};
  if (!options.enabled) return errors;
  if (!options.captainId) errors.captain = 'Choose the captain that will recommend fleets.';
  if (options.prompt.length > 32768) errors.prompt = 'Instructions must be 32768 characters or fewer.';
  return errors;
}

/** Name of the bucket the server fills with vessels the captain did not assign; it is never created as a fleet. */
export const UNCATEGORIZED_FLEET = 'Uncategorized';

/** One editable fleet card. */
export interface FleetDraft {
  key: string;
  name: string;
  description: string;
  rationale: string;
  vesselIds: string[];
  appliedFleetId: string | null;
}

let draftCounter = 0;

/** Turn stored recommendations into editable drafts. */
export function toDrafts(recommendations: VesselImportFleetRecommendation[]): FleetDraft[] {
  return recommendations.map((r) => ({
    key: r.id || `draft-${draftCounter++}`,
    name: r.name,
    description: r.description ?? '',
    rationale: r.rationale ?? '',
    vesselIds: [...r.vesselIds],
    appliedFleetId: r.appliedFleetId,
  }));
}

/** A new, empty fleet draft (the "+ Add fleet" button). */
export function newFleetDraft(): FleetDraft {
  return { key: `new-${draftCounter++}`, name: '', description: '', rationale: '', vesselIds: [], appliedFleetId: null };
}

/** Move a vessel from whichever draft holds it to the target draft. */
export function moveVessel(drafts: FleetDraft[], vesselId: string, targetKey: string): FleetDraft[] {
  return drafts.map((d) => {
    const without = d.vesselIds.filter((id) => id !== vesselId);
    return d.key === targetKey ? { ...d, vesselIds: [...without, vesselId] } : { ...d, vesselIds: without };
  });
}

/** Validation errors per draft key (English source strings). */
export function validateDrafts(drafts: FleetDraft[]): Record<string, string> {
  const errors: Record<string, string> = {};
  const seen = new Map<string, string>();
  for (const d of drafts) {
    const name = d.name.trim();
    if (d.vesselIds.length === 0) continue;
    if (!name) { errors[d.key] = 'Give this fleet a name.'; continue; }
    if (name.length > 256) { errors[d.key] = 'Fleet names must be 256 characters or fewer.'; continue; }
    const lower = name.toLowerCase();
    if (seen.has(lower)) errors[d.key] = 'Another fleet already uses this name.';
    else seen.set(lower, d.key);
  }
  return errors;
}

/** True when a draft is the Uncategorized bucket. */
export function isUncategorized(draft: Pick<FleetDraft, 'name'>): boolean {
  return draft.name.trim().toLowerCase() === UNCATEGORIZED_FLEET.toLowerCase();
}

/** Repositories assigned to named fleets (Uncategorized excluded) and the number of fleets that will be applied. */
export function draftTotals(drafts: FleetDraft[]): { assignedCount: number; fleetCount: number } {
  const named = drafts.filter((d) => !isUncategorized(d));
  return {
    assignedCount: named.reduce((n, d) => n + d.vesselIds.length, 0),
    fleetCount: named.filter((d) => d.vesselIds.length > 0).length,
  };
}

/** Request body for the apply endpoint: fleets without vessels are dropped and names trimmed. */
export function buildApplyPayload(drafts: FleetDraft[]): FleetRecommendationApplyRequest {
  return {
    Fleets: drafts
      .filter((d) => d.vesselIds.length > 0)
      .map((d) => ({ Name: d.name.trim(), Description: d.description.trim() || null, VesselIds: [...d.vesselIds] })),
  };
}
