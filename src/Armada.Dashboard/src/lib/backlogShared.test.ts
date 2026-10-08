import { describe, expect, it } from 'vitest';
import {
  applyBacklogReorder,
  backlogRankSwap,
  countBacklogGroups,
  DEFAULT_BACKLOG_FILTERS,
  filterBacklog,
  hasActiveBacklogFilters,
  sortBacklog,
} from './backlogUtils';
import {
  emptyObjectiveForm,
  objectiveFormFrom,
  objectivePayloadFromForm,
  parseTagEntries,
  replacePrimaryLinkedId,
  serializeTagEntries,
  toDateTimeLocalValue,
  toIsoOrNull,
} from './backlogForm';
import type { Objective } from '../types/models';

function createObjective(overrides: Partial<Objective> = {}): Objective {
  return {
    id: 'obj_123',
    tenantId: 'ten_123',
    userId: 'usr_123',
    title: 'Backlog hardening',
    description: 'Stabilize backlog replay and delivery flows.',
    status: 'Scoped',
    kind: 'Feature',
    category: 'Platform',
    priority: 'P1',
    rank: 12,
    backlogState: 'ReadyForPlanning',
    effort: 'M',
    owner: 'qa',
    targetVersion: '0.8.0',
    dueUtc: null,
    parentObjectiveId: null,
    blockedByObjectiveIds: [],
    refinementSummary: 'Use the selected captain transcript to sharpen acceptance criteria.',
    suggestedPipelineId: null,
    suggestedPlaybooks: [
      { playbookId: 'pb_inline', deliveryMode: 'InlineFullContent' },
      { playbookId: 'pb_reference', deliveryMode: 'InstructionWithReference' },
    ],
    refinementSessionIds: ['ref_old', 'ref_latest'],
    sourceProvider: null,
    sourceType: null,
    sourceId: null,
    sourceUrl: null,
    sourceUpdatedUtc: null,
    tags: ['backlog', 'refinement'],
    acceptanceCriteria: ['Replay fills parameters', 'History preserves redaction'],
    nonGoals: ['Redesign the API Explorer'],
    rolloutConstraints: ['Keep objective compatibility routes intact'],
    evidenceLinks: ['https://example.test/evidence'],
    fleetIds: [],
    vesselIds: [],
    planningSessionIds: [],
    voyageIds: [],
    missionIds: [],
    checkRunIds: [],
    releaseIds: [],
    deploymentIds: [],
    incidentIds: [],
    createdUtc: '2026-05-01T00:00:00Z',
    lastUpdateUtc: '2026-05-02T00:00:00Z',
    completedUtc: null,
    ...overrides,
  };
}

