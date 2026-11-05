import { describe, expect, it } from 'vitest';
import { formatWaitDuration, slotWaitSeconds, summarizeWaits, waitBreakdown, waitGateLabel } from './missionWaits';
import type { MissionWaitInterval } from '../types/models';

function interval(gate: MissionWaitInterval['gate'], start: string, end: string | null): MissionWaitInterval {
  return { id: 'mwi_' + start, tenantId: null, missionId: 'msn_1', vesselId: null, voyageId: null, captainId: null, gate, startedUtc: start, endedUtc: end, detail: null };
}

describe('formatWaitDuration', () => {
  it('formats seconds, minutes, hours and days compactly', () => {
    expect(formatWaitDuration(45)).toBe('45s');
    expect(formatWaitDuration(720)).toBe('12m');
    expect(formatWaitDuration(3900)).toBe('1h 5m');
    expect(formatWaitDuration(7200)).toBe('2h');
    expect(formatWaitDuration(183600)).toBe('2d 3h');
    expect(formatWaitDuration(-5)).toBe('0s');
    expect(formatWaitDuration(null)).toBe('0s');
  });
});

describe('summarizeWaits', () => {
  it('sums per gate, separates slot wait and reports the open gate', () => {
    const now = Date.parse('2026-11-05T10:20:00Z');
    const summary = summarizeWaits([
      interval('Parent', '2026-11-05T10:00:00Z', '2026-11-05T10:05:00Z'),
      interval('VoyageMissionSlot', '2026-11-05T10:05:00Z', '2026-11-05T10:08:00Z'),
      interval('CaptainSlot', '2026-11-05T10:08:00Z', null),
    ], now);
    expect(summary.totalSeconds).toBe(1200);
    expect(summary.slotSeconds).toBe(900);
    expect(summary.byGate.map(g => g.gate)).toEqual(['CaptainSlot', 'Parent', 'VoyageMissionSlot']);
    expect(summary.current).toBe('CaptainSlot');
    expect(summary.currentSinceUtc).toBe('2026-11-05T10:08:00Z');
    expect(waitBreakdown(summary)).toBe('Captain slot 12m, Parent mission 5m, Voyage mission slot 3m');
  });

  it('handles no intervals and skips unparseable times', () => {
    const empty = summarizeWaits([]);
    expect(empty.totalSeconds).toBe(0);
    expect(empty.current).toBeNull();
    expect(waitBreakdown(empty)).toBe('');
    expect(summarizeWaits([interval('CaptainSlot', 'not a time', null)]).byGate).toEqual([]);
  });

  it('limits the breakdown to four gates', () => {
    const gates: MissionWaitInterval['gate'][] = ['CaptainSlot', 'GlobalSlot', 'RuntimeSlot', 'EndpointSlot', 'Placement'];
    const summary = summarizeWaits(gates.map((g, i) => interval(g, `2026-11-05T10:0${i}:00Z`, `2026-11-05T10:0${i}:30Z`)));
    expect(waitBreakdown(summary).endsWith(', +1')).toBe(true);
  });
});

describe('slotWaitSeconds and labels', () => {
  it('measures assignment minus ready and tolerates missing values', () => {
    expect(slotWaitSeconds('2026-11-05T10:00:00Z', '2026-11-05T10:09:00Z')).toBe(540);
    expect(slotWaitSeconds(null, '2026-11-05T10:09:00Z')).toBeNull();
    expect(slotWaitSeconds('2026-11-05T10:09:00Z', '2026-11-05T10:00:00Z')).toBe(0);
  });

  it('labels known gates and passes unknown ones through', () => {
    expect(waitGateLabel('VoyageMissionSlot')).toBe('Voyage mission slot');
    expect(waitGateLabel('FutureGate')).toBe('FutureGate');
    expect(waitGateLabel(null)).toBe('Other');
  });
});
