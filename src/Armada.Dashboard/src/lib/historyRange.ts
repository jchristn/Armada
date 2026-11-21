/** The time ranges of the History (All Activity) page: the API Requests ranges, plus every entry. */
export type HistoryRangeId = 'lastHour' | 'lastDay' | 'lastWeek' | 'lastMonth' | 'all';

export const HISTORY_RANGE_OPTIONS: Array<{ id: HistoryRangeId; label: string; hours: number }> = [
  { id: 'lastHour', label: 'Last Hour', hours: 1 },
  { id: 'lastDay', label: 'Last Day', hours: 24 },
  { id: 'lastWeek', label: 'Last Week', hours: 24 * 7 },
  { id: 'lastMonth', label: 'Last Month', hours: 24 * 30 },
  { id: 'all', label: 'All Time', hours: 0 },
];

/** The range named by a `range` query value; the default (and anything unknown) is the last day. */
export function parseHistoryRange(value: string | null | undefined): HistoryRangeId {
  return HISTORY_RANGE_OPTIONS.some((option) => option.id === value) ? (value as HistoryRangeId) : 'lastDay';
}

/** The start of a range as an ISO timestamp, or undefined for every entry. */
export function historyRangeStart(rangeId: HistoryRangeId, now: number = Date.now()): string | undefined {
  const option = HISTORY_RANGE_OPTIONS.find((item) => item.id === rangeId);
  if (!option || !option.hours) return undefined;
  return new Date(now - option.hours * 3600 * 1000).toISOString();
}
