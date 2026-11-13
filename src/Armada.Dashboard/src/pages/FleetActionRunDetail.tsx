import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import {
  cancelFleetActionRun,
  enumerateFleetActionRunTargets,
  enumerateFleetActions,
  getFleetAction,
  getFleetActionRun,
} from '../api/client';
import type {
  FleetAction,
  FleetActionRun,
  FleetActionRunTargetSummary,
  FleetActionTargetStatus,
  FleetActionUpsertRequest,
} from '../types/models';
import { fetchAllPages, listAll } from '../api/paging';
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
import RunReport from '../components/fleetActions/RunReport';
import FollowUpPickerModal from '../components/fleetActions/FollowUpPickerModal';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import { usePersistedPageSize } from '../lib/usePersistedPageSize';
import {
  KIND_LABELS,
  MISSION_MODE_LABELS,
  TARGET_STATUSES,
  TARGET_STATUS_META,
  durationBetween,
  formatDurationMs,
  isReportOnly,
  isRunActive,
  reasonLabel,
  runStatusBadge,
  targetStatusBadge,
} from '../lib/fleetActionLabels';
import { definitionFromRun, orderFollowUpActions } from '../lib/fleetActionForm';

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
  const viewParam = searchParams.get('view');
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
  const [actions, setActions] = useState<FleetAction[] | null>(null);
  const [sourceBuiltInKey, setSourceBuiltInKey] = useState<string | null>(null);
  const [pickerOpen, setPickerOpen] = useState(false);
  const [followUpActionId, setFollowUpActionId] = useState<string | null>(null);

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

  // Saved actions and the run's own built-in key, for the Follow up picker and the suggested-fix call to action.
  const runActionId = run?.actionId ?? null;
  const runLoaded = run !== null;
  useEffect(() => {
    if (!runLoaded || !isTenantAdmin) return;
    let cancelled = false;
    listAll(enumerateFleetActions).then((r) => { if (!cancelled) setActions(r.objects || []); }).catch(() => { if (!cancelled) setActions([]); });
    if (runActionId) {
      getFleetAction(runActionId).then((a) => { if (!cancelled) setSourceBuiltInKey(a.builtInKey); }).catch(() => undefined);
    }
    return () => { cancelled = true; };
  }, [runLoaded, runActionId, isTenantAdmin]);

  const followUpActions = useMemo(
    () => orderFollowUpActions(actions ?? [], { actionId: runActionId, builtInKey: sourceBuiltInKey }),
    [actions, runActionId, sourceBuiltInKey],
  );

  const active = isRunActive(run?.status);
  const overlayOpen = drawerTargetId !== null || confirmCancel || showJson || rerun !== null || pickerOpen || followUpActionId !== null;
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
        fetchAllPages((pageNumber, pageSize) => enumerateFleetActionRunTargets(run.id, { pageNumber, pageSize, status: 'Failed' })),
        fetchAllPages((pageNumber, pageSize) => enumerateFleetActionRunTargets(run.id, { pageNumber, pageSize, status: 'TimedOut' })),
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
          breadcrumb={[
            { label: t('Fleet Actions'), to: '/fleet-actions' },
            { label: t('Runs'), to: '/fleet-actions?tab=runs' },
            { label: id },
          ]}
        />
        <ErrorState message={runError || t('Run not found.')} onRetry={() => void refresh()} />
      </div>
    );
  }

  const badge = runStatusBadge(t, run.status);
  const reportOnly = isReportOnly(run);
  const view: 'targets' | 'report' = run.kind !== 'Mission' ? 'targets' : viewParam === 'report' || viewParam === 'targets' ? viewParam : (reportOnly ? 'report' : 'targets');
  const fixCount = (run.findingsCount ?? 0) + run.failedCount;
  const suggestedFix = !active && fixCount > 0 ? followUpActions.suggested[0] ?? null : null;
  const duration = durationBetween(run.startedUtc, run.completedUtc, active);
  const refreshState = !active
    ? t('Run finished; auto-refresh stopped.')
    : autoRefreshing
      ? t('Live: refreshing every {{seconds}} s.', { seconds: RUN_DETAIL_REFRESH_SECONDS })
      : t('Auto-refresh paused while a panel is open.');

  return (
    <div className="fleet-run-detail">
      <PageHeader
        breadcrumb={[
          { label: t('Fleet Actions'), to: '/fleet-actions' },
          { label: t('Runs'), to: '/fleet-actions?tab=runs' },
          { label: run.id },
        ]}
        title={(
          <span className="fleet-run-title">
            <span>{run.actionName}</span>
            <CodeStatusBadge {...badge} />
            {reportOnly && <CodeStatusBadge label={t('Report-only')} tone="info" icon="info" title={t('Audit or Research run: captains change nothing, and each vessel report is kept on the run.')} />}
          </span>
        )}
        subtitle={(
          <span className="id-display">
            <span>{t(KIND_LABELS[run.kind])}</span>
            {!run.actionId && <span className="tag idle" title={t('Started with a one-off inline definition instead of a saved action.')}>{t('Ad hoc')}</span>}
            <span className="mono">{run.id}</span>
            <CopyButton text={run.id} title={t('Copy the run ID, for example to check it with the CLI or MCP tools.')} />
          </span>
        )}
        actions={(
          <>
            <span className={`refresh-state${autoRefreshing ? ' refresh-state-live' : ''}`} role="status" aria-live="polite" title={t('This page reloads on its own while the run is active and stops once it finishes. It pauses while a panel is open.')}>
              {autoRefreshing && <span className="follow-dot follow-dot-active" aria-hidden="true" />}
              {refreshState}
            </span>
            <button type="button" className="btn btn-sm" onClick={() => setShowJson(true)} title={t('Show the raw run record as returned by the API.')}>{t('View JSON')}</button>
            {suggestedFix && isTenantAdmin && (
              <button type="button" className="btn btn-sm btn-primary" onClick={() => setFollowUpActionId(suggestedFix.id)} title={t('Run {{name}} on the vessels with findings or failures from this run. You review it before it starts.', { name: suggestedFix.name })}>
                {t('{count, plural, one {Fix # vessel with {{name}}} other {Fix # vessels with {{name}}}}', { count: fixCount, name: suggestedFix.name })}
              </button>
            )}
            {isTenantAdmin && (
              <button type="button" className="btn btn-sm" onClick={() => setPickerOpen(true)} disabled={run.targetCount === 0} title={t('Run another action on vessels from this run, chosen by their result.')}>
                {t('Follow up...')}
              </button>
            )}
            {!active && run.failedCount > 0 && isTenantAdmin && (
              <button type="button" className="btn btn-sm" onClick={() => void startRerunFailed()} disabled={rerunLoading} title={t('Start a new run of the same action on only the vessels that failed or timed out. You review it before it starts.')}>
                {rerunLoading ? t('Loading...') : t('Re-run failed targets')}
              </button>
            )}
            {active && isTenantAdmin && (
              <button type="button" className="btn btn-sm btn-danger" onClick={() => setConfirmCancel(true)} title={t('Stop the run: pending vessels are cancelled, running commands are killed, and unfinished voyages are cancelled. Asks first.')}>{t('Cancel run')}</button>
            )}
          </>
        )}
      />

      {runError && <ErrorState message={runError} onRetry={() => void refresh()} />}

      <div className="card fleet-run-summary">
        <RunProgress run={run} />
        <div className="fleet-run-stats">
          <div className="fleet-run-stat" title={t('Vessels included in this run.')}><span className="fleet-run-stat-label">{t('Targets')}</span><span className="fleet-run-stat-value">{run.targetCount.toLocaleString()}</span></div>
          <div className="fleet-run-stat"><span className="fleet-run-stat-label">{t('Findings')}</span><span className="fleet-run-stat-value">{(run.findingsCount ?? 0).toLocaleString()}</span></div>
          <div className="fleet-run-stat" title={t('Vessels where the command exited 0 or the voyage completed.')}><span className="fleet-run-stat-label">{t('Succeeded')}</span><span className="fleet-run-stat-value">{run.succeededCount.toLocaleString()}</span></div>
          <div className="fleet-run-stat" title={t('Vessels where the command failed or timed out, or the voyage failed.')}><span className="fleet-run-stat-label">{t('Failed')}</span><span className="fleet-run-stat-value">{run.failedCount.toLocaleString()}</span></div>
          <div className="fleet-run-stat" title={t('Vessels Armada declined to act on, for example a dirty tree or no working directory. Not counted as failures.')}><span className="fleet-run-stat-label">{t('Skipped')}</span><span className="fleet-run-stat-value">{run.skippedCount.toLocaleString()}</span></div>
          <div className="fleet-run-stat" title={t('Vessels that were not finished because the run was cancelled.')}><span className="fleet-run-stat-label">{t('Cancelled')}</span><span className="fleet-run-stat-value">{run.cancelledCount.toLocaleString()}</span></div>
          <div className="fleet-run-stat" title={t('How many vessels this run works on at once.')}><span className="fleet-run-stat-label">{t('Concurrency')}</span><span className="fleet-run-stat-value">{run.concurrency.toLocaleString()}</span></div>
          <div className="fleet-run-stat" title={t('Time from the first vessel starting to the last one finishing, or so far.')}><span className="fleet-run-stat-label">{t('Duration')}</span><span className="fleet-run-stat-value mono">{formatDurationMs(t, locale, duration)}</span></div>
        </div>
        <dl className="detail-kv detail-kv-inline">
          <dt>{t('Created')}</dt><dd title={formatDateTime(run.createdUtc)}>{formatRelativeTime(run.createdUtc)}</dd>
          <dt>{t('Started')}</dt><dd title={formatDateTime(run.startedUtc)}>{run.startedUtc ? formatRelativeTime(run.startedUtc) : '-'}</dd>
          <dt>{t('Completed')}</dt><dd title={formatDateTime(run.completedUtc)}>{run.completedUtc ? formatRelativeTime(run.completedUtc) : '-'}</dd>
          {run.kind === 'Command' && <><dt title={t('Seconds the command may run on each vessel before it is killed.')}>{t('Timeout')}</dt><dd>{t('{{value}} s', { value: run.timeoutSeconds.toLocaleString() })}</dd></>}
          {run.kind === 'Command' && <><dt title={t('On means vessels with uncommitted or untracked changes were skipped.')}>{t('Clean-tree check')}</dt><dd>{run.requiresCleanWorkingTree ? t('On') : t('Off')}</dd></>}
          {run.kind === 'Mission' && <><dt title={t('Audit and Research are report-only: nothing lands, and the report appears on the run.')}>{t('Mission mode')}</dt><dd>{t(MISSION_MODE_LABELS[run.missionMode ?? 'Implementation'])}</dd></>}
        </dl>
        {run.applicabilityCommand && (
          <details className="fleet-run-definition">
            <summary title={t('Shell check run in each vessel first. A non-zero exit skipped the vessel as Not applicable.')}>{t('Applicability check (snapshot)')}</summary>
            <pre className="code-block" data-i18n-skip="true">{run.applicabilityCommand}</pre>
          </details>
        )}
        <details className="fleet-run-definition">
          <summary title={t('The definition as it was when this run started. Later edits to the action do not change it.')}>{run.kind === 'Command' ? t('Command (snapshot)') : t('Prompt template (snapshot)')}</summary>
          <pre className="code-block" data-i18n-skip="true">{(run.kind === 'Command' ? run.commandText : run.promptTemplate) ?? ''}</pre>
        </details>
      </div>

      {run.kind === 'Mission' && (
        <div className="segmented" role="radiogroup" aria-label={t('View')}>
          <label className={`segmented-option${view === 'targets' ? ' active' : ''}`} title={t('One row per vessel with its status and voyage.')}>
            <input type="radio" name="run-view" checked={view === 'targets'} onChange={() => updateParams({ view: 'targets' })} />
            <span>{t('Targets')}</span>
          </label>
          <label className={`segmented-option${view === 'report' ? ' active' : ''}`} title={t('Each vessel\'s final captain report, rendered as Markdown.')}>
            <input type="radio" name="run-view" checked={view === 'report'} onChange={() => updateParams({ view: 'report' })} />
            <span>{t('Report')}</span>
          </label>
        </div>
      )}

      {view === 'report' ? <RunReport run={run} /> : (<>
      <div className="table-toolbar">
        <div className="table-toolbar-left">
          <h3 className="table-toolbar-title">{t('Targets')}</h3>
          <label className="inline-filter">
            <span>{t('Status')}</span>
            <select value={targetStatus} onChange={(e) => updateParams({ status: e.target.value, page: null })} aria-label={t('Filter targets by status')} title={t('Show only vessels with one outcome, for example Failed or Skipped.')}>
              <option value="">{t('All statuses')}</option>
              {TARGET_STATUSES.map((s) => <option key={s} value={s}>{t(TARGET_STATUS_META[s].label)}</option>)}
            </select>
          </label>
          {targetStatus && <button type="button" className="btn btn-sm" onClick={() => updateParams({ status: null, page: null })} title={t('Show vessels of every status again.')}>{t('Clear filter')}</button>}
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
        refreshTitle={t('Reload the run and its vessel results now.')}
        placeholder={targetsTotal > 0 ? undefined : (targetsLoading && !targetsError) ? <LoadingState /> : !targetsError ? (
          <EmptyState title={targetStatus ? t('No targets match this status') : t('This run has no targets')} />
        ) : <></>}
        columns={[
          {
            key: 'vessel', label: t('Vessel'), headerTitle: t('The vessel this row is about. Click a row for its output, or its voyage for Mission runs.'), required: true,
            cellTitle: (target) => target.vesselId,
            render: (target) => <Link to={`/vessels/${target.vesselId}`} onClick={(e) => e.stopPropagation()}><strong>{target.vesselName}</strong></Link>,
          },
          {
            key: 'vesselId', label: t('Vessel ID'), headerTitle: t('Vessel ID (vsl_ prefix).'), defaultHidden: true, cellClassName: 'mono text-dim table-id-cell',
            render: (target) => (
              <span className="id-display">
                <span className="id-value" title={target.vesselId}>{target.vesselId}</span>
                <CopyButton text={target.vesselId} />
              </span>
            ),
          },
          { key: 'status', label: t('Status'), headerTitle: t('Outcome for this vessel. Hover a badge for what it means.'), cellClassName: 'cell-nowrap', render: (target) => <CodeStatusBadge {...targetStatusBadge(t, target.status)} /> },
          {
            key: 'reason', label: t('Reason'), headerTitle: t('Why the vessel was skipped or failed. Hover a cell for the reason code.'),
            cellTitle: (target) => target.skipReason ?? target.failureReason ?? '',
            render: (target) => reasonLabel(t, target.skipReason, target.failureReason) || <span className="text-dim">-</span>,
          },
          ...(run.kind === 'Command' ? [{
            key: 'exitCode', label: t('Exit code'), headerTitle: t('Exit code of the command. 0 is success.'), className: 'text-right', cellClassName: 'mono',
            render: (target: FleetActionRunTargetSummary) => target.exitCode ?? '-',
          }] : []),
          {
            key: 'duration', label: t('Duration'), headerTitle: t('How long this vessel took, or so far while running.'), className: 'text-right', cellClassName: 'mono nowrap',
            render: (target) => formatDurationMs(t, locale, target.durationMs ?? durationBetween(target.startedUtc, target.completedUtc, target.status === 'Running')),
          },
          ...(run.kind === 'Mission' ? [{
            key: 'voyage', label: t('Voyage'), headerTitle: t('The voyage dispatched for this vessel. Open it to follow the captain work.'), cellClassName: 'mono',
            render: (target: FleetActionRunTargetSummary) => (target.voyageId
              ? <Link to={`/voyages/${target.voyageId}`} className="mono" onClick={(e) => e.stopPropagation()}>{target.voyageId}</Link>
              : <span className="text-dim">-</span>),
          }] : []),
          ...(run.kind === 'Command' ? [{
            key: 'output', label: t('Output'), headerTitle: t('Captured standard output and error of the command.'), interactive: true, cellClassName: 'cell-nowrap',
            render: (target: FleetActionRunTargetSummary) => (
              <>
                <button type="button" className="btn btn-sm" onClick={() => setDrawerTargetId(target.id)} aria-label={t('View output for {{name}}', { name: target.vesselName })} title={t('Open the command, exit code, and captured output for this vessel.')}>
                  {t('View output')}
                </button>
                {target.outputTruncated && <span className="tag review" style={{ marginLeft: '0.35rem' }} title={t('The output was longer than the per-stream limit, so only its end was kept. Change the limit in Server Settings > Fleet Actions.')}>{t('Truncated')}</span>}
              </>
            ),
          }] : []),
        ]}
      />
      </>)}

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
      <FollowUpPickerModal
        open={pickerOpen}
        runId={run.id}
        suggested={followUpActions.suggested}
        others={followUpActions.others}
        loading={actions === null}
        onClose={() => setPickerOpen(false)}
        onPick={(actionId) => { setPickerOpen(false); setFollowUpActionId(actionId); }}
      />
      {followUpActionId && (
        <RunActionModal
          open
          vesselIds={[]}
          initialActionId={followUpActionId}
          followUp={{ runId: run.id }}
          previewVesselId={targets[0]?.vesselId ?? null}
          onClose={() => setFollowUpActionId(null)}
        />
      )}
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
