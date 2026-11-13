import { describe, expect, it } from 'vitest';
import type { Mission, Voyage, VoyageProgress } from '../types/models';
import { formatDeliveryMode, isVoyageActive, parseCaptainOverrides, retryMissionPayload, splitVoyageResponse, voyageProgress, voyageProgressFromDetail } from './voyageDetail';

describe('voyage detail helpers', () => {
  it('summarizes progress', () => {
    expect(voyageProgress([])).toEqual({ total: 0, completed: 0, failed: 0, percent: 0 });
    expect(voyageProgress([{ status: 'Complete' }, { status: 'Failed' }, { status: 'InProgress' }])).toEqual({ total: 3, completed: 1, failed: 1, percent: 33 });
  });

  it('knows which voyages are still running', () => {
    expect(isVoyageActive('Open')).toBe(true);
    expect(isVoyageActive('InProgress')).toBe(true);
    expect(isVoyageActive('Complete')).toBe(false);
    expect(isVoyageActive(null)).toBe(false);
  });

  it('parses captain overrides defensively', () => {
    expect(parseCaptainOverrides(null)).toEqual([]);
    expect(parseCaptainOverrides('not json')).toEqual([]);
    expect(parseCaptainOverrides('{"a":1}')).toEqual([]);
    expect(parseCaptainOverrides('[{"persona":"Worker","captainId":"cpt_1"}]')).toEqual([{ persona: 'Worker', captainId: 'cpt_1' }]);
  });

  it('formats delivery modes', () => {
    expect(formatDeliveryMode('InlineFullContent')).toBe('Inline Full Content');
    expect(formatDeliveryMode('AttachIntoWorktree')).toBe('Attach Into Worktree');
  });

  it('builds the retry payload', () => {
    expect(retryMissionPayload({ title: 'T', description: null, vesselId: 'vsl_1', voyageId: 'vyg_1', priority: 5 } as unknown as Mission))
      .toEqual({ title: 'T', description: undefined, vesselId: 'vsl_1', voyageId: 'vyg_1', priority: 5 });
  });

  it('splits the voyage detail (counts, no embedded missions) and a bare voyage', () => {
    const voyage = { id: 'vyg_1' } as Voyage;
    const detail = { voyage, totalMissions: 3, completedMissions: 1, failedMissions: 1, inProgressMissions: 1, missionCountsByStatus: { Complete: 1, Failed: 1, InProgress: 1 }, vesselId: 'vsl_1', vesselCount: 1 } as VoyageProgress;
    expect(splitVoyageResponse(detail)).toEqual({ voyage, detail });
    expect(splitVoyageResponse({ voyage })?.detail?.missionCountsByStatus).toEqual({});
    expect(splitVoyageResponse(voyage)).toEqual({ voyage, detail: null });
  });

  it('computes progress from the server counts, not from one page of missions', () => {
    expect(voyageProgressFromDetail(null)).toEqual({ total: 0, completed: 0, failed: 0, percent: 0 });
    expect(voyageProgressFromDetail({ totalMissions: 2500, missionCountsByStatus: { Complete: 1250, Failed: 10 } })).toEqual({ total: 2500, completed: 1250, failed: 10, percent: 50 });
  });
});
