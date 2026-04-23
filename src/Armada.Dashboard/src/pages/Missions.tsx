import { useEffect, useState, useMemo, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { useNotifications } from '../context/NotificationContext';
import {
  listMissions, createMission, updateMission, deleteMission, purgeMission,
  restartMission, retryMissionLanding, transitionMission, getMissionDiff, getMissionLog,
  listVessels, listCaptains, listVoyages,
} from '../api/client';
import type { Mission, Vessel, Captain, Voyage } from '../types/models';
import Pagination from '../components/shared/Pagination';
import ActionMenu from '../components/shared/ActionMenu';
import StatusBadge from '../components/shared/StatusBadge';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import DiffViewer from '../components/shared/DiffViewer';
import LogViewer from '../components/shared/LogViewer';
import ErrorModal from '../components/shared/ErrorModal';
import RefreshButton from '../components/shared/RefreshButton';
import CopyButton from '../components/shared/CopyButton';
import { useLocale } from '../context/LocaleContext';

type SortDir = 'asc' | 'desc';
type SortField = 'title' | 'status' | 'priority' | 'createdUtc';

const MISSION_STATUSES = ['Pending', 'Assigned', 'InProgress', 'WorkProduced', 'Testing', 'Review', 'Complete', 'Failed', 'Cancelled'];

export default function Missions() {
  const navigate = useNavigate();
  const { t } = useLocale();
  const [missions, setMissions] = useState<Mission[]>([]);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [captains, setCaptains] = useState<Captain[]>([]);
  const [voyages, setVoyages] = useState<Voyage[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const { pushToast } = useNotifications();

  // Pagination (server-side)
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [totalPages, setTotalPages] = useState(1);
  const [totalRecords, setTotalRecords] = useState(0);

  // Server-side status filter
  const [statusFilter, setStatusFilter] = useState('');

  // Modal
  const [showForm, setShowForm] = useState(false);
  const [formData, setFormData] = useState({ title: '', description: '', vesselId: '', priority: 100 });

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

  const vesselName = useCallback((id: string | null) => (id ? vesselMap.get(id) || id.substring(0, 8) : '-'), [vesselMap]);
  const captainName = useCallback((id: string | null) => (id ? captainMap.get(id) || id.substring(0, 8) : '-'), [captainMap]);

  const load = useCallback(async () => {
    try {
      setLoading(true);
      const filters: Record<string, string> = {};
      if (statusFilter) filters.status = statusFilter;
      const result = await listMissions({ pageNumber, pageSize, filters });
      setMissions(result.objects || []);
      setTotalPages(result.totalPages || 1);
      setTotalRecords(result.totalRecords || 0);
      setError('');
    } catch {
      setError(t('Failed to load missions.'));
    } finally {
      setLoading(false);
    }
  }, [pageNumber, pageSize, statusFilter, t]);

  useEffect(() => {
    listVessels({ pageSize: 1000 }).then(r => setVessels(r.objects || [])).catch(() => {});
    listCaptains({ pageSize: 1000 }).then(r => setCaptains(r.objects || [])).catch(() => {});
    listVoyages({ pageSize: 1000 }).then(r => setVoyages(r.objects || [])).catch(() => {});
  }, []);

  useEffect(() => { load(); }, [load]);

  // Client-side column filter + sort
  const filtered = useMemo(() => {
    return missions.filter(m =>
      (!colFilters.title || m.title.toLowerCase().includes(colFilters.title.toLowerCase())) &&
      (!colFilters.status || (m.status ?? '').toLowerCase().includes(colFilters.status.toLowerCase())) &&
      (!colFilters.branch || (m.branchName ?? '').toLowerCase().includes(colFilters.branch.toLowerCase()))
    );
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

  function sortIcon(field: SortField) {
    if (sortField !== field) return '';
    return sortDir === 'asc' ? ' \u25B2' : ' \u25BC';
  }

  // Selection
  const allSelected = selected.length > 0 && selected.length === sorted.length;
  function toggleSelect(id: string) {
    setSelected(s => s.includes(id) ? s.filter(x => x !== id) : [...s, id]);
  }
  function selectAll() { setSelected(sorted.map(m => m.id)); }
  function clearSelection() { setSelected([]); }

  // Create
  function openCreate() {
    setFormData({ title: '', description: '', vesselId: '', priority: 100 });
    setShowForm(true);
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    try {
      await createMission(formData);
      setShowForm(false);
      pushToast('success', t('Mission "{{title}}" created.', { title: formData.title }));
      load();
    } catch { setError(t('Create failed.')); }
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
        try {
          await purgeMission(id);
          pushToast('warning', t('Mission "{{title}}" purged.', { title }));
          load();
        } catch { setError(t('Purge failed.')); }
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
        for (const id of ids) {
          try { await purgeMission(id); } catch { failed++; }
        }
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

  async function handleRestart(m: Mission) {
    try {
      await restartMission(m.id);
      pushToast('success', t('Mission "{{title}}" restarted.', { title: m.title }));
      load();
    } catch { setError(t('Restart failed.')); }
  }

  async function handleRetryLanding(m: Mission) {
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

  return (
    <div>
      <div className="view-header">
        <div>
          <h2>{t('Missions')}</h2>
          <p className="text-dim view-subtitle">{t('Individual work units assigned to captains')}</p>
        </div>
        <div className="view-actions">
          <select className="filter-select" value={statusFilter} onChange={e => { setStatusFilter(e.target.value); setPageNumber(1); }} title={t('Filter by status')}>
            <option value="">{t('All Statuses')}</option>
            {MISSION_STATUSES.map(s => <option key={s} value={s}>{t(s)}</option>)}
          </select>
          {selected.length > 0 && (
            <button className="btn btn-sm btn-danger" onClick={handleBulkDelete}>
              {t('Delete Selected')} ({selected.length})
            </button>
          )}
          <button className="btn btn-primary btn-sm" onClick={openCreate}>+ {t('Mission')}</button>
          <RefreshButton onRefresh={load} title="Refresh mission data" />
        </div>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />

      {/* Create Modal */}
      {showForm && (
        <div className="modal-overlay" onClick={() => setShowForm(false)}>
          <form className="modal" onClick={e => e.stopPropagation()} onSubmit={handleSubmit}>
            <h3>{t('Create Mission')}</h3>
            <label>{t('Title')}<input value={formData.title} onChange={e => setFormData({ ...formData, title: e.target.value })} required /></label>
            <label>{t('Description')}<textarea value={formData.description} onChange={e => setFormData({ ...formData, description: e.target.value })} rows={3} /></label>
            <label>{t('Vessel')}
              <select value={formData.vesselId} onChange={e => setFormData({ ...formData, vesselId: e.target.value })} required>
                <option value="">{t('Select a vessel...')}</option>
                {vessels.map(v => <option key={v.id} value={v.id}>{v.name}</option>)}
              </select>
            </label>
            <label>{t('Priority')}<input type="number" value={formData.priority} onChange={e => setFormData({ ...formData, priority: Number(e.target.value) })} /></label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary">{t('Create')}</button>
              <button type="button" className="btn" onClick={() => setShowForm(false)}>{t('Cancel')}</button>
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
            <label>{t('New Status')}
              <select value={transitionTarget} onChange={e => setTransitionTarget(e.target.value)}>
                <option value="">{t('Select status...')}</option>
                {MISSION_STATUSES.filter(s => s !== transitionModal.currentStatus).map(s => <option key={s} value={s}>{t(s)}</option>)}
              </select>
            </label>
            <div className="modal-actions">
              <button className="btn btn-primary" onClick={handleTransition} disabled={!transitionTarget}>{t('Transition')}</button>
              <button className="btn" onClick={() => setTransitionModal(null)}>{t('Cancel')}</button>
            </div>
          </div>
        </div>
      )}

      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message}
        onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />
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

      {loading && missions.length === 0 && <p className="text-dim">{t('Loading...')}</p>}
      {!loading && missions.length === 0 && <p className="text-dim">{t('No missions found.')}</p>}

      {missions.length > 0 && (
        <>
          <Pagination pageNumber={pageNumber} pageSize={pageSize} totalPages={totalPages}
            totalRecords={totalRecords}
            onPageChange={p => setPageNumber(p)} onPageSizeChange={s => { setPageSize(s); setPageNumber(1); }} />

          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th className="col-checkbox">
                    <input type="checkbox" checked={allSelected} onChange={e => e.target.checked ? selectAll() : clearSelection()} title={t('Select all missions')} />
                  </th>
                  <th className="sortable" onClick={() => handleSort('title')} title={t('Mission title -- click to sort')}>
                    {t('Title')}{sortIcon('title')}
                  </th>
                  <th>{t('ID')}</th>
                  <th className="sortable" onClick={() => handleSort('status')} title={t('Status -- click to sort')}>
                    {t('Status')}{sortIcon('status')}
                  </th>
                  <th className="sortable" onClick={() => handleSort('priority')} title={t('Priority -- click to sort')}>
                    {t('Priority')}{sortIcon('priority')}
                  </th>
                  <th>{t('Vessel')}</th>
                  <th>{t('Captain')}</th>
                  <th>{t('Voyage')}</th>
                  <th>{t('Branch')}</th>
                  <th className="text-right">{t('Actions')}</th>
                </tr>
                <tr className="column-filter-row">
                  <td></td>
                  <td><input type="text" className="col-filter" value={colFilters.title} onChange={e => setColFilters(f => ({ ...f, title: e.target.value }))} placeholder={t('Search...')} /></td>
                  <td></td>
                  <td><input type="text" className="col-filter" value={colFilters.status} onChange={e => setColFilters(f => ({ ...f, status: e.target.value }))} placeholder={t('Search...')} /></td>
                  <td></td>
                  <td></td>
                  <td></td>
                  <td></td>
                  <td><input type="text" className="col-filter" value={colFilters.branch} onChange={e => setColFilters(f => ({ ...f, branch: e.target.value }))} placeholder={t('Search...')} /></td>
                  <td></td>
                </tr>
              </thead>
              <tbody>
                {sorted.map(m => (
                  <tr key={m.id} className="clickable" onClick={() => navigate(`/missions/${m.id}`)}>
                    <td className="col-checkbox" onClick={e => e.stopPropagation()}>
                      <input type="checkbox" checked={selected.includes(m.id)} onChange={() => toggleSelect(m.id)} title={t('Select this mission')} />
                    </td>
                    <td className="truncate-cell" style={{ maxWidth: 240 }} title={m.title}>
                      <strong className="truncate-text">
                        {m.title}
                      </strong>
                    </td>
                    <td className="mono text-dim table-id-cell">
                      <span className="id-display">
                        <span className="id-value" title={m.id}>{m.id}</span>
                        <CopyButton text={m.id} onClick={e => e.stopPropagation()} />
                      </span>
                    </td>
                    <td>
                      <StatusBadge status={m.status} />
                    </td>
                    <td>{m.priority}</td>
                    <td onClick={e => e.stopPropagation()}>
                      {m.vesselId ? (
                        <a href="#" onClick={e => { e.preventDefault(); navigate(`/vessels/${m.vesselId}`); }}>{vesselName(m.vesselId)}</a>
                      ) : '-'}
                    </td>
                    <td onClick={e => e.stopPropagation()}>
                      {m.captainId ? (
                        <a href="#" onClick={e => { e.preventDefault(); navigate(`/captains/${m.captainId}`); }}>{captainName(m.captainId)}</a>
                      ) : '-'}
                    </td>
                    <td onClick={e => e.stopPropagation()}>
                      {m.voyageId ? (
                          <a href="#" onClick={e => { e.preventDefault(); navigate(`/voyages/${m.voyageId}`); }}>{m.voyageId}</a>
                      ) : '-'}
                    </td>
                    <td className="mono text-dim table-url-cell" title={m.branchName || ''}>
                      {m.branchName ? (
                        <span className="id-display">
                          <span className="url-value">{m.branchName}</span>
                          <CopyButton text={m.branchName} onClick={e => e.stopPropagation()} title="Copy branch" />
                        </span>
                      ) : '-'}
                    </td>
                    <td className="text-right" onClick={e => e.stopPropagation()}>
                      <ActionMenu id={`mission-${m.id}`} items={[
                        { label: 'View Detail', onClick: () => navigate(`/missions/${m.id}`) },
                        { label: 'Edit', onClick: () => navigate(`/missions/${m.id}`) },
                        { label: 'Restart', onClick: () => handleRestart(m) },
                        ...((m.status === 'WorkProduced' || m.status === 'LandingFailed') ? [{ label: 'Retry Landing', onClick: () => handleRetryLanding(m) }] : []),
                        { label: 'View Diff', onClick: () => handleViewDiff(m.id) },
                        { label: 'View Log', onClick: () => handleViewLog(m.id, `Log: ${m.title}`) },
                        { label: 'Transition Status', onClick: () => { setTransitionModal({ missionId: m.id, currentStatus: m.status }); setTransitionTarget(''); } },
                        { label: 'View JSON', onClick: () => setJsonData({ open: true, title: `Mission: ${m.title}`, data: m }) },
                        { label: 'Cancel', danger: true, onClick: () => handleDelete(m.id, m.title) },
                        { label: 'Purge (permanent)', danger: true, onClick: () => handlePurge(m.id, m.title) },
                      ]} />
                    </td>
                  </tr>
                ))}
                {sorted.length === 0 && (
                  <tr><td colSpan={13} className="text-dim">{t('No missions match the current filters.')}</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
}
