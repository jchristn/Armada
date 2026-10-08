import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import {
  cancelFleetActionRun,
  enumerateFleetActionRunTargets,
  getFleetAction,
  getFleetActionRun,
} from '../api/client';
import type {
  FleetActionRun,
  FleetActionRunTargetSummary,
  FleetActionTargetStatus,
  FleetActionUpsertRequest,
} from '../types/models';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import { useAuth } from '../context/AuthContext';
import PageHeader from '../components/shared/PageHeader';
import DataTable from '../components/shared/DataTable';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import CopyButton from '../components/shared/CopyButton';
import CodeStatusBadge from '../components/shared/CodeStatusBadge';
import { EmptyState, ErrorState, LoadingState } from '../components/shared/StateBlocks';
import RunProgress from '../components/fleetActions/RunProgress';
import TargetDetailDrawer from '../components/fleetActions/TargetDetailDrawer';
import RunActionModal from '../components/fleetActions/RunActionModal';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import { usePersistedPageSize } from '../lib/usePersistedPageSize';
import {
  KIND_LABELS,
  TARGET_STATUSES,
  TARGET_STATUS_META,
  durationBetween,
  formatDurationMs,
  isRunActive,
  reasonLabel,
  runStatusBadge,
  targetStatusBadge,
} from '../lib/fleetActionLabels';
import { definitionFromRun } from '../lib/fleetActionForm';

/** Refresh cadence while a run is active, in seconds. */
export const RUN_DETAIL_REFRESH_SECONDS = 5;

/**
 * Live view of one fleet action run: header with status, counts and progress; a server-paged target table
 * with a status filter; auto-refresh every 5 s while the run is active (paused while a drawer or dialog is
 * open); cancel; and an output drawer for Command targets.
 */
