import type { BlackBoxEntry, BlackBoxProposedChange, BlackBoxResilience, BlackBoxSeverity } from '../types/blackbox';
import type { Vessel } from '../types/models';
import type { DispatchPrefillState } from './dispatchRequest';

/** Names and repository URLs that identify the vessel holding Armada's own source. */
const ARMADA_REPO = /(^|[/:])armada(\.git)?\/?$/i;

/**
 * The vessel that holds Armada's own source, or null: a vessel named "Armada" (case-insensitive) first, then one whose
 * repository URL ends in /armada or /armada.git.
 */
export function findArmadaVessel(vessels: ReadonlyArray<Pick<Vessel, 'id' | 'name' | 'repoUrl'>>): Pick<Vessel, 'id' | 'name' | 'repoUrl'> | null {
  const byName = vessels.find((v) => (v.name ?? '').trim().toLowerCase() === 'armada');
  if (byName) return byName;
  return vessels.find((v) => !!v.repoUrl && ARMADA_REPO.test(v.repoUrl.trim())) ?? null;
}

/** The top-ranked proposed Armada change of a resilience section, or null. */
export function topProposedChange(resilience: BlackBoxResilience | null | undefined): BlackBoxProposedChange | null {
  const changes = [...(resilience?.proposedChanges ?? [])].sort((a, b) => a.rank - b.rank);
  return changes[0] ?? null;
}

/** The mission brief of a proposed change: its own MissionBrief, or one assembled from its fields. */
export function proposedChangeBrief(change: BlackBoxProposedChange, entry?: Pick<BlackBoxEntry, 'id'> | null): string {
  if (change.missionBrief && change.missionBrief.trim()) return change.missionBrief;
  const lines = [change.title, '', change.change];
  if (change.acceptanceTest) lines.push('', `Acceptance test: ${change.acceptanceTest}`);
  if (change.codePointers?.length) lines.push('', `Code pointers: ${change.codePointers.join(', ')}`);
  if (entry?.id) lines.push('', `Evidence: black box entry ${entry.id}${change.ruleId ? ` (rule ${change.ruleId})` : ''}.`);
  return lines.join('\n');
}

/** Router state that opens Dispatch with the Armada fix mission drafted on the Armada vessel. */
export function armadaFixDispatchState(change: BlackBoxProposedChange, vesselId: string, entry?: Pick<BlackBoxEntry, 'id'> | null): DispatchPrefillState {
  return { fromBlackBox: true, vesselId, voyageTitle: change.title, prompt: proposedChangeBrief(change, entry) };
}

/** The Ask Armada prompt that drafts the Armada fix mission when no Armada vessel exists. */
export function armadaFixAskPrompt(entryId: string, change: BlackBoxProposedChange | null): string {
  return `Draft an Armada fix mission for black box entry ${entryId}: start from the entry's Resilience section`
    + (change ? ` (top proposed change: "${change.title}")` : '')
    + ', with its acceptance test, regression test and code pointers at the Armada build commit, and propose it for my approval.';
}

/** Chip class for a severity. */
export function severityChipClass(severity: BlackBoxSeverity | string | null | undefined): string {
  if (severity === 'Critical') return 'blackbox-chip blackbox-chip-danger';
  if (severity === 'High') return 'blackbox-chip blackbox-chip-warning';
  return 'blackbox-chip';
}
