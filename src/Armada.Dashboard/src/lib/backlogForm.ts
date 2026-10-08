import type {
  Objective,
  ObjectiveBacklogState,
  ObjectiveEffort,
  ObjectiveKind,
  ObjectivePriority,
  ObjectiveStatus,
  ObjectiveUpsertRequest,
} from '../types/models';
import { joinList, joinSuggestedPlaybooks, parseSuggestedPlaybooks, splitList } from './backlogUtils';

/**
 * The backlog item form (the dashboard's ObjectiveDetail editor, the mobile backlog item form): form state as text,
 * conversion from an Objective, and the upsert payload. Host-agnostic; shared with the mobile app.
 */

/** A UTC timestamp as a local `YYYY-MM-DDTHH:mm` value (datetime-local inputs), or '' when empty or invalid. */
export function toDateTimeLocalValue(value: string | null | undefined): string {
  if (!value) return '';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  const year = date.getFullYear();
  const month = `${date.getMonth() + 1}`.padStart(2, '0');
  const day = `${date.getDate()}`.padStart(2, '0');
  const hours = `${date.getHours()}`.padStart(2, '0');
  const minutes = `${date.getMinutes()}`.padStart(2, '0');
  return `${year}-${month}-${day}T${hours}:${minutes}`;
}

/** A local date-time text as an ISO UTC string, or null when empty or unparseable. */
export function toIsoOrNull(value: string): string | null {
  if (!value.trim()) return null;
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? null : parsed.toISOString();
}

/** One key / value tag row (tags are stored as `key:value` or a bare `key`). */
export interface TagEntry {
  key: string;
  value: string;
}

export function createEmptyTagEntry(): TagEntry {
  return { key: '', value: '' };
}

export function parseTagEntry(raw: string): TagEntry {
  const trimmed = raw.trim();
  if (!trimmed) return createEmptyTagEntry();

  const separatorIndex = [trimmed.indexOf(':'), trimmed.indexOf('=')]
    .filter((index) => index > 0)
    .sort((left, right) => left - right)[0] ?? -1;
  if (separatorIndex < 1) {
    return { key: trimmed, value: '' };
  }

  return {
    key: trimmed.slice(0, separatorIndex).trim(),
    value: trimmed.slice(separatorIndex + 1).trim(),
  };
}

/** Tag rows for an item's tags (always at least one, empty, row to type into). */
export function parseTagEntries(tags: string[]): TagEntry[] {
  const rows = tags
    .map(parseTagEntry)
    .filter((entry) => entry.key || entry.value);

  return rows.length > 0 ? rows : [createEmptyTagEntry()];
}

export function serializeTagEntries(entries: TagEntry[]): string[] {
  return entries
    .map((entry) => {
      const key = entry.key.trim();
      const value = entry.value.trim();
      if (!key && !value) return '';
      if (!value) return key;
      return `${key}:${value}`;
    })
    .filter((entry): entry is string => entry.length > 0);
}

/** Make `nextId` the first (primary) id of a newline list, keeping the others; '' clears the list. */
export function replacePrimaryLinkedId(currentValue: string, nextId: string): string {
  if (!nextId) return '';

  const existing = splitList(currentValue).filter((id, index) => index > 0 && id !== nextId);
  return joinList([nextId, ...existing]);
}

/** Editable fields of a backlog item as form text (lists are newline-separated). */
export interface ObjectiveFormState {
  title: string;
  description: string;
  status: ObjectiveStatus;
  kind: ObjectiveKind;
  category: string;
  priority: ObjectivePriority;
  rank: string;
  backlogState: ObjectiveBacklogState;
  effort: ObjectiveEffort;
  owner: string;
  targetVersion: string;
  /** Local `YYYY-MM-DDTHH:mm`. */
  dueUtc: string;
  parentObjectiveId: string;
  blockedByObjectiveIds: string[];
  refinementSummary: string;
  suggestedPipelineId: string;
  /** `playbook-id:delivery-mode` lines. */
  suggestedPlaybooks: string;
  tagEntries: TagEntry[];
  acceptanceCriteria: string;
  nonGoals: string;
  rolloutConstraints: string;
  evidenceLinks: string;
  fleetIds: string;
  vesselIds: string;
  planningSessionIds: string;
  refinementSessionIds: string;
  voyageIds: string;
  missionIds: string;
  checkRunIds: string;
  releaseIds: string;
  deploymentIds: string;
  incidentIds: string;
}

