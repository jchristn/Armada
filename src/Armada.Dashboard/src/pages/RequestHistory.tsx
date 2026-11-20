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
  RequestHistoryEntry,
  RequestHistoryQuery,
  RequestHistoryRecord,
  RequestHistorySummaryBucket,
  RequestHistorySummaryResult,
} from '../types/models';
import { useDeleteProgress } from '../lib/useDeleteProgress';
import { deleteMatchingRequestHistoryTitle } from '../lib/deleteProgress';
import DataTable, { type DataTableColumn } from '../components/shared/DataTable';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import CopyButton from '../components/shared/CopyButton';
import ErrorModal from '../components/shared/ErrorModal';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import { useAuth } from '../context/AuthContext';
import { formatBytes } from '../lib/format';
import {
  ACTIVITY_RANGE_OPTIONS,
  buildReplayState,
  buildRequestHistoryQuery,
  buildRequestHistorySummaryQuery,
  defaultRequestHistoryFilters,
  hasActiveRequestFilters,
  normalizeSummaryBuckets,
  requestDetailMaps,
  type ActivityRangeId,
  type RequestHistoryFilters,
} from '../lib/requestHistory';
import { reconcileServerPage, useListRequestGuard } from '../lib/serverPage';
import { usePersistedPageSize } from '../lib/usePersistedPageSize';

interface HistoryChartTooltipState {
  bucket: RequestHistorySummaryBucket;
  clientX: number;
  clientY: number;
}

type FiltersState = RequestHistoryFilters;

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

