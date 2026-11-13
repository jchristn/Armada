import { useDuplicateRequest } from '../lib/useDuplicateRequest';
import { getVessel } from '../api/client';
import { useEffect, useState, useMemo, useCallback } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { listVessels, listFleets, listPipelines, createVessel, deleteVessel } from '../api/client';
import { useVesselSyncStatus } from '../lib/useVesselSyncStatus';
import { listAll } from '../api/paging';
import BranchesModal from '../components/vessels/BranchesModal';
import VesselCleanupModal from '../components/vessels/VesselCleanupModal';
import type { Fleet, Vessel, Pipeline } from '../types/models';
import DataTable, { type DataTableColumn } from '../components/shared/DataTable';
import ActionMenu from '../components/shared/ActionMenu';
import BuildContextModal from '../components/vessels/BuildContextModal';
import StatusBadge from '../components/shared/StatusBadge';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import CopyButton from '../components/shared/CopyButton';
import UserScopeFilter from '../components/shared/UserScopeFilter';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import PageHeader from '../components/shared/PageHeader';
import ErrorModal from '../components/shared/ErrorModal';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import { buildVesselDuplicatePayload } from '../lib/duplicates';
import { useResourceTable } from '../lib/useResourceTable';
import { useAuth } from '../context/AuthContext';
import ImportWizard from '../components/vessels/import/ImportWizard';
import RunActionModal from '../components/fleetActions/RunActionModal';
import VesselFormModal from '../components/vessels/VesselFormModal';
import { findLandingMode, getLandingModes } from '../lib/vesselForm';
import { useDeleteProgress } from '../lib/useDeleteProgress';


