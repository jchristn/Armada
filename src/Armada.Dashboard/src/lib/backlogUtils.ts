import type {
  Objective,
  ObjectiveBacklogState,
  ObjectiveEffort,
  ObjectiveKind,
  ObjectivePriority,
  ObjectiveReorderRequest,
  ObjectiveStatus,
  SelectedPlaybook,
} from '../types/models';

export const OBJECTIVE_STATUSES: ObjectiveStatus[] = ['Draft', 'Scoped', 'Planned', 'InProgress', 'Released', 'Deployed', 'Completed', 'Blocked', 'Cancelled'];
export const OBJECTIVE_KINDS: ObjectiveKind[] = ['Feature', 'Bug', 'Refactor', 'Research', 'Chore', 'Initiative'];
export const OBJECTIVE_PRIORITIES: ObjectivePriority[] = ['P0', 'P1', 'P2', 'P3'];
export const OBJECTIVE_BACKLOG_STATES: ObjectiveBacklogState[] = ['Inbox', 'Triaged', 'Refining', 'ReadyForPlanning', 'ReadyForDispatch', 'Dispatched'];
export const OBJECTIVE_EFFORTS: ObjectiveEffort[] = ['XS', 'S', 'M', 'L', 'XL'];

export type BacklogGroupKey = 'all' | 'inbox' | 'planning' | 'dispatch' | 'blocked';

export interface BacklogGroupDefinition {
  key: BacklogGroupKey;
  label: string;
  description: string;
}

export const BACKLOG_GROUPS: BacklogGroupDefinition[] = [
  { key: 'all', label: 'All', description: 'All backlog items' },
  { key: 'inbox', label: 'Inbox', description: 'Needs triage or refinement' },
  { key: 'planning', label: 'Ready For Planning', description: 'Ready for a repository-aware plan' },
  { key: 'dispatch', label: 'Ready For Dispatch', description: 'Ready to launch work' },
  { key: 'blocked', label: 'Blocked', description: 'Blocked or dependency-constrained' },
];

export type BacklogSortKey = 'rank' | 'priority' | 'updated' | 'due';

export function splitList(value: string): string[] {
  return value
    .split(/\r?\n|,/)
    .map((item) => item.trim())
    .filter(Boolean);
}

export function joinList(values: string[] | null | undefined): string {
  return (values || []).join('\n');
}

export function joinSuggestedPlaybooks(values: SelectedPlaybook[] | null | undefined): string {
  return (values || [])
    .map((item) => `${item.playbookId}:${item.deliveryMode}`)
    .join('\n');
}

export function parseSuggestedPlaybooks(value: string): SelectedPlaybook[] {
  return splitList(value)
    .map((line) => {
      const [playbookId, deliveryMode] = line.split(':', 2).map((part) => part.trim());
      if (!playbookId) return null;
      return {
        playbookId,
        deliveryMode: (deliveryMode || 'InlineFullContent') as SelectedPlaybook['deliveryMode'],
      };
    })
    .filter((item): item is SelectedPlaybook => item !== null);
}

export function buildObjectivePlanningPrompt(objective: Objective): string {
  const lines: string[] = [];
  lines.push(`Backlog Item: ${objective.title}`);
  lines.push(`Kind: ${objective.kind}`);
  lines.push(`Priority: ${objective.priority}`);
  lines.push(`Backlog State: ${objective.backlogState}`);
  if (objective.description) {
    lines.push('');
    lines.push('Context');
    lines.push(objective.description);
  }
  if (objective.refinementSummary) {
    lines.push('');
    lines.push('Refinement Summary');
    lines.push(objective.refinementSummary);
  }
  if (objective.acceptanceCriteria.length > 0) {
    lines.push('');
    lines.push('Acceptance Criteria');
    objective.acceptanceCriteria.forEach((item) => lines.push(`- ${item}`));
  }
  if (objective.nonGoals.length > 0) {
    lines.push('');
    lines.push('Non-Goals');
    objective.nonGoals.forEach((item) => lines.push(`- ${item}`));
  }
  if (objective.rolloutConstraints.length > 0) {
    lines.push('');
    lines.push('Rollout Constraints');
    objective.rolloutConstraints.forEach((item) => lines.push(`- ${item}`));
  }
  lines.push('');
  lines.push('Turn this refined backlog item into a practical implementation plan, call out risks, and identify the best dispatch shape.');
  return lines.join('\n');
}