export default function RequestHistory() {
  const navigate = useNavigate();
  const { id } = useParams();
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const deletion = useDeleteProgress();
  const { pushToast } = useNotifications();
  const { isAdmin, isTenantAdmin } = useAuth();

  const defaultFilters = useMemo<FiltersState>(() => defaultRequestHistoryFilters(), []);

  const [filters, setFilters] = useState<FiltersState>(defaultFilters);
  // Filters are a supporting control surface, not the primary content, so the section starts collapsed.
  const [filtersExpanded, setFiltersExpanded] = useState(false);
  const [activityRange, setActivityRange] = useState<ActivityRangeId>('lastDay');
  const [entries, setEntries] = useState<RequestHistoryEntry[]>([]);
  const [summary, setSummary] = useState<RequestHistorySummaryResult | null>(null);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = usePersistedPageSize('request-history', 25);
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

  const query = useMemo<RequestHistoryQuery>(() => buildRequestHistoryQuery(filters, pageNumber, pageSize), [filters, pageNumber, pageSize]);

  const summaryQuery = useMemo<RequestHistoryQuery>(() => buildRequestHistorySummaryQuery(filters, activityRange), [activityRange, filters]);

  const hasActiveFilters = useMemo(() => hasActiveRequestFilters(filters), [filters]);

  const allSelected = entries.length > 0 && entries.length === selectedIds.length;

  const beginListRequest = useListRequestGuard();

  const loadEntries = useCallback(async () => {
    const request = beginListRequest();
    try {
      setLoading(true);
      const result = await listRequestHistory(query);
      if (!request.isCurrent()) return;
      const decision = reconcileServerPage(query.pageNumber ?? 1, result.totalPages);
      if (!decision.accept) {
        setPageNumber(decision.pageNumber);
        return;
      }
      setEntries(result.objects || []);
      setTotalPages(decision.totalPages);
      setTotalRecords(result.totalRecords || 0);
      setTotalMs(result.totalMs || 0);
      setSelectedIds([]);
      setError('');
    } catch (err) {
      if (request.isCurrent()) setError(err instanceof Error ? err.message : t('Failed to load request history.'));
    } finally {
      if (request.isCurrent()) setLoading(false);
    }
  }, [beginListRequest, query, t]);

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

  const runDelete = deletion.run;

  const handleDeleteSingle = useCallback(async () => {
    if (!deleteTarget) return;
    const target = deleteTarget;
    setDeleteTarget(null);
    const done = await runDelete({
      kind: 'requestHistoryEntry',
      action: () => deleteRequestHistoryEntry(target.id),
      errorMessage: (err) => (err instanceof Error ? err.message : t('Failed to delete request history entry.')),
    });
    if (!done) return;
    pushToast('warning', t('Request entry deleted.'));
    if (detailRecord?.entry.id === target.id) {
      setDetailRecord(null);
      navigate('/requests');
    }
    await loadEntries();
    await loadSummary();
  }, [deleteTarget, detailRecord?.entry.id, loadEntries, loadSummary, navigate, pushToast, runDelete, t]);

  const handleDeleteSelected = useCallback(async () => {
    const ids = [...selectedIds];
    setDeleteSelectedOpen(false);
    const done = await runDelete({
      kind: 'requestHistoryEntry',
      count: ids.length,
      action: () => deleteRequestHistoryEntries(ids),
      errorMessage: (err) => (err instanceof Error ? err.message : t('Failed to delete selected request entries.')),
    });
    if (!done) return;
    pushToast('warning', t('Deleted {{count}} request entries.', { count: ids.length }));
    await loadEntries();
    await loadSummary();
  }, [loadEntries, loadSummary, pushToast, runDelete, selectedIds, t]);

  const handleDeleteFiltered = useCallback(async () => {
    const deleteQuery: RequestHistoryQuery = { ...query };
    delete deleteQuery.pageNumber;
    delete deleteQuery.pageSize;
    setDeleteFilteredOpen(false);
    const done = await runDelete({
      kind: 'requestHistoryEntry',
      title: deleteMatchingRequestHistoryTitle(t),
      action: () => deleteRequestHistoryByFilter(deleteQuery),
      errorMessage: (err) => (err instanceof Error ? err.message : t('Failed to delete filtered request entries.')),
    });
    if (!done) return;
    pushToast('warning', t('Deleted the current filtered request set.'));
    setPageNumber(1);
    await loadEntries();
    await loadSummary();
  }, [loadEntries, loadSummary, pushToast, query, runDelete, t]);

  const detailHeaders = useMemo(() => requestDetailMaps(detailRecord), [detailRecord]);

  const columns: DataTableColumn<RequestHistoryEntry>[] = [
    {
      key: 'when', label: t('When'), headerTitle: t('When the Admiral received the request. Hover a cell for the exact time.'), required: true, cellClassName: 'cell-nowrap',
      cellTitle: (entry) => formatDateTime(entry.createdUtc),
      render: (entry) => formatRelativeTime(entry.createdUtc),
    },
    {
      key: 'method', label: t('Method'), headerTitle: t('HTTP method of the request, such as GET or POST.'),
      render: (entry) => <span className={`request-method-pill request-method-${entry.method.toLowerCase()}`}>{entry.method}</span>,
    },
    {
      key: 'route', label: t('Route'), headerTitle: t('API path that was called.'), required: true, cellClassName: 'request-route-cell mono', cellTitle: (entry) => entry.route,
      render: (entry) => entry.route,
    },
    { key: 'principal', label: t('Principal'), headerTitle: t('User or credential that made the request. Anonymous means it was unauthenticated.'), render: (entry) => entry.principalDisplay || t('Anonymous') },
    {
      key: 'status', label: t('Status'), headerTitle: t('HTTP status code returned. Green is success, red is a failure.'),
      render: (entry) => <span className={`request-status-pill ${entry.isSuccess ? 'success' : 'error'}`}>{entry.statusCode}</span>,
    },
    { key: 'duration', label: t('Duration'), headerTitle: t('Time the Admiral took to handle the request, in milliseconds.'), cellClassName: 'cell-nowrap', render: (entry) => `${entry.durationMs.toFixed(2)} ms` },
    {
      key: 'payloads', label: t('Payloads'), headerTitle: t('Request body size / response body size.'), cellClassName: 'cell-nowrap',
      render: (entry) => `${formatBytes(entry.requestSizeBytes)} / ${formatBytes(entry.responseSizeBytes)}`,
    },
    {
      key: 'actions', label: t('Actions'), fixed: true, interactive: true, className: 'text-right',
      render: (entry) => (
        <ActionMenu
          id={`request-${entry.id}`}
          items={[
            { label: 'View', title: 'Open the stored headers, parameters, and bodies of this request.', onClick: () => void openDetail(entry.id) },
            { label: 'Replay in API Explorer', title: 'Open API Explorer prefilled with this request so you can review and resend it. Nothing is sent until you run it.', onClick: () => void handleReplay(entry.id) },
            { label: 'Delete', title: 'Delete this stored request record. Cannot be undone.', danger: true, onClick: () => setDeleteTarget(entry) },
          ]}
        />
      ),
    },
  ];

  return (
    <div className="request-history-page">
      <PageHeader
        title={t('Requests')}
        subtitle={t('Inspect captured Armada API traffic, filter by route or principal, and replay stored requests into API Explorer.')}
        actions={(
          <>
            {selectedIds.length > 0 && (
              <button className="btn btn-danger btn-sm" onClick={() => setDeleteSelectedOpen(true)} title={t('Delete the stored records of the checked requests. Asks for confirmation; cannot be undone.')}>
                {t('Delete Selected')} ({selectedIds.length})
              </button>
            )}
            {totalRecords > 0 && (
              <button className="btn btn-danger btn-sm" onClick={() => setDeleteFilteredOpen(true)} title={hasActiveFilters ? t('Delete every stored request matching the filters and time range, on all pages, not just this one. Cannot be undone.') : t('Delete every stored request between From and To, on all pages, not just this one. Cannot be undone.')}>
                {hasActiveFilters ? t('Delete Filtered') : t('Delete Visible Range')}
              </button>
            )}
          </>
        )}
      />

      <ErrorModal error={error} onClose={() => setError('')} />

      <div className="request-history-summary-grid">
        <div className="card request-summary-card" title={t('Requests recorded in the activity range chosen below, with the current filters.')}>
          <div className="card-label">{t('Total Requests')}</div>
          <div className="card-value">{summaryLoading ? '...' : (summary?.totalCount || 0).toLocaleString()}</div>
          <div className="card-detail text-dim">{t('Current activity window')}</div>
        </div>
        <div className="card request-summary-card" title={t('Share of those requests that returned a success status.')}>
          <div className="card-label">{t('Success Rate')}</div>
          <div className="card-value">{summaryLoading ? '...' : `${(summary?.successRate || 0).toFixed(1)}%`}</div>
          <div className="card-detail text-dim">{t('Based on visible summary range')}</div>
        </div>
        <div className="card request-summary-card" title={t('Requests in the activity range that returned an error status, 4xx or 5xx.')}>
          <div className="card-label">{t('Failures')}</div>
          <div className="card-value">{summaryLoading ? '...' : (summary?.failureCount || 0).toLocaleString()}</div>
          <div className="card-detail text-dim">{t('Non-successful responses')}</div>
        </div>
        <div className="card request-summary-card" title={t('Mean time the Admiral took per request in the activity range, in milliseconds.')}>
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
          <div className="request-range-tabs" role="group" aria-label={t('Activity range')}>
            {ACTIVITY_RANGE_OPTIONS.map((option) => (
              <button
                key={option.id}
                type="button"
                className={`request-range-tab${activityRange === option.id ? ' active' : ''}`}
                aria-pressed={activityRange === option.id}
                onClick={() => setActivityRange(option.id)}
                title={t('Time range for the chart and the summary tiles. The table below uses the From and To filters instead.')}
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
            title={filtersExpanded ? t('Hide the request filters. Filters stay applied.') : t('Show filters for method, status, route, caller, and time range.')}
          >
            <span className="collapsible-caret" aria-hidden="true">{filtersExpanded ? '▾' : '▸'}</span>
            <span className="collapsible-title">{t('Filters')}</span>
            {hasActiveFilters && <span className="filter-active-pill" title={t('At least one filter besides the time range is narrowing the list.')}>{t('Active')}</span>}
            <span className="text-dim" style={{ marginLeft: 'auto', fontWeight: 400 }}>
              {t('Constrain the paginated request table without leaving the dashboard.')}
            </span>
          </button>
          {filtersExpanded && (
            <button
              className="btn btn-sm"
              onClick={() => { setFilters(defaultFilters); setPageNumber(1); }}
              style={{ marginLeft: '0.75rem' }}
              title={t('Clear every filter and go back to the last 24 hours.')}
            >
              {t('Reset')}
            </button>
          )}
        </div>
        {filtersExpanded && (
        <div className="request-card-body">
          <div className="request-filter-grid">
            <label className="request-filter-field" title={t('Show only requests with this HTTP method.')}>
              <span>{t('Method')}</span>
              <select title={t('Show only requests with this HTTP method.')} value={filters.method} onChange={(event) => { setFilters((current) => ({ ...current, method: event.target.value })); setPageNumber(1); }}>
                <option value="">{t('All methods')}</option>
                <option value="GET">GET</option>
                <option value="POST">POST</option>
                <option value="PUT">PUT</option>
                <option value="PATCH">PATCH</option>
                <option value="DELETE">DELETE</option>
              </select>
            </label>
            <label className="request-filter-field" title={t('Show only requests that returned exactly this HTTP status code, such as 200 or 404.')}>
              <span>{t('Status Code')}</span>
              <input title={t('Show only requests that returned exactly this HTTP status code, such as 200 or 404.')} value={filters.statusCode} onChange={(event) => { setFilters((current) => ({ ...current, statusCode: event.target.value })); setPageNumber(1); }} placeholder="200" />
            </label>
            <label className="request-filter-field" title={t('Show only requests whose route contains this text, such as /api/v1/missions.')}>
              <span>{t('Route')}</span>
              <input title={t('Show only requests whose route contains this text, such as /api/v1/missions.')} value={filters.route} onChange={(event) => { setFilters((current) => ({ ...current, route: event.target.value })); setPageNumber(1); }} placeholder="/api/v1/missions" />
            </label>
            <label className="request-filter-field" title={t('Show only requests whose caller name contains this text, such as an email address.')}>
              <span>{t('Principal')}</span>
              <input title={t('Show only requests whose caller name contains this text, such as an email address.')} value={filters.principal} onChange={(event) => { setFilters((current) => ({ ...current, principal: event.target.value })); setPageNumber(1); }} placeholder={t('user@tenant')} />
            </label>
            <label className="request-filter-field" title={t('Show only requests made with this API credential ID, such as crd_abc123.')}>
              <span>{t('Credential')}</span>
              <input title={t('Show only requests made with this API credential ID, such as crd_abc123.')} value={filters.credentialId} onChange={(event) => { setFilters((current) => ({ ...current, credentialId: event.target.value })); setPageNumber(1); }} placeholder="crd_" />
            </label>
            <label className="request-filter-field" title={t('Show only successful (2xx and 3xx) or only failed requests.')}>
              <span>{t('Result')}</span>
              <select title={t('Show only successful (2xx and 3xx) or only failed requests.')} value={filters.isSuccess} onChange={(event) => { setFilters((current) => ({ ...current, isSuccess: event.target.value as FiltersState['isSuccess'] })); setPageNumber(1); }}>
                <option value="all">{t('All')}</option>
                <option value="true">{t('Success')}</option>
                <option value="false">{t('Failure')}</option>
              </select>
            </label>
            {isAdmin && (
              <label className="request-filter-field" title={t('Show only requests from this tenant ID, such as ten_abc123. Administrators only.')}>
                <span>{t('Tenant')}</span>
                <input title={t('Show only requests from this tenant ID, such as ten_abc123. Administrators only.')} value={filters.tenantId} onChange={(event) => { setFilters((current) => ({ ...current, tenantId: event.target.value })); setPageNumber(1); }} placeholder="ten_" />
              </label>
            )}
            {(isAdmin || isTenantAdmin) && (
              <label className="request-filter-field" title={t('Show only requests from this user ID, such as usr_abc123.')}>
                <span>{t('User')}</span>
                <input title={t('Show only requests from this user ID, such as usr_abc123.')} value={filters.userId} onChange={(event) => { setFilters((current) => ({ ...current, userId: event.target.value })); setPageNumber(1); }} placeholder="usr_" />
              </label>
            )}
            <label className="request-filter-field" title={t('Earliest request time to list, in your local time. Defaults to 24 hours ago. Also bounds Delete Visible Range.')}>
              <span>{t('From')}</span>
              <input title={t('Earliest request time to list, in your local time. Defaults to 24 hours ago. Also bounds Delete Visible Range.')} type="datetime-local" value={filters.fromUtc} onChange={(event) => { setFilters((current) => ({ ...current, fromUtc: event.target.value })); setPageNumber(1); }} />
            </label>
            <label className="request-filter-field" title={t('Latest request time to list, in your local time. Defaults to now. Also bounds Delete Visible Range.')}>
              <span>{t('To')}</span>
              <input title={t('Latest request time to list, in your local time. Defaults to now. Also bounds Delete Visible Range.')} type="datetime-local" value={filters.toUtc} onChange={(event) => { setFilters((current) => ({ ...current, toUtc: event.target.value })); setPageNumber(1); }} />
            </label>
          </div>
        </div>
        )}
      </div>

      <DataTable
        tableKey="requesthistory"
        columns={columns}
        rows={loading ? [] : entries}
        rowKey={(entry) => entry.id}
        onRowClick={(entry) => void openDetail(entry.id)}
        className="request-history-table"
        wrapClassName="request-history-table-wrap"
        pagination={{
          pageNumber, pageSize, totalPages, totalRecords, totalMs,
          onPageChange: setPageNumber,
          onPageSizeChange: (size) => {
            setPageSize(size);
            setPageNumber(1);
          },
        }}
        autoRefresh={{ seconds: refreshSeconds, onChange: setRefreshSeconds }}
        onRefresh={async () => { await Promise.all([loadEntries(), loadSummary()]); }}
        refreshTitle={t('Refresh request data')}
        selection={{
          isSelected: (entry) => selectedIds.includes(entry.id),
          onToggle: (entry) => setSelectedIds((current) => current.includes(entry.id) ? current.filter((idValue) => idValue !== entry.id) : [...current, entry.id]),
          allSelected,
          onToggleAll: (checked) => setSelectedIds(checked ? entries.map((entry) => entry.id) : []),
          selectAllLabel: t('Select all visible requests'),
          rowLabel: () => t('Select this request'),
        }}
        emptyMessage={loading ? t('Loading request history...') : t('No request history entries match the current filters.')}
      />

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
                    <button className="btn btn-sm" onClick={() => void handleReplay(detailRecord.entry.id)} title={t('Open API Explorer prefilled with this request so you can review and resend it. Nothing is sent until you run it.')}>
                      {t('Replay')}
                    </button>
                    <button className="btn btn-danger btn-sm" onClick={() => setDeleteTarget(detailRecord.entry)} title={t('Delete this stored request record. Cannot be undone.')}>
                      {t('Delete')}
                    </button>
                  </>
                )}
                <button className="btn btn-sm" onClick={() => { setDetailRecord(null); navigate('/requests'); }} title={t('Close the request detail.')}>
                  {t('Close')}
                </button>
              </div>
            </div>
            {detailLoading || !detailRecord ? (
              <div className="request-history-empty">{t('Loading request detail...')}</div>
            ) : (
              <div className="request-detail-body">
                <div className="request-detail-summary-grid">
                  <div className="request-detail-summary-item" title={t('Request record ID (req_ prefix).')}>
                    <span>{t('Entry ID')}</span>
                    <strong className="mono">{detailRecord.entry.id}</strong>
                  </div>
                  <div className="request-detail-summary-item" title={t('User or credential that made the request. Anonymous means it was unauthenticated.')}>
                    <span>{t('Principal')}</span>
                    <strong>{detailRecord.entry.principalDisplay || t('Anonymous')}</strong>
                  </div>
                  <div className="request-detail-summary-item" title={t('How the caller authenticated, such as a bearer token or session.')}>
                    <span>{t('Auth Method')}</span>
                    <strong>{detailRecord.entry.authMethod || '-'}</strong>
                  </div>
                  <div className="request-detail-summary-item" title={t('HTTP status code returned.')}>
                    <span>{t('Status')}</span>
                    <strong>{detailRecord.entry.statusCode}</strong>
                  </div>
                  <div className="request-detail-summary-item" title={t('Time the Admiral took to handle the request, in milliseconds.')}>
                    <span>{t('Duration')}</span>
                    <strong>{detailRecord.entry.durationMs.toFixed(2)} ms</strong>
                  </div>
                  <div className="request-detail-summary-item" title={t('When the Admiral received the request.')}>
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

      {deletion.dialog}
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
