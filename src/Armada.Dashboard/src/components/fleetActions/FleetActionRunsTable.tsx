import { useCallback, useEffect, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { cancelFleetActionRun, enumerateFleetActionRuns } from '../../api/client';
import type { FleetActionRun, FleetActionRunStatus } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import { useNotifications } from '../../context/NotificationContext';
import { useAuth } from '../../context/AuthContext';
import Pagination from '../shared/Pagination';
import ActionMenu from '../shared/ActionMenu';
import ConfirmDialog from '../shared/ConfirmDialog';
import JsonViewer from '../shared/JsonViewer';
import RefreshButton from '../shared/RefreshButton';
import AutoRefreshSelect from '../shared/AutoRefreshSelect';
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
        <div className="table-toolbar-right">
          <AutoRefreshSelect seconds={seconds} onChange={setSeconds} />
          <RefreshButton onRefresh={load} title={t('Refresh runs')} />
        </div>
      </div>

      {error && <ErrorState message={error} onRetry={() => void load()} />}
      {loading && rows.length === 0 && !error && <LoadingState />}

      {!loading && !error && totalRecords === 0 && !filtered && (
        <EmptyState
          title={t('No fleet action runs yet')}
          actions={<button type="button" className="btn btn-primary btn-sm" onClick={() => navigate('/vessels')}>{t('Go to Vessels')}</button>}
        >
          <p>{t('Select vessels on the Vessels page and choose Run action... in the selection bar, or run an action from the Actions tab.')}</p>
        </EmptyState>
      )}
      {!loading && !error && totalRecords === 0 && filtered && (
        <EmptyState title={t('No runs match this status')} actions={<button type="button" className="btn btn-sm" onClick={() => updateParams({ status: null, page: null })}>{t('Clear filter')}</button>} />
      )}

      {totalRecords > 0 && (
        <>
          <Pagination
            pageNumber={pageNumber}
            pageSize={pageSize}
            totalPages={totalPages}
            totalRecords={totalRecords}
            totalMs={totalMs}
            onPageChange={(p) => updateParams({ page: String(p) })}
            onPageSizeChange={(s) => { setPageSize(s); updateParams({ page: null }); }}
          />
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th scope="col">{t('Action')}</th>
                  <th scope="col">{t('Kind')}</th>
                  <th scope="col">{t('Status')}</th>
                  <th scope="col">{t('Progress')}</th>
                  <th scope="col" className="sortable" aria-sort={order === 'CreatedAscending' ? 'ascending' : 'descending'}>
                    <button type="button" className="th-sort-btn" onClick={() => updateParams({ order: order === 'CreatedAscending' ? null : 'asc', page: null })} title={t('Sort by created date')}>
                      {t('Created')} <span aria-hidden="true">{order === 'CreatedAscending' ? '\u25B2' : '\u25BC'}</span>
                    </button>
                  </th>
                  <th scope="col">{t('Started')}</th>
                  <th scope="col">{t('Completed')}</th>
                  <th scope="col" className="text-right">{t('Duration')}</th>
                  <th scope="col" className="text-right">{t('Actions')}</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((r) => {
                  const badge = runStatusBadge(t, r.status);
                  return (
                    <tr key={r.id} className="clickable" onClick={() => navigate(`/fleet-actions/runs/${r.id}`)}>
                      <td>
                        <strong>{r.actionName}</strong>
                        {!r.actionId && <span className="tag idle" style={{ marginLeft: '0.4rem' }}>{t('Ad hoc')}</span>}
                        <div className="text-dim mono cell-subline">{r.id}</div>
                      </td>
                      <td>{t(KIND_LABELS[r.kind])}</td>
                      <td><CodeStatusBadge {...badge} /></td>
                      <td className="progress-cell"><RunProgress run={r} compact /></td>
                      <td className="text-dim nowrap" title={formatDateTime(r.createdUtc)}>{formatRelativeTime(r.createdUtc)}</td>
                      <td className="text-dim nowrap" title={formatDateTime(r.startedUtc)}>{r.startedUtc ? formatRelativeTime(r.startedUtc) : '-'}</td>
                      <td className="text-dim nowrap" title={formatDateTime(r.completedUtc)}>{r.completedUtc ? formatRelativeTime(r.completedUtc) : '-'}</td>
                      <td className="text-right mono nowrap">{formatDurationMs(t, locale, durationBetween(r.startedUtc, r.completedUtc, isRunActive(r.status)))}</td>
                      <td className="text-right" onClick={(e) => e.stopPropagation()}>
                        <ActionMenu
                          id={`fleet-run-${r.id}`}
                          items={[
                            { label: 'View', onClick: () => navigate(`/fleet-actions/runs/${r.id}`) },
                            ...(isRunActive(r.status) ? [{ label: 'Cancel', danger: true, onClick: () => setConfirmCancel(r), disabled: !isTenantAdmin }] : []),
                            { label: 'View JSON', onClick: () => setJson(r) },
                          ]}
                        />
                      </td>
                    </tr>
                  );
                })}
                {rows.length === 0 && <tr><td colSpan={9} className="text-dim">{t('No runs on this page.')}</td></tr>}
              </tbody>
            </table>
          </div>
        </>
      )}

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