export function buildObjectiveDispatchPrompt(objective: Objective): string {
  const lines: string[] = [];
  lines.push(`Implement backlog item: ${objective.title}`);
  if (objective.description) {
    lines.push('');
    lines.push(objective.description);
  }
  if (objective.refinementSummary) {
    lines.push('');
    lines.push('Refinement Summary');
    lines.push(objective.refinementSummary);
  }
  if (objective.acceptanceCriteria.length > 0) {
    lines.push('');
    lines.push('Acceptance Criteria');
    objective.acceptanceCriteria.forEach((item) => lines.push(`- ${item}`));
  }
  if (objective.nonGoals.length > 0) {
    lines.push('');
    lines.push('Non-Goals');
    objective.nonGoals.forEach((item) => lines.push(`- ${item}`));
  }
  if (objective.rolloutConstraints.length > 0) {
    lines.push('');
    lines.push('Constraints');
    objective.rolloutConstraints.forEach((item) => lines.push(`- ${item}`));
  }
  return lines.join('\n');
}

export function buildObjectiveReleaseNotes(objective: Objective): string {
  const lines: string[] = [];
  lines.push(`Backlog-derived release notes for ${objective.title}`);
  if (objective.description) {
    lines.push('');
    lines.push(objective.description);
  }
  if (objective.refinementSummary) {
    lines.push('');
    lines.push('Refinement Summary');
    lines.push(objective.refinementSummary);
  }
  if (objective.acceptanceCriteria.length > 0) {
    lines.push('');
    lines.push('Acceptance Criteria');
    objective.acceptanceCriteria.forEach((item) => lines.push(`- ${item}`));
  }
  if (objective.rolloutConstraints.length > 0) {
    lines.push('');
    lines.push('Rollout Constraints');
    objective.rolloutConstraints.forEach((item) => lines.push(`- ${item}`));
  }
  if (objective.evidenceLinks.length > 0) {
    lines.push('');
    lines.push('Evidence Links');
    objective.evidenceLinks.forEach((item) => lines.push(`- ${item}`));
  }
  return lines.join('\n');
}

export function getLatestRefinementSessionId(objective: Objective): string | null {
  if (!objective.refinementSessionIds || objective.refinementSessionIds.length < 1) return null;
  return objective.refinementSessionIds[objective.refinementSessionIds.length - 1] || null;
}

export function getBacklogGroup(objective: Objective): BacklogGroupKey {
  if (objective.status === 'Blocked' || objective.blockedByObjectiveIds.length > 0) return 'blocked';
  if (objective.backlogState === 'ReadyForDispatch' || objective.backlogState === 'Dispatched') return 'dispatch';
  if (objective.backlogState === 'ReadyForPlanning') return 'planning';
  if (objective.backlogState === 'Inbox' || objective.backlogState === 'Triaged' || objective.backlogState === 'Refining') return 'inbox';
  return 'all';
}

export function getPriorityWeight(priority: ObjectivePriority): number {
  switch (priority) {
    case 'P0': return 0;
    case 'P1': return 1;
    case 'P2': return 2;
    case 'P3': return 3;
    default: return 99;
  }
}

/** The Backlog list's filters (the dashboard's filter card; the mobile filter sheet). 'all' / '' mean no filter. */
export interface BacklogFilters {
  search: string;
  status: 'all' | ObjectiveStatus;
  kind: 'all' | ObjectiveKind;
  priority: 'all' | ObjectivePriority;
  backlogState: 'all' | ObjectiveBacklogState;
  effort: 'all' | ObjectiveEffort;
  fleetId: string;
  vesselId: string;
  owner: string;
  targetVersion: string;
  group: BacklogGroupKey;
  /** Title column filter (dashboard table only). */
  title: string;
  sortBy: BacklogSortKey;
}

export const DEFAULT_BACKLOG_FILTERS: BacklogFilters = {
  search: '',
  status: 'all',
  kind: 'all',
  priority: 'all',
  backlogState: 'all',
  effort: 'all',
  fleetId: 'all',
  vesselId: 'all',
  owner: '',
  targetVersion: '',
  group: 'all',
  title: '',
  sortBy: 'rank',
};

/** Number of backlog items in each group view (the group pills' counts). */
export function countBacklogGroups(objectives: Objective[]): Record<BacklogGroupKey, number> {
  const counts: Record<BacklogGroupKey, number> = { all: objectives.length, inbox: 0, planning: 0, dispatch: 0, blocked: 0 };
  objectives.forEach((objective) => {
    counts[getBacklogGroup(objective)] += 1;
  });
  return counts;
}

