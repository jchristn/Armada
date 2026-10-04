import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { enumerateVesselHealth, getVesselHealthSummary, listFleets, listVessels } from '../api/client';
import { useAuth } from '../context/AuthContext';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import type { Fleet, Job, VesselHealth as VesselHealthRow, VesselHealthSortField, VesselHealthStatus, VesselHealthSummary } from '../types/models';
import PageHeader from '../components/shared/PageHeader';
import Pagination from '../components/shared/Pagination';
import ActionMenu from '../components/shared/ActionMenu';
import JsonViewer from '../components/shared/JsonViewer';
import RefreshButton from '../components/shared/RefreshButton';
import AutoRefreshSelect from '../components/shared/AutoRefreshSelect';
import LoadingIndicator from '../components/shared/LoadingIndicator';
import BranchesModal from '../components/vessels/BranchesModal';
import HealthStatusBadge from '../components/vessels/health/HealthStatusBadge';
import ChecklistDropdown from '../components/vessels/health/ChecklistDropdown';
import VesselHealthDetailModal, { type HealthDetailSection } from '../components/vessels/health/VesselHealthDetailModal';
import '../components/vessels/health/vesselHealth.css';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import { useTablePrefs } from '../lib/useTablePrefs';
import {
  DEFAULT_HEALTH_FILTERS,
  DIVERGENCE_FILTERS,
  buildEnumerateRequest,
  filtersFromQuery,
  filtersToQuery,
  hasActiveFilters,
  toggleSort,
  type HealthFilters,
  type TriState,
} from '../lib/health/healthFilters';
import { HEALTH_STATUSES, formatCount, msg, severityLabel, statusLabel } from '../lib/health/healthText';
import { describeEvaluationStart, useHealthEvaluation } from '../lib/health/useHealthEvaluation';
import RunActionModal from '../components/fleetActions/RunActionModal';

const TABLE_KEY = 'vessel-health';
const PINNED_COLUMNS = ['vessel', 'overall'];
const TEXT_DEBOUNCE_MS = 350;

interface VesselHealthProps {
  /**
   * Optional bulk "Run action..." handler. When omitted, the page opens the shared RunActionModal with the
   * selected vessels and the Mission kind preselected (the built-in templates reference {{health.summary}}).
   */
  onRunAction?: (vesselIds: string[]) => void;
}

interface ColumnDef {
  key: string;
  label: string;
  title: string;
  sort?: VesselHealthSortField;
  className?: string;
  render: (row: VesselHealthRow) => ReactNode;
}

interface DetailState {
  vesselId: string;
  vesselName: string;
  section: HealthDetailSection;
}

function parseJobResult(job: Job): { evaluated: number; failed: number } | null {
  if (!job.resultJson) return null;
  try {
    const parsed = JSON.parse(job.resultJson) as { evaluated?: number; failed?: number };
    return { evaluated: parsed.evaluated ?? 0, failed: parsed.failed ?? 0 };
  } catch {
    return null;
  }
}

/**
 * Vessel Health tab (`/vessels/health`): find vessels that need attention and act on them. Filtering,
 * sorting and paging run on the server; the filter state lives in the URL query string so Home KPI links
 * and reloads restore it.
 */
