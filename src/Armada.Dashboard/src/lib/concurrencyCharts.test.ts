import { describe, expect, it } from 'vitest';
import { bucketBars, niceMax, waitGroupOf, WAIT_GROUPS } from './concurrencyCharts';
import type { ConcurrencyMetrics } from '../types/models';

describe('concurrency chart helpers', () => {
  it('groups every gate and keeps a fixed order of four groups', () => {
    expect(WAIT_GROUPS.map(g => g.key)).toEqual(['captain', 'vessel', 'capacity', 'readiness']);
    expect(waitGroupOf('CaptainSlot')).toBe('captain');
    expect(waitGroupOf('VoyageMissionSlot')).toBe('vessel');
    expect(waitGroupOf('Placement')).toBe('capacity');
    expect(waitGroupOf('Parent')).toBe('readiness');
    expect(waitGroupOf('SomethingNew')).toBe('readiness');
  });

  it('turns buckets into per-group minutes', () => {
    const metrics = { buckets: [{ startUtc: 't0', missionsWaiting: 3, byGate: [{ gate: 'CaptainSlot', seconds: 120, missions: 2 }, { gate: 'GlobalSlot', seconds: 60, missions: 1 }, { gate: 'RuntimeSlot', seconds: 60, missions: 1 }] }] } as unknown as ConcurrencyMetrics;
    const [bar] = bucketBars(metrics);
    expect(bar.minutes).toEqual({ captain: 2, vessel: 0, capacity: 2, readiness: 0 });
    expect(bar.totalMinutes).toBe(4);
    expect(bar.missionsWaiting).toBe(3);
    expect(bucketBars(null)).toEqual([]);
  });

  it('rounds axis maxima up to 1, 2 or 5 times a power of ten', () => {
    expect(niceMax(0)).toBe(1);
    expect(niceMax(3.2)).toBe(5);
    expect(niceMax(17)).toBe(20);
    expect(niceMax(100)).toBe(100);
    expect(niceMax(0.4)).toBe(0.5);
  });
});
