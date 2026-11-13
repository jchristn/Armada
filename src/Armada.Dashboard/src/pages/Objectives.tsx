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
import { listAll } from '../api/paging';
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
  applyBacklogReorder,
  BACKLOG_GROUPS,
  backlogRankSwap,
  countBacklogGroups,
  filterBacklog,
  hasActiveBacklogFilters,
  OBJECTIVE_BACKLOG_STATES,
  OBJECTIVE_EFFORTS,
  OBJECTIVE_KINDS,
  OBJECTIVE_PRIORITIES,
  OBJECTIVE_STATUSES,
  sortBacklog,
  type BacklogFilters,
  type BacklogGroupKey,
  type BacklogSortKey,
} from '../lib/backlogUtils';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import ErrorModal from '../components/shared/ErrorModal';
import JsonViewer from '../components/shared/JsonViewer';
import RecordDetailModal from '../components/shared/RecordDetailModal';
import CopyButton from '../components/shared/CopyButton';
import DataTable, { type DataTableColumn } from '../components/shared/DataTable';
import StatusBadge from '../components/shared/StatusBadge';
import UserScopeFilter from '../components/shared/UserScopeFilter';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import { buildObjectiveDuplicatePayload } from '../lib/duplicates';
import { sortByName } from '../lib/sortByName';
import { useDeleteProgress } from '../lib/useDeleteProgress';

