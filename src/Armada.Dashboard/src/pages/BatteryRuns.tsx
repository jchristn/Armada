import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { apiErrorMessage, cancelBatteryRun, listBatteryRuns } from '../api/client';
import type { BatteryRun } from '../types/reliability';
import DataTable, { type DataTableColumn } from '../components/shared/DataTable';
import LoadingIndicator from '../components/shared/LoadingIndicator';
import PageHeader from '../components/shared/PageHeader';
import ErrorModal from '../components/shared/ErrorModal';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import { useAuth } from '../context/AuthContext';
import { useNotifications } from '../context/NotificationContext';
import { useLocale } from '../context/LocaleContext';
import { usePersistedPageSize } from '../lib/usePersistedPageSize';

/** Battery run statuses the list can filter by. */
export const BATTERY_RUN_STATUSES = ['Queued', 'Running', 'Completed', 'Failed', 'Cancelled'];

/** True while a run can still be cancelled. */
export function isBatteryRunOpen(run: BatteryRun): boolean {
  return run.status === 'Queued' || run.status === 'Running';
}

/**
 * Battery runs (Full captain evaluations and gate runs), newest first, with task counts. Global and tenant administrators can cancel a
 * queued or running run, which stops its open missions and releases their docks. A Captains hub tab.
 */
export default function BatteryRuns() {
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const { isAdmin, isTenantAdmin } = useAuth();
  const canCancel = isAdmin || isTenantAdmin;
  const { pushToast } = useNotifications();
  const [runs, setRuns] = useState<BatteryRun[]>([]);
  const [loading, setLoading] = useState(true);
  const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState('');
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = usePersistedPageSize('battery-runs', 25);
  const [totalPages, setTotalPages] = useState(1);
  const [totalRecords, setTotalRecords] = useState(0);
  const [status, setStatus] = useState('');
  const [confirmRun, setConfirmRun] = useState<BatteryRun | null>(null);

  const load = useCallback(async () => {
    try {
      setLoading(true);
      const page = await listBatteryRuns({ status: status || undefined, pageNumber, pageSize });
      setRuns(page.objects || []);
      setTotalPages(page.totalPages || 1);
      setTotalRecords(page.totalRecords || 0);
      setError('');
    } catch (err: unknown) {
      setError(apiErrorMessage(err, t('Failed to load battery runs.')));
    } finally {
      setLoading(false);
      setLoaded(true);
    }
  }, [status, pageNumber, pageSize, t]);

  useEffect(() => { void load(); }, [load]);

  async function cancel(run: BatteryRun) {
    setConfirmRun(null);
    try {
      await cancelBatteryRun(run.id, 'Cancelled from the dashboard');
      pushToast('success', t('Battery run cancelled. Its open missions are stopping.'));
      await load();
    } catch (err: unknown) {
      pushToast('error', apiErrorMessage(err, t('Failed to cancel the battery run.')));
    }
  }

  const columns: DataTableColumn<BatteryRun>[] = [
    { key: 'id', label: t('Run'), required: true, cellClassName: 'cell-ident mono', render: (r) => r.id },
    {
      key: 'status', label: t('Status'), cellClassName: 'cell-nowrap',
      clearFilter: () => { setStatus(''); setPageNumber(1); },
      filter: (
        <select className="col-filter" aria-label={t('Status')} value={status} onChange={e => { setStatus(e.target.value); setPageNumber(1); }}>
          <option value="">{t('All')}</option>
          {BATTERY_RUN_STATUSES.map(s => <option key={s} value={s}>{t(s)}</option>)}
        </select>
      ),
      cellTitle: (r) => r.failureReason || undefined,
      render: (r) => <span className={`tag ${r.status.toLowerCase()}`}>{t(r.status)}</span>,
    },
    {
      key: 'captain', label: t('Captain'), cellClassName: 'cell-nowrap', interactive: true,
      render: (r) => r.captainId ? <Link to={`/captains/${encodeURIComponent(r.captainId)}`}>{r.captainName || r.captainId}</Link> : '-',
    },
    { key: 'model', label: t('Model'), cellClassName: 'cell-nowrap', render: (r) => r.model || '-' },
    {
      key: 'tasks', label: t('Tasks'), cellClassName: 'cell-nowrap',
      headerTitle: t('Passed, failed, could not run, and waiting for evaluation, of the total tasks.'),
      render: (r) => t('{{passed}} passed, {{failed}} failed of {{total}}', { passed: r.passedCount, failed: r.failedCount, total: r.totalTasks }),
      cellTitle: (r) => t('{{couldNotRun}} could not run, {{pending}} pending evaluation', { couldNotRun: r.couldNotRunCount, pending: r.pendingEvaluationCount }),
    },
    {
      key: 'created', label: t('Created'), cellClassName: 'text-dim cell-nowrap',
      cellTitle: (r) => formatDateTime(r.createdUtc),
      render: (r) => formatRelativeTime(r.createdUtc),
    },
    {
      key: 'actions', label: '', fixed: true, interactive: true,
      render: (r) => canCancel && isBatteryRunOpen(r)
        ? <button type="button" className="btn btn-sm btn-danger" onClick={() => setConfirmRun(r)} title={t('Stop this run: its open missions are cancelled and their docks released.')}>{t('Cancel')}</button>
        : null,
    },
  ];

  return (
    <div>
      <PageHeader
        title={t('Battery Runs')}
        subtitle={t('Full captain evaluations and acceptance gate runs, newest first. A queued or running run can be cancelled.')}
      />
      <ErrorModal error={error} onClose={() => setError('')} />
      <ConfirmDialog
        open={confirmRun !== null}
        title={t('Cancel battery run')}
        message={t('Cancel this battery run? Its open missions are stopped and their docks released. Finished task results are kept.')}
        onConfirm={() => { if (confirmRun) void cancel(confirmRun); }}
        danger
        confirmLabel={t('Cancel run')}
        cancelLabel={t('Keep running')}
        onCancel={() => setConfirmRun(null)}
      />
      <DataTable
        tableKey="battery-runs"
        columns={columns}
        rows={runs}
        rowKey={(r) => r.id}
        pagination={{
          pageNumber, pageSize, totalPages, totalRecords,
          onPageChange: (p) => setPageNumber(p),
          onPageSizeChange: (size) => { setPageSize(size); setPageNumber(1); },
        }}
        onRefresh={load}
        refreshTitle={t('Reload the battery run list now.')}
        loading={loading}
        emptyMessage={t('No battery runs match the current filters.')}
        placeholder={runs.length > 0 ? undefined : !loaded ? <LoadingIndicator compact label={t('Loading...')} /> : <p className="text-dim">{t('No battery runs yet. A Full captain evaluation or a gate run starts one.')}</p>}
      />
    </div>
  );
}