/** The backlog items matching every filter; free-text search covers title, description, owner, category, version, refinement summary, tags, acceptance criteria, and id. */
export function filterBacklog(objectives: Objective[], filters: BacklogFilters): Objective[] {
  const normalizedSearch = filters.search.trim().toLowerCase();
  const owner = filters.owner.trim().toLowerCase();
  const targetVersion = filters.targetVersion.trim().toLowerCase();
  const title = filters.title.toLowerCase();
  return objectives.filter((objective) => {
    if (filters.group !== 'all' && getBacklogGroup(objective) !== filters.group) return false;
    if (filters.status !== 'all' && objective.status !== filters.status) return false;
    if (filters.kind !== 'all' && objective.kind !== filters.kind) return false;
    if (filters.priority !== 'all' && objective.priority !== filters.priority) return false;
    if (filters.backlogState !== 'all' && objective.backlogState !== filters.backlogState) return false;
    if (filters.effort !== 'all' && objective.effort !== filters.effort) return false;
    if (filters.fleetId !== 'all' && !objective.fleetIds.includes(filters.fleetId)) return false;
    if (filters.vesselId !== 'all' && !objective.vesselIds.includes(filters.vesselId)) return false;
    if (owner && !(objective.owner || '').toLowerCase().includes(owner)) return false;
    if (targetVersion && !(objective.targetVersion || '').toLowerCase().includes(targetVersion)) return false;
    if (title && !(objective.title || '').toLowerCase().includes(title)) return false;
    if (!normalizedSearch) return true;

    return (
      objective.title.toLowerCase().includes(normalizedSearch)
      || (objective.description || '').toLowerCase().includes(normalizedSearch)
      || (objective.owner || '').toLowerCase().includes(normalizedSearch)
      || (objective.category || '').toLowerCase().includes(normalizedSearch)
      || (objective.targetVersion || '').toLowerCase().includes(normalizedSearch)
      || (objective.refinementSummary || '').toLowerCase().includes(normalizedSearch)
      || objective.tags.some((tag) => tag.toLowerCase().includes(normalizedSearch))
      || objective.acceptanceCriteria.some((criteria) => criteria.toLowerCase().includes(normalizedSearch))
      || objective.id.toLowerCase().includes(normalizedSearch)
    );
  });
}

/** Backlog items in the chosen order (rank, priority, last updated, or due date; rank breaks ties). */
export function sortBacklog(objectives: Objective[], sortBy: BacklogSortKey): Objective[] {
  const sorted = [...objectives];
  sorted.sort((left, right) => {
    if (sortBy === 'priority') {
      const priorityDelta = getPriorityWeight(left.priority) - getPriorityWeight(right.priority);
      if (priorityDelta !== 0) return priorityDelta;
      return left.rank - right.rank;
    }

    if (sortBy === 'due') {
      const leftDue = left.dueUtc ? new Date(left.dueUtc).getTime() : Number.MAX_SAFE_INTEGER;
      const rightDue = right.dueUtc ? new Date(right.dueUtc).getTime() : Number.MAX_SAFE_INTEGER;
      if (leftDue !== rightDue) return leftDue - rightDue;
      return left.rank - right.rank;
    }

    if (sortBy === 'updated') {
      const updatedDelta = new Date(right.lastUpdateUtc).getTime() - new Date(left.lastUpdateUtc).getTime();
      if (updatedDelta !== 0) return updatedDelta;
      return left.rank - right.rank;
    }

    const rankDelta = left.rank - right.rank;
    if (rankDelta !== 0) return rankDelta;
    return getPriorityWeight(left.priority) - getPriorityWeight(right.priority);
  });
  return sorted;
}

/** True when any filter (or a non-default sort) is set. */
export function hasActiveBacklogFilters(filters: BacklogFilters): boolean {
  return (
    filters.search.trim().length > 0
    || filters.status !== 'all'
    || filters.kind !== 'all'
    || filters.priority !== 'all'
    || filters.backlogState !== 'all'
    || filters.effort !== 'all'
    || filters.fleetId !== 'all'
    || filters.vesselId !== 'all'
    || filters.owner.trim().length > 0
    || filters.targetVersion.trim().length > 0
    || filters.group !== 'all'
    || filters.sortBy !== 'rank'
  );
}

/**
 * The reorder request that moves a backlog item one place up (-1) or down (1) in rank order by swapping ranks with
 * its neighbor, or null at either end (or for an unknown id).
 */
export function backlogRankSwap(objectives: Objective[], objectiveId: string, direction: -1 | 1): ObjectiveReorderRequest | null {
  const ranked = [...objectives].sort((left, right) => left.rank - right.rank);
  const index = ranked.findIndex((objective) => objective.id === objectiveId);
  const neighborIndex = index + direction;
  if (index < 0 || neighborIndex < 0 || neighborIndex >= ranked.length) return null;
  const current = ranked[index];
  const neighbor = ranked[neighborIndex];
  return {
    items: [
      { objectiveId: current.id, rank: neighbor.rank },
      { objectiveId: neighbor.id, rank: current.rank },
    ],
  };
}

/** Replace the reordered items (the reorder response) in a list. */
export function applyBacklogReorder(objectives: Objective[], updated: Objective[]): Objective[] {
  return objectives.map((objective) => updated.find((item) => item.id === objective.id) || objective);
}
