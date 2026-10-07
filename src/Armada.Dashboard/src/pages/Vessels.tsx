import { useEffect, useState, useMemo, useCallback } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { listVessels, listFleets, listPipelines, createVessel, deleteVessel, getVesselGitStatus, getVesselBranches } from '../api/client';
import BranchesModal from '../components/vessels/BranchesModal';
import type { Fleet, Vessel, Pipeline } from '../types/models';
import Pagination from '../components/shared/Pagination';
import ActionMenu from '../components/shared/ActionMenu';
import BuildContextModal from '../components/vessels/BuildContextModal';
import StatusBadge from '../components/shared/StatusBadge';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import CopyButton from '../components/shared/CopyButton';
import RefreshButton from '../components/shared/RefreshButton';
import AutoRefreshSelect from '../components/shared/AutoRefreshSelect';
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


export default function Vessels() {
  const navigate = useNavigate();
  const location = useLocation();
  const { t } = useLocale();
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
  const [gitStatus, setGitStatus] = useState<Record<string, { ahead: number | null; behind: number | null }>>({});
  const [branchCounts, setBranchCounts] = useState<Record<string, number | null>>({});
  const [branchesModal, setBranchesModal] = useState<{ vesselId: string; vesselName: string } | null>(null);

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
      const [vResult, fResult, pResult] = await Promise.all([listVessels({ pageSize: 9999, filters: userScope ? { userId: userScope } : undefined }), listFleets({ pageSize: 9999 }), listPipelines({ pageSize: 9999 })]);
      setVessels(vResult.objects);
      setFleets(fResult.objects);
      setPipelines(pResult.objects);
      setError('');

      // Fetch git status and branch counts for each vessel in the background (non-blocking)
      const statusMap: Record<string, { ahead: number | null; behind: number | null }> = {};
      const countMap: Record<string, number | null> = {};
      await Promise.all(vResult.objects.map(async (v: Vessel) => {
        try {
          const gs = await getVesselGitStatus(v.id);
          statusMap[v.id] = { ahead: gs.commitsAhead, behind: gs.commitsBehind };
        } catch {
          statusMap[v.id] = { ahead: null, behind: null };
        }
        try {
          const br = await getVesselBranches(v.id);
          countMap[v.id] = br.branchCount;
        } catch {
          countMap[v.id] = null;
        }
      }));
      setGitStatus(statusMap);
      setBranchCounts(countMap);
    } catch {
      setError(t('Failed to load vessels.'));
    } finally {
      setLoading(false);
    }
  }, [userScope, t]);

  useEffect(() => { load(); }, [load]);

  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('vessels', load);

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
        try {
          await deleteVessel(id);
          pushToast('warning', t('Vessel "{{name}}" deleted.', { name }));
          load();
        } catch { setError(t('Delete failed.')); }
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
        for (const id of ids) {
          try { await deleteVessel(id); } catch { failed++; }
        }
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

  async function handleDuplicate(vessel: Vessel) {
    try {
      const created = await createVessel(buildVesselDuplicatePayload(vessel));
      pushToast('success', t('Vessel "{{name}}" duplicated.', { name: created.name }));
      navigate(`/vessels/${created.id}?edit=1`);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Duplicate failed.'));
    }
  }

  return (
    <div>
      <PageHeader
        title={t('Vessels')}
        subtitle={t('Git repositories registered with Armada')}
        actions={(
          <>
            <UserScopeFilter value={userScope} onChange={setUserScope} />
            <AutoRefreshSelect seconds={refreshSeconds} onChange={setRefreshSeconds} />
            <RefreshButton onRefresh={load} title="Refresh vessel data" />
            {isTenantAdmin && (
              <button className="btn btn-sm" onClick={() => setImportOpen(true)} title={t('Discover and onboard many local repositories at once')}>
                {t('Import repositories')}
              </button>
            )}
            <button className="btn btn-primary btn-sm" onClick={openCreate}>+ {t('Vessel')}</button>
          </>
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

      {loading && vessels.length === 0 && <p className="text-dim">{t('Loading...')}</p>}
      {!loading && vessels.length === 0 && (
        <div className="empty-state card" role="status">
          <h4 className="empty-state-title">{t('No vessels configured.')}</h4>
          <div className="empty-state-body text-dim">{t('Add a single repository with + Vessel, or import many existing local repositories at once.')}</div>
          {isTenantAdmin && (
            <div className="empty-state-actions">
              <button className="btn btn-primary btn-sm" onClick={() => setImportOpen(true)}>{t('Import repositories')}</button>
            </div>
          )}
        </div>
      )}

      {table.selected.length > 0 && (
        <div className="bulk-bar" role="region" aria-label={t('Bulk actions')}>
          <span className="bulk-bar-count">{t('{count, plural, one {# vessel selected} other {# vessels selected}}', { count: table.selected.length })}</span>
          {isTenantAdmin && (
            <button className="btn btn-sm btn-primary" onClick={() => setRunActionIds([...table.selected])} title={t('Run a fleet action on the selected vessels')}>
              {t('Run action...')}
            </button>
          )}
          <button className="btn btn-sm btn-danger" onClick={handleBulkDelete}>
            {t('Delete Selected')} ({table.selected.length})
          </button>
          <button className="btn btn-sm" onClick={() => table.clearSelection()}>{t('Clear selection')}</button>
        </div>
      )}

      {vessels.length > 0 && (
        <>
          <Pagination pageNumber={table.currentPage} pageSize={table.pageSize} totalPages={table.totalPages}
            totalRecords={table.sorted.length}
            onPageChange={p => table.setPageNumber(p)} onPageSizeChange={s => { table.setPageSize(s); table.setPageNumber(1); }} />

          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th className="col-checkbox">
                    <input aria-label={t('Select all vessels')} type="checkbox" checked={table.allSelected} onChange={e => e.target.checked ? table.selectAll() : table.clearSelection()} title={t('Select all vessels')} />
                  </th>
                  <th className="sortable" onClick={() => table.handleSort('name')} title={t('Vessel name -- click to sort')}>
                    {t('Name')}{table.sortIcon('name')}
                  </th>
                  <th>{t('ID')}</th>
                  <th className="sortable" onClick={() => table.handleSort('fleetId')} title={t('Fleet -- click to sort')}>
                    {t('Fleet')}{table.sortIcon('fleetId')}
                  </th>
                  <th className="sortable" onClick={() => table.handleSort('repoUrl')} title={t('Remote git repository URL')}>
                    {t('Repository')}{table.sortIcon('repoUrl')}
                  </th>
                  <th title={t('How completed mission work is integrated (LocalMerge, MergeAndPush, PullRequest, MergeQueue, None)')}>{t('Landing Mode')}</th>
                  <th title={t('Commits ahead and behind the remote default branch')}>{t('Sync')}</th>
                  <th title={t('Number of branches in the vessel repository')}>{t('Branches')}</th>
                  <th className="text-right">{t('Actions')}</th>
                </tr>
                <tr className="column-filter-row">
                  <td></td>
                  <td><input type="text" className="col-filter" value={table.colFilters.name ?? ''} onChange={e => table.setColFilter('name', e.target.value)} placeholder={t('Search...')} /></td>
                  <td></td>
                  <td>
                    <select aria-label={t('Filter vessels by fleet')} className="col-filter" title={t('Filter vessels by fleet')} value={fleetFilter} onChange={e => { setFleetFilter(e.target.value); table.setPageNumber(1); }}>
                      <option value="">{t('All Fleets')}</option>
                      {fleets.map(f => <option key={f.id} value={f.id}>{f.name}</option>)}
                    </select>
                  </td>
                  <td><input type="text" className="col-filter" value={table.colFilters.repoUrl ?? ''} onChange={e => table.setColFilter('repoUrl', e.target.value)} placeholder={t('Search...')} /></td>
                  <td>
                    <select aria-label={t('Filter vessels by landing mode')} className="col-filter" title={t('Filter vessels by landing mode')} value={landingModeFilter} onChange={e => { setLandingModeFilter(e.target.value); table.setPageNumber(1); }}>
                      <option value="">{t('All Modes')}</option>
                      {landingModes.filter(m => m.value).map(m => (
                        <option key={m.value} value={m.value} title={m.description}>{m.value} -- {m.short}</option>
                      ))}
                    </select>
                  </td>
                  <td></td>
                  <td></td>
                  <td></td>
                </tr>
              </thead>
              <tbody>
                {table.paginated.map(v => (
                  <tr key={v.id} className="clickable" onClick={() => openEdit(v)}>
                    <td className="col-checkbox" onClick={e => e.stopPropagation()}>
                      <input aria-label={t('Select this vessel')} type="checkbox" checked={table.selected.includes(v.id)} onChange={() => table.toggleSelect(v.id)} title={t('Select this vessel')} />
                    </td>
                    <td><strong>{v.name}</strong></td>
                    <td className="mono text-dim table-id-cell">
                      <span className="id-display">
                        <span className="id-value" title={v.id}>{v.id}</span>
                        <CopyButton text={v.id} onClick={e => e.stopPropagation()} />
                      </span>
                    </td>
                    <td>
                      {v.fleetId ? (
                        <a href="#" onClick={e => { e.preventDefault(); e.stopPropagation(); navigate(`/fleets/${v.fleetId}`); }}>
                          {fleetName(v.fleetId)}
                        </a>
                      ) : '-'}
                    </td>
                    <td className="text-dim table-url-cell">
                      {v.repoUrl ? (
                        <span className="id-display">
                          <span className="url-value" title={v.repoUrl}>{v.repoUrl}</span>
                          <CopyButton text={v.repoUrl} onClick={e => e.stopPropagation()} title="Copy URL" />
                        </span>
                      ) : '-'}
                      <span className="id-display mono cell-subline" title={t('Default branch')}>
                        <span className="url-value" title={v.defaultBranch || 'main'}>{v.defaultBranch || 'main'}</span>
                        <CopyButton text={v.defaultBranch || 'main'} onClick={e => e.stopPropagation()} title="Copy branch" />
                      </span>
                    </td>
                    <td title={landingModeInfo(v.landingMode).description}>
                      <div>{v.landingMode || t('Default')}</div>
                      <div className="text-dim" style={{ fontSize: '0.75rem' }}>{landingModeInfo(v.landingMode).short}</div>
                    </td>
                    <td>
                      {(() => {
                        const gs = gitStatus[v.id];
                        if (!gs || (gs.ahead === null && gs.behind === null)) return <span className="text-dim">-</span>;
                        const ahead = gs.ahead ?? 0;
                        const behind = gs.behind ?? 0;
                        if (ahead === 0 && behind === 0) return <span className="git-sync-badge git-sync-even" title={t('Up to date with remote')}>{t('in sync')}</span>;
                        return (
                          <span className="git-sync-badges">
                            {ahead > 0 && <span className="git-sync-badge git-sync-ahead" title={t('{{count}} commit(s) ahead of remote -- needs push', { count: ahead })}>{ahead} {t('ahead')}</span>}
                            {behind > 0 && <span className="git-sync-badge git-sync-behind" title={t('{{count}} commit(s) behind remote -- needs pull', { count: behind })}>{behind} {t('behind')}</span>}
                          </span>
                        );
                      })()}
                    </td>
                    <td onClick={e => e.stopPropagation()}>
                      {(() => {
                        const count = branchCounts[v.id];
                        return (
                          <button
                            className="btn btn-sm"
                            title={t('Manage branches')}
                            onClick={() => setBranchesModal({ vesselId: v.id, vesselName: v.name })}>
                            {count === null || count === undefined ? t('Branches') : t('{{count}} branches', { count })}
                          </button>
                        );
                      })()}
                    </td>
                    <td className="text-right" onClick={e => e.stopPropagation()}>
                      <ActionMenu id={`vessel-${v.id}`} items={[
                        { label: 'Dispatch', onClick: () => navigate('/dispatch', { state: { fromVessel: true, vesselId: v.id } }) },
                        { label: 'Manage Branches', onClick: () => setBranchesModal({ vesselId: v.id, vesselName: v.name }) },
                        { label: 'View History', onClick: () => navigate(`/vessels/${v.id}/history`) },
                        { label: 'Manage Objectives', onClick: () => manageObjectives(v) },
                        { label: 'Manage Fleet', onClick: () => navigate(`/fleets/${v.fleetId}`), disabled: !v.fleetId },
                        { label: 'Open Workspace', onClick: () => navigate(`/workspace/${v.id}`) },
                        { label: 'View Detail', onClick: () => navigate(`/vessels/${v.id}`) },
                        { label: v.modelContext && v.modelContext.trim().length > 0 ? 'Refine Context' : 'Build Context', onClick: () => setBuildContextVessel(v) },
                        { label: 'Edit', onClick: () => openEdit(v) },
                        { label: 'Duplicate', onClick: () => void handleDuplicate(v) },
                        { label: 'View JSON', onClick: () => setJsonData({ open: true, title: `${t('Vessel')}: ${v.name}`, data: v }) },
                        { label: 'Delete', danger: true, onClick: () => handleDelete(v.id, v.name) },
                      ]} />
                    </td>
                  </tr>
                ))}
                {table.paginated.length === 0 && (
                  <tr><td colSpan={9} className="text-dim">{t('No vessels match the current filters.')}</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </>
      )}

      {branchesModal && (
        <BranchesModal
          vesselId={branchesModal.vesselId}
          vesselName={branchesModal.vesselName}
          open={true}
          onClose={() => { setBranchesModal(null); void load(); }}
        />
      )}
    </div>
  );
}