export default function Vessels() {
  const navigate = useNavigate();
  const location = useLocation();
  const { t } = useLocale();
  const deletion = useDeleteProgress();
  const { pushToast } = useNotifications();
  const { isTenantAdmin, user } = useAuth();

  // Import wizard: opened from the header button, or by the /vessels/import deep link.
  const importRoute = location.pathname.replace(/\/+$/, '').endsWith('/vessels/import');
  const importBatchId = importRoute ? new URLSearchParams(location.search).get('batch') : null;
  const [importOpen, setImportOpen] = useState(importRoute);
  useEffect(() => { if (importRoute) setImportOpen(true); }, [importRoute]);
  function closeImport() {
    setImportOpen(false);
    if (importRoute) navigate('/vessels', { replace: true });
  }

  // Bulk "Run action..." on the selected vessels.
  const [runActionIds, setRunActionIds] = useState<string[] | null>(null);

  // Landing-mode metadata: a short self-describing label and a full explanation of what each mode does to
  // completed mission work. Shared by the edit modal, the filter, and the table so wording stays consistent.
  const landingModes = getLandingModes(t);
  const landingModeInfo = (mode: string | null | undefined) => findLandingMode(landingModes, mode);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [fleets, setFleets] = useState<Fleet[]>([]);
  const [pipelines, setPipelines] = useState<Pipeline[]>([]);
  const [loading, setLoading] = useState(true);
  const [userScope, setUserScope] = useState('');
  const [error, setError] = useState('');
  const [branchesModal, setBranchesModal] = useState<{ vesselId: string; vesselName: string } | null>(null);
  const [cleanupModal, setCleanupModal] = useState<{ vesselId: string; vesselName: string } | null>(null);

  // Modal
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState<Vessel | null>(null);

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });
  const [buildContextVessel, setBuildContextVessel] = useState<Vessel | null>(null);

  // Confirm
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

  // Select-based column filters (equality; applied before the shared table hook)
  const [fleetFilter, setFleetFilter] = useState('');
  const [landingModeFilter, setLandingModeFilter] = useState('');

  const fleetMap = useMemo(() => {
    const m = new Map<string, string>();
    for (const f of fleets) m.set(f.id, f.name);
    return m;
  }, [fleets]);

  function fleetName(id: string | null): string {
    if (!id) return '';
    return fleetMap.get(id) ?? id.substring(0, 8);
  }

  const baseRows = useMemo(() => {
    return vessels.filter(v =>
      (!fleetFilter || v.fleetId === fleetFilter) &&
      (!landingModeFilter || (v.landingMode ?? '') === landingModeFilter)
    );
  }, [vessels, fleetFilter, landingModeFilter]);

  const table = useResourceTable({
    rows: baseRows,
    getId: (v) => v.id,
    columnValues: {
      name: (v) => v.name.toLowerCase(),
      repoUrl: (v) => (v.repoUrl ?? '').toLowerCase(),
      fleetId: (v) => fleetName(v.fleetId).toLowerCase(),
      defaultBranch: (v) => (v.defaultBranch ?? 'main').toLowerCase(),
      createdUtc: (v) => v.createdUtc,
    },
    initialSortField: 'name',
    initialSortDir: 'asc',
    initialPageSize: 25,
  });

  const load = useCallback(async () => {
    try {
      setLoading(true);
      const [vResult, fResult, pResult] = await Promise.all([listAll(listVessels, { filters: userScope ? { userId: userScope } : undefined }), listAll(listFleets), listAll(listPipelines)]);
      setVessels(vResult.objects);
      setFleets(fResult.objects);
      setPipelines(pResult.objects);
      setError('');
    } catch {
      setError(t('Failed to load vessels.'));
    } finally {
      setLoading(false);
    }
  }, [userScope, t]);

  useEffect(() => { load(); }, [load]);

  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('vessels', load);

  // Sync and branch counts for the rows on screen only, a few at a time, and not again on every auto-refresh:
  // each one runs git (and a fetch) on the vessel's host, which for a Harbor is the developer's machine.
  const { gitStatus, branchCounts, refresh: refreshSync, refreshVessel } = useVesselSyncStatus(table.paginated.map((v) => v.id), vessels);
  const reloadAll = useCallback(async () => {
    refreshSync();
    await load();
  }, [refreshSync, load]);

  // CRUD
  function openCreate() { setEditing(null); setShowForm(true); }
  function openEdit(v: Vessel) { setEditing(v); setShowForm(true); }

  function handleDelete(id: string, name: string) {
    setConfirm({
      open: true,
      title: t('Delete Vessel'),
      message: t('Delete vessel "{{name}}"? This cannot be undone.', { name }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        if (!(await deletion.run({ kind: 'vessel', action: () => deleteVessel(id) }))) return;
        pushToast('warning', t('Vessel "{{name}}" deleted.', { name }));
        load();
      },
    });
  }

  function handleBulkDelete() {
    setConfirm({
      open: true,
      title: t('Delete Selected Vessels'),
      message: t('Delete {{count}} selected vessel(s)? This cannot be undone.', { count: table.selected.length }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        const ids = [...table.selected];
        table.setSelected([]);
        let failed = 0;
        await deletion.run({
          kind: 'vessel',
          count: ids.length,
          action: async () => {
            failed = 0;
            for (const id of ids) {
              try { await deleteVessel(id); } catch { failed++; }
            }
          },
        });
        const success = ids.length - failed;
        if (success > 0) {
          pushToast(failed > 0 ? 'warning' : 'success', failed > 0
            ? t('Deleted {{success}} vessels. {{failed}} failed.', { success, failed })
            : t('Deleted {{success}} vessels.', { success }));
        }
        if (failed > 0) setError(t('Deleted {{success}} vessels, {{failed}} failed.', { success: ids.length - failed, failed }));
        load();
      },
    });
  }

  function manageObjectives(vessel: Vessel) {
    const params = new URLSearchParams({ vesselId: vessel.id });
    if (vessel.fleetId) {
      params.set('fleetId', vessel.fleetId);
    }

    navigate(`/backlog?${params.toString()}`);
  }

  useDuplicateRequest(async id => { await handleDuplicate(await getVessel(id)); }, error => setError(error instanceof Error ? error.message : t('Duplicate failed.')));

  async function handleDuplicate(vessel: Vessel) {
    try {
      const created = await createVessel(buildVesselDuplicatePayload(vessel));
      pushToast('success', t('Vessel "{{name}}" duplicated.', { name: created.name }));
      navigate(`/vessels/${created.id}?edit=1`);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Duplicate failed.'));
    }
  }

  function syncCell(v: Vessel) {
    const gs = gitStatus[v.id];
    if (!gs || (gs.ahead === null && gs.behind === null)) return <span className="text-dim">-</span>;
    const ahead = gs.ahead ?? 0;
    const behind = gs.behind ?? 0;
    if (ahead === 0 && behind === 0) return <span className="git-sync-badge git-sync-even" title={t('The working directory has every commit on the remote default branch, and nothing unpushed.')}>{t('in sync')}</span>;
    return (
      <span className="git-sync-badges">
        {ahead > 0 && <span className="git-sync-badge git-sync-ahead" title={t('{{count}} commit(s) in the working directory that the remote default branch does not have yet. Push to share them.', { count: ahead })}>{ahead} {t('ahead')}</span>}
        {behind > 0 && <span className="git-sync-badge git-sync-behind" title={t('{{count}} commit(s) on the remote default branch that the working directory does not have. Pull to catch up.', { count: behind })}>{behind} {t('behind')}</span>}
      </span>
    );
  }

  const columns: DataTableColumn<Vessel>[] = [
    {
      key: 'id', label: t('ID'), headerTitle: t('Vessel ID (vsl_...), used by the CLI, API and MCP tools.'), required: true, cellClassName: 'mono text-dim table-id-cell',
      render: (v) => (
        <span className="id-display">
          <span className="id-value" title={v.id}>{v.id}</span>
          <CopyButton text={v.id} onClick={e => e.stopPropagation()} />
        </span>
      ),
    },
    {
      key: 'name', label: t('Name'), required: true, sortKey: 'name', headerTitle: t('Vessel name. Click to sort; type in the box below to filter.'), cellClassName: 'cell-nowrap',
      filter: <input type="text" className="col-filter" aria-label={t('Filter vessels by name')} value={table.colFilters.name ?? ''} onChange={e => table.setColFilter('name', e.target.value)} placeholder={t('Search...')} />,
      render: (v) => <strong>{v.name}</strong>,
    },
    {
      key: 'fleet', label: t('Fleet'), sortKey: 'fleetId', headerTitle: t('Fleet the vessel belongs to. Click a fleet name to open it; use the box below to show one fleet.'), cellClassName: 'cell-nowrap',
      clearFilter: () => setFleetFilter(''),
      filter: (
        <select aria-label={t('Filter vessels by fleet')} className="col-filter" title={t('Show only vessels in this fleet.')} value={fleetFilter} onChange={e => { setFleetFilter(e.target.value); table.setPageNumber(1); }}>
          <option value="">{t('All Fleets')}</option>
          {fleets.map(f => <option key={f.id} value={f.id}>{f.name}</option>)}
        </select>
      ),
      render: (v) => v.fleetId ? (
        <a href="#" onClick={e => { e.preventDefault(); e.stopPropagation(); navigate(`/fleets/${v.fleetId}`); }}>
          {fleetName(v.fleetId)}
        </a>
      ) : '-',
    },
    {
      key: 'repoUrl', label: t('Repository'), sortKey: 'repoUrl', headerTitle: t('Git remote Armada clones from and pushes to. Click to sort; type in the box below to filter.'),
      cellClassName: 'text-dim table-url-cell',
      clearFilter: () => table.setColFilter('repoUrl', ''),
      filter: <input type="text" className="col-filter" aria-label={t('Filter vessels by repository')} value={table.colFilters.repoUrl ?? ''} onChange={e => table.setColFilter('repoUrl', e.target.value)} placeholder={t('Search...')} />,
      render: (v) => v.repoUrl ? (
        <span className="id-display">
          <span className="url-value" title={v.repoUrl}>{v.repoUrl}</span>
          <CopyButton text={v.repoUrl} onClick={e => e.stopPropagation()} />
        </span>
      ) : '-',
    },
    {
      key: 'defaultBranch', label: t('Branch'), sortKey: 'defaultBranch', headerTitle: t('Default branch: where missions start from and land into.'),
      cellClassName: 'mono text-dim table-url-cell',
      render: (v) => <span className="cell-one-line" title={v.defaultBranch || 'main'}>{v.defaultBranch || 'main'}</span>,
    },
    {
      key: 'landingMode', label: t('Landing Mode'),
      headerTitle: t('How finished mission work reaches the repository. Default (global) means the vessel uses the Admiral-wide setting. Hover a cell for details.'),
      cellClassName: 'cell-nowrap',
      clearFilter: () => setLandingModeFilter(''),
      filter: (
        <select aria-label={t('Filter vessels by landing mode')} className="col-filter" title={t('Show only vessels that set this landing mode explicitly.')} value={landingModeFilter} onChange={e => { setLandingModeFilter(e.target.value); table.setPageNumber(1); }}>
          <option value="">{t('All Modes')}</option>
          {landingModes.filter(m => m.value).map(m => (
            <option key={m.value} value={m.value} title={m.description}>{m.value} -- {m.short}</option>
          ))}
        </select>
      ),
      // One line: the mode (or "Default (global)"); the short summary and full explanation are in the tooltip.
      render: (v) => {
        const info = landingModeInfo(v.landingMode);
        return (
          <span className="cell-one-line" title={`${info.short} -- ${info.description}`}>
            {v.landingMode || t('Default (global)')}
          </span>
        );
      },
    },
    { key: 'sync', label: t('Sync'), headerTitle: t('How far the vessel working directory is ahead of or behind the remote default branch, after a quick fetch. A dash means there is no working directory to compare.'), cellClassName: 'cell-nowrap', render: syncCell },
    {
      key: 'branches', label: t('Branches'), headerTitle: t('Number of branches in the vessel repository. Click a count to manage branches.'), interactive: true,
      render: (v) => {
        const count = branchCounts[v.id];
        return (
          <button
            className="btn btn-sm"
            title={t('Open branch management: see ahead/behind counts, push a branch, or merge one into another.')}
            onClick={() => setBranchesModal({ vesselId: v.id, vesselName: v.name })}>
            {count === null || count === undefined ? t('Branches') : t('{{count}} branches', { count })}
          </button>
        );
      },
    },
    {
      key: 'actions', label: t('Actions'), headerTitle: t('Dispatch, branches, history, workspace, context, edit, duplicate or delete.'), fixed: true, interactive: true, className: 'text-right',
      render: (v) => (
        <ActionMenu id={`vessel-${v.id}`} items={[
          { label: 'Dispatch', title: 'Go to Dispatch with this vessel selected, to send a captain a new mission.', onClick: () => navigate('/dispatch', { state: { fromVessel: true, vesselId: v.id } }) },
          { label: 'Manage Branches', title: 'List branches with ahead/behind counts, push a branch, or merge one branch into another.', onClick: () => setBranchesModal({ vesselId: v.id, vesselName: v.name }) },
          { label: 'Cleanup', title: "Inspect this vessel's docks, worktrees and Armada branches, see which are safe, in use or unknown, and remove safe ones after you confirm. Opening it deletes nothing.", onClick: () => setCleanupModal({ vesselId: v.id, vesselName: v.name }) },
          { label: 'View History', title: 'Open the commit history and activity heatmap for this vessel.', onClick: () => navigate(`/vessels/${v.id}/history`) },
          { label: 'Manage Objectives', title: 'Open the objectives (backlog) filtered to this vessel and its fleet.', onClick: () => manageObjectives(v) },
          { label: 'Manage Fleet', title: 'Open the fleet this vessel belongs to. Unavailable when the vessel has no fleet.', onClick: () => navigate(`/fleets/${v.fleetId}`), disabled: !v.fleetId },
          { label: 'Open Workspace', title: 'Browse, edit and run commands in this vessel working directory.', onClick: () => navigate(`/workspace/${v.id}`) },
          { label: 'View Detail', title: 'Open the vessel page with its settings, readiness, missions and docks.', onClick: () => navigate(`/vessels/${v.id}`) },
          { label: v.modelContext && v.modelContext.trim().length > 0 ? 'Refine Context' : 'Build Context', title: 'Launch a captain to study the repository and write or refine its Model Context. Takes a few minutes and uses model tokens.', onClick: () => setBuildContextVessel(v) },
          { label: 'Edit', title: 'Open the Edit Vessel form for this vessel.', onClick: () => openEdit(v) },
          { label: 'Duplicate', title: 'Create a copy of this vessel with the same settings and paths, then open it for editing. Change the name and paths before using both.', onClick: () => void handleDuplicate(v) },
          { label: 'View JSON', title: 'Show the raw vessel record as JSON.', onClick: () => setJsonData({ open: true, title: `${t('Vessel')}: ${v.name}`, data: v }) },
          { label: 'Delete', title: 'Delete this vessel after you confirm, with its missions, docks and Armada-managed bare clone. Your working directory is never touched.', danger: true, onClick: () => handleDelete(v.id, v.name) },
        ]} />
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title={t('Vessels')}
        subtitle={t('Git repositories registered with Armada')}
        actions={(
          <div className="vessels-toolbar">
            <UserScopeFilter value={userScope} onChange={setUserScope} />
            {isTenantAdmin && (
              <button className="btn btn-sm" onClick={() => setImportOpen(true)} title={t('Scan folders on the Admiral host for git repositories and create a vessel for each one you pick. You review the list before anything is created.')}>
                {t('Import repositories')}
              </button>
            )}
            <button className="btn btn-primary btn-sm" onClick={openCreate} title={t('Register one git repository as a new vessel.')}>+ {t('Vessel')}</button>
          </div>
        )}
      />

      <ErrorModal error={error} onClose={() => setError('')} />

      {/* Create/Edit Modal (shared with the vessel page) */}
      {showForm && (
        <VesselFormModal
          vessel={editing}
          fleets={fleets}
          pipelines={pipelines}
          onClose={() => setShowForm(false)}
          onError={setError}
          onSaved={(name, created) => {
            setShowForm(false);
            pushToast('success', created
              ? t('Vessel "{{name}}" created.', { name })
              : t('Vessel "{{name}}" saved.', { name }));
            load();
          }}
        />
      )}

      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      {buildContextVessel && (
        <BuildContextModal
          vessel={buildContextVessel}
          onClose={() => setBuildContextVessel(null)}
          onBuilt={(updated) => {
            setVessels((prev) => prev.map((x) => (x.id === updated.id ? updated : x)));
            pushToast('success', t('Model Context updated for "{{name}}".', { name: updated.name }));
          }}
        />
      )}
      {deletion.dialog}
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message}
        onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />

      <ImportWizard open={importOpen} onClose={closeImport} onImported={() => void load()} initialBatchId={importBatchId} tenantId={user?.user?.tenantId ?? null} />
      {runActionIds && (
        <RunActionModal
          open
          vesselIds={runActionIds}
          onClose={() => setRunActionIds(null)}
          onStarted={(result) => { table.clearSelection(); navigate(`/fleet-actions/runs/${result.runId}`); }}
        />
      )}

      {table.selected.length > 0 && (
        <div className="bulk-bar" role="region" aria-label={t('Bulk actions')}>
          <span className="bulk-bar-count">{t('{count, plural, one {# vessel selected} other {# vessels selected}}', { count: table.selected.length })}</span>
          {isTenantAdmin && (
            <button className="btn btn-sm btn-primary" onClick={() => setRunActionIds([...table.selected])} title={t('Run a saved fleet action (such as a git pull or a command) on every selected vessel in its working directory.')}>
              {t('Run action...')}
            </button>
          )}
          <button className="btn btn-sm btn-danger" onClick={handleBulkDelete} title={t('Permanently delete the selected vessels, their missions, docks and Armada-managed bare clones. Working directories are not touched. You confirm first.')}>
            {t('Delete Selected')} ({table.selected.length})
          </button>
          <button className="btn btn-sm" onClick={() => table.clearSelection()} title={t('Uncheck all selected vessels.')}>{t('Clear selection')}</button>
        </div>
      )}

      <DataTable
        tableKey="vessels"
        columns={columns}
        rows={table.paginated}
        rowKey={(v) => v.id}
        onRowClick={openEdit}
        sort={table.sortState}
        pagination={table.paginationProps}
        autoRefresh={{ seconds: refreshSeconds, onChange: setRefreshSeconds }}
        onRefresh={reloadAll}
        refreshTitle={t('Reload the vessel list, sync status and branch counts now.')}
        selection={{
          isSelected: (v) => table.selected.includes(v.id),
          onToggle: (v) => table.toggleSelect(v.id),
          allSelected: table.allSelected,
          onToggleAll: (checked) => (checked ? table.selectAll() : table.clearSelection()),
          selectAllLabel: t('Select all vessels'),
          rowLabel: () => t('Select this vessel'),
        }}
        emptyMessage={t('No vessels match the current filters.')}
        placeholder={vessels.length > 0 ? undefined : loading ? <p className="text-dim">{t('Loading...')}</p> : (
          <div className="empty-state card" role="status">
            <h4 className="empty-state-title">{t('No vessels configured.')}</h4>
            <div className="empty-state-body text-dim">{t('Add a single repository with + Vessel, or import many existing local repositories at once.')}</div>
            {isTenantAdmin && (
              <div className="empty-state-actions">
                <button className="btn btn-primary btn-sm" onClick={() => setImportOpen(true)} title={t('Scan folders on the Admiral host for git repositories and create a vessel for each one you pick. You review the list before anything is created.')}>{t('Import repositories')}</button>
              </div>
            )}
          </div>
        )}
      />

      {cleanupModal && (
        <VesselCleanupModal
          vesselId={cleanupModal.vesselId}
          vesselName={cleanupModal.vesselName}
          open
          onClose={() => setCleanupModal(null)}
        />
      )}
      {branchesModal && (
        <BranchesModal
          vesselId={branchesModal.vesselId}
          vesselName={branchesModal.vesselName}
          open={true}
          onClose={() => { refreshVessel(branchesModal.vesselId); setBranchesModal(null); void load(); }}
        />
      )}
    </div>
  );
}
