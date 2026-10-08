import type { RequestHistoryQuery, RequestHistoryRecord, RequestHistorySummaryBucket, RequestHistorySummaryResult } from '../types/models';
import { parseJsonString } from './format';

export type ActivityRangeId = 'lastHour' | 'lastDay' | 'lastWeek' | 'lastMonth';

export interface ActivityRangeOption {
  id: ActivityRangeId;
  label: string;
  bucketMinutes: number;
  sliceCount: number;
}

/** Request activity chart ranges (bucket size and number of buckets). */
export const ACTIVITY_RANGE_OPTIONS: ActivityRangeOption[] = [
  { id: 'lastHour', label: 'Last Hour', bucketMinutes: 1, sliceCount: 60 },
  { id: 'lastDay', label: 'Last Day', bucketMinutes: 15, sliceCount: 96 },
  { id: 'lastWeek', label: 'Last Week', bucketMinutes: 120, sliceCount: 84 },
  { id: 'lastMonth', label: 'Last Month', bucketMinutes: 720, sliceCount: 60 },
];

/** Request history filters (dates are local datetime-input values, 'yyyy-MM-ddTHH:mm'). */
export interface RequestHistoryFilters {
  method: string;
  route: string;
  statusCode: string;
  principal: string;
  tenantId: string;
  userId: string;
  credentialId: string;
  isSuccess: 'all' | 'true' | 'false';
  fromUtc: string;
  toUtc: string;
}

export function toLocalInputValue(date: Date): string {
  const offsetMs = date.getTimezoneOffset() * 60000;
  return new Date(date.getTime() - offsetMs).toISOString().slice(0, 16);
}

export function buildApiDate(value: string): string | undefined {
  return value ? new Date(value).toISOString() : undefined;
}

/** Default filters: nothing narrowed, the last 24 hours. */
export function defaultRequestHistoryFilters(now: number = Date.now()): RequestHistoryFilters {
  return {
    method: '',
    route: '',
    statusCode: '',
    principal: '',
    tenantId: '',
    userId: '',
    credentialId: '',
    isSuccess: 'all',
    fromUtc: toLocalInputValue(new Date(now - 24 * 60 * 60 * 1000)),
    toUtc: toLocalInputValue(new Date(now)),
  };
}

function baseQuery(filters: RequestHistoryFilters): RequestHistoryQuery {
  return {
    method: filters.method || undefined,
    route: filters.route || undefined,
    principal: filters.principal || undefined,
    tenantId: filters.tenantId || undefined,
    userId: filters.userId || undefined,
    credentialId: filters.credentialId || undefined,
    statusCode: filters.statusCode ? Number(filters.statusCode) : undefined,
    isSuccess: filters.isSuccess === 'all' ? undefined : filters.isSuccess === 'true',
  };
}

/** The paged list query for the filters. */
export function buildRequestHistoryQuery(filters: RequestHistoryFilters, pageNumber: number, pageSize: number): RequestHistoryQuery {
  return {
    pageNumber,
    pageSize,
    ...baseQuery(filters),
    fromUtc: buildApiDate(filters.fromUtc),
    toUtc: buildApiDate(filters.toUtc),
  };
}

/** The same query without paging (Delete Filtered). */
export function buildRequestHistoryDeleteQuery(filters: RequestHistoryFilters): RequestHistoryQuery {
  const query = buildRequestHistoryQuery(filters, 1, 1);
  delete query.pageNumber;
  delete query.pageSize;
  return query;
}

/** True when any filter other than the date range narrows the list. */
export function hasActiveRequestFilters(filters: RequestHistoryFilters): boolean {
  return filters.method !== ''
    || filters.route !== ''
    || filters.statusCode !== ''
    || filters.principal !== ''
    || filters.tenantId !== ''
    || filters.userId !== ''
    || filters.credentialId !== ''
    || filters.isSuccess !== 'all';
}

export function floorToBucketTimestamp(value: string, bucketMs: number): number {
  return Math.floor(new Date(value).getTime() / bucketMs) * bucketMs;
}

