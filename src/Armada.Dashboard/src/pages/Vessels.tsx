import { useEffect, useState, useMemo, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { listVessels, listFleets, listPipelines, createVessel, updateVessel, deleteVessel, getVesselGitStatus } from '../api/client';
import type { Fleet, Vessel, Pipeline } from '../types/models';
import Pagination from '../components/shared/Pagination';
import ActionMenu from '../components/shared/ActionMenu';
import StatusBadge from '../components/shared/StatusBadge';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import CopyButton from '../components/shared/CopyButton';
import RefreshButton from '../components/shared/RefreshButton';
import ErrorModal from '../components/shared/ErrorModal';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';

type SortDir = 'asc' | 'desc';
type SortField = 'name' | 'fleetId' | 'defaultBranch' | 'createdUtc';

interface VesselForm {
  name: string;
  fleetId: string;
  repoUrl: string;
  defaultBranch: string;
  localPath: string;
  workingDirectory: string;
  projectContext: string;
  styleGuide: string;
  enableModelContext: boolean;
  modelContext: string;
  landingMode: string;
  branchCleanupPolicy: string;
  allowConcurrentMissions: boolean;
  defaultPipelineId: string;
}

const emptyForm: VesselForm = {
  name: '', fleetId: '', repoUrl: '', defaultBranch: 'main', localPath: '', workingDirectory: '',
  projectContext: '', styleGuide: '', enableModelContext: true, modelContext: '', landingMode: 'LocalMerge', branchCleanupPolicy: 'LocalAndRemote', allowConcurrentMissions: false, defaultPipelineId: '',
};

export default function Vessels() {
  const navigate = useNavigate();
  const { t } = useLocale();
  const { pushToast } = useNotifications();
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [fleets, setFleets] = useState<Fleet[]>([]);
  const [pipelines, setPipelines] = useState<Pipeline[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [gitStatus, setGitStatus] = useState<Record<string, { ahead: number | null; behind: number | null }>>({});

  // Modal
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState<Vessel | null>(null);
  const [form, setForm] = useState<VesselForm>({ ...emptyForm });

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });

  // Confirm
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

  // Selection
  const [selected, setSelected] = useState<string[]>([]);

  // Sorting
  const [sortField, setSortField] = useState<SortField>('name');
  const [sortDir, setSortDir] = useState<SortDir>('asc');

  // Column filters
  const [colFilters, setColFilters] = useState({ name: '', fleetId: '', repoUrl: '', landingMode: '' });

  // Pagination
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(25);

  const fleetMap = useMemo(() => {
    const m = new Map<string, string>();
    for (const f of fleets) m.set(f.id, f.name);
    return m;
  }, [fleets]);

  function fleetName(id: string | null): string {
    if (!id) return '';
    return fleetMap.get(id) ?? id.substring(0, 8);
  }

  const load = useCallback(async () => {
    try {
      setLoading(true);
      const [vResult, fResult, pResult] = await Promise.all([listVessels({ pageSize: 9999 }), listFleets({ pageSize: 9999 }), listPipelines({ pageSize: 9999 })]);
      setVessels(vResult.objects);
      setFleets(fResult.objects);
      setPipelines(pResult.objects);
      setError('');

      // Fetch git status for each vessel in the background (non-blocking)
      const statusMap: Record<string, { ahead: number | null; behind: number | null }> = {};
      await Promise.all(vResult.objects.map(async (v: Vessel) => {
        try {
          const gs = await getVesselGitStatus(v.id);
          statusMap[v.id] = { ahead: gs.commitsAhead, behind: gs.commitsBehind };
        } catch {
          statusMap[v.id] = { ahead: null, behind: null };
        }
      }));
      setGitStatus(statusMap);
    } catch {
      setError(t('Failed to load vessels.'));
    } finally {
      setLoading(false);
    }
  }, [t]);

  useEffect(() => { load(); }, [load]);

  // Filtered
  const filtered = useMemo(() => {
    return vessels.filter(v =>
      (!colFilters.name || v.name.toLowerCase().includes(colFilters.name.toLowerCase())) &&
      (!colFilters.fleetId || v.fleetId === colFilters.fleetId) &&
      (!colFilters.repoUrl || (v.repoUrl ?? '').toLowerCase().includes(colFilters.repoUrl.toLowerCase())) &&
      (!colFilters.landingMode || (v.landingMode ?? '') === colFilters.landingMode)
    );
  }, [vessels, colFilters, fleetMap]);

  // Sorted
  const sorted = useMemo(() => {
    const arr = [...filtered];
    arr.sort((a, b) => {
      let va: string = '';
      let vb: string = '';
      switch (sortField) {
        case 'fleetId': va = fleetName(a.fleetId).toLowerCase(); vb = fleetName(b.fleetId).toLowerCase(); break;
        case 'defaultBranch': va = (a.defaultBranch ?? 'main').toLowerCase(); vb = (b.defaultBranch ?? 'main').toLowerCase(); break;
        case 'createdUtc': va = a.createdUtc; vb = b.createdUtc; break;
        default: va = a.name.toLowerCase(); vb = b.name.toLowerCase();
      }
      if (va < vb) return sortDir === 'asc' ? -1 : 1;
      if (va > vb) return sortDir === 'asc' ? 1 : -1;
      return 0;
    });
    return arr;
  }, [filtered, sortField, sortDir, fleetMap]);

  const totalPages = Math.max(1, Math.ceil(sorted.length / pageSize));
  const currentPage = Math.min(pageNumber, totalPages);
  const paginated = useMemo(() => {
    const start = (currentPage - 1) * pageSize;
    return sorted.slice(start, start + pageSize);
  }, [sorted, currentPage, pageSize]);

  function handleSort(field: SortField) {
    if (sortField === field) setSortDir(d => d === 'asc' ? 'desc' : 'asc');
    else { setSortField(field); setSortDir('asc'); }
  }

  function sortIcon(field: SortField) {
    if (sortField !== field) return '';
    return sortDir === 'asc' ? ' \u25B2' : ' \u25BC';
  }

  // Selection
  const allSelected = selected.length > 0 && selected.length === filtered.length;
  function toggleSelect(id: string) {
    setSelected(s => s.includes(id) ? s.filter(x => x !== id) : [...s, id]);
  }
  function selectAll() { setSelected(filtered.map(v => v.id)); }
  function clearSelection() { setSelected([]); }

  // CRUD
  function openCreate() { setForm({ ...emptyForm }); setEditing(null); setShowForm(true); }
  function openEdit(v: Vessel) {
    setForm({
      name: v.name,
      fleetId: v.fleetId ?? '',
      repoUrl: v.repoUrl ?? '',
      defaultBranch: v.defaultBranch || 'main',
      localPath: v.localPath ?? '',
      workingDirectory: v.workingDirectory ?? '',
      projectContext: v.projectContext ?? '',
      styleGuide: v.styleGuide ?? '',
      landingMode: v.landingMode ?? '',
      branchCleanupPolicy: v.branchCleanupPolicy ?? '',
      allowConcurrentMissions: v.allowConcurrentMissions,
      enableModelContext: v.enableModelContext,
      modelContext: v.modelContext ?? '',
      defaultPipelineId: v.defaultPipelineId ?? '',
    });
    setEditing(v);
    setShowForm(true);
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    try {
      const payload: Record<string, unknown> = { ...form };
      if (!payload.localPath) delete payload.localPath;
      if (!payload.workingDirectory) delete payload.workingDirectory;
      if (!payload.projectContext) delete payload.projectContext;
      if (!payload.styleGuide) delete payload.styleGuide;
      if (!payload.landingMode) delete payload.landingMode;
      if (!payload.branchCleanupPolicy) delete payload.branchCleanupPolicy;
      if (!payload.modelContext) delete payload.modelContext;
      if (!payload.defaultPipelineId) delete payload.defaultPipelineId;
      if (editing) await updateVessel(editing.id, payload);
      else await createVessel(payload);
      setShowForm(false);
      pushToast('success', editing
        ? t('Vessel "{{name}}" saved.', { name: form.name })
        : t('Vessel "{{name}}" created.', { name: form.name }));
      load();
    } catch { setError(t('Save failed.')); }
  }

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
      message: t('Delete {{count}} selected vessel(s)? This cannot be undone.', { count: selected.length }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        const ids = [...selected];
        setSelected([]);
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

  return (
    <div>
      <div className="view-header">
        <div>
          <h2>{t('Vessels')}</h2>
          <p className="text-dim view-subtitle">{t('Git repositories registered with Armada')}</p>
        </div>
        <div className="view-actions">
          {selected.length > 0 && (
            <button className="btn btn-sm btn-danger" onClick={handleBulkDelete}>
              {t('Delete Selected')} ({selected.length})
            </button>
          )}
          <button className="btn btn-primary btn-sm" onClick={openCreate}>+ {t('Vessel')}</button>
          <RefreshButton onRefresh={load} title="Refresh vessel data" />
        </div>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />

      {/* Create/Edit Modal */}
      {showForm && (
        <div className="modal-overlay" onClick={() => setShowForm(false)}>
          <form className="modal" style={{ width: '95vw', maxWidth: '95vw', height: '95vh', maxHeight: '95vh', overflowY: 'auto', display: 'flex', flexDirection: 'column' }} onClick={e => e.stopPropagation()} onSubmit={handleSubmit}>
            <h3>{editing ? t('Edit Vessel') : t('Create Vessel')}</h3>

            {/* Row 1: Name + Fleet + Repo URL (3 cols) */}
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 2fr', gap: '0 1.5rem' }}>
              <label>{t('Name')}<input value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} required /></label>
              <label>{t('Fleet')}
                <select value={form.fleetId} onChange={e => setForm({ ...form, fleetId: e.target.value })}>
                  <option value="">{t('Select a fleet...')}</option>
                  {fleets.map(f => <option key={f.id} value={f.id}>{f.name}</option>)}
                </select>
              </label>
              <label>{t('Repository URL')}<input value={form.repoUrl} onChange={e => setForm({ ...form, repoUrl: e.target.value })} required placeholder="https://github.com/org/repo.git" /></label>
            </div>

            {/* Row 2: Default Branch + Local Path + Working Directory (3 cols) */}
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: '0 1.5rem' }}>
              <label>{t('Default Branch')}<input value={form.defaultBranch} onChange={e => setForm({ ...form, defaultBranch: e.target.value })} /></label>
              <label>{t('Local Path')}<input value={form.localPath} onChange={e => setForm({ ...form, localPath: e.target.value })} /></label>
              <label>{t('Working Directory')}<input value={form.workingDirectory} onChange={e => setForm({ ...form, workingDirectory: e.target.value })} /></label>
            </div>

            {/* Row 3: Landing Mode + Branch Cleanup + Pipeline (3 cols) */}
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: '0 1.5rem' }}>
              <label title={t('How completed mission work is integrated.')}>{t('Landing Mode')}
                <select value={form.landingMode} onChange={e => setForm({ ...form, landingMode: e.target.value })}>
                  <option value="">{t('Default')}</option>
                  <option value="LocalMerge">{t('Local Merge')}</option>
                  <option value="PullRequest">{t('Pull Request')}</option>
                  <option value="MergeQueue">Merge Queue</option>
                  <option value="None">{t('None')}</option>
                </select>
              </label>
              <label title={t('When and how mission branches are deleted after successful landing.')}>{t('Branch Cleanup')}
                <select value={form.branchCleanupPolicy} onChange={e => setForm({ ...form, branchCleanupPolicy: e.target.value })}>
                  <option value="">{t('Default')}</option>
                  <option value="LocalOnly">{t('Local Only')}</option>
                  <option value="LocalAndRemote">{t('Local and Remote')}</option>
                  <option value="None">{t('None')}</option>
                </select>
              </label>
              <label>{t('Default Pipeline')}
                <select value={form.defaultPipelineId} onChange={e => setForm({ ...form, defaultPipelineId: e.target.value })}>
                  <option value="">{t('None (WorkerOnly)')}</option>
                  {pipelines.map(p => (
                    <option key={p.id} value={p.id}>{p.name} ({p.stages.map(s => s.personaName).join(' -> ')})</option>
                  ))}
                </select>
              </label>
            </div>

            {/* Checkboxes */}
            <div style={{ display: 'flex', gap: '2rem', marginBottom: '0.5rem' }}>
              <label style={{ display: 'inline-flex', alignItems: 'center', gap: '0.4rem', marginBottom: 0, lineHeight: 1, cursor: 'pointer' }} title={t('When enabled, multiple missions can run on this vessel at the same time.')}>
                <input type="checkbox" checked={form.allowConcurrentMissions} onChange={e => setForm({ ...form, allowConcurrentMissions: e.target.checked })} style={{ width: 'auto', margin: 0, verticalAlign: 'middle' }} />
                <span style={{ verticalAlign: 'middle' }}>{t('Allow Concurrent Missions')}</span>
              </label>
              <label style={{ display: 'inline-flex', alignItems: 'center', gap: '0.4rem', marginBottom: 0, lineHeight: 1, cursor: 'pointer' }} title={t('When enabled, AI agents accumulate key knowledge about this repository during missions.')}>
                <input type="checkbox" checked={form.enableModelContext} onChange={e => setForm({ ...form, enableModelContext: e.target.checked })} style={{ width: 'auto', margin: 0, verticalAlign: 'middle' }} />
                <span style={{ verticalAlign: 'middle' }}>{t('Enable Model Context')}</span>
              </label>
            </div>

            {/* Context textareas always 3 cols -- fills remaining vertical space */}
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: '0 1.5rem', flex: 1, minHeight: 0 }}>
              <label style={{ display: 'flex', flexDirection: 'column' }}>
                {t('Project Context')}
                <textarea value={form.projectContext} onChange={e => setForm({ ...form, projectContext: e.target.value })} style={{ flex: 1, minHeight: '150px', resize: 'none' }} />
              </label>
              <label style={{ display: 'flex', flexDirection: 'column' }}>
                {t('Style Guide')}
                <textarea value={form.styleGuide} onChange={e => setForm({ ...form, styleGuide: e.target.value })} style={{ flex: 1, minHeight: '150px', resize: 'none' }} />
              </label>
              <label style={{ display: 'flex', flexDirection: 'column' }}>
                {t('Model Context')}
                <textarea value={form.modelContext} onChange={e => setForm({ ...form, modelContext: e.target.value })} placeholder={form.enableModelContext ? t('Agent-accumulated context...') : t('Enable Model Context to use')} disabled={!form.enableModelContext} style={{ flex: 1, minHeight: '150px', resize: 'none', ...(form.enableModelContext ? {} : { opacity: 0.4 }) }} />
              </label>
            </div>

            <div className="modal-actions">
              <button type="submit" className="btn btn-primary">{t('Save')}</button>
              <button type="button" className="btn" onClick={() => setShowForm(false)}>{t('Cancel')}</button>
            </div>
          </form>
        </div>
      )}

      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message}
        onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />

      {loading && vessels.length === 0 && <p className="text-dim">{t('Loading...')}</p>}
      {!loading && vessels.length === 0 && <p className="text-dim">{t('No vessels configured.')}</p>}

      {vessels.length > 0 && (
        <>
          <Pagination pageNumber={currentPage} pageSize={pageSize} totalPages={totalPages}
            totalRecords={sorted.length}
            onPageChange={p => setPageNumber(p)} onPageSizeChange={s => { setPageSize(s); setPageNumber(1); }} />

          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th className="col-checkbox">
                    <input type="checkbox" checked={allSelected} onChange={e => e.target.checked ? selectAll() : clearSelection()} title={t('Select all vessels')} />
                  </th>
                  <th className="sortable" onClick={() => handleSort('name')} title={t('Vessel name -- click to sort')}>
                    {t('Name')}{sortIcon('name')}
                  </th>
                  <th>{t('ID')}</th>
                  <th className="sortable" onClick={() => handleSort('fleetId')} title={t('Fleet -- click to sort')}>
                    {t('Fleet')}{sortIcon('fleetId')}
                  </th>
                  <th title={t('Remote git repository URL')}>{t('Repo URL')}</th>
                  <th className="sortable" onClick={() => handleSort('defaultBranch')} title={t('Default branch -- click to sort')}>
                    {t('Branch')}{sortIcon('defaultBranch')}
                  </th>
                  <th title={t('How completed mission work is integrated (LocalMerge, PullRequest, MergeQueue, None)')}>{t('Landing Mode')}</th>
                  <th title={t('Commits ahead and behind the remote default branch')}>{t('Sync')}</th>
                  <th className="text-right">{t('Actions')}</th>
                </tr>
                <tr className="column-filter-row">
                  <td></td>
                  <td><input type="text" className="col-filter" value={colFilters.name} onChange={e => { setColFilters(f => ({ ...f, name: e.target.value })); setPageNumber(1); }} placeholder={t('Search...')} /></td>
                  <td></td>
                  <td>
                    <select className="col-filter" title={t('Filter vessels by fleet')} value={colFilters.fleetId} onChange={e => { setColFilters(f => ({ ...f, fleetId: e.target.value })); setPageNumber(1); }}>
                      <option value="">{t('All Fleets')}</option>
                      {fleets.map(f => <option key={f.id} value={f.id}>{f.name}</option>)}
                    </select>
                  </td>
                  <td><input type="text" className="col-filter" value={colFilters.repoUrl} onChange={e => { setColFilters(f => ({ ...f, repoUrl: e.target.value })); setPageNumber(1); }} placeholder={t('Search...')} /></td>
                  <td></td>
                  <td>
                    <select className="col-filter" title={t('Filter vessels by landing mode')} value={colFilters.landingMode} onChange={e => { setColFilters(f => ({ ...f, landingMode: e.target.value })); setPageNumber(1); }}>
                      <option value="">{t('All Modes')}</option>
                      <option value="LocalMerge">LocalMerge</option>
                      <option value="PullRequest">PullRequest</option>
                      <option value="MergeQueue">MergeQueue</option>
                      <option value="None">None</option>
                    </select>
                  </td>
                  <td></td>
                  <td></td>
                </tr>
              </thead>
              <tbody>
                {paginated.map(v => (
                  <tr key={v.id} className="clickable" onClick={() => openEdit(v)}>
                    <td className="col-checkbox" onClick={e => e.stopPropagation()}>
                      <input type="checkbox" checked={selected.includes(v.id)} onChange={() => toggleSelect(v.id)} title={t('Select this vessel')} />
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
                    </td>
                    <td className="text-dim table-url-cell">
                      <span className="id-display">
                        <span className="url-value" title={v.defaultBranch || 'main'}>{v.defaultBranch || 'main'}</span>
                        <CopyButton text={v.defaultBranch || 'main'} onClick={e => e.stopPropagation()} title="Copy branch" />
                      </span>
                    </td>
                    <td className="text-dim" title={v.landingMode === 'LocalMerge' ? t('Merge into local working directory') : v.landingMode === 'PullRequest' ? t('Push and create pull request') : v.landingMode === 'MergeQueue' ? t('Enqueue for validated merge') : v.landingMode === 'None' ? t('No automatic landing') : t('Uses global setting')}>{v.landingMode || '-'}</td>
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
                    <td className="text-right" onClick={e => e.stopPropagation()}>
                      <ActionMenu id={`vessel-${v.id}`} items={[
                        { label: 'View Detail', onClick: () => navigate(`/vessels/${v.id}`) },
                        { label: 'Edit', onClick: () => openEdit(v) },
                        { label: 'View JSON', onClick: () => setJsonData({ open: true, title: `Vessel: ${v.name}`, data: v }) },
                        { label: 'Delete', danger: true, onClick: () => handleDelete(v.id, v.name) },
                      ]} />
                    </td>
                  </tr>
                ))}
                {paginated.length === 0 && (
                  <tr><td colSpan={9} className="text-dim">{t('No vessels match the current filters.')}</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
}
