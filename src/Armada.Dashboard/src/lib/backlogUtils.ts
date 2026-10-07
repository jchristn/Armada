import type {
  Objective,
  ObjectiveBacklogState,
  ObjectiveEffort,
  ObjectiveKind,
  ObjectivePriority,
  ObjectiveStatus,
  SelectedPlaybook,
} from '../../types/models';

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
