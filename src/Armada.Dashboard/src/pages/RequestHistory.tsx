import { useCallback, useEffect, useMemo, useState } from 'react';
import { createPortal } from 'react-dom';
import { useNavigate, useParams } from 'react-router-dom';
import PageHeader from '../components/shared/PageHeader';
import {
  deleteRequestHistoryByFilter,
  deleteRequestHistoryEntries,
  deleteRequestHistoryEntry,
  getRequestHistoryEntry,
  getRequestHistorySummary,
  listRequestHistory,
} from '../api/client';
import type {
  RequestHistoryDetail,
  RequestHistoryEntry,
  RequestHistoryQuery,
  RequestHistoryRecord,
  RequestHistorySummaryBucket,
  RequestHistorySummaryResult,
} from '../types/models';
import Pagination from '../components/shared/Pagination';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import RefreshButton from '../components/shared/RefreshButton';
import AutoRefreshSelect from '../components/shared/AutoRefreshSelect';
import CopyButton from '../components/shared/CopyButton';
import ErrorModal from '../components/shared/ErrorModal';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import { useAuth } from '../context/AuthContext';
import { formatBytes, parseJsonString } from '../lib/format';

type ActivityRangeId = 'lastHour' | 'lastDay' | 'lastWeek' | 'lastMonth';

interface ActivityRangeOption {
  id: ActivityRangeId;
  label: string;
  bucketMinutes: number;
  sliceCount: number;
}

interface HistoryChartTooltipState {
  bucket: RequestHistorySummaryBucket;
  clientX: number;
  clientY: number;
}

