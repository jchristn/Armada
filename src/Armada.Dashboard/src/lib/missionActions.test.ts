import { describe, expect, it } from 'vitest';
import {
  assignmentBlockerTitle,
  canRetryLanding,
  formatMissionDuration,
  isMissionLogCompleted,
  matchesMissionColumnFilters,
  missionLandingState,
  reviewVerdictNeedsComment,
} from './missionActions';

const t = (text: string, params?: Record<string, string | number>) =>
  text.replace(/\{\{(\w+)\}\}/g, (_m, key: string) => String(params?.[key] ?? ''));

const base = { status: 'WorkProduced', requiresReview: false, vesselId: 'vsl_1', branchName: 'armada/x' };

describe('missionLandingState', () => {
  it('offers Land on produced work and Retry Landing after a failed landing', () => {
    expect(missionLandingState(base, { isReadyToLand: true, manualLandingOnly: false })).toMatchObject({
      canLand: true, showLand: true, showManualMerge: false, isRetry: false, pill: { tone: 'ready', label: 'Ready To Land' },
    });
    expect(missionLandingState({ ...base, status: 'LandingFailed' }, null)).toMatchObject({ showLand: true, isRetry: true, pill: { label: 'Needs Review' } });
  });

  it('offers the manual merge instead of Land when the landing mode is None', () => {
    const state = missionLandingState(base, { isReadyToLand: false, manualLandingOnly: true });
    expect(state).toMatchObject({ showLand: false, showManualMerge: true, pill: { label: 'Merge By Hand' } });
    expect(missionLandingState({ ...base, branchName: null }, { isReadyToLand: false, manualLandingOnly: true }).showLand).toBe(true);
  });

  it('separates review gates from Mark Complete', () => {
    expect(missionLandingState({ ...base, status: 'Review', requiresReview: true }, null)).toMatchObject({ canResolveReview: true, canMarkComplete: false, canLand: false, pill: { label: 'Not Ready Yet' } });
    expect(missionLandingState({ ...base, status: 'Review' }, null)).toMatchObject({ canResolveReview: false, canMarkComplete: true, canLand: true });
    expect(missionLandingState({ ...base, status: 'Complete' }, null).pill).toEqual({ tone: 'ready', label: 'Landed' });
    expect(missionLandingState({ ...base, status: 'InProgress' }, null)).toMatchObject({ canLand: false, pill: { label: 'Not Ready Yet' } });
  });
});

describe('mission helpers', () => {
  it('formats runtimes', () => {
    expect(formatMissionDuration(null, t)).toBe('N/A');
    expect(formatMissionDuration(12_500, t)).toBe('12.5s');
    expect(formatMissionDuration(184_000, t)).toBe('3m 4s');
    expect(formatMissionDuration(180_000, t)).toBe('3m');
    expect(formatMissionDuration(3_720_000, t)).toBe('1h 2m');
  });

  it('filters by column text, knows retry and log completion, and titles blockers', () => {
    const m = { title: 'Fix Login', status: 'Failed', branchName: 'armada/fix' };
    expect(matchesMissionColumnFilters(m, { title: 'login', status: '', branch: '' })).toBe(true);
    expect(matchesMissionColumnFilters(m, { title: '', status: 'comp', branch: '' })).toBe(false);
    expect(matchesMissionColumnFilters({ ...m, branchName: null }, { title: '', status: '', branch: 'x' })).toBe(false);
    expect(canRetryLanding('LandingFailed')).toBe(true);
    expect(canRetryLanding('Failed')).toBe(false);
    expect(isMissionLogCompleted('Review')).toBe(true);
    expect(isMissionLogCompleted('InProgress')).toBe(false);
    expect(reviewVerdictNeedsComment('conditional')).toBe(true);
    expect(reviewVerdictNeedsComment('deny')).toBe(false);
    expect(assignmentBlockerTitle('NoIdleCaptain')).toBe('Waiting for a captain');
    expect(assignmentBlockerTitle('Unknown')).toBe('Waiting');
  });
});