export default function FleetActionRunDetail() {
  const { id = '' } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { t, locale, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const { isTenantAdmin } = useAuth();
  const [searchParams, setSearchParams] = useSearchParams();

  const statusParam = searchParams.get('status') ?? '';
  const targetStatus = (TARGET_STATUSES as string[]).includes(statusParam) ? (statusParam as FleetActionTargetStatus) : '';
  const pageNumber = Math.max(1, parseInt(searchParams.get('page') ?? '1', 10) || 1);
  const [pageSize, setPageSize] = usePersistedPageSize('fleet-action-run-targets', 25);

  const [run, setRun] = useState<FleetActionRun | null>(null);
  const [runError, setRunError] = useState('');
  const [runLoading, setRunLoading] = useState(true);
  const [targets, setTargets] = useState<FleetActionRunTargetSummary[]>([]);
  const [targetsTotal, setTargetsTotal] = useState(0);
  const [targetsPages, setTargetsPages] = useState(1);
  const [targetsError, setTargetsError] = useState('');
  const [targetsLoading, setTargetsLoading] = useState(true);
  const [lastRefreshed, setLastRefreshed] = useState<string | null>(null);

  const [drawerTargetId, setDrawerTargetId] = useState<string | null>(null);
  const [confirmCancel, setConfirmCancel] = useState(false);
  const [showJson, setShowJson] = useState(false);
  const [rerun, setRerun] = useState<{ vesselIds: string[]; actionId: string | null; definition: FleetActionUpsertRequest | null } | null>(null);
  const [rerunLoading, setRerunLoading] = useState(false);

  const requestSeq = useRef(0);

  function updateParams(patch: Record<string, string | null>) {
    const next = new URLSearchParams(searchParams);
    for (const [k, v] of Object.entries(patch)) {
      if (v === null || v === '') next.delete(k);
      else next.set(k, v);
    }
    setSearchParams(next, { replace: true });
  }

  const refresh = useCallback(async () => {
    const seq = ++requestSeq.current;
    try {
      const [detail, page] = await Promise.all([
        getFleetActionRun(id).then((d) => ({ ok: true as const, d })).catch((e: unknown) => ({ ok: false as const, e })),
        enumerateFleetActionRunTargets(id, { pageNumber, pageSize, status: targetStatus }).then((p) => ({ ok: true as const, p })).catch((e: unknown) => ({ ok: false as const, e })),
      ]);
      if (seq !== requestSeq.current) return;
      if (detail.ok) {
        setRun(detail.d.run);
        setRunError('');
      } else {
        setRunError(detail.e instanceof Error ? detail.e.message : t('Failed to load the run.'));
      }
      if (page.ok) {
        setTargets(page.p.objects || []);
        setTargetsTotal(page.p.totalRecords || 0);
        setTargetsPages(Math.max(1, page.p.totalPages || 1));
        setTargetsError('');
      } else {
        setTargetsError(page.e instanceof Error ? page.e.message : t('Failed to load targets.'));
      }
      setLastRefreshed(new Date().toISOString());
    } finally {
      if (seq === requestSeq.current) {
        setRunLoading(false);
        setTargetsLoading(false);
      }
    }
  }, [id, pageNumber, pageSize, targetStatus, t]);

  useEffect(() => {
    setTargetsLoading(true);
    void refresh();
  }, [refresh]);

  const active = isRunActive(run?.status);
  const overlayOpen = drawerTargetId !== null || confirmCancel || showJson || rerun !== null;
  const { active: autoRefreshing } = useAutoRefresh('fleet-action-run-detail', () => { void refresh(); }, {
    intervalSeconds: RUN_DETAIL_REFRESH_SECONDS,
    paused: !active || overlayOpen,
  });

  async function handleCancel() {
    setConfirmCancel(false);
    if (!run) return;
    try {
      const updated = await cancelFleetActionRun(run.id);
      setRun(updated);
      pushToast('warning', t('Run "{{name}}" cancelled.', { name: run.actionName }));
      void refresh();
    } catch (err: unknown) {
      setRunError(err instanceof Error ? err.message : t('Failed to cancel the run.'));
    }
  }

  async function startRerunFailed() {
    if (!run) return;
    setRerunLoading(true);
    try {
      const [failed, timedOut] = await Promise.all([
        enumerateFleetActionRunTargets(run.id, { pageNumber: 1, pageSize: 500, status: 'Failed' }),
        enumerateFleetActionRunTargets(run.id, { pageNumber: 1, pageSize: 500, status: 'TimedOut' }),
      ]);
      const vesselIds = Array.from(new Set([...(failed.objects || []), ...(timedOut.objects || [])].map((x) => x.vesselId)));
      if (vesselIds.length === 0) {
        pushToast('info', t('No failed targets to re-run.'));
        return;
      }
      let actionId: string | null = null;
      if (run.actionId) {
        try {
          const action = await getFleetAction(run.actionId);
          if (action.active) actionId = action.id;
        } catch {
          actionId = null;
        }
      }
      const definition: FleetActionUpsertRequest | null = actionId ? null : definitionFromRun(run);
      setRerun({ vesselIds, actionId, definition });
    } catch (err: unknown) {
      setRunError(err instanceof Error ? err.message : t('Failed to load failed targets.'));
    } finally {
      setRerunLoading(false);
    }
  }

  function onTargetClick(target: FleetActionRunTargetSummary) {
    if (run?.kind === 'Mission') {
      if (target.voyageId) navigate(`/voyages/${target.voyageId}`);
      return;
    }
    setDrawerTargetId(target.id);
  }

  if (runLoading && !run) {
    return <LoadingState label={t('Loading run...')} />;
  }

  if (!run) {
    return (
      <div>
        <PageHeader
          title={t('Fleet action run')}
          breadcrumb={<><Link to="/fleet-actions?tab=runs">{t('Fleet Actions')}</Link> / <span className="mono">{id}</span></>}
        />
        <ErrorState message={runError || t('Run not found.')} onRetry={() => void refresh()} />
      </div>
    );
  }

  const badge = runStatusBadge(t, run.status);
  const duration = durationBetween(run.startedUtc, run.completedUtc, active);
  const refreshState = !active
    ? t('Run finished; auto-refresh stopped.')
    : autoRefreshing
      ? t('Live: refreshing every {{seconds}} s.', { seconds: RUN_DETAIL_REFRESH_SECONDS })
      : t('Auto-refresh paused while a panel is open.');

  return (
    <div className="fleet-run-detail">
      <PageHeader
        breadcrumb={<><Link to="/fleet-actions?tab=runs">{t('Fleet Actions')}</Link> / <Link to="/fleet-actions?tab=runs">{t('Runs')}</Link> / <span className="mono">{run.id}</span></>}
        title={(
          <span className="fleet-run-title">
            <span>{run.actionName}</span>
            <CodeStatusBadge {...badge} />
          </span>
        )}
        subtitle={(
          <span className="id-display">
            <span>{t(KIND_LABELS[run.kind])}</span>
            {!run.actionId && <span className="tag idle">{t('Ad hoc')}</span>}
            <span className="mono">{run.id}</span>
            <CopyButton text={run.id} />
          </span>
        )}
        actions={(
          <>
            <span className={`refresh-state${autoRefreshing ? ' refresh-state-live' : ''}`} role="status" aria-live="polite">
              {autoRefreshing && <span className="follow-dot follow-dot-active" aria-hidden="true" />}
              {refreshState}
            </span>
            <button type="button" className="btn btn-sm" onClick={() => setShowJson(true)}>{t('View JSON')}</button>
            {!active && run.failedCount > 0 && isTenantAdmin && (
              <button type="button" className="btn btn-sm" onClick={() => void startRerunFailed()} disabled={rerunLoading}>
                {rerunLoading ? t('Loading...') : t('Re-run failed targets')}
              </button>
            )}
            {active && isTenantAdmin && (
              <button type="button" className="btn btn-sm btn-danger" onClick={() => setConfirmCancel(true)}>{t('Cancel run')}</button>
            )}
          </>
        )}
      />

      {runError && <ErrorState message={runError} onRetry={() => void refresh()} />}

      <div className="card fleet-run-summary">
        <RunProgress run={run} />
        <div className="fleet-run-stats">
          <div className="fleet-run-stat"><span className="fleet-run-stat-label">{t('Targets')}</span><span className="fleet-run-stat-value">{run.targetCount.toLocaleString()}</span></div>
          <div className="fleet-run-stat"><span className="fleet-run-stat-label">{t('Succeeded')}</span><span className="fleet-run-stat-value">{run.succeededCount.toLocaleString()}</span></div>
          <div className="fleet-run-stat"><span className="fleet-run-stat-label">{t('Failed')}</span><span className="fleet-run-stat-value">{run.failedCount.toLocaleString()}</span></div>
          <div className="fleet-run-stat"><span className="fleet-run-stat-label">{t('Skipped')}</span><span className="fleet-run-stat-value">{run.skippedCount.toLocaleString()}</span></div>
          <div className="fleet-run-stat"><span className="fleet-run-stat-label">{t('Cancelled')}</span><span className="fleet-run-stat-value">{run.cancelledCount.toLocaleString()}</span></div>
          <div className="fleet-run-stat"><span className="fleet-run-stat-label">{t('Concurrency')}</span><span className="fleet-run-stat-value">{run.concurrency.toLocaleString()}</span></div>
          <div className="fleet-run-stat"><span className="fleet-run-stat-label">{t('Duration')}</span><span className="fleet-run-stat-value mono">{formatDurationMs(t, locale, duration)}</span></div>
        </div>
        <dl className="detail-kv detail-kv-inline">
          <dt>{t('Created')}</dt><dd title={formatDateTime(run.createdUtc)}>{formatRelativeTime(run.createdUtc)}</dd>
          <dt>{t('Started')}</dt><dd title={formatDateTime(run.startedUtc)}>{run.startedUtc ? formatRelativeTime(run.startedUtc) : '-'}</dd>
          <dt>{t('Completed')}</dt><dd title={formatDateTime(run.completedUtc)}>{run.completedUtc ? formatRelativeTime(run.completedUtc) : '-'}</dd>
          {run.kind === 'Command' && <><dt>{t('Timeout')}</dt><dd>{t('{{value}} s', { value: run.timeoutSeconds.toLocaleString() })}</dd></>}
          {run.kind === 'Command' && <><dt>{t('Clean-tree check')}</dt><dd>{run.requiresCleanWorkingTree ? t('On') : t('Off')}</dd></>}
        </dl>
        <details className="fleet-run-definition">
          <summary>{run.kind === 'Command' ? t('Command (snapshot)') : t('Prompt template (snapshot)')}</summary>
          <pre className="code-block" data-i18n-skip="true">{(run.kind === 'Command' ? run.commandText : run.promptTemplate) ?? ''}</pre>
        </details>
      </div>

      <div className="table-toolbar">
        <div className="table-toolbar-left">
          <h3 className="table-toolbar-title">{t('Targets')}</h3>
          <label className="inline-filter">
            <span>{t('Status')}</span>
            <select value={targetStatus} onChange={(e) => updateParams({ status: e.target.value, page: null })} aria-label={t('Filter targets by status')}>
              <option value="">{t('All statuses')}</option>
              {TARGET_STATUSES.map((s) => <option key={s} value={s}>{t(TARGET_STATUS_META[s].label)}</option>)}
            </select>
          </label>
          {targetStatus && <button type="button" className="btn btn-sm" onClick={() => updateParams({ status: null, page: null })}>{t('Clear filter')}</button>}
        </div>
      </div>

      {targetsError && <ErrorState message={targetsError} onRetry={() => void refresh()} />}
      <DataTable
        tableKey="fleet-action-run-targets"
        rows={targets}
        rowKey={(target) => target.id}
        onRowClick={onTargetClick}
        isRowClickable={(target) => run?.kind !== 'Mission' || Boolean(target.voyageId)}
        pagination={{
          pageNumber,
          pageSize,
          totalPages: targetsPages,
          totalRecords: targetsTotal,
          onPageChange: (p) => updateParams({ page: String(p) }),
          onPageSizeChange: (s) => { setPageSize(s); updateParams({ page: null }); },
        }}
        onRefresh={refresh}
        refreshTitle={t('Refresh run')}
        placeholder={targetsTotal > 0 ? undefined : (targetsLoading && !targetsError) ? <LoadingState /> : !targetsError ? (
          <EmptyState title={targetStatus ? t('No targets match this status') : t('This run has no targets')} />
        ) : <></>}
        columns={[
          {
            key: 'vessel', label: t('Vessel'), required: true,
            cellTitle: (target) => target.vesselId,
            render: (target) => <Link to={`/vessels/${target.vesselId}`} onClick={(e) => e.stopPropagation()}><strong>{target.vesselName}</strong></Link>,
          },
          {
            key: 'vesselId', label: t('Vessel ID'), defaultHidden: true, cellClassName: 'mono text-dim table-id-cell',
            render: (target) => (
              <span className="id-display">
                <span className="id-value" title={target.vesselId}>{target.vesselId}</span>
                <CopyButton text={target.vesselId} />
              </span>
            ),
          },
          { key: 'status', label: t('Status'), cellClassName: 'cell-nowrap', render: (target) => <CodeStatusBadge {...targetStatusBadge(t, target.status)} /> },
          {
            key: 'reason', label: t('Reason'),
            cellTitle: (target) => target.skipReason ?? target.failureReason ?? '',
            render: (target) => reasonLabel(t, target.skipReason, target.failureReason) || <span className="text-dim">-</span>,
          },
          ...(run.kind === 'Command' ? [{
            key: 'exitCode', label: t('Exit code'), className: 'text-right', cellClassName: 'mono',
            render: (target: FleetActionRunTargetSummary) => target.exitCode ?? '-',
          }] : []),
          {
            key: 'duration', label: t('Duration'), className: 'text-right', cellClassName: 'mono nowrap',
            render: (target) => formatDurationMs(t, locale, target.durationMs ?? durationBetween(target.startedUtc, target.completedUtc, target.status === 'Running')),
          },
          ...(run.kind === 'Mission' ? [{
            key: 'voyage', label: t('Voyage'), cellClassName: 'mono',
            render: (target: FleetActionRunTargetSummary) => (target.voyageId
              ? <Link to={`/voyages/${target.voyageId}`} className="mono" onClick={(e) => e.stopPropagation()}>{target.voyageId}</Link>
              : <span className="text-dim">-</span>),
          }] : []),
          ...(run.kind === 'Command' ? [{
            key: 'output', label: t('Output'), interactive: true, cellClassName: 'cell-nowrap',
            render: (target: FleetActionRunTargetSummary) => (
              <>
                <button type="button" className="btn btn-sm" onClick={() => setDrawerTargetId(target.id)} aria-label={t('View output for {{name}}', { name: target.vesselName })}>
                  {t('View output')}
                </button>
                {target.outputTruncated && <span className="tag review" style={{ marginLeft: '0.35rem' }} title={t('Output was truncated.')}>{t('Truncated')}</span>}
              </>
            ),
          }] : []),
        ]}
      />

      <TargetDetailDrawer runId={run.id} targetId={drawerTargetId} onClose={() => setDrawerTargetId(null)} />
      <JsonViewer open={showJson} title={t('Fleet action run: {{name}}', { name: run.actionName })} id={run.id} data={run} onClose={() => setShowJson(false)} />
      <ConfirmDialog
        open={confirmCancel}
        title={t('Cancel run')}
        message={t('Cancel "{{name}}"? Pending targets are cancelled and running commands are stopped. Mission runs cancel voyages that have not finished.', { name: run.actionName })}
        confirmLabel={t('Cancel run')}
        cancelLabel={t('Keep running')}
        danger
        onConfirm={() => void handleCancel()}
        onCancel={() => setConfirmCancel(false)}
      />
      {rerun && (
        <RunActionModal
          open
          vesselIds={rerun.vesselIds}
          initialActionId={rerun.actionId}
          initialDefinition={rerun.definition}
          onClose={() => setRerun(null)}
        />
      )}
    </div>
  );
}