interface FiltersState {
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

const ACTIVITY_RANGE_OPTIONS: ActivityRangeOption[] = [
  { id: 'lastHour', label: 'Last Hour', bucketMinutes: 1, sliceCount: 60 },
  { id: 'lastDay', label: 'Last Day', bucketMinutes: 15, sliceCount: 96 },
  { id: 'lastWeek', label: 'Last Week', bucketMinutes: 120, sliceCount: 84 },
  { id: 'lastMonth', label: 'Last Month', bucketMinutes: 720, sliceCount: 60 },
];

function toLocalInputValue(date: Date) {
  const offsetMs = date.getTimezoneOffset() * 60000;
  return new Date(date.getTime() - offsetMs).toISOString().slice(0, 16);
}

function buildApiDate(value: string) {
  return value ? new Date(value).toISOString() : undefined;
}

function formatChartLabel(value: string, rangeId: ActivityRangeId) {
  const date = new Date(value);
  if (rangeId === 'lastHour' || rangeId === 'lastDay') {
    return date.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
  }
  if (rangeId === 'lastWeek') {
    return date.toLocaleString([], { weekday: 'short', hour: 'numeric' });
  }
  return date.toLocaleString([], { month: 'short', day: 'numeric' });
}

function floorToBucketTimestamp(value: string, bucketMs: number) {
  return Math.floor(new Date(value).getTime() / bucketMs) * bucketMs;
}

function getActivityRangeConfig(rangeId: ActivityRangeId) {
  return ACTIVITY_RANGE_OPTIONS.find((option) => option.id === rangeId) ?? ACTIVITY_RANGE_OPTIONS[1];
}

function getActivityRangeWindow(rangeId: ActivityRangeId, now = new Date()) {
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

function normalizeSummaryBuckets(summary: RequestHistorySummaryResult | null, rangeId: ActivityRangeId) {
  const range = getActivityRangeWindow(rangeId);
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

function getTooltipPosition(clientX: number, clientY: number) {
  const tooltipWidth = 280;
  const tooltipHeight = 136;
  const offset = 14;
  const viewportWidth = window.innerWidth;
  const viewportHeight = window.innerHeight;
  let left = clientX + offset;
  let top = clientY + offset;
  if (left + tooltipWidth > viewportWidth - 12) left = clientX - tooltipWidth - offset;
  if (top + tooltipHeight > viewportHeight - 12) top = clientY - tooltipHeight - offset;
  return {
    left: Math.max(12, left),
    top: Math.max(12, top),
  };
}

function HistoryChart({
  summary,
  rangeId,
  t,
  formatDateTime,
}: {
  summary: RequestHistorySummaryResult | null;
  rangeId: ActivityRangeId;
  t: (text: string, params?: Record<string, string | number | null | undefined>) => string;
  formatDateTime: (utc: string | null | undefined) => string;
}) {
  const [tooltip, setTooltip] = useState<HistoryChartTooltipState | null>(null);
  const buckets = normalizeSummaryBuckets(summary, rangeId);
  const windowTotal = buckets.reduce((sum, bucket) => sum + bucket.totalCount, 0);
  const maxCount = Math.max(...buckets.map((bucket) => bucket.totalCount), 1);
  const labels = useMemo(() => {
    if (buckets.length === 0) return [];
    const labelCount = Math.min(8, buckets.length);
    const indices = new Set<number>();
    if (labelCount === 1) {
      indices.add(0);
    } else {
      for (let index = 0; index < labelCount; index += 1) {
        indices.add(Math.round((index * (buckets.length - 1)) / (labelCount - 1)));
      }
    }

    return Array.from(indices)
      .sort((left, right) => left - right)
      .map((index, position, values) => ({
        key: buckets[index].bucketStartUtc,
        label: formatChartLabel(buckets[index].bucketStartUtc, rangeId),
        left: values.length === 1 ? 0 : (position / (values.length - 1)) * 100,
        align: position === 0 ? 'start' : position === values.length - 1 ? 'end' : 'center',
      }));
  }, [buckets, rangeId]);

  if (windowTotal === 0) {
    return (
      <div className="request-history-empty">
        {t('No requests in this time range. Widen the range above to see older traffic.')}
      </div>
    );
  }

  return (
    <>
      <div className="request-chart-shell">
        <div className="request-chart-axis-label">{t('Requests')}</div>
        <div className="request-chart-main">
          <div className="request-chart-axis-values">
            {[maxCount, Math.round(maxCount / 2), 0]
              .filter((value, index, values) => values.indexOf(value) === index)
              .map((value) => (
                <span key={value}>{value}</span>
              ))}
          </div>
          <div className="request-chart-plot">
            {buckets.map((bucket) => {
              const totalHeight = `${Math.max((bucket.totalCount / maxCount) * 100, bucket.totalCount > 0 ? 6 : 2)}%`;
              const successHeight = bucket.totalCount > 0 ? `${(bucket.successCount / bucket.totalCount) * 100}%` : '0%';
              const failureHeight = bucket.totalCount > 0 ? `${(bucket.failureCount / bucket.totalCount) * 100}%` : '0%';
              return (
                <div
                  key={bucket.bucketStartUtc}
                  className={`request-chart-column${tooltip?.bucket.bucketStartUtc === bucket.bucketStartUtc ? ' is-hovered' : ''}`}
                  onMouseEnter={(event) => setTooltip({ bucket, clientX: event.clientX, clientY: event.clientY })}
                  onMouseMove={(event) => setTooltip({ bucket, clientX: event.clientX, clientY: event.clientY })}
                  onMouseLeave={() => setTooltip(null)}
                >
                  <div className="request-chart-track">
                    <div className="request-chart-bar" style={{ height: totalHeight }}>
                      <span className="request-chart-success" style={{ height: successHeight }} />
                      <span className="request-chart-failure" style={{ height: failureHeight }} />
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
          <div className="request-chart-label-row">
            {labels.map((label) => (
              <span
                key={label.key}
                className={`request-chart-label request-chart-label-${label.align}`}
                style={{ left: `${label.left}%` }}
              >
                {label.label}
              </span>
            ))}
          </div>
        </div>
      </div>
      {tooltip &&
        createPortal(
          <div
            className="request-chart-tooltip"
            style={{
              left: `${getTooltipPosition(tooltip.clientX, tooltip.clientY).left}px`,
              top: `${getTooltipPosition(tooltip.clientX, tooltip.clientY).top}px`,
            }}
          >
            <strong>
              {formatDateTime(tooltip.bucket.bucketStartUtc)} - {formatDateTime(tooltip.bucket.bucketEndUtc)}
            </strong>
            <span>{t('{{count}} total', { count: tooltip.bucket.totalCount })}</span>
            <span>{t('{{count}} success', { count: tooltip.bucket.successCount })}</span>
            <span>{t('{{count}} failed', { count: tooltip.bucket.failureCount })}</span>
            <span>{t('{{ms}} ms avg', { ms: tooltip.bucket.averageDurationMs.toFixed(2) })}</span>
          </div>,
          document.body,
        )}
    </>
  );
}

function RequestDetailBlock({
  title,
  note,
  value,
  defaultExpanded = true,
}: {
  title: string;
  note?: string;
  value: unknown;
  defaultExpanded?: boolean;
}) {
  const [expanded, setExpanded] = useState(defaultExpanded);
  const text = typeof value === 'string' ? value : JSON.stringify(value ?? {}, null, 2);
  const hasContent = !!text && text !== '{}' && text !== '(empty)';
  return (
    <div className="request-detail-block">
      <div className="request-detail-block-row">
        <button
          type="button"
          className="request-detail-block-toggle"
          aria-expanded={expanded}
          onClick={() => setExpanded((current) => !current)}
        >
          <div className="request-detail-block-header">
            <div className="request-detail-block-title">
              <span className={`request-detail-block-chevron${expanded ? ' expanded' : ''}`} aria-hidden="true">
                <svg viewBox="0 0 16 16" focusable="false">
                  <path d="M5 3.5 10 8l-5 4.5" />
                </svg>
              </span>
              <h4>{title}</h4>
            </div>
            {note && <span className="request-detail-block-note">{note}</span>}
          </div>
        </button>
        {hasContent && <CopyButton text={text} title={`Copy ${title}`} />}
      </div>
      {expanded && <pre className="request-detail-code">{text || '(empty)'}</pre>}
    </div>
  );
}

function buildReplayState(record: RequestHistoryRecord) {
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

export default function RequestHistory() {
  const navigate = useNavigate();
  const { id } = useParams();
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const { isAdmin, isTenantAdmin } = useAuth();

  const defaultFilters = useMemo<FiltersState>(() => ({
    method: '',
    route: '',
    statusCode: '',
    principal: '',
    tenantId: '',
    userId: '',
    credentialId: '',
    isSuccess: 'all',
    fromUtc: toLocalInputValue(new Date(Date.now() - 24 * 60 * 60 * 1000)),
    toUtc: toLocalInputValue(new Date()),
  }), []);

  const [filters, setFilters] = useState<FiltersState>(defaultFilters);
  // Filters are a supporting control surface, not the primary content, so the section starts collapsed.
  const [filtersExpanded, setFiltersExpanded] = useState(false);
  const [activityRange, setActivityRange] = useState<ActivityRangeId>('lastDay');
  const [entries, setEntries] = useState<RequestHistoryEntry[]>([]);
  const [summary, setSummary] = useState<RequestHistorySummaryResult | null>(null);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [totalPages, setTotalPages] = useState(1);
  const [totalRecords, setTotalRecords] = useState(0);
  const [totalMs, setTotalMs] = useState(0);
  const [loading, setLoading] = useState(true);
  const [summaryLoading, setSummaryLoading] = useState(true);
  const [error, setError] = useState('');
  const [selectedIds, setSelectedIds] = useState<string[]>([]);
  const [detailRecord, setDetailRecord] = useState<RequestHistoryRecord | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<RequestHistoryEntry | null>(null);
  const [deleteSelectedOpen, setDeleteSelectedOpen] = useState(false);
  const [deleteFilteredOpen, setDeleteFilteredOpen] = useState(false);

  const query = useMemo<RequestHistoryQuery>(() => ({
    pageNumber,
    pageSize,
    method: filters.method || undefined,
    route: filters.route || undefined,
    principal: filters.principal || undefined,
    tenantId: filters.tenantId || undefined,
    userId: filters.userId || undefined,
    credentialId: filters.credentialId || undefined,
    statusCode: filters.statusCode ? Number(filters.statusCode) : undefined,
    isSuccess: filters.isSuccess === 'all' ? undefined : filters.isSuccess === 'true',
    fromUtc: buildApiDate(filters.fromUtc),
    toUtc: buildApiDate(filters.toUtc),
  }), [filters, pageNumber, pageSize]);

  const summaryQuery = useMemo<RequestHistoryQuery>(() => {
    const range = getActivityRangeWindow(activityRange);
    return {
      method: filters.method || undefined,
      route: filters.route || undefined,
      principal: filters.principal || undefined,
      tenantId: filters.tenantId || undefined,
      userId: filters.userId || undefined,
      credentialId: filters.credentialId || undefined,
      statusCode: filters.statusCode ? Number(filters.statusCode) : undefined,
      isSuccess: filters.isSuccess === 'all' ? undefined : filters.isSuccess === 'true',
      fromUtc: range.startUtc.toISOString(),
      toUtc: range.endUtc.toISOString(),
      bucketMinutes: range.bucketMinutes,
    };
  }, [activityRange, filters]);

  const hasActiveFilters = useMemo(() => (
    filters.method !== ''
    || filters.route !== ''
    || filters.statusCode !== ''
    || filters.principal !== ''
    || filters.tenantId !== ''
    || filters.userId !== ''
    || filters.credentialId !== ''
    || filters.isSuccess !== 'all'
  ), [filters]);

  const allSelected = entries.length > 0 && entries.length === selectedIds.length;

  const loadEntries = useCallback(async () => {
    try {
      setLoading(true);
      const result = await listRequestHistory(query);
      setEntries(result.objects || []);
      setTotalPages(result.totalPages || 1);
      setTotalRecords(result.totalRecords || 0);
      setTotalMs(result.totalMs || 0);
      setSelectedIds([]);
      setError('');
    } catch (err) {
      setError(err instanceof Error ? err.message : t('Failed to load request history.'));
    } finally {
      setLoading(false);
    }
  }, [query, t]);

  const loadSummary = useCallback(async () => {
    try {
      setSummaryLoading(true);
      const result = await getRequestHistorySummary(summaryQuery);
      setSummary(result);
    } catch (err) {
      setError(err instanceof Error ? err.message : t('Failed to load request history summary.'));
    } finally {
      setSummaryLoading(false);
    }
  }, [summaryQuery, t]);

  const openDetail = useCallback(async (entryId: string, routePush = true) => {
    try {
      setDetailLoading(true);
      if (routePush) navigate(`/requests/${entryId}`);
      const record = await getRequestHistoryEntry(entryId);
      setDetailRecord(record);
      setError('');
    } catch (err) {
      setError(err instanceof Error ? err.message : t('Failed to load request detail.'));
      if (routePush) navigate('/requests');
    } finally {
      setDetailLoading(false);
    }
  }, [navigate, t]);

  useEffect(() => {
    loadEntries();
  }, [loadEntries]);

  useEffect(() => {
    loadSummary();
  }, [loadSummary]);

  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('requesthistory', async () => { await Promise.all([loadEntries(), loadSummary()]); });

  useEffect(() => {
    if (!id) {
      setDetailRecord(null);
      setDetailLoading(false);
      return;
    }

    if (detailRecord?.entry.id === id) return;
    void openDetail(id, false);
  }, [detailRecord?.entry.id, id, openDetail]);

  const handleReplay = useCallback(async (entryId: string) => {
    try {
      const record = await getRequestHistoryEntry(entryId);
      navigate('/api-explorer', { state: { replayRequest: buildReplayState(record) } });
    } catch (err) {
      setError(err instanceof Error ? err.message : t('Failed to prepare replay request.'));
    }
  }, [navigate, t]);

  const handleDeleteSingle = useCallback(async () => {
    if (!deleteTarget) return;
    try {
      await deleteRequestHistoryEntry(deleteTarget.id);
      pushToast('warning', t('Request entry deleted.'));
      setDeleteTarget(null);
      if (detailRecord?.entry.id === deleteTarget.id) {
        setDetailRecord(null);
        navigate('/requests');
      }
      await loadEntries();
      await loadSummary();
    } catch (err) {
      setError(err instanceof Error ? err.message : t('Failed to delete request history entry.'));
    }
  }, [deleteTarget, detailRecord?.entry.id, loadEntries, loadSummary, navigate, pushToast, t]);

  const handleDeleteSelected = useCallback(async () => {
    try {
      await deleteRequestHistoryEntries(selectedIds);
      pushToast('warning', t('Deleted {{count}} request entries.', { count: selectedIds.length }));
      setDeleteSelectedOpen(false);
      await loadEntries();
      await loadSummary();
    } catch (err) {
      setError(err instanceof Error ? err.message : t('Failed to delete selected request entries.'));
    }
  }, [loadEntries, loadSummary, pushToast, selectedIds, t]);

  const handleDeleteFiltered = useCallback(async () => {
    try {
      const deleteQuery: RequestHistoryQuery = { ...query };
      delete deleteQuery.pageNumber;
      delete deleteQuery.pageSize;
      await deleteRequestHistoryByFilter(deleteQuery);
      pushToast('warning', t('Deleted the current filtered request set.'));
      setDeleteFilteredOpen(false);
      setPageNumber(1);
      await loadEntries();
      await loadSummary();
    } catch (err) {
      setError(err instanceof Error ? err.message : t('Failed to delete filtered request entries.'));
    }
  }, [loadEntries, loadSummary, pushToast, query, t]);

  const detailHeaders = useMemo(() => ({
    query: parseJsonString<Record<string, string | null>>(detailRecord?.detail?.queryParamsJson, {}),
    path: parseJsonString<Record<string, string | null>>(detailRecord?.detail?.pathParamsJson, {}),
    request: parseJsonString<Record<string, string | null>>(detailRecord?.detail?.requestHeadersJson, {}),
    response: parseJsonString<Record<string, string | null>>(detailRecord?.detail?.responseHeadersJson, {}),
  }), [detailRecord]);

  return (
    <div className="request-history-page">
      <PageHeader
        title={t('Requests')}
        subtitle={t('Inspect captured Armada API traffic, filter by route or principal, and replay stored requests into API Explorer.')}
        actions={(
          <>
            {selectedIds.length > 0 && (
              <button className="btn btn-danger btn-sm" onClick={() => setDeleteSelectedOpen(true)}>
                {t('Delete Selected')} ({selectedIds.length})
              </button>
            )}
            {totalRecords > 0 && (
              <button className="btn btn-danger btn-sm" onClick={() => setDeleteFilteredOpen(true)}>
                {hasActiveFilters ? t('Delete Filtered') : t('Delete Visible Range')}
              </button>
            )}
            <AutoRefreshSelect seconds={refreshSeconds} onChange={setRefreshSeconds} />
            <RefreshButton onRefresh={async () => { await Promise.all([loadEntries(), loadSummary()]); }} title={t('Refresh request data')} />
          </>
        )}
      />

      <ErrorModal error={error} onClose={() => setError('')} />

      <div className="request-history-summary-grid">
        <div className="card request-summary-card">
          <div className="card-label">{t('Total Requests')}</div>
          <div className="card-value">{summaryLoading ? '...' : (summary?.totalCount || 0).toLocaleString()}</div>
          <div className="card-detail text-dim">{t('Current activity window')}</div>
        </div>
        <div className="card request-summary-card">
          <div className="card-label">{t('Success Rate')}</div>
          <div className="card-value">{summaryLoading ? '...' : `${(summary?.successRate || 0).toFixed(1)}%`}</div>
          <div className="card-detail text-dim">{t('Based on visible summary range')}</div>
        </div>
        <div className="card request-summary-card">
          <div className="card-label">{t('Failures')}</div>
          <div className="card-value">{summaryLoading ? '...' : (summary?.failureCount || 0).toLocaleString()}</div>
          <div className="card-detail text-dim">{t('Non-successful responses')}</div>
        </div>
        <div className="card request-summary-card">
          <div className="card-label">{t('Average Duration')}</div>
          <div className="card-value">{summaryLoading ? '...' : `${(summary?.averageDurationMs || 0).toFixed(2)} ms`}</div>
          <div className="card-detail text-dim">{t('Across summary buckets')}</div>
        </div>
      </div>

      <div className="card request-history-activity-card">
        <div className="request-card-header">
          <div>
            <h3>{t('Activity')}</h3>
            <p className="text-dim">{t('Bucketed request volume with success and failure breakdown.')}</p>
          </div>
          <div className="request-range-tabs" role="tablist" aria-label={t('Activity range')}>
            {ACTIVITY_RANGE_OPTIONS.map((option) => (
              <button
                key={option.id}
                type="button"
                className={`request-range-tab${activityRange === option.id ? ' active' : ''}`}
                onClick={() => setActivityRange(option.id)}
              >
                {t(option.label)}
              </button>
            ))}
          </div>
        </div>
        <div className="request-card-body">
          {summaryLoading ? (
            <div className="request-history-empty">{t('Loading summary...')}</div>
          ) : (
            <HistoryChart summary={summary} rangeId={activityRange} t={t} formatDateTime={formatDateTime} />
          )}
        </div>
      </div>

      <div className="card request-history-filters-card">
        <div className="request-card-header">
          <button
            type="button"
            className="collapsible-header"
            onClick={() => setFiltersExpanded((current) => !current)}
            aria-expanded={filtersExpanded}
          >
            <span className="collapsible-caret" aria-hidden="true">{filtersExpanded ? '▾' : '▸'}</span>
            <span className="collapsible-title">{t('Filters')}</span>
            {hasActiveFilters && <span className="filter-active-pill">{t('Active')}</span>}
            <span className="text-dim" style={{ marginLeft: 'auto', fontWeight: 400 }}>
              {t('Constrain the paginated request table without leaving the dashboard.')}
            </span>
          </button>
          {filtersExpanded && (
            <button
              className="btn btn-sm"
              onClick={() => { setFilters(defaultFilters); setPageNumber(1); }}
              style={{ marginLeft: '0.75rem' }}
            >
              {t('Reset')}
            </button>
          )}
        </div>
        {filtersExpanded && (
        <div className="request-card-body">
          <div className="request-filter-grid">
            <label className="request-filter-field">
              <span>{t('Method')}</span>
              <select value={filters.method} onChange={(event) => { setFilters((current) => ({ ...current, method: event.target.value })); setPageNumber(1); }}>
                <option value="">{t('All methods')}</option>
                <option value="GET">GET</option>
                <option value="POST">POST</option>
                <option value="PUT">PUT</option>
                <option value="PATCH">PATCH</option>
                <option value="DELETE">DELETE</option>
              </select>
            </label>
            <label className="request-filter-field">
              <span>{t('Status Code')}</span>
              <input value={filters.statusCode} onChange={(event) => { setFilters((current) => ({ ...current, statusCode: event.target.value })); setPageNumber(1); }} placeholder="200" />
            </label>
            <label className="request-filter-field">
              <span>{t('Route')}</span>
              <input value={filters.route} onChange={(event) => { setFilters((current) => ({ ...current, route: event.target.value })); setPageNumber(1); }} placeholder="/api/v1/missions" />
            </label>
            <label className="request-filter-field">
              <span>{t('Principal')}</span>
              <input value={filters.principal} onChange={(event) => { setFilters((current) => ({ ...current, principal: event.target.value })); setPageNumber(1); }} placeholder={t('user@tenant')} />
            </label>
            <label className="request-filter-field">
              <span>{t('Credential')}</span>
              <input value={filters.credentialId} onChange={(event) => { setFilters((current) => ({ ...current, credentialId: event.target.value })); setPageNumber(1); }} placeholder="cred_" />
            </label>
            <label className="request-filter-field">
              <span>{t('Result')}</span>
              <select value={filters.isSuccess} onChange={(event) => { setFilters((current) => ({ ...current, isSuccess: event.target.value as FiltersState['isSuccess'] })); setPageNumber(1); }}>
                <option value="all">{t('All')}</option>
                <option value="true">{t('Success')}</option>
                <option value="false">{t('Failure')}</option>
              </select>
            </label>
            {isAdmin && (
              <label className="request-filter-field">
                <span>{t('Tenant')}</span>
                <input value={filters.tenantId} onChange={(event) => { setFilters((current) => ({ ...current, tenantId: event.target.value })); setPageNumber(1); }} placeholder="ten_" />
              </label>
            )}
            {(isAdmin || isTenantAdmin) && (
              <label className="request-filter-field">
                <span>{t('User')}</span>
                <input value={filters.userId} onChange={(event) => { setFilters((current) => ({ ...current, userId: event.target.value })); setPageNumber(1); }} placeholder="usr_" />
              </label>
            )}
            <label className="request-filter-field">
              <span>{t('From')}</span>
              <input type="datetime-local" value={filters.fromUtc} onChange={(event) => { setFilters((current) => ({ ...current, fromUtc: event.target.value })); setPageNumber(1); }} />
            </label>
            <label className="request-filter-field">
              <span>{t('To')}</span>
              <input type="datetime-local" value={filters.toUtc} onChange={(event) => { setFilters((current) => ({ ...current, toUtc: event.target.value })); setPageNumber(1); }} />
            </label>
          </div>
        </div>
        )}
      </div>

      <Pagination
        pageNumber={pageNumber}
        pageSize={pageSize}
        totalPages={totalPages}
        totalRecords={totalRecords}
        totalMs={totalMs}
        onPageChange={setPageNumber}
        onPageSizeChange={(size) => {
          setPageSize(size);
          setPageNumber(1);
        }}
      />

      <div className="table-wrap request-history-table-wrap">
        <table className="request-history-table">
          <thead>
            <tr>
              <th className="col-checkbox">
                <input
                  type="checkbox"
                  checked={allSelected}
                  onChange={(event) => setSelectedIds(event.target.checked ? entries.map((entry) => entry.id) : [])}
                  title={t('Select all visible requests')}
                />
              </th>
              <th>{t('When')}</th>
              <th>{t('Method')}</th>
              <th>{t('Route')}</th>
              <th>{t('Principal')}</th>
              <th>{t('Status')}</th>
              <th>{t('Duration')}</th>
              <th>{t('Payloads')}</th>
              <th className="text-right">{t('Actions')}</th>
            </tr>
          </thead>
          <tbody>
            {loading ? (
              <tr>
                <td colSpan={9} className="text-dim">{t('Loading request history...')}</td>
              </tr>
            ) : entries.length === 0 ? (
              <tr>
                <td colSpan={9} className="text-dim">{t('No request history entries match the current filters.')}</td>
              </tr>
            ) : (
              entries.map((entry) => (
                <tr key={entry.id} className="clickable" onClick={() => void openDetail(entry.id)}>
                  <td className="col-checkbox" onClick={(event) => event.stopPropagation()}>
                    <input
                      type="checkbox"
                      checked={selectedIds.includes(entry.id)}
                      onChange={() => setSelectedIds((current) => current.includes(entry.id) ? current.filter((idValue) => idValue !== entry.id) : [...current, entry.id])}
                      title={t('Select this request')}
                    />
                  </td>
                  <td title={formatDateTime(entry.createdUtc)}>{formatRelativeTime(entry.createdUtc)}</td>
                  <td><span className={`request-method-pill request-method-${entry.method.toLowerCase()}`}>{entry.method}</span></td>
                  <td className="request-route-cell mono" title={entry.route}>{entry.route}</td>
                  <td>{entry.principalDisplay || t('Anonymous')}</td>
                  <td><span className={`request-status-pill ${entry.isSuccess ? 'success' : 'error'}`}>{entry.statusCode}</span></td>
                  <td>{entry.durationMs.toFixed(2)} ms</td>
                  <td>{formatBytes(entry.requestSizeBytes)} / {formatBytes(entry.responseSizeBytes)}</td>
                  <td className="text-right" onClick={(event) => event.stopPropagation()}>
                    <ActionMenu
                      id={`request-${entry.id}`}
                      items={[
                        { label: 'View', onClick: () => void openDetail(entry.id) },
                        { label: 'Replay in API Explorer', onClick: () => void handleReplay(entry.id) },
                        { label: 'Delete', danger: true, onClick: () => setDeleteTarget(entry) },
                      ]}
                    />
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>

      {(detailLoading || detailRecord) && (
        <div className="modal-overlay" onClick={() => { setDetailRecord(null); navigate('/requests'); }}>
          <div className="modal-box request-history-detail-modal" onClick={(event) => event.stopPropagation()}>
            <div className="request-detail-header">
              <div>
                <h3>{t('Request Detail')}</h3>
                {detailRecord && (
                  <p className="text-dim">
                    {detailRecord.entry.method} {detailRecord.entry.route}
                  </p>
                )}
              </div>
              <div className="request-detail-header-actions">
                {detailRecord && (
                  <>
                    <button className="btn btn-sm" onClick={() => void handleReplay(detailRecord.entry.id)}>
                      {t('Replay')}
                    </button>
                    <button className="btn btn-danger btn-sm" onClick={() => setDeleteTarget(detailRecord.entry)}>
                      {t('Delete')}
                    </button>
                  </>
                )}
                <button className="btn btn-sm" onClick={() => { setDetailRecord(null); navigate('/requests'); }}>
                  {t('Close')}
                </button>
              </div>
            </div>
            {detailLoading || !detailRecord ? (
              <div className="request-history-empty">{t('Loading request detail...')}</div>
            ) : (
              <div className="request-detail-body">
                <div className="request-detail-summary-grid">
                  <div className="request-detail-summary-item">
                    <span>{t('Entry ID')}</span>
                    <strong className="mono">{detailRecord.entry.id}</strong>
                  </div>
                  <div className="request-detail-summary-item">
                    <span>{t('Principal')}</span>
                    <strong>{detailRecord.entry.principalDisplay || t('Anonymous')}</strong>
                  </div>
                  <div className="request-detail-summary-item">
                    <span>{t('Auth Method')}</span>
                    <strong>{detailRecord.entry.authMethod || '-'}</strong>
                  </div>
                  <div className="request-detail-summary-item">
                    <span>{t('Status')}</span>
                    <strong>{detailRecord.entry.statusCode}</strong>
                  </div>
                  <div className="request-detail-summary-item">
                    <span>{t('Duration')}</span>
                    <strong>{detailRecord.entry.durationMs.toFixed(2)} ms</strong>
                  </div>
                  <div className="request-detail-summary-item">
                    <span>{t('Captured')}</span>
                    <strong>{formatDateTime(detailRecord.entry.createdUtc)}</strong>
                  </div>
                </div>

                <div className="request-detail-grid">
                  <RequestDetailBlock key={`${detailRecord.entry.id}-path`} title={t('Path Parameters')} value={detailHeaders.path} />
                  <RequestDetailBlock key={`${detailRecord.entry.id}-query`} title={t('Query Parameters')} value={detailHeaders.query} />
                  <RequestDetailBlock key={`${detailRecord.entry.id}-request-headers`} title={t('Request Headers')} value={detailHeaders.request} />
                  <RequestDetailBlock key={`${detailRecord.entry.id}-response-headers`} title={t('Response Headers')} value={detailHeaders.response} />
                </div>

                <RequestDetailBlock
                  key={`${detailRecord.entry.id}-request-body`}
                  title={t('Request Body')}
                  value={detailRecord.detail?.requestBodyText || '(empty)'}
                  note={detailRecord.detail?.requestBodyTruncated ? t('Stored body was truncated') : formatBytes(detailRecord.entry.requestSizeBytes)}
                />
                <RequestDetailBlock
                  key={`${detailRecord.entry.id}-response-body`}
                  title={t('Response Body')}
                  value={detailRecord.detail?.responseBodyText || '(empty)'}
                  note={detailRecord.detail?.responseBodyTruncated ? t('Stored body was truncated') : formatBytes(detailRecord.entry.responseSizeBytes)}
                />
              </div>
            )}
          </div>
        </div>
      )}

      <ConfirmDialog
        open={!!deleteTarget}
        title={t('Delete Request Entry')}
        message={t('Delete the stored request-history entry for {{route}}?', { route: deleteTarget?.route || '' })}
        confirmLabel={t('Delete')}
        danger
        onConfirm={() => void handleDeleteSingle()}
        onCancel={() => setDeleteTarget(null)}
      />

      <ConfirmDialog
        open={deleteSelectedOpen}
        title={t('Delete Selected Requests')}
        message={t('Delete {{count}} selected request-history entries?', { count: selectedIds.length })}
        confirmLabel={t('Delete Selected')}
        danger
        onConfirm={() => void handleDeleteSelected()}
        onCancel={() => setDeleteSelectedOpen(false)}
      />

      <ConfirmDialog
        open={deleteFilteredOpen}
        title={t('Delete Filtered Requests')}
        message={t('Delete all request-history entries matching the current filters?')}
        confirmLabel={t('Delete Filtered')}
        danger
        onConfirm={() => void handleDeleteFiltered()}
        onCancel={() => setDeleteFilteredOpen(false)}
      />
    </div>
  );
}