export default function Objectives() {
  const navigate = useNavigate();
  const location = useLocation();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const deletion = useDeleteProgress();
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
        listAll(listBacklog, { userId: userScope || undefined }),
        listAll(listFleets),
        listAll(listVessels),
      ]);

      const loadedObjectives = objectiveResult.objects || [];
      setObjectives(loadedObjectives);
      setFleets(fleetResult.objects || []);
      setVessels(sortByName(vesselResult.objects));
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

  const groupCounts = useMemo(() => countBacklogGroups(objectives), [objectives]);

  const filters = useMemo<BacklogFilters>(() => ({
    search,
    status: statusFilter,
    kind: kindFilter,
    priority: priorityFilter,
    backlogState: backlogStateFilter,
    effort: effortFilter,
    fleetId: fleetFilter,
    vesselId: vesselFilter,
    owner: ownerFilter,
    targetVersion: targetVersionFilter,
    group: groupFilter,
    title: colFilters.title,
    sortBy,
  }), [
    backlogStateFilter,
    colFilters,
    effortFilter,
    fleetFilter,
    groupFilter,
    kindFilter,
    ownerFilter,
    priorityFilter,
    search,
    sortBy,
    statusFilter,
    targetVersionFilter,
    vesselFilter,
  ]);

  const filteredObjectives = useMemo(() => filterBacklog(objectives, filters), [objectives, filters]);

  const orderedObjectives = useMemo(() => sortBacklog(filteredObjectives, sortBy), [filteredObjectives, sortBy]);

  const hasActiveFilters = hasActiveBacklogFilters(filters);

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
    const request = backlogRankSwap(objectives, objectiveId, direction);
    if (!request) return;

    try {
      const updated = await reorderBacklog(request);
      setObjectives((existing) => applyBacklogReorder(existing, updated));
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
        if (!(await deletion.run({ kind: 'backlogItem', action: () => deleteBacklogItem(objective.id), errorMessage: (err) => err instanceof Error ? err.message : t('Delete failed.') }))) return;
        pushToast('warning', t('Backlog item "{{title}}" deleted.', { title: objective.title }));
        await load();
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

  // The backlog item, scope and due/updated cells used to stack up to four lines each. Every value is its own one-line
  // column now; the less essential ones start hidden and the column chooser turns them on.
  const columns: DataTableColumn<Objective>[] = [
    {
      key: 'rank', label: t('Rank'), headerTitle: t('Position in the backlog. 1 is the top; lower numbers should be worked first. Use the arrows to move an item.'), interactive: true,
      render: (objective) => (
        <div className="backlog-rank-cell">
          <strong>{objective.rank}</strong>
          {canManage && (
            <div className="backlog-rank-buttons">
              <button type="button" className="btn btn-sm" onClick={() => void handleMoveRank(objective.id, -1)} aria-label={t('Move backlog item up')} title={t('Move this item one place up the backlog (toward rank 1).')}>
                {'\u2191'}
              </button>
              <button type="button" className="btn btn-sm" onClick={() => void handleMoveRank(objective.id, 1)} aria-label={t('Move backlog item down')} title={t('Move this item one place down the backlog.')}>
                {'\u2193'}
              </button>
            </div>
          )}
        </div>
      ),
    },
    {
      key: 'id', label: t('ID'), required: true, headerTitle: t('Unique backlog item ID. Use it with the REST API, MCP tools and the armada CLI.'), cellClassName: 'mono text-dim table-id-cell',
      render: (objective) => (
        <span className="id-display">
          <span className="id-value" title={objective.id}>{objective.id}</span>
          <CopyButton text={objective.id} onClick={e => e.stopPropagation()} />
        </span>
      ),
    },
    {
      key: 'title', label: t('Backlog Item'), required: true, headerTitle: t('Title of the backlog item. Hover a title to read its description; click the row for a summary.'),
      clearFilter: () => setColFilters(f => ({ ...f, title: '' })),
      filter: <input type="text" className="col-filter" aria-label={t('Backlog Item')} title={t('Show only backlog items whose title contains this text.')} value={colFilters.title} onChange={e => setColFilters(f => ({ ...f, title: e.target.value }))} placeholder={t('Filter...')} />,
      cellTitle: (objective) => objective.description || undefined,
      render: (objective) => <strong>{objective.title}</strong>,
    },
    {
      key: 'owner', label: t('Owner'), headerTitle: t('Person responsible for the item. Free text; it does not have to be an Armada user.'), cellClassName: 'text-dim',
      render: (objective) => <span className="cell-one-line" title={objective.owner || undefined}>{objective.owner || t('No owner')}</span>,
    },
    {
      key: 'category', label: t('Category'), headerTitle: t('Free-text grouping for the item, such as an area of the product.'), defaultHidden: true, cellClassName: 'text-dim',
      render: (objective) => objective.category ? <span className="cell-one-line" title={objective.category}>{objective.category}</span> : '-',
    },
    {
      key: 'targetVersion', label: t('Target Version'), headerTitle: t('Release the item is aimed at, for example 1.2.0.'), defaultHidden: true, cellClassName: 'text-dim cell-nowrap',
      render: (objective) => objective.targetVersion || '-',
    },
    {
      key: 'description', label: t('Description'), headerTitle: t('What the item is about. Hover a cell to read all of it.'), defaultHidden: true, cellClassName: 'text-dim truncate-cell',
      render: (objective) => objective.description ? <span className="truncate-text" title={objective.description}>{objective.description}</span> : '-',
    },
    {
      key: 'shape', label: t('Shape'), headerTitle: t('Kind of work, priority band (P0 is most urgent) and effort size (XS to XL).'), cellClassName: 'cell-nowrap',
      render: (objective) => (
        <div className="backlog-chip-row">
          <span className={`tag ${objective.kind.toLowerCase()}`} title={t('Kind of work: Feature, Bug, Refactor, Research, Chore or Initiative.')}>{objective.kind}</span>
          <span className={`tag ${objective.priority.toLowerCase()}`} title={t('Priority band. P0 is the most urgent, P3 the least.')}>{objective.priority}</span>
          <span className={`tag ${objective.effort.toLowerCase()}`} title={t('Rough effort estimate, from XS (tiny) to XL (very large).')}>{objective.effort}</span>
        </div>
      ),
    },
    {
      key: 'state', label: t('State'), headerTitle: t('Lifecycle status (Draft through Completed), backlog stage (Inbox through Dispatched), and how many other items block it.'), cellClassName: 'cell-nowrap',
      render: (objective) => (
        <div className="backlog-chip-row">
          <span title={t('Lifecycle status: Draft, Scoped, Planned, InProgress, Released, Deployed, Completed, Blocked or Cancelled.')}><StatusBadge status={objective.status} /></span>
          <span className={`tag ${objective.backlogState.toLowerCase()}`} title={t('Backlog stage before dispatch: Inbox, Triaged, Refining, ReadyForPlanning, ReadyForDispatch, then Dispatched.')}>{objective.backlogState}</span>
          {objective.blockedByObjectiveIds.length > 0 && (
            <span className="text-dim" title={t('Number of other backlog items that must be finished before this one can proceed.')}>{t('Blocked by')} {objective.blockedByObjectiveIds.length}</span>
          )}
        </div>
      ),
    },
    {
      key: 'vessels', label: t('Vessels'), headerTitle: t('Repositories this item touches. A vessel is needed before planning, dispatch or release drafting.'), cellClassName: 'text-dim',
      render: (objective) => {
        if (objective.vesselIds.length > 0) {
          const names = renderNames(objective.vesselIds, vesselMap);
          return <span className="cell-one-line" title={names}>{names}</span>;
        }
        // The planning note used to be a third line under the scope; it is the tooltip now.
        return <span className="cell-one-line" title={t('Needs a vessel before planning, dispatch, or release drafting can start.')}>{t('No vessel linked')}</span>;
      },
    },
    {
      key: 'fleets', label: t('Fleets'), headerTitle: t('Fleets (groups of repositories) this item is linked to.'), cellClassName: 'text-dim',
      render: (objective) => {
        const names = objective.fleetIds.length > 0 ? renderNames(objective.fleetIds, fleetMap) : t('No fleet linked');
        return <span className="cell-one-line" title={names}>{names}</span>;
      },
    },
    {
      key: 'due', label: t('Due'), headerTitle: t('Target date for the item. Hover a cell for the exact date and time.'), cellClassName: 'text-dim cell-nowrap',
      cellTitle: (objective) => (objective.dueUtc ? formatDateTime(objective.dueUtc) : undefined),
      render: (objective) => (objective.dueUtc ? formatRelativeTime(objective.dueUtc) : t('No due date')),
    },
    {
      key: 'lastUpdated', label: t('Last Updated'), headerTitle: t('When the item was last changed.'), cellClassName: 'text-dim cell-nowrap',
      cellTitle: (objective) => formatDateTime(objective.lastUpdateUtc),
      render: (objective) => formatRelativeTime(objective.lastUpdateUtc),
    },
    {
      key: 'actions', label: t('Actions'), headerTitle: t('Open, duplicate, reorder, view JSON or delete a backlog item.'), fixed: true, interactive: true, className: 'text-right',
      render: (objective) => (
        <ActionMenu
          id={`backlog-${objective.id}`}
          items={[
            { label: 'Open', title: 'Open the backlog item to edit it, refine it with a captain, plan or dispatch it.', onClick: () => navigate(`/backlog/${objective.id}`) },
            ...(canManage ? [{ label: 'Duplicate', title: 'Create a copy of this item with the same details and open the copy.', onClick: () => void handleDuplicate(objective) }] : []),
            { label: 'View JSON', title: 'Show the raw backlog item record as the API returns it.', onClick: () => setJsonData({ open: true, title: objective.title, data: objective }) },
            ...(canManage ? [
              { label: 'Move Up', title: 'Move this item one place up the backlog (toward rank 1).', onClick: () => void handleMoveRank(objective.id, -1) },
              { label: 'Move Down', title: 'Move this item one place down the backlog.', onClick: () => void handleMoveRank(objective.id, 1) },
              { label: 'Delete', danger: true as const, title: 'Delete this backlog item and its history. Linked missions, releases, deployments and incidents are kept. You confirm first.', onClick: () => handleDelete(objective) },
            ] : []),
          ]}
        />
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title={t('Backlog')}
        subtitle={t('Capture future work, refine it, and carry the same record through planning, dispatch, release, deployment, and incident follow-through.')}
        actions={(
          <>
            <UserScopeFilter value={userScope} onChange={setUserScope} />
            {canManage && (
              <button className="btn" onClick={() => setImportModalOpen(true)} title={t('Create a backlog item from a GitHub issue or pull request, copying its title and body.')}>
                {t('Import GitHub')}
              </button>
            )}
            {canManage && (
              <button className="btn btn-primary" onClick={() => navigate(createBacklogRoute)} title={t('Capture a new piece of future work. It starts in the Inbox stage.')}>
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
      {deletion.dialog}
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
              <span title={t('Vessel whose GitHub repository holds the issue or pull request. Its GitHub token (or the server\'s) is used to read it. Required.')}>{t('Vessel')}</span>
              <select title={t('Vessel whose GitHub repository holds the issue or pull request. Its GitHub token (or the server\'s) is used to read it. Required.')} value={importVesselId} onChange={(event) => setImportVesselId(event.target.value)}>
                <option value="">{t('Select a vessel')}</option>
                {vessels.map((vessel) => (
                  <option key={vessel.id} value={vessel.id}>{vessel.name}</option>
                ))}
              </select>
            </label>
            <label style={{ marginTop: '0.9rem', display: 'grid', gap: '0.35rem' }}>
              <span title={t('Whether the number below refers to a GitHub issue or a pull request in that repository.')}>{t('Source Type')}</span>
              <select title={t('Whether the number below refers to a GitHub issue or a pull request in that repository.')} value={importSourceType} onChange={(event) => setImportSourceType(event.target.value as GitHubObjectiveSourceType)}>
                <option value="Issue">{t('Issue')}</option>
                <option value="PullRequest">{t('Pull Request')}</option>
              </select>
            </label>
            <label style={{ marginTop: '0.9rem', display: 'grid', gap: '0.35rem' }}>
              <span title={t('The issue or pull request number as shown on GitHub, without the #. Example: 123.')}>{t('Number')}</span>
              <input
                title={t('The issue or pull request number as shown on GitHub, without the #. Example: 123.')}
                type="number"
                min={1}
                step={1}
                value={importNumber}
                onChange={(event) => setImportNumber(event.target.value)}
                placeholder={t('123')}
              />
            </label>
            <div className="modal-actions">
              <button className="btn btn-primary" disabled={importSaving} onClick={() => void handleImportFromGitHub()} title={t('Read the issue or pull request from GitHub and create a backlog item from it, then open the new item.')}>
                {importSaving ? t('Importing...') : t('Import')}
              </button>
              <button className="btn" disabled={importSaving} onClick={() => setImportModalOpen(false)} title={t('Close without importing anything.')}>
                {t('Cancel')}
              </button>
            </div>
          </div>
        </div>
      )}

      <div className="playbook-overview-grid">
        <div className="card playbook-overview-card">
          <span title={t('Total backlog items loaded, before the filters below are applied.')}>{t('Backlog Items')}</span>
          <strong>{objectives.length}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span title={t('Items in the ReadyForPlanning stage: scoped well enough to start a planning session.')}>{t('Ready For Planning')}</span>
          <strong>{planningReadyCount}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span title={t('Items in the ReadyForDispatch stage: planned and ready to send to captains.')}>{t('Ready For Dispatch')}</span>
          <strong>{dispatchReadyCount}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span title={t('Items whose status is Blocked or that are waiting on other backlog items.')}>{t('Blocked')}</span>
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
            title={t('Show only items whose title, description, owner, category, captain, tags or ID contain this text.')}
            placeholder={t('Search by title, description, owner, category, captain, tags, or ID...')}
          />
          <select aria-label={t('All kinds')} title={t('Show only one kind of work, such as Bug or Feature.')} value={kindFilter} onChange={(event) => setKindFilter(event.target.value as typeof kindFilter)}>
            <option value="all">{t('All kinds')}</option>
            {OBJECTIVE_KINDS.map((kind) => (
              <option key={kind} value={kind}>{kind}</option>
            ))}
          </select>
          <select aria-label={t('All priorities')} title={t('Show only one priority band. P0 is the most urgent.')} value={priorityFilter} onChange={(event) => setPriorityFilter(event.target.value as typeof priorityFilter)}>
            <option value="all">{t('All priorities')}</option>
            {OBJECTIVE_PRIORITIES.map((priority) => (
              <option key={priority} value={priority}>{priority}</option>
            ))}
          </select>
          <select aria-label={t('All backlog states')} title={t('Show only items in one backlog stage, from Inbox to Dispatched.')} value={backlogStateFilter} onChange={(event) => setBacklogStateFilter(event.target.value as typeof backlogStateFilter)}>
            <option value="all">{t('All backlog states')}</option>
            {OBJECTIVE_BACKLOG_STATES.map((state) => (
              <option key={state} value={state}>{state}</option>
            ))}
          </select>
          <select aria-label={t('All effort sizes')} title={t('Show only items with one effort estimate, from XS to XL.')} value={effortFilter} onChange={(event) => setEffortFilter(event.target.value as typeof effortFilter)}>
            <option value="all">{t('All effort sizes')}</option>
            {OBJECTIVE_EFFORTS.map((effort) => (
              <option key={effort} value={effort}>{effort}</option>
            ))}
          </select>
          <select aria-label={t('All lifecycle statuses')} title={t('Show only items in one lifecycle status, from Draft to Completed.')} value={statusFilter} onChange={(event) => setStatusFilter(event.target.value as typeof statusFilter)}>
            <option value="all">{t('All lifecycle statuses')}</option>
            {OBJECTIVE_STATUSES.map((status) => (
              <option key={status} value={status}>{status}</option>
            ))}
          </select>
          <select aria-label={t('All fleets')} title={t('Show only items linked to this fleet.')} value={fleetFilter} onChange={(event) => setFleetFilter(event.target.value)}>
            <option value="all">{t('All fleets')}</option>
            {fleets.map((fleet) => (
              <option key={fleet.id} value={fleet.id}>{fleet.name}</option>
            ))}
          </select>
          <select aria-label={t('All vessels')} title={t('Show only items linked to this vessel. New items you create start linked to it.')} value={vesselFilter} onChange={(event) => setVesselFilter(event.target.value)}>
            <option value="all">{t('All vessels')}</option>
            {vessels.map((vessel) => (
              <option key={vessel.id} value={vessel.id}>{vessel.name}</option>
            ))}
          </select>
          <input
            type="text"
            value={ownerFilter}
            onChange={(event) => setOwnerFilter(event.target.value)}
            title={t('Show only items whose owner contains this text.')}
            placeholder={t('Owner filter')}
          />
          <input
            type="text"
            value={targetVersionFilter}
            onChange={(event) => setTargetVersionFilter(event.target.value)}
            title={t('Show only items whose target version contains this text, for example 1.2.')}
            placeholder={t('Target version filter')}
          />
          <select aria-label={t('Sort by rank')} title={t('Order the list by backlog rank, priority band, last update or due date.')} value={sortBy} onChange={(event) => setSortBy(event.target.value as BacklogSortKey)}>
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
              <button type="button" className="btn btn-sm" onClick={clearFilters} title={t('Reset every filter and the search box so all backlog items show.')}>
                {t('Clear Filters')}
              </button>
            )}
          </div>
        </div>
      </div>

      <DataTable
        tableKey="objectives"
        columns={columns}
        rows={orderedObjectives}
        rowKey={(objective) => objective.id}
        onRowClick={(objective) => setViewRecord(objective as unknown as Record<string, unknown>)}
        autoRefresh={{ seconds: refreshSeconds, onChange: setRefreshSeconds }}
        onRefresh={load}
        refreshTitle="Reload the backlog from the server now."
        emptyMessage={(
          <div className="playbook-empty-state">
            <strong>{t('No backlog items match the current filters.')}</strong>
            <span>
              {canManage
                ? t('Capture a backlog item to start with inbox triage, refinement, planning readiness, and delivery lineage inside Armada.')
                : t('Ask a tenant administrator to create and manage backlog items.')}
            </span>
            {hasActiveFilters && (
              <div className="backlog-empty-actions">
                <button type="button" className="btn btn-sm" onClick={clearFilters} title={t('Reset every filter and the search box so all backlog items show.')}>
                  {t('Reset Filters')}
                </button>
              </div>
            )}
          </div>
        )}
        placeholder={objectives.length > 0 ? undefined : loading ? (
          <div className="playbook-empty-state">
            <strong>{t('Loading backlog...')}</strong>
            <span>{t('Checking for backlog items and scope links.')}</span>
          </div>
        ) : (
          <div className="playbook-empty-state">
            <strong>{t('No backlog items yet.')}</strong>
            <span>
              {canManage
                ? t('Create a backlog item to start triage, refinement, planning readiness, and delivery lineage inside Armada.')
                : t('Ask a tenant administrator to create and manage backlog items.')}
            </span>
          </div>
        )}
      />
    </div>
  );
}
