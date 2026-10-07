import { describe, expect, it } from 'vitest';
import type { Mission, Voyage } from '../types/models';
import { formatDeliveryMode, isVoyageActive, parseCaptainOverrides, retryMissionPayload, splitVoyageResponse, voyageProgress } from './voyageDetail';

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

  it('splits both getVoyage response shapes', () => {
    const voyage = { id: 'vyg_1' } as Voyage;
    expect(splitVoyageResponse({ voyage, missions: [{ id: 'msn_1' }] })).toEqual({ voyage, missions: [{ id: 'msn_1' }] });
    expect(splitVoyageResponse({ voyage })).toEqual({ voyage, missions: [] });
    expect(splitVoyageResponse(voyage)).toEqual({ voyage, missions: null });
  });
});