describe('backlog list logic (shared with the mobile app)', () => {
  const a = createObjective({ id: 'obj_a', title: 'Alpha', rank: 2, priority: 'P2', backlogState: 'Inbox', lastUpdateUtc: '2026-05-01T00:00:00Z', dueUtc: '2026-06-01T00:00:00Z', vesselIds: ['vsl_1'] });
  const b = createObjective({ id: 'obj_b', title: 'Beta', rank: 1, priority: 'P0', backlogState: 'ReadyForDispatch', lastUpdateUtc: '2026-05-03T00:00:00Z', dueUtc: null, owner: 'ada', tags: ['area:ui'] });
  const c = createObjective({ id: 'obj_c', title: 'Gamma', rank: 3, priority: 'P1', status: 'Blocked', backlogState: 'ReadyForPlanning', lastUpdateUtc: '2026-05-02T00:00:00Z', dueUtc: '2026-05-15T00:00:00Z' });
  const all = [a, b, c];

  it('counts group views', () => {
    expect(countBacklogGroups(all)).toEqual({ all: 3, inbox: 1, planning: 0, dispatch: 1, blocked: 1 });
  });

  it('filters by group, fields, owner, and free-text search', () => {
    expect(filterBacklog(all, DEFAULT_BACKLOG_FILTERS).map((o) => o.id)).toEqual(['obj_a', 'obj_b', 'obj_c']);
    expect(filterBacklog(all, { ...DEFAULT_BACKLOG_FILTERS, group: 'blocked' }).map((o) => o.id)).toEqual(['obj_c']);
    expect(filterBacklog(all, { ...DEFAULT_BACKLOG_FILTERS, priority: 'P0' }).map((o) => o.id)).toEqual(['obj_b']);
    expect(filterBacklog(all, { ...DEFAULT_BACKLOG_FILTERS, vesselId: 'vsl_1' }).map((o) => o.id)).toEqual(['obj_a']);
    expect(filterBacklog(all, { ...DEFAULT_BACKLOG_FILTERS, owner: ' AD ' }).map((o) => o.id)).toEqual(['obj_b']);
    expect(filterBacklog(all, { ...DEFAULT_BACKLOG_FILTERS, search: 'area:u' }).map((o) => o.id)).toEqual(['obj_b']);
    expect(filterBacklog(all, { ...DEFAULT_BACKLOG_FILTERS, search: 'obj_c' }).map((o) => o.id)).toEqual(['obj_c']);
    expect(filterBacklog(all, { ...DEFAULT_BACKLOG_FILTERS, title: 'gam' }).map((o) => o.id)).toEqual(['obj_c']);
  });

  it('sorts by rank, priority, last updated, and due date', () => {
    expect(sortBacklog(all, 'rank').map((o) => o.id)).toEqual(['obj_b', 'obj_a', 'obj_c']);
    expect(sortBacklog(all, 'priority').map((o) => o.id)).toEqual(['obj_b', 'obj_c', 'obj_a']);
    expect(sortBacklog(all, 'updated').map((o) => o.id)).toEqual(['obj_b', 'obj_c', 'obj_a']);
    expect(sortBacklog(all, 'due').map((o) => o.id)).toEqual(['obj_c', 'obj_a', 'obj_b']);
  });

  it('reports active filters, including a non-default sort', () => {
    expect(hasActiveBacklogFilters(DEFAULT_BACKLOG_FILTERS)).toBe(false);
    expect(hasActiveBacklogFilters({ ...DEFAULT_BACKLOG_FILTERS, sortBy: 'due' })).toBe(true);
    expect(hasActiveBacklogFilters({ ...DEFAULT_BACKLOG_FILTERS, owner: '  ' })).toBe(false);
  });

  it('swaps ranks with the neighbor in rank order and stops at the ends', () => {
    expect(backlogRankSwap(all, 'obj_a', -1)).toEqual({ items: [{ objectiveId: 'obj_a', rank: 1 }, { objectiveId: 'obj_b', rank: 2 }] });
    expect(backlogRankSwap(all, 'obj_b', -1)).toBeNull();
    expect(backlogRankSwap(all, 'obj_c', 1)).toBeNull();
    expect(backlogRankSwap(all, 'missing', 1)).toBeNull();
    const updated = applyBacklogReorder(all, [{ ...a, rank: 1 }]);
    expect(updated.find((o) => o.id === 'obj_a')?.rank).toBe(1);
    expect(updated).toHaveLength(3);
  });
});

describe('backlog item form (shared with the mobile app)', () => {
  it('round-trips an objective through the form into the upsert payload', () => {
    const objective = createObjective({ tags: ['area:ui', 'solo'], vesselIds: ['vsl_1', 'vsl_2'], dueUtc: '2026-06-01T12:30:00.000Z' });
    const form = objectiveFormFrom(objective);
    expect(form.rank).toBe('12');
    expect(form.tagEntries).toEqual([{ key: 'area', value: 'ui' }, { key: 'solo', value: '' }]);
    const payload = objectivePayloadFromForm(form);
    expect(payload).toMatchObject({
      title: 'Backlog hardening',
      rank: 12,
      tags: ['area:ui', 'solo'],
      vesselIds: ['vsl_1', 'vsl_2'],
      acceptanceCriteria: ['Replay fills parameters', 'History preserves redaction'],
      suggestedPlaybooks: objective.suggestedPlaybooks,
      dueUtc: '2026-06-01T12:30:00.000Z',
    });
  });

  it('maps blank text to null and keeps create defaults', () => {
    const payload = objectivePayloadFromForm({ ...emptyObjectiveForm(), title: '  New  ', rank: 'x' });
    expect(payload).toMatchObject({ title: 'New', description: null, rank: null, status: 'Draft', kind: 'Feature', priority: 'P2', backlogState: 'Inbox', effort: 'M', tags: [], dueUtc: null });
  });

  it('parses tags, dates, and primary linked ids', () => {
    expect(parseTagEntries([])).toEqual([{ key: '', value: '' }]);
    expect(parseTagEntries(['k=v'])).toEqual([{ key: 'k', value: 'v' }]);
    expect(serializeTagEntries([{ key: ' a ', value: '' }, { key: '', value: '' }, { key: 'b', value: 'c' }])).toEqual(['a', 'b:c']);
    expect(toDateTimeLocalValue('not a date')).toBe('');
    expect(toDateTimeLocalValue(null)).toBe('');
    expect(toIsoOrNull('  ')).toBeNull();
    expect(toIsoOrNull('nope')).toBeNull();
    expect(replacePrimaryLinkedId('vsl_1\nvsl_2', 'vsl_3')).toBe('vsl_3\nvsl_2');
    expect(replacePrimaryLinkedId('vsl_1', '')).toBe('');
  });
});