/** The form of a new backlog item (the dashboard's create defaults). */
export function emptyObjectiveForm(): ObjectiveFormState {
  return {
    title: '', description: '', status: 'Draft', kind: 'Feature', category: '', priority: 'P2', rank: '',
    backlogState: 'Inbox', effort: 'M', owner: '', targetVersion: '', dueUtc: '', parentObjectiveId: '',
    blockedByObjectiveIds: [], refinementSummary: '', suggestedPipelineId: '', suggestedPlaybooks: '',
    tagEntries: [createEmptyTagEntry()], acceptanceCriteria: '', nonGoals: '', rolloutConstraints: '', evidenceLinks: '',
    fleetIds: '', vesselIds: '', planningSessionIds: '', refinementSessionIds: '', voyageIds: '', missionIds: '',
    checkRunIds: '', releaseIds: '', deploymentIds: '', incidentIds: '',
  };
}

/** The form for an existing backlog item. */
export function objectiveFormFrom(next: Objective): ObjectiveFormState {
  return {
    title: next.title,
    description: next.description || '',
    status: next.status,
    kind: next.kind,
    category: next.category || '',
    priority: next.priority,
    rank: String(next.rank),
    backlogState: next.backlogState,
    effort: next.effort,
    owner: next.owner || '',
    targetVersion: next.targetVersion || '',
    dueUtc: toDateTimeLocalValue(next.dueUtc),
    parentObjectiveId: next.parentObjectiveId || '',
    blockedByObjectiveIds: next.blockedByObjectiveIds || [],
    refinementSummary: next.refinementSummary || '',
    suggestedPipelineId: next.suggestedPipelineId || '',
    suggestedPlaybooks: joinSuggestedPlaybooks(next.suggestedPlaybooks),
    tagEntries: parseTagEntries(next.tags),
    acceptanceCriteria: joinList(next.acceptanceCriteria),
    nonGoals: joinList(next.nonGoals),
    rolloutConstraints: joinList(next.rolloutConstraints),
    evidenceLinks: joinList(next.evidenceLinks),
    fleetIds: joinList(next.fleetIds),
    vesselIds: joinList(next.vesselIds),
    planningSessionIds: joinList(next.planningSessionIds),
    refinementSessionIds: joinList(next.refinementSessionIds),
    voyageIds: joinList(next.voyageIds),
    missionIds: joinList(next.missionIds),
    checkRunIds: joinList(next.checkRunIds),
    releaseIds: joinList(next.releaseIds),
    deploymentIds: joinList(next.deploymentIds),
    incidentIds: joinList(next.incidentIds),
  };
}

/** The create / update payload for a form (blank text becomes null, lists are split, the rank is parsed). */
export function objectivePayloadFromForm(form: ObjectiveFormState): ObjectiveUpsertRequest {
  const parsedRank = Number.parseInt(form.rank, 10);
  return {
    title: form.title.trim() || null,
    description: form.description.trim() || null,
    status: form.status,
    kind: form.kind,
    category: form.category.trim() || null,
    priority: form.priority,
    rank: Number.isFinite(parsedRank) ? parsedRank : null,
    backlogState: form.backlogState,
    effort: form.effort,
    owner: form.owner.trim() || null,
    targetVersion: form.targetVersion.trim() || null,
    dueUtc: toIsoOrNull(form.dueUtc),
    parentObjectiveId: form.parentObjectiveId.trim() || null,
    blockedByObjectiveIds: form.blockedByObjectiveIds,
    refinementSummary: form.refinementSummary.trim() || null,
    suggestedPipelineId: form.suggestedPipelineId.trim() || null,
    suggestedPlaybooks: parseSuggestedPlaybooks(form.suggestedPlaybooks),
    tags: serializeTagEntries(form.tagEntries),
    acceptanceCriteria: splitList(form.acceptanceCriteria),
    nonGoals: splitList(form.nonGoals),
    rolloutConstraints: splitList(form.rolloutConstraints),
    evidenceLinks: splitList(form.evidenceLinks),
    fleetIds: splitList(form.fleetIds),
    vesselIds: splitList(form.vesselIds),
    planningSessionIds: splitList(form.planningSessionIds),
    refinementSessionIds: splitList(form.refinementSessionIds),
    voyageIds: splitList(form.voyageIds),
    missionIds: splitList(form.missionIds),
    checkRunIds: splitList(form.checkRunIds),
    releaseIds: splitList(form.releaseIds),
    deploymentIds: splitList(form.deploymentIds),
    incidentIds: splitList(form.incidentIds),
  };
}
