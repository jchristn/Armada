import { describe, expect, it } from 'vitest';
import { computeYTicks, historyBuckets, MISSION_HISTORY_RANGES, missionHistoryQuery } from './missionHistory';

describe('missionHistory', () => {
  it('builds the history query for a range ending now', () => {
    const week = MISSION_HISTORY_RANGES.find((r) => r.value === 'week')!;
    const now = new Date('2026-10-07T12:00:00.000Z');
    expect(missionHistoryQuery(week, '', 'vsl_1', now)).toEqual({
      fromUtc: '2026-09-30T12:00:00.000Z', toUtc: '2026-10-07T12:00:00.000Z', bucketMinutes: 60, fleetId: undefined, vesselId: 'vsl_1',
    });
  });

  it('maps buckets and computes ticks', () => {
    expect(historyBuckets(null)).toEqual([]);
    expect(historyBuckets({ totalCount: 3, completeCount: 2, failedCount: 1, otherCount: 0, fromUtc: '', toUtc: '', bucketMinutes: 60,
      buckets: [{ startUtc: '2026-10-07T00:00:00Z', completeCount: 2, failedCount: 1, otherCount: 0, totalCount: 3 }] }))
      .toEqual([{ timestampMs: Date.parse('2026-10-07T00:00:00Z'), complete: 2, failed: 1, other: 0 }]);
    expect(computeYTicks(0)).toEqual([0]);
    expect(computeYTicks(1)).toEqual([0, 1]);
    expect(computeYTicks(10)).toEqual([0, 3, 6, 9, 12]);
  });
});
