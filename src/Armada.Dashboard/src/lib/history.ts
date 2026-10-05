import type { HistoricalTimelineEntry } from '../types/models';

/**
 * Timeline entries are a read-only aggregation; only request-sourced rows (SourceType "Request") map to a
 * deletable underlying record (the request-history entry). Decided by the typed source type, not the id prefix.
 */
export function canDeleteHistoryEntry(entry: Pick<HistoricalTimelineEntry, 'sourceType' | 'sourceId'>): boolean {
  // The server writes "Request"; some stubs and older clients use "request", so the enum value is compared
  // case-insensitively (the same as the TUI).
  return (entry.sourceType ?? '').toLowerCase() === 'request' && !!entry.sourceId;
}
