import type { HistoricalTimelineEntry, HistoricalTimelineQuery } from '../types/models';

/** History page filter state (the select values use 'all' for "no filter"). */
export interface HistoryFilterState {
  objectiveId: string;
  text: string;
  actor: string;
  vesselId: string;
  sourceType: string;
  postmortemOnly: boolean;
  showReadRequests: boolean;
}

/** The timeline query for the History filters (GET/HEAD/OPTIONS request rows are left out unless asked for). */
export function buildHistoryTimelineQuery(filters: HistoryFilterState, pageSize = 250, pageNumber = 1): HistoricalTimelineQuery {
  return {
    pageNumber,
    pageSize,
    objectiveId: filters.objectiveId === 'all' ? null : filters.objectiveId,
    text: filters.text || null,
    actor: filters.actor || null,
    vesselId: filters.vesselId === 'all' ? null : filters.vesselId,
    sourceTypes: filters.sourceType === 'all' ? [] : [filters.sourceType],
    postmortemOnly: filters.postmortemOnly || undefined,
    excludeReadRequests: !filters.showReadRequests,
  };
}

export function escapeCsvValue(value: string | null | undefined): string {
  const normalized = value || '';
  if (!/[",\r\n]/.test(normalized)) return normalized;
  return `"${normalized.replace(/"/g, '""')}"`;
}

/** History entries as CSV (the History page's Export CSV). */
export function buildHistoryCsv(entries: HistoricalTimelineEntry[]): string {
  const header = [
    'id',
    'sourceType',
    'title',
    'status',
    'severity',
    'occurredUtc',
    'actorDisplay',
    'vesselId',
    'missionId',
    'voyageId',
    'route',
    'description',
  ];
  const rows = entries.map((entry) => [
    escapeCsvValue(entry.id),
    escapeCsvValue(entry.sourceType),
    escapeCsvValue(entry.title),
    escapeCsvValue(entry.status),
    escapeCsvValue(entry.severity),
    escapeCsvValue(entry.occurredUtc),
    escapeCsvValue(entry.actorDisplay),
    escapeCsvValue(entry.vesselId),
    escapeCsvValue(entry.missionId),
    escapeCsvValue(entry.voyageId),
    escapeCsvValue(entry.route),
    escapeCsvValue(entry.description),
  ].join(','));
  return [header.join(','), ...rows].join('\r\n');
}

/** History entries as Markdown (the History page's Export Markdown). */
export function buildHistoryMarkdown(query: HistoricalTimelineQuery, entries: HistoricalTimelineEntry[], exportedUtc: string = new Date().toISOString()): string {
  const activeFilters: string[] = [];
  if (query.objectiveId) activeFilters.push(`objective=\`${query.objectiveId}\``);
  if (query.text) activeFilters.push(`text=\`${query.text}\``);
  if (query.actor) activeFilters.push(`actor=\`${query.actor}\``);
  if (query.vesselId) activeFilters.push(`vessel=\`${query.vesselId}\``);
  if (query.postmortemOnly) activeFilters.push('postmortemOnly=`true`');
  if (query.excludeReadRequests) activeFilters.push('excludeReadRequests=`true`');
  if (query.sourceTypes && query.sourceTypes.length > 0) activeFilters.push(`sourceTypes=\`${query.sourceTypes.join(', ')}\``);

  const lines: string[] = [
    '# Armada History Export',
    '',
    `Exported: ${exportedUtc}`,
    `Entries: ${entries.length}`,
  ];

  if (activeFilters.length > 0) {
    lines.push(`Filters: ${activeFilters.join(', ')}`);
  }

  lines.push('');
  for (const entry of entries) {
    lines.push(`## ${entry.title}`);
    lines.push(`- Source: ${entry.sourceType}`);
    lines.push(`- Time: ${entry.occurredUtc}`);
    if (entry.status) lines.push(`- Status: ${entry.status}`);
    if (entry.severity) lines.push(`- Severity: ${entry.severity}`);
    if (entry.actorDisplay) lines.push(`- Actor: ${entry.actorDisplay}`);
    if (entry.vesselId) lines.push(`- Vessel: ${entry.vesselId}`);
    if (entry.route) lines.push(`- Route: ${entry.route}`);
    if (entry.description) {
      lines.push('');
      lines.push(entry.description);
    }
    lines.push('');
  }

  return lines.join('\n');
}

/** History entries as the JSON export document. */
export function buildHistoryJson(query: HistoricalTimelineQuery, entries: HistoricalTimelineEntry[], exportedUtc: string = new Date().toISOString()): string {
  return JSON.stringify({
    exportedUtc,
    query,
    totalCount: entries.length,
    entries,
  }, null, 2);
}

/** Counts per source type, in first-seen order. */
export function countBySourceType(entries: HistoricalTimelineEntry[]): Map<string, number> {
  const counts = new Map<string, number>();
  for (const entry of entries) counts.set(entry.sourceType, (counts.get(entry.sourceType) || 0) + 1);
  return counts;
}
