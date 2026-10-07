import { useCallback, useEffect, useState, type ReactNode } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { cancelFleetActionRun, enumerateFleetActionRuns } from '../../api/client';
import type { FleetActionRun, FleetActionRunStatus } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import { useNotifications } from '../../context/NotificationContext';
import { useAuth } from '../../context/AuthContext';
import DataTable, { type DataTableColumn } from '../shared/DataTable';
import CopyButton from '../shared/CopyButton';
import ActionMenu from '../shared/ActionMenu';
import ConfirmDialog from '../shared/ConfirmDialog';
import JsonViewer from '../shared/JsonViewer';
import CodeStatusBadge from '../shared/CodeStatusBadge';
import { EmptyState, ErrorState, LoadingState } from '../shared/StateBlocks';
import RunProgress from './RunProgress';
import { useAutoRefresh } from '../../lib/useAutoRefresh';
import { usePersistedPageSize } from '../../lib/usePersistedPageSize';
import {
  KIND_LABELS,
  RUN_STATUSES,
  RUN_STATUS_META,
  durationBetween,
  formatDurationMs,
  isRunActive,
  runStatusBadge,
} from '../../lib/fleetActionLabels';

/** Runs tab: server-paged, status-filterable list of fleet action runs. Filter, page and order live in the URL. */
export default function FleetActionRunsTable() {
  const { t, locale, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const { isTenantAdmin } = useAuth();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();

  const statusParam = searchParams.get('status') ?? '';
  const status = (RUN_STATUSES as string[]).includes(statusParam) ? (statusParam as FleetActionRunStatus) : '';
  const pageNumber = Math.max(1, parseInt(searchParams.get('page') ?? '1', 10) || 1);
  const order = searchParams.get('order') === 'asc' ? 'CreatedAscending' : 'CreatedDescending';
  const [pageSize, setPageSize] = usePersistedPageSize('fleet-action-runs', 25);

  const [rows, setRows] = useState<FleetActionRun[]>([]);
  const [totalPages, setTotalPages] = useState(1);
  const [totalRecords, setTotalRecords] = useState(0);
  const [totalMs, setTotalMs] = useState<number | undefined>(undefined);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [json, setJson] = useState<FleetActionRun | null>(null);
  const [confirmCancel, setConfirmCancel] = useState<FleetActionRun | null>(null);

  function updateParams(patch: Record<string, string | null>) {
    const next = new URLSearchParams(searchParams);
    for (const [k, v] of Object.entries(patch)) {
      if (v === null || v === '') next.delete(k);
      else next.set(k, v);
    }
    setSearchParams(next, { replace: true });
  }

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const result = await enumerateFleetActionRuns({ pageNumber, pageSize, status, order });
      setRows(result.objects || []);
      setTotalPages(Math.max(1, result.totalPages || 1));
      setTotalRecords(result.totalRecords || 0);
      setTotalMs(result.totalMs);
      setError('');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to load fleet action runs.'));
    } finally {
      setLoading(false);
    }
  }, [pageNumber, pageSize, status, order, t]);

  useEffect(() => { void load(); }, [load]);

  const modalOpen = json !== null || confirmCancel !== null;
  const { seconds, setSeconds } = useAutoRefresh('fleet-action-runs', () => { void load(); }, { paused: modalOpen });

  async function handleCancel(run: FleetActionRun) {
    setConfirmCancel(null);
    try {
      await cancelFleetActionRun(run.id);
      pushToast('warning', t('Run "{{name}}" cancelled.', { name: run.actionName }));
      void load();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to cancel the run.'));
    }
  }

  const filtered = status !== '';

  const columns: DataTableColumn<FleetActionRun>[] = [
    {
      key: 'action', label: t('Action'), required: true, cellClassName: 'cell-nowrap',
      render: (r) => (
        <>
          <strong>{r.actionName}</strong>
          {!r.actionId && <span className="tag idle" style={{ marginLeft: '0.4rem' }}>{t('Ad hoc')}</span>}
        </>
      ),
    },
    {
      // The run ID used to sit on a second line under the action name; it is its own one-line column now.
      key: 'id', label: t('ID'), required: true, cellClassName: 'mono text-dim table-id-cell',
      render: (r) => (
        <span className="id-display">
          <span className="id-value" title={r.id}>{r.id}</span>
          <CopyButton text={r.id} onClick={(e) => e.stopPropagation()} />
        </span>
      ),
    },
    { key: 'kind', label: t('Kind'), cellClassName: 'cell-nowrap', render: (r) => t(KIND_LABELS[r.kind]) },
    { key: 'status', label: t('Status'), cellClassName: 'cell-nowrap', render: (r) => <CodeStatusBadge {...runStatusBadge(t, r.status)} /> },
    { key: 'progress', label: t('Progress'), cellClassName: 'progress-cell', render: (r) => <RunProgress run={r} compact /> },
    {
      key: 'created', label: t('Created'), sortKey: 'created', headerTitle: t('Sort by created date'), cellClassName: 'text-dim nowrap',
      cellTitle: (r) => formatDateTime(r.createdUtc), render: (r) => formatRelativeTime(r.createdUtc),
    },
    {
      key: 'started', label: t('Started'), cellClassName: 'text-dim nowrap',
      cellTitle: (r) => formatDateTime(r.startedUtc), render: (r) => (r.startedUtc ? formatRelativeTime(r.startedUtc) : '-'),
    },
    {
      key: 'completed', label: t('Completed'), cellClassName: 'text-dim nowrap',
      cellTitle: (r) => formatDateTime(r.completedUtc), render: (r) => (r.completedUtc ? formatRelativeTime(r.completedUtc) : '-'),
    },
    {
      key: 'duration', label: t('Duration'), className: 'text-right', cellClassName: 'mono nowrap',
      render: (r) => formatDurationMs(t, locale, durationBetween(r.startedUtc, r.completedUtc, isRunActive(r.status))),
    },
    {
      key: 'actions', label: t('Actions'), fixed: true, interactive: true, className: 'text-right',
      render: (r) => (
        <ActionMenu
          id={`fleet-run-${r.id}`}
          items={[
            { label: 'View', onClick: () => navigate(`/fleet-actions/runs/${r.id}`) },
            ...(isRunActive(r.status) ? [{ label: 'Cancel', danger: true, onClick: () => setConfirmCancel(r), disabled: !isTenantAdmin }] : []),
            { label: 'View JSON', onClick: () => setJson(r) },
          ]}
        />
      ),
    },
  ];

  let placeholder: ReactNode = undefined;
  if (totalRecords === 0) {
    if (loading && !error) placeholder = <LoadingState />;
    else if (error) placeholder = <></>;
    else if (!filtered) {
      placeholder = (
        <EmptyState
          title={t('No fleet action runs yet')}
          actions={<button type="button" className="btn btn-primary btn-sm" onClick={() => navigate('/vessels')}>{t('Go to Vessels')}</button>}
        >
          <p>{t('Select vessels on the Vessels page and choose Run action... in the selection bar, or run an action from the Actions tab.')}</p>
        </EmptyState>
      );
    } else {
      placeholder = <EmptyState title={t('No runs match this status')} actions={<button type="button" className="btn btn-sm" onClick={() => updateParams({ status: null, page: null })}>{t('Clear filter')}</button>} />;
    }
  }

  return (
    <div>
      <div className="table-toolbar">
        <div className="table-toolbar-left">
          <label className="inline-filter">
            <span>{t('Status')}</span>
            <select value={status} onChange={(e) => updateParams({ status: e.target.value, page: null })} aria-label={t('Filter runs by status')}>
              <option value="">{t('All statuses')}</option>
              {RUN_STATUSES.map((s) => <option key={s} value={s}>{t(RUN_STATUS_META[s].label)}</option>)}
            </select>
          </label>
          {filtered && <button type="button" className="btn btn-sm" onClick={() => updateParams({ status: null, page: null })}>{t('Clear filter')}</button>}
        </div>
      </div>

      {error && <ErrorState message={error} onRetry={() => void load()} />}

      <DataTable
        tableKey="fleet-action-runs"
        columns={columns}
        rows={rows}
        rowKey={(r) => r.id}
        onRowClick={(r) => navigate(`/fleet-actions/runs/${r.id}`)}
        sort={{
          field: 'created',
          dir: order === 'CreatedAscending' ? 'asc' : 'desc',
          onSort: () => updateParams({ order: order === 'CreatedAscending' ? null : 'asc', page: null }),
        }}
        pagination={{
          pageNumber,
          pageSize,
          totalPages,
          totalRecords,
          totalMs,
          onPageChange: (p) => updateParams({ page: String(p) }),
          onPageSizeChange: (size) => { setPageSize(size); updateParams({ page: null }); },
        }}
        autoRefresh={{ seconds, onChange: setSeconds }}
        onRefresh={load}
        refreshTitle="Refresh runs"
        emptyMessage={t('No runs on this page.')}
        placeholder={placeholder}
      />

      <JsonViewer open={json !== null} title={json ? t('Fleet action run: {{name}}', { name: json.actionName }) : ''} id={json?.id} data={json} onClose={() => setJson(null)} />
      <ConfirmDialog
        open={confirmCancel !== null}
        title={t('Cancel run')}
        message={confirmCancel ? t('Cancel "{{name}}"? Pending targets are cancelled and running commands are stopped. Mission runs cancel voyages that have not finished.', { name: confirmCancel.actionName }) : ''}
        confirmLabel={t('Cancel run')}
        cancelLabel={t('Keep running')}
        danger
        onConfirm={() => { if (confirmCancel) void handleCancel(confirmCancel); }}
        onCancel={() => setConfirmCancel(null)}
      />
    </div>
  );
}
