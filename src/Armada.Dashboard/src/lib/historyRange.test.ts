import { describe, expect, it } from 'vitest';
import { historyRangeStart, parseHistoryRange } from './historyRange';

describe('history ranges', () => {
  it('defaults to the last day and reads a known range from the query', () => {
    expect(parseHistoryRange(null)).toBe('lastDay');
    expect(parseHistoryRange('nonsense')).toBe('lastDay');
    expect(parseHistoryRange('lastWeek')).toBe('lastWeek');
    expect(parseHistoryRange('all')).toBe('all');
  });

  it('starts each range that far back, and All Time has no start', () => {
    const now = Date.parse('2026-11-21T12:00:00Z');
    expect(historyRangeStart('lastHour', now)).toBe('2026-11-21T11:00:00.000Z');
    expect(historyRangeStart('lastMonth', now)).toBe('2026-10-22T12:00:00.000Z');
    expect(historyRangeStart('all', now)).toBeUndefined();
  });
});
