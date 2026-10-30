import { useDuplicateRequest } from '../lib/useDuplicateRequest';
import { duplicateDisplayName, buildMissionDuplicatePayload } from '../lib/duplicates';
import { useEffect, useState, useMemo, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { useNotifications } from '../context/NotificationContext';
import {
  getMission, listMissionSummaries, createMission, updateMission, deleteMission, purgeMission,
  restartMission, retryMissionLanding, transitionMission, getMissionDiff, getMissionLog,
  listVessels, listCaptains, listVoyages, apiErrorMessage,
} from '../api/client';
import { listAll } from '../api/paging';
import { reconcileServerPage, useListRequestGuard } from '../lib/serverPage';
import type { Mission, MissionSummary, Vessel, Captain, Voyage, MissionMode } from '../types/models';
import DataTable, { type DataTableColumn } from '../components/shared/DataTable';
import LoadingIndicator from '../components/shared/LoadingIndicator';
import ActionMenu from '../components/shared/ActionMenu';
import StatusBadge from '../components/shared/StatusBadge';
import EvaluationBadge from '../components/shared/EvaluationBadge';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import PageHeader from '../components/shared/PageHeader';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import DiffViewer from '../components/shared/DiffViewer';
import LogViewer from '../components/shared/LogViewer';
import ErrorModal from '../components/shared/ErrorModal';
import UserScopeFilter from '../components/shared/UserScopeFilter';
import CopyButton from '../components/shared/CopyButton';
import { useLocale } from '../context/LocaleContext';
import { MISSION_LIST_STATUSES, canRetryLanding, matchesMissionColumnFilters } from '../lib/missionActions';
import { canRestartFromHome } from '../lib/dashboardStatus';
import { usePersistedPageSize } from '../lib/usePersistedPageSize';
import { useDeleteProgress } from '../lib/useDeleteProgress';
import DispatchPreview, { DispatchWarningsBanner } from '../components/blackbox/DispatchWarnings';
import { dispatchWarningsFrom, type DispatchWarnings } from '../lib/dispatchPreview';

type SortDir = 'asc' | 'desc';
type SortField = 'title' | 'status' | 'priority' | 'createdUtc';

const MISSION_STATUSES = MISSION_LIST_STATUSES;

export default function Missions() {
  const navigate = useNavigate();
  const { t } = useLocale();
  const [missions, setMissions] = useState<MissionSummary[]>([]);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [captains, setCaptains] = useState<Captain[]>([]);
  const [voyages, setVoyages] = useState<Voyage[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const { pushToast } = useNotifications();
  const deletion = useDeleteProgress();

  // Pagination (server-side)
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = usePersistedPageSize('missions', 25);
  const [totalPages, setTotalPages] = useState(1);
  const [totalRecords, setTotalRecords] = useState(0);

  // Server-side status filter
  const [statusFilter, setStatusFilter] = useState('');
  const [userScope, setUserScope] = useState('');

  // Modal
  const [saving, setSaving] = useState(false);
  const [showForm, setShowForm] = useState(false);
  const [formData, setFormData] = useState<{ title: string; description: string; vesselId: string; priority: number; mode: MissionMode }>({ title: '', description: '', vesselId: '', priority: 100, mode: 'Implementation' });

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });

  // Confirm dialog
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

  // Diff/Log modals
  const [diffModal, setDiffModal] = useState<{ title: string; rawDiff: string } | null>(null);
  const [logModal, setLogModal] = useState<{ title: string; missionId: string; content: string; totalLines?: number; loading: boolean } | null>(null);

  // Transition modal
  const [transitionModal, setTransitionModal] = useState<{ missionId: string; currentStatus: string } | null>(null);
  const [transitionTarget, setTransitionTarget] = useState('');

  // Selection
  const [selected, setSelected] = useState<string[]>([]);

  // Sorting
  const [sortField, setSortField] = useState<SortField>('createdUtc');
  const [sortDir, setSortDir] = useState<SortDir>('desc');

  // Column filters
  const [colFilters, setColFilters] = useState({ title: '', status: '', branch: '' });

  // Lookups
  const vesselMap = useMemo(() => {
    const m = new Map<string, string>();
    vessels.forEach(v => m.set(v.id, v.name));
    return m;
  }, [vessels]);

  const captainMap = useMemo(() => {
    const m = new Map<string, string>();
    captains.forEach(c => m.set(c.id, c.name));
    return m;
  }, [captains]);

  const voyageMap = useMemo(() => new Map(voyages.map(v => [v.id, v])), [voyages]);

  const vesselName = useCallback((id: string | null) => (id ? vesselMap.get(id) || id.substring(0, 8) : '-'), [vesselMap]);
  const captainName = useCallback((id: string | null) => (id ? captainMap.get(id) || id.substring(0, 8) : '-'), [captainMap]);

  const beginListRequest = useListRequestGuard();

  const load = useCallback(async () => {
    const request = beginListRequest();
    try {
      setLoading(true);
      const filters: Record<string, string> = {};
      if (statusFilter) filters.status = statusFilter;
      if (userScope) filters.userId = userScope;
      const result = await listMissionSummaries({ pageNumber, pageSize, filters });
      if (!request.isCurrent()) return;
      const decision = reconcileServerPage(pageNumber, result.totalPages);
      if (!decision.accept) {
        setPageNumber(decision.pageNumber);
        return;
      }
      setMissions(result.objects || []);
      setTotalPages(decision.totalPages);
      setTotalRecords(result.totalRecords || 0);
      setError('');
    } catch {
      if (request.isCurrent()) setError(t('Failed to load missions.'));
    } finally {
      if (request.isCurrent()) setLoading(false);
    }
  }, [beginListRequest, pageNumber, pageSize, statusFilter, userScope, t]);

  useEffect(() => {
    listAll(listVessels).then(r => setVessels(r.objects || [])).catch(() => {});
    listAll(listCaptains).then(r => setCaptains(r.objects || [])).catch(() => {});
    listAll(listVoyages).then(r => setVoyages(r.objects || [])).catch(() => {});
  }, []);

  useEffect(() => { load(); }, [load]);
  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('missions', load);

  // Client-side column filter + sort
  const filtered = useMemo(() => {
    return missions.filter(m => matchesMissionColumnFilters(m, colFilters));
  }, [missions, colFilters]);

  const sorted = useMemo(() => {
    const arr = [...filtered];
    arr.sort((a, b) => {
      let va: string | number = '';
      let vb: string | number = '';
      switch (sortField) {
        case 'title': va = a.title.toLowerCase(); vb = b.title.toLowerCase(); break;
        case 'status': va = (a.status ?? '').toLowerCase(); vb = (b.status ?? '').toLowerCase(); break;
        case 'priority': va = a.priority; vb = b.priority; break;
        case 'createdUtc': va = a.createdUtc; vb = b.createdUtc; break;
      }
      if (va < vb) return sortDir === 'asc' ? -1 : 1;
      if (va > vb) return sortDir === 'asc' ? 1 : -1;
      return 0;
    });
    return arr;
  }, [filtered, sortField, sortDir]);

  function handleSort(field: SortField) {
    if (sortField === field) setSortDir(d => d === 'asc' ? 'desc' : 'asc');
    else { setSortField(field); setSortDir('asc'); }
  }


  // Selection
  const allSelected = selected.length > 0 && selected.length === sorted.length;
  function toggleSelect(id: string) {
    setSelected(s => s.includes(id) ? s.filter(x => x !== id) : [...s, id]);
  }
  function selectAll() { setSelected(sorted.map(m => m.id)); }
  function clearSelection() { setSelected([]); }

  const [duplicateSource, setDuplicateSource] = useState<Mission | null>(null);
  // Warnings the last create response carried (overlaps and brief lint); dispatch went ahead regardless.
  const [createdWarnings, setCreatedWarnings] = useState<DispatchWarnings | null>(null);

  // Create
  function openCreate() {
    setDuplicateSource(null);
    setFormData({ title: '', description: '', vesselId: '', priority: 100, mode: 'Implementation' });
    setShowForm(true);
  }

  async function openDuplicate(summary: { id: string }) {
    try {
      const mission = await getMission(summary.id);
      setDuplicateSource(mission);
      setFormData({ title: duplicateDisplayName(mission.title), description: mission.description || '', vesselId: mission.vesselId || '', priority: mission.priority, mode: mission.mode || 'Implementation' });
      setShowForm(true);
    } catch (error) { setError(apiErrorMessage(error, t('Duplicate failed.'))); }
  }

  useDuplicateRequest(id => openDuplicate({ id }), error => setError(apiErrorMessage(error, t('Duplicate failed.'))));

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (saving) return;
    setSaving(true);
    try {
      const created = await createMission({ ...(duplicateSource ? buildMissionDuplicatePayload(duplicateSource) : {}), ...formData });
      setShowForm(false);
      setCreatedWarnings(dispatchWarningsFrom(created));
      pushToast('success', t('Mission "{{title}}" created.', { title: formData.title }));
      load();
    } catch (err: unknown) { setError(apiErrorMessage(err, t('Create failed.'))); }
    finally { setSaving(false); }
  }

  // Actions
  function handleDelete(id: string, title: string) {
    setConfirm({
      open: true,
      title: t('Cancel Mission'),
      message: t('Cancel mission "{{title}}"? The mission will be set to Cancelled status but remains in the database. Use Purge to permanently remove it.', { title }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await deleteMission(id);
          pushToast('warning', t('Mission "{{title}}" cancelled.', { title }));
          load();
        } catch { setError(t('Cancel failed.')); }
      },
    });
  }

  function handlePurge(id: string, title: string) {
    setConfirm({
      open: true,
      title: t('Purge Mission'),
      message: t('Purge mission "{{title}}"? This will permanently remove it and clean up all associated resources. This cannot be undone.', { title }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        if (!(await deletion.run({ kind: 'missionPurge', action: () => purgeMission(id), errorMessage: (err) => apiErrorMessage(err, t('Purge failed.')) }))) return;
        pushToast('warning', t('Mission "{{title}}" purged.', { title }));
        load();
      },
    });
  }

  function handleBulkDelete() {
    setConfirm({
      open: true,
      title: t('Delete Selected Missions'),
      message: t('Delete {{count}} selected mission(s)? This cannot be undone.', { count: selected.length }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        const ids = [...selected];
        setSelected([]);
        let failed = 0;
        if (!(await deletion.run({
          kind: 'missionPurge',
          count: ids.length,
          action: async () => {
            failed = 0;
            for (const id of ids) {
              try { await purgeMission(id); } catch { failed++; }
            }
          },
        }))) return;
        const success = ids.length - failed;
        if (success > 0) {
          pushToast(failed > 0 ? 'warning' : 'success', failed > 0
            ? t('Purged {{success}} missions. {{failed}} failed.', { success, failed })
            : t('Purged {{success}} missions.', { success }));
        }
        if (failed > 0) setError(t('Deleted {{success}} missions, {{failed}} failed.', { success: ids.length - failed, failed }));
        load();
      },
    });
  }

  async function handleRestart(m: MissionSummary) {
    try {
      await restartMission(m.id);
      pushToast('success', m.status === 'Pending'
        ? t('Mission "{{title}}" started.', { title: m.title })
        : t('Mission "{{title}}" restarted.', { title: m.title }));
      load();
    } catch { setError(t('Restart failed.')); }
  }

  async function handleRetryLanding(m: MissionSummary) {
    try {
      await retryMissionLanding(m.id);
      pushToast('success', t('Landing succeeded for "{{title}}"', { title: m.title }));
      load();
    } catch (e) { setError(e instanceof Error ? e.message : t('Retry landing failed.')); }
  }

  async function handleViewDiff(id: string) {
    try {
      const result = await getMissionDiff(id);
      setDiffModal({ title: t('Mission Diff'), rawDiff: result?.diff || '' });
    } catch {
      setDiffModal({ title: t('Mission Diff'), rawDiff: '' });
    }
  }

  async function handleViewLog(missionId: string, title: string, lineCount = 200) {
    setLogModal({ title, missionId, content: '', loading: true });
    try {
      const result = await getMissionLog(missionId, lineCount);
      setLogModal(prev => prev ? { ...prev, content: result.log || t('No log output'), totalLines: result.totalLines, loading: false } : null);
    } catch (e: unknown) {
      setLogModal(prev => prev ? { ...prev, content: t('Log unavailable: {{message}}', { message: e instanceof Error ? e.message : String(e) }), loading: false } : null);
    }
  }

  async function handleTransition() {
    if (!transitionModal || !transitionTarget) return;
    try {
      await transitionMission(transitionModal.missionId, { status: transitionTarget });
      const missionTitle = missions.find(m => m.id === transitionModal.missionId)?.title;
      setTransitionModal(null);
      setTransitionTarget('');
      pushToast('success', missionTitle
        ? t('Mission "{{title}}" moved to {{status}}.', { title: missionTitle, status: transitionTarget })
        : t('Mission status changed to {{status}}.', { status: transitionTarget }));
      load();
    } catch { setError(t('Transition failed.')); }
  }

  const columns: DataTableColumn<MissionSummary>[] = [
    {
      key: 'id', label: t('ID'), required: true, headerTitle: t('Unique mission ID (msn_ prefix). Use it with the REST API, MCP tools and the armada CLI.'), cellClassName: 'mono text-dim table-id-cell',
      render: (m) => (
        <span className="id-display">
          <span className="id-value" title={m.id}>{m.id}</span>
          <CopyButton text={m.id} onClick={e => e.stopPropagation()} />
        </span>
      ),
    },
    {
      key: 'title', label: t('Title'), required: true, sortKey: 'title', headerTitle: t('Short name of the mission, written by whoever created it. Click to sort.'),
      cellClassName: 'cell-title', cellTitle: (m) => m.title,
      filter: <input type="text" className="col-filter" aria-label={t('Filter by title')} title={t('Show only missions on this page whose title contains this text. Not case sensitive.')} value={colFilters.title} onChange={e => setColFilters(f => ({ ...f, title: e.target.value }))} placeholder={t('Search...')} />,
      render: (m) => <><strong className="line-clamp-2">{m.title}</strong>{m.isEvaluation && <EvaluationBadge isEvaluation />}</>,
    },
    {
      key: 'status', label: t('Status'), sortKey: 'status', headerTitle: t('Lifecycle state: Pending waits for a captain, InProgress is running, WorkProduced awaits landing, then Complete, Failed or Cancelled. Click to sort.'), cellClassName: 'cell-nowrap',
      clearFilter: () => setColFilters(f => ({ ...f, status: '' })),
      filter: <input type="text" className="col-filter" aria-label={t('Filter by status')} title={t('Show only missions on this page whose status contains this text, for example Failed.')} value={colFilters.status} onChange={e => setColFilters(f => ({ ...f, status: e.target.value }))} placeholder={t('Search...')} />,
      render: (m) => <StatusBadge status={m.status} />,
    },
    { key: 'priority', label: t('Priority'), sortKey: 'priority', headerTitle: t('Dispatch order. Lower numbers are assigned to a captain first; the default is 100. Click to sort.'), render: (m) => m.priority },
    {
      key: 'vessel', label: t('Vessel'), headerTitle: t('Repository the mission works in. Click a name to open that vessel.'), interactive: true,
      render: (m) => m.vesselId ? (
        <a href="#" onClick={e => { e.preventDefault(); navigate(`/vessels/${m.vesselId}`); }}>{vesselName(m.vesselId)}</a>
      ) : '-',
    },
    {
      key: 'captain', label: t('Captain'), headerTitle: t('Agent that ran or is running the mission. Shows - until dispatch assigns one.'), interactive: true,
      render: (m) => m.captainId ? (
        <a href="#" onClick={e => { e.preventDefault(); navigate(`/captains/${m.captainId}`); }}>{captainName(m.captainId)}</a>
      ) : '-',
    },
    {
      key: 'voyage', label: t('Voyage'), headerTitle: t('Voyage (batch of related missions) this mission belongs to. Shows - for a standalone mission.'), interactive: true,
      render: (m) => m.voyageId ? (
        <span className="cell-clip mono"><a href="#" title={m.voyageId} onClick={e => { e.preventDefault(); navigate(`/voyages/${m.voyageId}`); }}>{voyageMap.get(m.voyageId)?.title || m.voyageId}</a></span>
      ) : '-',
    },
    {
      key: 'branch', label: t('Branch'), headerTitle: t('Git branch that holds the mission\'s commits. Its work lands from this branch by merge, pull request or push.'), cellClassName: 'mono text-dim table-url-cell', cellTitle: (m) => m.branchName || '',
      clearFilter: () => setColFilters(f => ({ ...f, branch: '' })),
      filter: <input type="text" className="col-filter" aria-label={t('Filter by branch')} title={t('Show only missions on this page whose branch name contains this text.')} value={colFilters.branch} onChange={e => setColFilters(f => ({ ...f, branch: e.target.value }))} placeholder={t('Search...')} />,
      render: (m) => m.branchName ? (
        <span className="id-display">
          <span className="url-value">{m.branchName}</span>
          <CopyButton text={m.branchName} onClick={e => e.stopPropagation()} title="Copy the branch name to the clipboard" />
        </span>
      ) : '-',
    },
    {
      key: 'actions', label: t('Actions'), headerTitle: t('Open, restart, retry landing, view the diff or log, change status, cancel or purge a mission.'), fixed: true, interactive: true, className: 'text-right',
      render: (m) => (
        <ActionMenu id={`mission-${m.id}`} items={[
          { label: 'Duplicate', title: 'Edit a fresh standalone mission with the same instructions. Execution state and dependencies are reset.', onClick: () => void openDuplicate(m) },
          { label: 'View Detail', title: 'Open the mission page with its description, captain activity, diff and log.', onClick: () => navigate(`/missions/${m.id}`) },
          { label: 'Edit', title: 'Open the mission page, where Edit changes its title, description and priority.', onClick: () => navigate(`/missions/${m.id}`) },
          ...(m.status === 'Pending' ? [{ label: 'Start', title: 'Start this Pending mission now. A free captain takes it at once; if none is free it stays Pending until one is.', onClick: () => handleRestart(m) }]
            : canRestartFromHome(m.status) ? [{ label: 'Restart', title: 'Reset a Failed or Cancelled mission to Pending so it is dispatched again. Clears its captain, branch, pull request and timing.', onClick: () => handleRestart(m) }] : []),
          ...(canRetryLanding(m.status) ? [{ label: 'Retry Landing', title: 'Rebase the mission branch onto the current target branch and try to merge, push or open the pull request again.', onClick: () => handleRetryLanding(m) }] : []),
          { label: 'View Diff', title: 'Show the code changes the mission made, from its live worktree or the snapshot saved at completion.', onClick: () => handleViewDiff(m.id) },
          { label: 'View Log', title: 'Show the last lines of the captain\'s output for this mission.', onClick: () => handleViewLog(m.id, `${t('Log')}: ${m.title}`) },
          { label: 'Transition Status', title: 'Move the mission to another status by hand, such as InProgress to Failed. Only valid moves are accepted. Use it to correct a stuck mission.', onClick: () => { setTransitionModal({ missionId: m.id, currentStatus: m.status }); setTransitionTarget(''); } },
          { label: 'View JSON', title: 'Show the raw mission record as the API returns it.', onClick: () => setJsonData({ open: true, title: `${t('Mission')}: ${m.title}`, data: m }) },
          { label: 'Cancel', danger: true, title: 'Mark the mission Cancelled. The record is kept and can be restarted later; use Purge to delete it.', onClick: () => handleDelete(m.id, m.title) },
          { label: 'Purge (permanent)', danger: true, title: 'Permanently delete the mission and clean up its resources. This cannot be undone.', onClick: () => handlePurge(m.id, m.title) },
        ]} />
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title={t('Missions')}
        subtitle={t('Individual work units assigned to captains')}
        actions={(
          <div className="missions-toolbar">
            <select aria-label={t('Filter by status')} className="filter-select" value={statusFilter} onChange={e => { setStatusFilter(e.target.value); setPageNumber(1); }} title={t('Show only missions in this status. Filters on the server across every page.')}>
              <option value="">{t('All Statuses')}</option>
              {MISSION_STATUSES.map(s => <option key={s} value={s}>{t(s)}</option>)}
            </select>
            <UserScopeFilter value={userScope} onChange={(id) => { setUserScope(id); setPageNumber(1); }} />
            {selected.length > 0 && (
              <button className="btn btn-sm btn-danger" onClick={handleBulkDelete} title={t('Permanently delete the selected missions. You confirm first; this cannot be undone.')}>
                {t('Delete Selected')} ({selected.length})
              </button>
            )}
            <button className="btn btn-primary btn-sm" onClick={openCreate} title={t('Create a single mission by hand. It waits in Pending until an idle captain for its vessel picks it up.')}>+ {t('Mission')}</button>
          </div>
        )}
      />

      <ErrorModal error={error} onClose={() => setError('')} />
      {createdWarnings && <DispatchWarningsBanner overlapWarnings={createdWarnings.overlapWarnings} briefLintWarnings={createdWarnings.briefLintWarnings} onDismiss={() => setCreatedWarnings(null)} />}

      {/* Create Modal */}
      {showForm && (
        <div className="modal-overlay" onClick={() => setShowForm(false)}>
          <form className="modal" onClick={e => e.stopPropagation()} onSubmit={handleSubmit}>
            <h3>{t(duplicateSource ? 'Duplicate Mission' : 'Create Mission')}</h3>
            <label title={t('Short name for the mission, shown in lists and notifications. Required. Example: Add retry to the webhook sender.')}>{t('Title')}<input value={formData.title} onChange={e => setFormData({ ...formData, title: e.target.value })} required title={t('Short name for the mission, shown in lists and notifications. Required. Example: Add retry to the webhook sender.')} /></label>
            <label title={t('Instructions the captain works from: what to change, where, and how to know it is done. Markdown is fine. Optional but strongly recommended.')}>{t('Description')}<textarea value={formData.description} onChange={e => setFormData({ ...formData, description: e.target.value })} rows={3} title={t('Instructions the captain works from: what to change, where, and how to know it is done. Markdown is fine. Optional but strongly recommended.')} /></label>
            <label title={t('Repository the captain works in. The mission gets its own worktree and branch in this vessel. Required.')}>{t('Vessel')}
              <select value={formData.vesselId} onChange={e => setFormData({ ...formData, vesselId: e.target.value })} required title={t('Repository the captain works in. The mission gets its own worktree and branch in this vessel. Required.')}>
                <option value="">{t('Select a vessel...')}</option>
                {vessels.map(v => <option key={v.id} value={v.id}>{v.name}</option>)}
              </select>
            </label>
            <DispatchPreview compact vesselId={formData.vesselId} title={formData.title} description={formData.description} enabled={!saving} />
            <label title={t('Implementation changes code and lands a diff (an empty diff fails). Audit and Research are read-only and produce a written report instead.')}>{t('Mode')}
              <select value={formData.mode} onChange={e => setFormData({ ...formData, mode: e.target.value as MissionMode })} title={t('Implementation changes code and lands a diff (an empty diff fails). Audit and Research are read-only and produce a written report instead.')}>
                <option value="Implementation">{t('Implementation')}</option>
                <option value="Audit">{t('Audit (read-only)')}</option>
                <option value="Research">{t('Research (read-only)')}</option>
              </select>
            </label>
            <label title={t('Dispatch order. Lower numbers are assigned to a captain first; the default is 100. Example: 10 for urgent work.')}>{t('Priority')}<input type="number" value={formData.priority} onChange={e => setFormData({ ...formData, priority: Number(e.target.value) })} title={t('Dispatch order. Lower numbers are assigned to a captain first; the default is 100. Example: 10 for urgent work.')} /></label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary" disabled={saving} title={t('Create the mission in Pending. Dispatch assigns it to an idle captain on the chosen vessel.')}>{t(duplicateSource ? 'Save Copy' : 'Create')}</button>
              <button type="button" className="btn" onClick={() => setShowForm(false)} title={t('Close this form without creating a mission.')}>{t('Cancel')}</button>
            </div>
          </form>
        </div>
      )}

      {/* Transition Modal */}
      {transitionModal && (
        <div className="modal-overlay" onClick={() => setTransitionModal(null)}>
          <div className="modal" onClick={e => e.stopPropagation()}>
            <h3>{t('Transition Mission Status')}</h3>
            <p>{t('Current status:')} <strong>{t(transitionModal.currentStatus)}</strong></p>
            <label title={t('Status to move the mission to. The server rejects moves that are not valid from the current status.')}>{t('New Status')}
              <select value={transitionTarget} onChange={e => setTransitionTarget(e.target.value)} title={t('Status to move the mission to. The server rejects moves that are not valid from the current status.')}>
                <option value="">{t('Select status...')}</option>
                {MISSION_STATUSES.filter(s => s !== transitionModal.currentStatus).map(s => <option key={s} value={s}>{t(s)}</option>)}
              </select>
            </label>
            <div className="modal-actions">
              <button className="btn btn-primary" onClick={handleTransition} disabled={!transitionTarget} title={t('Move the mission to the selected status now. Use this only to correct a mission stuck in the wrong state.')}>{t('Transition')}</button>
              <button className="btn" onClick={() => setTransitionModal(null)} title={t('Close without changing the mission status.')}>{t('Cancel')}</button>
            </div>
          </div>
        </div>
      )}

      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message}
        onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />
      {deletion.dialog}
      <DiffViewer open={diffModal !== null} title={diffModal?.title ?? ''} rawDiff={diffModal?.rawDiff ?? ''} onClose={() => setDiffModal(null)} />
      <LogViewer
        open={logModal !== null}
        title={logModal?.title ?? ''}
        content={logModal?.content ?? ''}
        totalLines={logModal?.totalLines}
        loading={logModal?.loading}
        onClose={() => setLogModal(null)}
        onRefresh={logModal ? () => handleViewLog(logModal.missionId, logModal.title) : undefined}
        onLineCountChange={logModal ? (lines) => handleViewLog(logModal.missionId, logModal.title, lines) : undefined}
      />

      <DataTable
        tableKey="missions"
        columns={columns}
        rows={sorted}
        rowKey={(m) => m.id}
        onRowClick={(m) => navigate(`/missions/${m.id}`)}
        sort={{ field: sortField, dir: sortDir, onSort: (f) => handleSort(f as SortField) }}
        pagination={{
          pageNumber, pageSize, totalPages, totalRecords,
          onPageChange: (p) => setPageNumber(p),
          onPageSizeChange: (s) => { setPageSize(s); setPageNumber(1); },
        }}
        autoRefresh={{ seconds: refreshSeconds, onChange: setRefreshSeconds }}
        onRefresh={load}
        refreshTitle="Reload the mission list from the server now."
        selection={{
          isSelected: (m) => selected.includes(m.id),
          onToggle: (m) => toggleSelect(m.id),
          allSelected,
          onToggleAll: (checked) => (checked ? selectAll() : clearSelection()),
          selectAllLabel: t('Select all missions'),
          rowLabel: () => t('Select this mission for Delete Selected.'),
        }}
        emptyMessage={t('No missions match the current filters.')}
        placeholder={missions.length > 0 ? undefined : loading
          ? <LoadingIndicator fullHeight label={t('Loading missions...')} />
          : <p className="text-dim">{t('No missions found.')}</p>}
      />
    </div>
  );
}