export default function VesselHealth({ onRunAction }: VesselHealthProps) {
  const navigate = useNavigate();
  const { t, locale, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const { isTenantAdmin } = useAuth();
  const [searchParams, setSearchParams] = useSearchParams();
  const queryKey = searchParams.toString();
  const filters = useMemo(() => filtersFromQuery(new URLSearchParams(queryKey)), [queryKey]);
  const prefs = useTablePrefs(TABLE_KEY, { pinned: PINNED_COLUMNS });

  const [rows, setRows] = useState<VesselHealthRow[]>([]);
  const [runActionIds, setRunActionIds] = useState<string[] | null>(null);
  const [totalRecords, setTotalRecords] = useState(0);
  const [totalPages, setTotalPages] = useState(1);
  const [loading, setLoading] = useState(true);
  const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState('');
  const [summary, setSummary] = useState<VesselHealthSummary | null>(null);
  const [summaryError, setSummaryError] = useState(false);
  const [fleets, setFleets] = useState<Fleet[]>([]);
  const [defaultBranches, setDefaultBranches] = useState<Record<string, string>>({});
  const [selected, setSelected] = useState<string[]>([]);
  const [detail, setDetail] = useState<DetailState | null>(null);
  const [branches, setBranches] = useState<{ vesselId: string; vesselName: string } | null>(null);
  const [jsonView, setJsonView] = useState<{ title: string; data: unknown } | null>(null);
  const [refreshToken, setRefreshToken] = useState(0);

  // Text inputs are drafts that write to the URL after a short pause.
  const [nameDraft, setNameDraft] = useState(filters.name);
  const [minDraft, setMinDraft] = useState(filters.minBranches);
  const [maxDraft, setMaxDraft] = useState(filters.maxBranches);
  useEffect(() => { setNameDraft(filters.name); }, [filters.name]);
  useEffect(() => { setMinDraft(filters.minBranches); }, [filters.minBranches]);
  useEffect(() => { setMaxDraft(filters.maxBranches); }, [filters.maxBranches]);

  const updateFilters = useCallback((next: HealthFilters) => {
    setSearchParams((current) => filtersToQuery(next, current), { replace: true });
  }, [setSearchParams]);

  /** Applies a filter change and returns to page 1. */
  const patchFilters = useCallback((patch: Partial<HealthFilters>) => {
    updateFilters({ ...filters, ...patch, page: 1 });
  }, [filters, updateFilters]);

  useEffect(() => {
    const handle = window.setTimeout(() => {
      const name = nameDraft.trim();
      const min = /^\d*$/.test(minDraft.trim()) ? minDraft.trim() : filters.minBranches;
      const max = /^\d*$/.test(maxDraft.trim()) ? maxDraft.trim() : filters.maxBranches;
      if (name !== filters.name.trim() || min !== filters.minBranches || max !== filters.maxBranches) {
        updateFilters({ ...filters, name, minBranches: min, maxBranches: max, page: 1 });
      }
    }, TEXT_DEBOUNCE_MS);
    return () => window.clearTimeout(handle);
  }, [nameDraft, minDraft, maxDraft, filters, updateFilters]);

  // ---- Data loading ----
  const requestSeq = useRef(0);
  const loadRows = useCallback(async () => {
    const seq = ++requestSeq.current;
    setLoading(true);
    try {
      const result = await enumerateVesselHealth(buildEnumerateRequest(filters, prefs.pageSize));
      if (seq !== requestSeq.current) return;
      setRows(result.objects ?? []);
      setTotalRecords(result.totalRecords ?? 0);
      setTotalPages(Math.max(1, result.totalPages ?? 1));
      setError('');
      setLoaded(true);
    } catch (err) {
      if (seq !== requestSeq.current) return;
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      if (seq === requestSeq.current) setLoading(false);
    }
  }, [filters, prefs.pageSize]);

  const loadSummary = useCallback(async () => {
    try {
      const result = await getVesselHealthSummary();
      setSummary(result);
      setSummaryError(false);
    } catch {
      setSummaryError(true);
    }
  }, []);

  const refreshAll = useCallback(async () => {
    await Promise.all([loadRows(), loadSummary()]);
  }, [loadRows, loadSummary]);

  useEffect(() => { void loadRows(); }, [loadRows]);
  useEffect(() => { void loadSummary(); }, [loadSummary]);

  // Selection never survives a page, filter, sort or page-size change.
  useEffect(() => { setSelected([]); }, [queryKey, prefs.pageSize]);

  useEffect(() => {
    listFleets({ pageSize: 9999 }).then((r) => setFleets(r.objects ?? [])).catch(() => setFleets([]));
    listVessels({ pageSize: 9999 }).then((r) => {
      const map: Record<string, string> = {};
      for (const v of r.objects ?? []) if (v.defaultBranch) map[v.id] = v.defaultBranch;
      setDefaultBranches(map);
    }).catch(() => setDefaultBranches({}));
  }, []);

  const modalOpen = detail !== null || branches !== null || jsonView !== null;
  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh(TABLE_KEY, () => {
    if (!modalOpen) void refreshAll();
  });

  // ---- Evaluation ----
  const evaluation = useHealthEvaluation({
    onFinished: (job) => {
      const result = parseJobResult(job);
      if (job.status === 'Succeeded') {
        pushToast(result && result.failed > 0 ? 'warning' : 'success', result
          ? t('Evaluation finished: {{evaluated}} evaluated, {{failed}} failed.', { evaluated: formatCount(locale, result.evaluated), failed: formatCount(locale, result.failed) })
          : t('Evaluation finished.'));
      } else if (job.status === 'Failed') {
        pushToast('error', t('Evaluation failed: {{reason}}', { reason: job.errorReason ?? t('Unknown error') }));
      } else {
        pushToast('warning', t('Evaluation was cancelled.'));
      }
      setRefreshToken((n) => n + 1);
      void refreshAll();
    },
  });
  const { discover } = evaluation;
  useEffect(() => { void discover(); }, [discover]);

  const startEvaluation = useCallback(async (vesselIds?: string[]) => {
    try {
      const body = vesselIds && vesselIds.length > 0 ? { VesselIds: vesselIds, Force: true } : { Force: true };
      const start = await evaluation.start(body);
      const message = describeEvaluationStart(start);
      const params: Record<string, string> = {};
      for (const [k, v] of Object.entries(message.params)) params[k] = formatCount(locale, v);
      pushToast(message.severity, t(message.key, params));
      return true;
    } catch (err) {
      pushToast('error', t('Could not start the evaluation: {{message}}', { message: err instanceof Error ? err.message : String(err) }));
      return false;
    }
  }, [evaluation, locale, pushToast, t]);

  // ---- Bulk actions ----
  const runAction = useCallback((vesselIds: string[]) => {
    if (onRunAction) {
      onRunAction(vesselIds);
      return;
    }
    setRunActionIds(vesselIds);
  }, [onRunAction]);

  async function reevaluateSelected() {
    const ids = [...selected];
    const ok = await startEvaluation(ids);
    if (ok) setSelected([]);
  }

  // ---- Helpers ----
  const fleetOptions = useMemo(() => [...fleets].sort((a, b) => a.name.localeCompare(b.name)), [fleets]);

  function baseBranch(row: VesselHealthRow): string {
    return defaultBranches[row.vesselId] || t('the default branch');
  }

  function openDetail(row: VesselHealthRow, section: HealthDetailSection = 'summary') {
    setDetail({ vesselId: row.vesselId, vesselName: row.vesselName ?? row.vesselId, section });
  }

  function statusSummary(list: VesselHealthStatus[]): string | undefined {
    if (list.length === 0) return undefined;
    if (list.length === 1) return statusLabel(t, list[0]);
    return formatCount(locale, list.length);
  }

  function toggleStatus(list: VesselHealthStatus[], value: string): VesselHealthStatus[] {
    const status = value as VesselHealthStatus;
    const next = list.includes(status) ? list.filter((s) => s !== status) : [...list, status];
    return HEALTH_STATUSES.filter((s) => next.includes(s));
  }

  const statusOptions = HEALTH_STATUSES.map((s) => ({ value: s, label: statusLabel(t, s) }));

  // ---- Columns ----
  const columns: ColumnDef[] = [
    {
      key: 'vessel',
      label: msg('Vessel'),
      title: msg('Vessel name; opens the vessel page'),
      sort: 'VesselName',
      className: 'vh-col-vessel',
      render: (row) => (
        <div className="vh-vessel-cell">
          <Link to={`/vessels/${encodeURIComponent(row.vesselId)}`} onClick={(e) => e.stopPropagation()} title={row.vesselId}>
            <strong>{row.vesselName || row.vesselId}</strong>
          </Link>
          {row.currentBranch && <span className="mono text-dim vh-branch" title={t('Checked-out branch')}>{row.currentBranch}</span>}
        </div>
      ),
    },
    {
      key: 'fleet',
      label: msg('Fleet'),
      title: msg('Fleet the vessel belongs to'),
      sort: 'FleetName',
      render: (row) => (row.fleetId
        ? <Link to={`/fleets/${encodeURIComponent(row.fleetId)}`} onClick={(e) => e.stopPropagation()}>{row.fleetName || row.fleetId}</Link>
        : <span className="text-dim">-</span>),
    },
    {
      key: 'overall',
      label: msg('Overall'),
      title: msg('Worst status among the scored criteria, after overrides'),
      sort: 'OverallStatus',
      render: (row) => <HealthStatusBadge status={row.overallStatus} />,
    },
    {
      key: 'divergence',
      label: msg('Divergence'),
      title: msg('Commits ahead of and behind the default branch'),
      sort: 'Divergence',
      render: (row) => {
        if ((row.aheadOfDefault === null || row.aheadOfDefault === undefined) && (row.behindDefault === null || row.behindDefault === undefined)) {
          return <span className="text-dim" title={t('Not measured')}>-</span>;
        }
        const ahead = row.aheadOfDefault ?? 0;
        const behind = row.behindDefault ?? 0;
        const tooltip = t('{{ahead}} ahead and {{behind}} behind {{base}}', { ahead: formatCount(locale, ahead), behind: formatCount(locale, behind), base: baseBranch(row) });
        return (
          <span className={`vh-div vh-div-${row.divergenceStatus.toLowerCase()}`} title={tooltip} aria-label={tooltip}>
            <span className={ahead > 0 ? 'vh-div-ahead' : 'text-dim'}>{'\u2191'}{formatCount(locale, ahead)}</span>
            <span className={behind > 0 ? 'vh-div-behind' : 'text-dim'}>{'\u2193'}{formatCount(locale, behind)}</span>
          </span>
        );
      },
    },
    {
      key: 'dirty',
      label: msg('Dirty'),
      title: msg('Uncommitted changes in the evaluated checkout'),
      sort: 'IsDirty',
      render: (row) => {
        if (row.isDirty === null || row.isDirty === undefined) return <span className="text-dim" title={t('Not measured')}>-</span>;
        if (row.isDirty) {
          return <span className="tag vh-badge warn" title={t('Modified tracked files ({{untracked}} untracked)', { untracked: formatCount(locale, row.untrackedCount ?? 0) })}>{t('Dirty')}</span>;
        }
        return (row.untrackedCount ?? 0) > 0
          ? <span className="text-dim">{t('{{count}} untracked', { count: formatCount(locale, row.untrackedCount ?? 0) })}</span>
          : <span className="text-dim">{t('Clean')}</span>;
      },
    },
    {
      key: 'branches',
      label: msg('Branches'),
      title: msg('Local branches, with stale branches in parentheses'),
      sort: 'BranchCount',
      render: (row) => (row.branchCount === null || row.branchCount === undefined
        ? <span className="text-dim">-</span>
        : (
          <span className="vh-nowrap" title={t('{{stale}} stale, {{armada}} leftover armada/*', { stale: formatCount(locale, row.staleBranchCount ?? 0), armada: formatCount(locale, row.armadaBranchCount ?? 0) })}>
            {formatCount(locale, row.branchCount)}
            {(row.staleBranchCount ?? 0) > 0 && <span className="text-dim"> {t('({{count}} stale)', { count: formatCount(locale, row.staleBranchCount ?? 0) })}</span>}
          </span>
        )),
    },
    {
      key: 'dependencies',
      label: msg('Dependencies'),
      title: msg('Outdated packages, with major drift in parentheses'),
      sort: 'OutdatedCount',
      render: (row) => (
        <span className="vh-cell-stack">
          <HealthStatusBadge status={row.dependencyStatus} compact title={t('Dependencies')} />
          {row.outdatedCount !== null && row.outdatedCount !== undefined && (
            <span className="vh-nowrap">
              {formatCount(locale, row.outdatedCount)}
              {(row.outdatedMajorCount ?? 0) > 0 && <span className="text-dim"> {t('({{count}} major)', { count: formatCount(locale, row.outdatedMajorCount ?? 0) })}</span>}
            </span>
          )}
        </span>
      ),
    },
    {
      key: 'vulnerabilities',
      label: msg('Vulnerabilities'),
      title: msg('Vulnerable packages and the highest severity'),
      sort: 'VulnerableCount',
      render: (row) => (
        <span className="vh-cell-stack">
          <HealthStatusBadge status={row.vulnerabilityStatus} compact title={t('Vulnerabilities')} />
          {row.vulnerableCount !== null && row.vulnerableCount !== undefined && row.vulnerableCount > 0 && (
            <span className="vh-nowrap">
              {formatCount(locale, row.vulnerableCount)}{' '}
              <span className={`tag vh-sev vh-sev-${(row.maxVulnerabilitySeverity ?? 'None').toLowerCase()}`}>{severityLabel(t, row.maxVulnerabilitySeverity)}</span>
            </span>
          )}
        </span>
      ),
    },
    {
      key: 'tests',
      label: msg('Tests'),
      title: msg('Test infrastructure and the latest test run'),
      sort: 'TestInfraStatus',
      render: (row) => <HealthStatusBadge status={row.testInfraStatus} compact title={t('Test infrastructure')} />,
    },
    {
      key: 'ci',
      label: msg('CI'),
      title: msg('Continuous integration configuration'),
      sort: 'CiStatus',
      render: (row) => <HealthStatusBadge status={row.ciStatus} compact title={t('Continuous integration')} />,
    },
    {
      key: 'lastCommit',
      label: msg('Last commit'),
      title: msg('Time of the last commit on the checked-out branch'),
      sort: 'LastCommitUtc',
      render: (row) => (row.lastCommitUtc
        ? <span className="vh-nowrap" title={formatDateTime(row.lastCommitUtc)}>{formatRelativeTime(row.lastCommitUtc)}</span>
        : <span className="text-dim">-</span>),
    },
    {
      key: 'evaluated',
      label: msg('Evaluated'),
      title: msg('When this vessel was last evaluated'),
      sort: 'EvaluatedUtc',
      render: (row) => (row.evaluatedUtc
        ? <span className="vh-nowrap" title={formatDateTime(row.evaluatedUtc)}>{formatRelativeTime(row.evaluatedUtc)}</span>
        : <span className="text-dim">{t('Never')}</span>),
    },
  ];
  const visibleColumns = columns.filter((c) => prefs.isVisible(c.key));
  const colSpan = visibleColumns.length + 2;

  function ariaSort(field?: VesselHealthSortField): 'ascending' | 'descending' | 'none' | undefined {
    if (!field) return undefined;
    if (filters.sortBy !== field) return 'none';
    return filters.sortDesc ? 'descending' : 'ascending';
  }

  function rowActions(row: VesselHealthRow) {
    return [
      { label: msg('View details'), onClick: () => openDetail(row) },
      { label: msg('Branches'), onClick: () => setBranches({ vesselId: row.vesselId, vesselName: row.vesselName ?? row.vesselId }) },
      { label: msg('Override...'), onClick: () => openDetail(row, 'overrides'), disabled: !isTenantAdmin },
      { label: msg('Re-evaluate'), onClick: () => { void startEvaluation([row.vesselId]); }, disabled: !isTenantAdmin || evaluation.running },
      { label: msg('Open vessel'), onClick: () => navigate(`/vessels/${encodeURIComponent(row.vesselId)}`) },
      { label: msg('View JSON'), onClick: () => setJsonView({ title: t('Health: {{name}}', { name: row.vesselName ?? row.vesselId }), data: row }) },
    ];
  }

  // ---- Page states ----
  const filtered = hasActiveFilters(filters);
  const noVessels = loaded && !error && totalRecords === 0 && !filtered && (summary ? summary.totalVessels === 0 : true);
  const noneEvaluated = !!summary && summary.totalVessels > 0 && summary.notEvaluated === summary.totalVessels;
  const pageIds = rows.map((r) => r.vesselId);
  const allSelected = pageIds.length > 0 && pageIds.every((id) => selected.includes(id));

  const summaryTiles: Array<{ key: string; value: number; status: VesselHealthStatus; text?: string; hint: string }> = summary ? [
    { key: 'fail', value: summary.fail, status: 'Fail', hint: msg('Show vessels that fail') },
    { key: 'warn', value: summary.warn, status: 'Warn', hint: msg('Show vessels with warnings') },
    { key: 'pass', value: summary.pass, status: 'Pass', hint: msg('Show healthy vessels') },
    { key: 'unknown', value: summary.unknown, status: 'Unknown', hint: msg('Show vessels whose status is unknown (includes never-evaluated vessels)') },
    ...(summary.notApplicable > 0
      ? [{ key: 'notApplicable', value: summary.notApplicable, status: 'NotApplicable' as VesselHealthStatus, hint: msg('Show vessels where no scored criterion applies') }]
      : []),
    { key: 'notEvaluated', value: summary.notEvaluated, status: 'Unknown', text: msg('Not evaluated'), hint: msg('Never-evaluated vessels have Unknown status; this shows all Unknown vessels') },
  ] : [];

  const lastJob = evaluation.lastJob;
  const activeJob = evaluation.activeJob;

  return (
    <div className="vh-page">
      <PageHeader
        title={t('Vessel Health')}
        subtitle={t('Find repositories that need attention: divergence, dirty checkouts, stale branches, outdated or vulnerable dependencies, tests, and CI.')}
        actions={(
          <>
            <AutoRefreshSelect seconds={refreshSeconds} onChange={setRefreshSeconds} />
            <RefreshButton onRefresh={refreshAll} title={msg('Refresh vessel health')} />
            {isTenantAdmin && (
              <button
                type="button"
                className="btn btn-primary btn-sm"
                disabled={evaluation.running || evaluation.starting}
                onClick={() => { void startEvaluation(); }}
                title={evaluation.running ? t('An evaluation is already running.') : t('Evaluate every active vessel now, including dependency checks.')}
              >
                {evaluation.running ? t('Evaluating...') : t('Evaluate all')}
              </button>
            )}
          </>
        )}
      />

      {/* Summary strip */}
      <section className="vh-summary" aria-label={t('Health summary')}>
        {summaryTiles.map((tile) => {
          const active = !tile.text && filters.overall.length === 1 && filters.overall[0] === tile.status;
          return (
            <button
              key={tile.key}
              type="button"
              className={`vh-summary-tile${active ? ' active' : ''}`}
              title={t(tile.hint)}
              aria-pressed={active}
              onClick={() => patchFilters({ overall: active ? [] : [tile.status] })}
            >
              <span className="vh-summary-value">{formatCount(locale, tile.value)}</span>
              {tile.text
                ? <span className="tag vh-badge notevaluated">{t(tile.text)}</span>
                : <HealthStatusBadge status={tile.status} compact />}
            </button>
          );
        })}
        {summaryError && !summary && <span className="text-dim">{t('Summary unavailable.')}</span>}
        <div className="vh-summary-meta">
          {summary && <span>{t('{{count}} vessels', { count: formatCount(locale, summary.totalVessels) })}</span>}
          {evaluation.running ? (
            <span className="vh-progress" role="status" aria-live="polite">
              <span className="vh-progress-label">
                {activeJob ? t('Evaluating... {{percent}}%', { percent: formatCount(locale, activeJob.progress ?? 0) }) : t('Evaluating...')}
              </span>
              <span className="vh-progress-track" aria-hidden="true">
                <span className="vh-progress-fill" style={{ width: `${Math.max(3, Math.min(100, activeJob?.progress ?? 0))}%` }} />
              </span>
            </span>
          ) : lastJob ? (
            <span className="text-dim" title={lastJob.completedUtc ? formatDateTime(lastJob.completedUtc) : undefined}>
              {t('Last evaluation {{when}}', { when: formatRelativeTime(lastJob.completedUtc ?? lastJob.lastUpdateUtc) })}
            </span>
          ) : null}
        </div>
      </section>

      {noneEvaluated && !noVessels && (
        <div className="vh-cta">
          <div>
            <strong>{t('No vessel has been evaluated yet.')}</strong>
            <div className="text-dim">{t('Run an evaluation to grade every vessel. Dependency checks can take a minute per repository.')}</div>
          </div>
          {isTenantAdmin && (
            <button type="button" className="btn btn-primary btn-sm" disabled={evaluation.running} onClick={() => { void startEvaluation(); }}>
              {t('Evaluate now')}
            </button>
          )}
        </div>
      )}

      {noVessels ? (
        <div className="vh-empty">
          <h3>{t('No vessels yet')}</h3>
          <p className="text-dim">{t('Import repositories to start tracking their health.')}</p>
          <Link className="btn btn-primary" to="/vessels/import">{t('Import repositories')}</Link>
        </div>
      ) : (
        <>
          {/* Filters */}
          <div className="vh-filters" role="search" aria-label={t('Health filters')}>
            <input
              type="search"
              className="vh-filter-name"
              value={nameDraft}
              placeholder={t('Vessel name contains')}
              aria-label={t('Vessel name contains')}
              onChange={(e) => setNameDraft(e.target.value)}
            />
            <select value={filters.fleetId} aria-label={t('Fleet')} title={t('Fleet')} onChange={(e) => patchFilters({ fleetId: e.target.value })}>
              <option value="">{t('All fleets')}</option>
              {fleetOptions.map((f) => <option key={f.id} value={f.id}>{f.name}</option>)}
            </select>
            <ChecklistDropdown
              label={t('Overall')}
              summary={statusSummary(filters.overall)}
              options={statusOptions}
              selected={filters.overall}
              ariaLabel={t('Overall status filter')}
              onToggle={(v) => patchFilters({ overall: toggleStatus(filters.overall, v) })}
              onClear={() => patchFilters({ overall: [] })}
            />
            <ChecklistDropdown
              label={t('Dependencies')}
              summary={statusSummary(filters.deps)}
              options={statusOptions}
              selected={filters.deps}
              ariaLabel={t('Dependency status filter')}
              onToggle={(v) => patchFilters({ deps: toggleStatus(filters.deps, v) })}
              onClear={() => patchFilters({ deps: [] })}
            />
            <ChecklistDropdown
              label={t('Tests')}
              summary={statusSummary(filters.tests)}
              options={statusOptions}
              selected={filters.tests}
              ariaLabel={t('Test status filter')}
              onToggle={(v) => patchFilters({ tests: toggleStatus(filters.tests, v) })}
              onClear={() => patchFilters({ tests: [] })}
            />
            <label className="vh-inline-field">
              <span>{t('Dirty')}</span>
              <select value={filters.dirty} onChange={(e) => patchFilters({ dirty: e.target.value as TriState })}>
                <option value="">{t('Any')}</option>
                <option value="yes">{t('Yes')}</option>
                <option value="no">{t('No')}</option>
              </select>
            </label>
            <label className="vh-inline-field">
              <span>{t('CI')}</span>
              <select value={filters.ci} onChange={(e) => patchFilters({ ci: e.target.value as TriState })}>
                <option value="">{t('Any')}</option>
                <option value="yes">{t('Has CI')}</option>
                <option value="no">{t('No CI')}</option>
              </select>
            </label>
            <label className="vh-inline-field">
              <span>{t('Divergence')}</span>
              <select value={filters.divergence} onChange={(e) => patchFilters({ divergence: e.target.value as HealthFilters['divergence'] })}>
                <option value="">{t('Any')}</option>
                {DIVERGENCE_FILTERS.map((d) => (
                  <option key={d} value={d}>{t(d === 'Ahead' ? msg('Ahead only') : d === 'Behind' ? msg('Behind only') : d === 'Diverged' ? msg('Diverged') : msg('Even'))}</option>
                ))}
              </select>
            </label>
            <label className="vh-inline-field">
              <span>{t('Branches')}</span>
              <input
                type="number"
                min={0}
                inputMode="numeric"
                className="vh-num"
                value={minDraft}
                placeholder={t('Min')}
                aria-label={t('Minimum branch count')}
                onChange={(e) => setMinDraft(e.target.value)}
              />
              <span aria-hidden="true">-</span>
              <input
                type="number"
                min={0}
                inputMode="numeric"
                className="vh-num"
                value={maxDraft}
                placeholder={t('Max')}
                aria-label={t('Maximum branch count')}
                onChange={(e) => setMaxDraft(e.target.value)}
              />
            </label>
            <label className="vh-inline-field">
              <span>{t('Last commit')}</span>
              <input
                type="date"
                value={filters.commitAfter}
                max={filters.commitBefore || undefined}
                aria-label={t('Last commit on or after')}
                title={t('Last commit on or after')}
                onChange={(e) => patchFilters({ commitAfter: e.target.value })}
              />
              <span aria-hidden="true">-</span>
              <input
                type="date"
                value={filters.commitBefore}
                min={filters.commitAfter || undefined}
                aria-label={t('Last commit on or before')}
                title={t('Last commit on or before')}
                onChange={(e) => patchFilters({ commitBefore: e.target.value })}
              />
            </label>
            {filtered && (
              <button
                type="button"
                className="btn btn-sm"
                onClick={() => updateFilters({ ...DEFAULT_HEALTH_FILTERS, sortBy: filters.sortBy, sortDesc: filters.sortDesc })}
              >
                {t('Clear filters')}
              </button>
            )}
            <div className="vh-filters-spacer" />
            <ChecklistDropdown
              label={t('Columns')}
              options={columns.map((c) => ({ value: c.key, label: t(c.label), locked: PINNED_COLUMNS.includes(c.key) }))}
              selected={columns.filter((c) => prefs.isVisible(c.key)).map((c) => c.key)}
              ariaLabel={t('Choose visible columns')}
              onToggle={(v) => prefs.toggleColumn(v)}
              align="right"
              footer={<button type="button" className="btn btn-sm" onClick={() => prefs.resetColumns()}>{t('Show all')}</button>}
            />
          </div>

          {error && (
            <div className="alert alert-error vh-inline-alert" role="alert">
              <span>{t('Could not load vessel health: {{message}}', { message: error })}</span>
              <button type="button" className="btn btn-sm" onClick={() => void loadRows()}>{t('Retry')}</button>
            </div>
          )}

          {selected.length > 0 && (
            <div className="vh-bulk-bar" role="region" aria-label={t('Bulk actions')}>
              <span className="vh-bulk-count">{t(selected.length === 1 ? msg('{{count}} vessel selected') : msg('{{count}} vessels selected'), { count: formatCount(locale, selected.length) })}</span>
              {isTenantAdmin && (
                <>
                  <button type="button" className="btn btn-sm btn-primary" disabled={evaluation.running || evaluation.starting} onClick={() => { void reevaluateSelected(); }}>
                    {t('Re-evaluate selected')}
                  </button>
                  <button type="button" className="btn btn-sm" onClick={() => runAction([...selected])} title={t('Run a fleet action against the selected vessels')}>
                    {t('Run action...')}
                  </button>
                </>
              )}
              <button type="button" className="btn btn-sm" onClick={() => setSelected([])}>{t('Clear selection')}</button>
            </div>
          )}

          <Pagination
            key={`${filters.page}-${prefs.pageSize}`}
            pageNumber={filters.page}
            pageSize={prefs.pageSize}
            totalPages={totalPages}
            totalRecords={totalRecords}
            onPageChange={(p) => updateFilters({ ...filters, page: Math.max(1, Math.min(totalPages, p)) })}
            onPageSizeChange={(size) => { prefs.setPageSize(size); updateFilters({ ...filters, page: 1 }); }}
          />

          {!loaded && loading ? (
            <LoadingIndicator label={t('Loading vessel health...')} />
          ) : (
            <div className={`table-wrap vh-table-wrap${loading ? ' vh-loading' : ''}`} aria-busy={loading}>
              <table className="vh-table">
                <thead>
                  <tr>
                    <th className="col-checkbox" scope="col">
                      <input
                        type="checkbox"
                        checked={allSelected}
                        aria-label={t('Select all vessels on this page')}
                        title={t('Select all vessels on this page')}
                        onChange={(e) => setSelected(e.target.checked ? pageIds : [])}
                      />
                    </th>
                    {visibleColumns.map((col) => (
                      <th key={col.key} scope="col" aria-sort={ariaSort(col.sort)} className={col.className}>
                        {col.sort ? (
                          <button type="button" className="vh-sort-btn" title={t(col.title)} onClick={() => updateFilters(toggleSort(filters, col.sort!))}>
                            {t(col.label)}
                            <span aria-hidden="true" className="vh-sort-icon">
                              {filters.sortBy === col.sort ? (filters.sortDesc ? '\u25BC' : '\u25B2') : '\u21C5'}
                            </span>
                          </button>
                        ) : t(col.label)}
                      </th>
                    ))}
                    <th scope="col" className="text-right">{t('Actions')}</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((row) => (
                    <tr key={row.vesselId} className="clickable" onClick={() => openDetail(row)}>
                      <td className="col-checkbox" onClick={(e) => e.stopPropagation()}>
                        <input
                          type="checkbox"
                          checked={selected.includes(row.vesselId)}
                          aria-label={t('Select {{name}}', { name: row.vesselName ?? row.vesselId })}
                          onChange={() => setSelected((s) => (s.includes(row.vesselId) ? s.filter((x) => x !== row.vesselId) : [...s, row.vesselId]))}
                        />
                      </td>
                      {visibleColumns.map((col) => (
                        <td key={col.key} className={col.className}>{col.render(row)}</td>
                      ))}
                      <td className="text-right" onClick={(e) => e.stopPropagation()}>
                        <ActionMenu id={`vessel-health-${row.vesselId}`} items={rowActions(row)} />
                      </td>
                    </tr>
                  ))}
                  {rows.length === 0 && !error && (
                    <tr>
                      <td colSpan={colSpan} className="text-dim vh-no-match">
                        {t('No vessels match the current filters.')}{' '}
                        {filtered && (
                          <button type="button" className="btn btn-sm" onClick={() => updateFilters({ ...DEFAULT_HEALTH_FILTERS, sortBy: filters.sortBy, sortDesc: filters.sortDesc })}>
                            {t('Clear filters')}
                          </button>
                        )}
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          )}
        </>
      )}

      {detail && (
        <VesselHealthDetailModal
          vesselId={detail.vesselId}
          vesselName={detail.vesselName}
          defaultBranch={defaultBranches[detail.vesselId]}
          initialSection={detail.section}
          canAdmin={isTenantAdmin}
          evaluationRunning={evaluation.running}
          refreshToken={refreshToken}
          onReevaluate={(id) => { void startEvaluation([id]); }}
          onChanged={() => { void refreshAll(); }}
          onOpenBranches={(id, name) => { setDetail(null); setBranches({ vesselId: id, vesselName: name }); }}
          onClose={() => setDetail(null)}
        />
      )}

      {branches && (
        <BranchesModal
          vesselId={branches.vesselId}
          vesselName={branches.vesselName}
          open
          onClose={() => setBranches(null)}
        />
      )}

      {runActionIds && (
        <RunActionModal
          open
          vesselIds={runActionIds}
          initialKind="Mission"
          onClose={() => setRunActionIds(null)}
          onStarted={(result) => { setSelected([]); navigate(`/fleet-actions/runs/${result.runId}`); }}
        />
      )}

      <JsonViewer open={jsonView !== null} title={jsonView?.title ?? ''} data={jsonView?.data ?? null} onClose={() => setJsonView(null)} />
    </div>
  );
}
