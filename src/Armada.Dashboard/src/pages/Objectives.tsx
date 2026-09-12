import { useEffect, useMemo, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import PageHeader from '../components/shared/PageHeader';
import {
  createBacklogItem,
  deleteBacklogItem,
  importObjectiveFromGitHub,
  listBacklog,
  listFleets,
  listVessels,
  reorderBacklog,
} from '../api/client';
import type {
  Fleet,
  GitHubObjectiveSourceType,
  Objective,
  Vessel,
} from '../types/models';
import { useAuth } from '../context/AuthContext';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import BacklogGroupPills from '../components/backlog/BacklogGroupPills';
import {
  BACKLOG_GROUPS,
  getBacklogGroup,
  getPriorityWeight,
  OBJECTIVE_BACKLOG_STATES,
  OBJECTIVE_EFFORTS,
  OBJECTIVE_KINDS,
  OBJECTIVE_PRIORITIES,
  OBJECTIVE_STATUSES,
  type BacklogGroupKey,
  type BacklogSortKey,
} from '../components/backlog/backlogUtils';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import ErrorModal from '../components/shared/ErrorModal';
import JsonViewer from '../components/shared/JsonViewer';
import RecordDetailModal from '../components/shared/RecordDetailModal';
import RefreshButton from '../components/shared/RefreshButton';
import StatusBadge from '../components/shared/StatusBadge';
import AutoRefreshSelect from '../components/shared/AutoRefreshSelect';
import UserScopeFilter from '../components/shared/UserScopeFilter';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import { buildObjectiveDuplicatePayload } from '../lib/duplicates';

export default function Objectives() {
  const navigate = useNavigate();
  const location = useLocation();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();

  const [objectives, setObjectives] = useState<Objective[]>([]);
  const [fleets, setFleets] = useState<Fleet[]>([]);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [loading, setLoading] = useState(true);
  const [userScope, setUserScope] = useState('');
  const [error, setError] = useState('');
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState<'all' | Objective['status']>('all');
  const [kindFilter, setKindFilter] = useState<'all' | Objective['kind']>('all');
  const [priorityFilter, setPriorityFilter] = useState<'all' | Objective['priority']>('all');
  const [backlogStateFilter, setBacklogStateFilter] = useState<'all' | Objective['backlogState']>('all');
  const [effortFilter, setEffortFilter] = useState<'all' | Objective['effort']>('all');
  const [fleetFilter, setFleetFilter] = useState('all');
  const [vesselFilter, setVesselFilter] = useState('all');
  const [ownerFilter, setOwnerFilter] = useState('');
  const [targetVersionFilter, setTargetVersionFilter] = useState('');
  const [groupFilter, setGroupFilter] = useState<BacklogGroupKey>('all');
  const [colFilters, setColFilters] = useState({ title: '' });
  const [sortBy, setSortBy] = useState<BacklogSortKey>('rank');
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });
  const [viewRecord, setViewRecord] = useState<Record<string, unknown> | null>(null);
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({
    open: false,
    title: '',
    message: '',
    onConfirm: () => {},
  });
  const [importModalOpen, setImportModalOpen] = useState(false);
  const [importSaving, setImportSaving] = useState(false);
  const [importVesselId, setImportVesselId] = useState('');
  const [importSourceType, setImportSourceType] = useState<GitHubObjectiveSourceType>('Issue');
  const [importNumber, setImportNumber] = useState('');

  const canManage = isAdmin || isTenantAdmin;

  async function load() {
    try {
      setLoading(true);
      const [objectiveResult, fleetResult, vesselResult] = await Promise.all([
        listBacklog({ pageSize: 9999, userId: userScope || undefined }),
        listFleets({ pageSize: 9999 }),
        listVessels({ pageSize: 9999 }),
      ]);

      const loadedObjectives = objectiveResult.objects || [];
      setObjectives(loadedObjectives);
      setFleets(fleetResult.objects || []);
      setVessels(vesselResult.objects || []);
      setError('');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to load backlog.'));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [userScope]);

  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('objectives', load);

  useEffect(() => {
    const params = new URLSearchParams(location.search);
    const nextFleetId = params.get('fleetId');
    const nextVesselId = params.get('vesselId');

    setFleetFilter(nextFleetId || 'all');
    setVesselFilter(nextVesselId || 'all');
  }, [location.search]);

  const fleetMap = useMemo(() => new Map(fleets.map((fleet) => [fleet.id, fleet.name])), [fleets]);
  const vesselMap = useMemo(() => new Map(vessels.map((vessel) => [vessel.id, vessel.name])), [vessels]);

  const groupCounts = useMemo(() => {
    const counts: Record<BacklogGroupKey, number> = {
      all: objectives.length,
      inbox: 0,
      planning: 0,
      dispatch: 0,
      blocked: 0,
    };
    objectives.forEach((objective) => {
      const group = getBacklogGroup(objective);
      counts[group] += 1;
    });
    return counts;
  }, [objectives]);

  const filteredObjectives = useMemo(() => {
    const normalizedSearch = search.trim().toLowerCase();
    return objectives.filter((objective) => {
      if (groupFilter !== 'all' && getBacklogGroup(objective) !== groupFilter) return false;
      if (statusFilter !== 'all' && objective.status !== statusFilter) return false;
      if (kindFilter !== 'all' && objective.kind !== kindFilter) return false;
      if (priorityFilter !== 'all' && objective.priority !== priorityFilter) return false;
      if (backlogStateFilter !== 'all' && objective.backlogState !== backlogStateFilter) return false;
      if (effortFilter !== 'all' && objective.effort !== effortFilter) return false;
      if (fleetFilter !== 'all' && !objective.fleetIds.includes(fleetFilter)) return false;
      if (vesselFilter !== 'all' && !objective.vesselIds.includes(vesselFilter)) return false;
      if (ownerFilter.trim() && !(objective.owner || '').toLowerCase().includes(ownerFilter.trim().toLowerCase())) return false;
      if (targetVersionFilter.trim() && !(objective.targetVersion || '').toLowerCase().includes(targetVersionFilter.trim().toLowerCase())) return false;
      if (colFilters.title && !(objective.title || '').toLowerCase().includes(colFilters.title.toLowerCase())) return false;
      if (!normalizedSearch) return true;

      return (
        objective.title.toLowerCase().includes(normalizedSearch)
        || (objective.description || '').toLowerCase().includes(normalizedSearch)
        || (objective.owner || '').toLowerCase().includes(normalizedSearch)
        || (objective.category || '').toLowerCase().includes(normalizedSearch)
        || (objective.targetVersion || '').toLowerCase().includes(normalizedSearch)
        || (objective.refinementSummary || '').toLowerCase().includes(normalizedSearch)
        || objective.tags.some((tag) => tag.toLowerCase().includes(normalizedSearch))
        || objective.acceptanceCriteria.some((criteria) => criteria.toLowerCase().includes(normalizedSearch))
        || objective.id.toLowerCase().includes(normalizedSearch)
      );
    });
  }, [
    backlogStateFilter,
    colFilters,
    effortFilter,
    fleetFilter,
    groupFilter,
    kindFilter,
    objectives,
    ownerFilter,
    priorityFilter,
    search,
    statusFilter,
    targetVersionFilter,
    vesselFilter,
  ]);

  const orderedObjectives = useMemo(() => {
    const sorted = [...filteredObjectives];
    sorted.sort((left, right) => {
      if (sortBy === 'priority') {
        const priorityDelta = getPriorityWeight(left.priority) - getPriorityWeight(right.priority);
        if (priorityDelta !== 0) return priorityDelta;
        return left.rank - right.rank;
      }

      if (sortBy === 'due') {
        const leftDue = left.dueUtc ? new Date(left.dueUtc).getTime() : Number.MAX_SAFE_INTEGER;
        const rightDue = right.dueUtc ? new Date(right.dueUtc).getTime() : Number.MAX_SAFE_INTEGER;
        if (leftDue !== rightDue) return leftDue - rightDue;
        return left.rank - right.rank;
      }

      if (sortBy === 'updated') {
        const updatedDelta = new Date(right.lastUpdateUtc).getTime() - new Date(left.lastUpdateUtc).getTime();
        if (updatedDelta !== 0) return updatedDelta;
        return left.rank - right.rank;
      }

      const rankDelta = left.rank - right.rank;
      if (rankDelta !== 0) return rankDelta;
      return getPriorityWeight(left.priority) - getPriorityWeight(right.priority);
    });
    return sorted;
  }, [filteredObjectives, sortBy]);

  const hasActiveFilters = (
    search.trim().length > 0
    || statusFilter !== 'all'
    || kindFilter !== 'all'
    || priorityFilter !== 'all'
    || backlogStateFilter !== 'all'
    || effortFilter !== 'all'
    || fleetFilter !== 'all'
    || vesselFilter !== 'all'
    || ownerFilter.trim().length > 0
    || targetVersionFilter.trim().length > 0
    || groupFilter !== 'all'
    || sortBy !== 'rank'
  );

  const blockedCount = objectives.filter((objective) => objective.status === 'Blocked' || objective.blockedByObjectiveIds.length > 0).length;
  const planningReadyCount = objectives.filter((objective) => objective.backlogState === 'ReadyForPlanning').length;
  const dispatchReadyCount = objectives.filter((objective) => objective.backlogState === 'ReadyForDispatch').length;

  function renderNames(ids: string[], sourceMap: Map<string, string>) {
    if (ids.length === 0) return t('None');
    return ids.slice(0, 2).map((id) => sourceMap.get(id) || id).join(', ') + (ids.length > 2 ? ` +${ids.length - 2}` : '');
  }

  function clearFilters() {
    setSearch('');
    setStatusFilter('all');
    setKindFilter('all');
    setPriorityFilter('all');
    setBacklogStateFilter('all');
    setEffortFilter('all');
    setFleetFilter('all');
    setVesselFilter('all');
    setOwnerFilter('');
    setTargetVersionFilter('');
    setGroupFilter('all');
    setSortBy('rank');
    if (location.search) {
      navigate(location.pathname, { replace: true });
    }
  }

  async function handleMoveRank(objectiveId: string, direction: -1 | 1) {
    const ranked = [...objectives].sort((left, right) => left.rank - right.rank);
    const index = ranked.findIndex((objective) => objective.id === objectiveId);
    const neighborIndex = index + direction;
    if (index < 0 || neighborIndex < 0 || neighborIndex >= ranked.length) return;

    const current = ranked[index];
    const neighbor = ranked[neighborIndex];

    try {
      const updated = await reorderBacklog({
        items: [
          { objectiveId: current.id, rank: neighbor.rank },
          { objectiveId: neighbor.id, rank: current.rank },
        ],
      });
      setObjectives((existing) => existing.map((objective) => {
        const match = updated.find((item) => item.id === objective.id);
        return match || objective;
      }));
      pushToast('success', t('Backlog ranking updated.'));
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to reorder backlog.'));
    }
  }

  function handleDelete(objective: Objective) {
    setConfirm({
      open: true,
      title: t('Delete Backlog Item'),
      message: t('Delete "{{title}}"? This removes the backlog item and its objective snapshot history, but leaves linked missions, releases, deployments, and incidents intact.', { title: objective.title }),
      onConfirm: async () => {
        setConfirm((current) => ({ ...current, open: false }));
        try {
          await deleteBacklogItem(objective.id);
          pushToast('warning', t('Backlog item "{{title}}" deleted.', { title: objective.title }));
          await load();
        } catch (err: unknown) {
          setError(err instanceof Error ? err.message : t('Delete failed.'));
        }
      },
    });
  }

  async function handleDuplicate(objective: Objective) {
    try {
      const created = await createBacklogItem(buildObjectiveDuplicatePayload(objective));
      pushToast('success', t('Backlog item "{{title}}" duplicated.', { title: created.title }));
      navigate(`/backlog/${created.id}`);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Duplicate failed.'));
    }
  }

  async function handleImportFromGitHub() {
    const parsedNumber = Number(importNumber);
    if (!importVesselId || !Number.isFinite(parsedNumber) || parsedNumber <= 0) {
      setError(t('Select a vessel and enter a valid GitHub issue or pull-request number.'));
      return;
    }

    try {
      setImportSaving(true);
      const imported = await importObjectiveFromGitHub({
        vesselId: importVesselId,
        sourceType: importSourceType,
        number: parsedNumber,
      });
      pushToast('success', t('Imported GitHub {{sourceType}} #{{number}} into backlog item "{{title}}".', { sourceType: importSourceType, number: parsedNumber, title: imported.title }));
      setImportModalOpen(false);
      setImportVesselId('');
      setImportSourceType('Issue');
      setImportNumber('');
      await load();
      navigate(`/backlog/${imported.id}`);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('GitHub import failed.'));
    } finally {
      setImportSaving(false);
    }
  }

  const createBacklogRoute = useMemo(() => {
    const params = new URLSearchParams();

    if (vesselFilter !== 'all') {
      params.set('vesselId', vesselFilter);
    }

    const searchParams = params.toString();
    return searchParams ? `/backlog/new?${searchParams}` : '/backlog/new';
  }, [vesselFilter]);

  return (
    <div>
      <PageHeader
        title={t('Backlog')}
        subtitle={t('Capture future work, refine it, and carry the same record through planning, dispatch, release, deployment, and incident follow-through.')}
        actions={(
          <>
            <UserScopeFilter value={userScope} onChange={setUserScope} />
            <AutoRefreshSelect seconds={refreshSeconds} onChange={setRefreshSeconds} />
            <RefreshButton onRefresh={load} title={t('Refresh backlog')} />
            {canManage && (
              <button className="btn" onClick={() => setImportModalOpen(true)}>
                {t('Import GitHub')}
              </button>
            )}
            {canManage && (
              <button className="btn btn-primary" onClick={() => navigate(createBacklogRoute)}>
                + {t('Backlog Item')}
              </button>
            )}
          </>
        )}
      />

      <ErrorModal error={error} onClose={() => setError('')} />
      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <RecordDetailModal
        open={!!viewRecord}
        title={viewRecord ? String(viewRecord.title || viewRecord.id || '') : ''}
        subtitle={viewRecord ? String(viewRecord.owner || '') : undefined}
        record={viewRecord}
        onClose={() => setViewRecord(null)}
        onEdit={() => {
          const id = viewRecord?.id;
          setViewRecord(null);
          if (id) navigate(`/backlog/${String(id)}`);
        }}
        editLabel={t('Open Details')}
      />
      <ConfirmDialog
        open={confirm.open}
        title={confirm.title}
        message={confirm.message}
        onConfirm={confirm.onConfirm}
        onCancel={() => setConfirm((current) => ({ ...current, open: false }))}
      />

      {importModalOpen && (
        <div className="modal-overlay" onClick={() => !importSaving && setImportModalOpen(false)}>
          <div className="modal" onClick={(event) => event.stopPropagation()}>
            <h3>{t('Import GitHub Backlog Item')}</h3>
            <p className="text-dim">
              {t('Create a backlog item from a GitHub issue or pull request using the selected vessel repository and configured GitHub token.')}
            </p>
            <label style={{ marginTop: '0.9rem', display: 'grid', gap: '0.35rem' }}>
              <span>{t('Vessel')}</span>
              <select value={importVesselId} onChange={(event) => setImportVesselId(event.target.value)}>
                <option value="">{t('Select a vessel')}</option>
                {vessels.map((vessel) => (
                  <option key={vessel.id} value={vessel.id}>{vessel.name}</option>
                ))}
              </select>
            </label>
            <label style={{ marginTop: '0.9rem', display: 'grid', gap: '0.35rem' }}>
              <span>{t('Source Type')}</span>
              <select value={importSourceType} onChange={(event) => setImportSourceType(event.target.value as GitHubObjectiveSourceType)}>
                <option value="Issue">{t('Issue')}</option>
                <option value="PullRequest">{t('Pull Request')}</option>
              </select>
            </label>
            <label style={{ marginTop: '0.9rem', display: 'grid', gap: '0.35rem' }}>
              <span>{t('Number')}</span>
              <input
                type="number"
                min={1}
                step={1}
                value={importNumber}
                onChange={(event) => setImportNumber(event.target.value)}
                placeholder={t('123')}
              />
            </label>
            <div className="modal-actions">
              <button className="btn btn-primary" disabled={importSaving} onClick={() => void handleImportFromGitHub()}>
                {importSaving ? t('Importing...') : t('Import')}
              </button>
              <button className="btn" disabled={importSaving} onClick={() => setImportModalOpen(false)}>
                {t('Cancel')}
              </button>
            </div>
          </div>
        </div>
      )}

      <div className="playbook-overview-grid">
        <div className="card playbook-overview-card">
          <span>{t('Backlog Items')}</span>
          <strong>{objectives.length}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Ready For Planning')}</span>
          <strong>{planningReadyCount}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Ready For Dispatch')}</span>
          <strong>{dispatchReadyCount}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Blocked')}</span>
          <strong>{blockedCount}</strong>
        </div>
      </div>

      <BacklogGroupPills
        groups={BACKLOG_GROUPS}
        activeGroup={groupFilter}
        counts={groupCounts}
        onChange={setGroupFilter}
      />

      <div className="card backlog-filter-card">
        <div className="backlog-filter-grid">
          <input
            type="text"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder={t('Search by title, description, owner, category, captain, tags, or ID...')}
          />
          <select value={kindFilter} onChange={(event) => setKindFilter(event.target.value as typeof kindFilter)}>
            <option value="all">{t('All kinds')}</option>
            {OBJECTIVE_KINDS.map((kind) => (
              <option key={kind} value={kind}>{kind}</option>
            ))}
          </select>
          <select value={priorityFilter} onChange={(event) => setPriorityFilter(event.target.value as typeof priorityFilter)}>
            <option value="all">{t('All priorities')}</option>
            {OBJECTIVE_PRIORITIES.map((priority) => (
              <option key={priority} value={priority}>{priority}</option>
            ))}
          </select>
          <select value={backlogStateFilter} onChange={(event) => setBacklogStateFilter(event.target.value as typeof backlogStateFilter)}>
            <option value="all">{t('All backlog states')}</option>
            {OBJECTIVE_BACKLOG_STATES.map((state) => (
              <option key={state} value={state}>{state}</option>
            ))}
          </select>
          <select value={effortFilter} onChange={(event) => setEffortFilter(event.target.value as typeof effortFilter)}>
            <option value="all">{t('All effort sizes')}</option>
            {OBJECTIVE_EFFORTS.map((effort) => (
              <option key={effort} value={effort}>{effort}</option>
            ))}
          </select>
          <select value={statusFilter} onChange={(event) => setStatusFilter(event.target.value as typeof statusFilter)}>
            <option value="all">{t('All lifecycle statuses')}</option>
            {OBJECTIVE_STATUSES.map((status) => (
              <option key={status} value={status}>{status}</option>
            ))}
          </select>
          <select value={fleetFilter} onChange={(event) => setFleetFilter(event.target.value)}>
            <option value="all">{t('All fleets')}</option>
            {fleets.map((fleet) => (
              <option key={fleet.id} value={fleet.id}>{fleet.name}</option>
            ))}
          </select>
          <select value={vesselFilter} onChange={(event) => setVesselFilter(event.target.value)}>
            <option value="all">{t('All vessels')}</option>
            {vessels.map((vessel) => (
              <option key={vessel.id} value={vessel.id}>{vessel.name}</option>
            ))}
          </select>
          <input
            type="text"
            value={ownerFilter}
            onChange={(event) => setOwnerFilter(event.target.value)}
            placeholder={t('Owner filter')}
          />
          <input
            type="text"
            value={targetVersionFilter}
            onChange={(event) => setTargetVersionFilter(event.target.value)}
            placeholder={t('Target version filter')}
          />
          <select value={sortBy} onChange={(event) => setSortBy(event.target.value as BacklogSortKey)}>
            <option value="rank">{t('Sort by rank')}</option>
            <option value="priority">{t('Sort by priority')}</option>
            <option value="updated">{t('Sort by last updated')}</option>
            <option value="due">{t('Sort by due date')}</option>
          </select>
        </div>
        <div className="backlog-filter-meta">
          <span className="text-dim">
            {t('Showing {{shown}} of {{total}} backlog items.', { shown: orderedObjectives.length, total: objectives.length })}
          </span>
          <div className="backlog-filter-actions">
            {hasActiveFilters && (
              <button type="button" className="btn btn-sm" onClick={clearFilters}>
                {t('Clear Filters')}
              </button>
            )}
          </div>
        </div>
      </div>

      {loading && objectives.length === 0 ? (
        <div className="playbook-empty-state">
          <strong>{t('Loading backlog...')}</strong>
          <span>{t('Checking for backlog items and scope links.')}</span>
        </div>
      ) : objectives.length === 0 ? (
        <div className="playbook-empty-state">
          <strong>{t('No backlog items yet.')}</strong>
          <span>
            {canManage
              ? t('Create a backlog item to start triage, refinement, planning readiness, and delivery lineage inside Armada.')
              : t('Ask a tenant administrator to create and manage backlog items.')}
          </span>
        </div>
      ) : orderedObjectives.length === 0 ? (
        <div className="playbook-empty-state">
          <strong>{t('No backlog items match the current filters.')}</strong>
          <span>
            {canManage
              ? t('Capture a backlog item to start with inbox triage, refinement, planning readiness, and delivery lineage inside Armada.')
              : t('Ask a tenant administrator to create and manage backlog items.')}
          </span>
          {hasActiveFilters && (
            <div className="backlog-empty-actions">
              <button type="button" className="btn btn-sm" onClick={clearFilters}>
                {t('Reset Filters')}
              </button>
            </div>
          )}
        </div>
      ) : (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>{t('Rank')}</th>
                <th>{t('Backlog Item')}</th>
                <th>{t('Shape')}</th>
                <th>{t('State')}</th>
                <th>{t('Scope')}</th>
                <th>{t('Due / Updated')}</th>
                <th className="text-right">{t('Actions')}</th>
              </tr>
              <tr className="column-filter-row">
                <td></td>
                <td><input type="text" className="col-filter" value={colFilters.title} onChange={e => setColFilters(f => ({ ...f, title: e.target.value }))} placeholder={t('Filter...')} /></td>
                <td></td>
                <td></td>
                <td></td>
                <td></td>
                <td></td>
              </tr>
            </thead>
            <tbody>
              {orderedObjectives.map((objective) => {
                return (
                  <tr key={objective.id} className="clickable" onClick={() => setViewRecord(objective as unknown as Record<string, unknown>)}>
                    <td onClick={(event) => event.stopPropagation()}>
                      <div className="backlog-rank-cell">
                        <strong>{objective.rank}</strong>
                        {canManage && (
                          <div className="backlog-rank-buttons">
                            <button type="button" className="btn btn-sm" onClick={() => void handleMoveRank(objective.id, -1)} aria-label={t('Move backlog item up')}>
                              ↑
                            </button>
                            <button type="button" className="btn btn-sm" onClick={() => void handleMoveRank(objective.id, 1)} aria-label={t('Move backlog item down')}>
                              ↓
                            </button>
                          </div>
                        )}
                      </div>
                    </td>
                    <td>
                      <strong>{objective.title}</strong>
                      <div className="text-dim" style={{ marginTop: '0.2rem' }}>
                        {objective.owner || t('No owner')}
                        {objective.category ? ` · ${objective.category}` : ''}
                        {objective.targetVersion ? ` · ${objective.targetVersion}` : ''}
                      </div>
                      <div className="mono text-dim" style={{ fontSize: '0.78rem' }}>{objective.id}</div>
                      {objective.description && (
                        <div className="text-dim" style={{ marginTop: '0.2rem' }}>{objective.description}</div>
                      )}
                    </td>
                    <td>
                      <div className="backlog-chip-row">
                        <span className={`tag ${objective.kind.toLowerCase()}`}>{objective.kind}</span>
                        <span className={`tag ${objective.priority.toLowerCase()}`}>{objective.priority}</span>
                        <span className={`tag ${objective.effort.toLowerCase()}`}>{objective.effort}</span>
                      </div>
                    </td>
                    <td>
                      <div className="backlog-chip-row">
                        <StatusBadge status={objective.status} />
                        <span className={`tag ${objective.backlogState.toLowerCase()}`}>{objective.backlogState}</span>
                      </div>
                      {objective.blockedByObjectiveIds.length > 0 && (
                        <div className="text-dim" style={{ marginTop: '0.3rem' }}>
                          {t('Blocked by')} {objective.blockedByObjectiveIds.length}
                        </div>
                      )}
                    </td>
                    <td className="text-dim">
                      <div>{objective.vesselIds.length > 0 ? renderNames(objective.vesselIds, vesselMap) : t('No vessel linked')}</div>
                      <div style={{ marginTop: '0.25rem' }}>{objective.fleetIds.length > 0 ? renderNames(objective.fleetIds, fleetMap) : t('No fleet linked')}</div>
                      {objective.vesselIds.length < 1 && (
                        <div className="text-dim backlog-scope-note">
                          {t('Needs a vessel before planning, dispatch, or release drafting can start.')}
                        </div>
                      )}
                    </td>
                    <td className="text-dim">
                      <div title={objective.dueUtc ? formatDateTime(objective.dueUtc) : undefined}>
                        {objective.dueUtc ? t('Due {{date}}', { date: formatRelativeTime(objective.dueUtc) }) : t('No due date')}
                      </div>
                      <div style={{ marginTop: '0.25rem' }} title={formatDateTime(objective.lastUpdateUtc)}>
                        {t('Updated {{time}}', { time: formatRelativeTime(objective.lastUpdateUtc) })}
                      </div>
                    </td>
                    <td className="text-right" onClick={(event) => event.stopPropagation()}>
                      <ActionMenu
                        id={`backlog-${objective.id}`}
                        items={[
                          { label: 'Open', onClick: () => navigate(`/backlog/${objective.id}`) },
                          ...(canManage ? [{ label: 'Duplicate', onClick: () => void handleDuplicate(objective) }] : []),
                          { label: 'View JSON', onClick: () => setJsonData({ open: true, title: objective.title, data: objective }) },
                          ...(canManage ? [
                            { label: 'Move Up', onClick: () => void handleMoveRank(objective.id, -1) },
                            { label: 'Move Down', onClick: () => void handleMoveRank(objective.id, 1) },
                            { label: 'Delete', danger: true as const, onClick: () => handleDelete(objective) },
                          ] : []),
                        ]}
                      />
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