export function getActivityRangeConfig(rangeId: ActivityRangeId): ActivityRangeOption {
  return ACTIVITY_RANGE_OPTIONS.find((option) => option.id === rangeId) ?? ACTIVITY_RANGE_OPTIONS[1];
}

/** The bucket-aligned window of a range ending with the bucket that contains `now`. */
export function getActivityRangeWindow(rangeId: ActivityRangeId, now = new Date()) {
  const config = getActivityRangeConfig(rangeId);
  const bucketMs = config.bucketMinutes * 60 * 1000;
  const endExclusiveMs = Math.floor(now.getTime() / bucketMs) * bucketMs + bucketMs;
  const startMs = endExclusiveMs - config.sliceCount * bucketMs;
  return {
    ...config,
    bucketMs,
    startMs,
    endExclusiveMs,
    startUtc: new Date(startMs),
    endUtc: new Date(endExclusiveMs - 1),
  };
}

/** The summary query for the activity chart (filters plus the range window and bucket size). */
export function buildRequestHistorySummaryQuery(filters: RequestHistoryFilters, rangeId: ActivityRangeId, now = new Date()): RequestHistoryQuery {
  const range = getActivityRangeWindow(rangeId, now);
  return {
    ...baseQuery(filters),
    fromUtc: range.startUtc.toISOString(),
    toUtc: range.endUtc.toISOString(),
    bucketMinutes: range.bucketMinutes,
  };
}

/** Every bucket of the range, filled with zeros where the server returned none. */
export function normalizeSummaryBuckets(summary: RequestHistorySummaryResult | null, rangeId: ActivityRangeId, now = new Date()): RequestHistorySummaryBucket[] {
  const range = getActivityRangeWindow(rangeId, now);
  const apiBuckets = new Map<number, RequestHistorySummaryBucket>(
    (summary?.buckets || []).map((bucket) => [floorToBucketTimestamp(bucket.bucketStartUtc, range.bucketMs), bucket]),
  );

  return Array.from({ length: range.sliceCount }, (_, index) => {
    const bucketStartMs = range.startMs + index * range.bucketMs;
    const source = apiBuckets.get(bucketStartMs);
    return {
      bucketStartUtc: new Date(bucketStartMs).toISOString(),
      bucketEndUtc: new Date(bucketStartMs + range.bucketMs).toISOString(),
      totalCount: source?.totalCount || 0,
      successCount: source?.successCount || 0,
      failureCount: source?.failureCount || 0,
      averageDurationMs: source?.averageDurationMs || 0,
    };
  });
}

/** What API Explorer needs to replay a stored request. */
export interface RequestReplayState {
  method: string;
  route: string;
  routeTemplate: string | null;
  queryValues: Record<string, string | null>;
  headerValues: Record<string, string | null>;
  bodyValue: string;
  pathValues: Record<string, string | null>;
}

export function buildReplayState(record: RequestHistoryRecord): RequestReplayState {
  const detail = record.detail;
  return {
    method: record.entry.method,
    route: record.entry.route,
    routeTemplate: record.entry.routeTemplate,
    queryValues: parseJsonString<Record<string, string | null>>(detail?.queryParamsJson, {}),
    headerValues: parseJsonString<Record<string, string | null>>(detail?.requestHeadersJson, {}),
    bodyValue: detail?.requestBodyText || '',
    pathValues: parseJsonString<Record<string, string | null>>(detail?.pathParamsJson, {}),
  };
}

/** The parsed key/value blocks of a request detail (path, query, request headers, response headers). */
export function requestDetailMaps(record: RequestHistoryRecord | null) {
  return {
    query: parseJsonString<Record<string, string | null>>(record?.detail?.queryParamsJson, {}),
    path: parseJsonString<Record<string, string | null>>(record?.detail?.pathParamsJson, {}),
    request: parseJsonString<Record<string, string | null>>(record?.detail?.requestHeadersJson, {}),
    response: parseJsonString<Record<string, string | null>>(record?.detail?.responseHeadersJson, {}),
  };
}
