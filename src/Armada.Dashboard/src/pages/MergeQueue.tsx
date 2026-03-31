import { useEffect, useState, useMemo, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  listMergeQueue, enqueueMerge, deleteMergeEntry, processMergeEntry,
  processAllMergeQueue, cancelMergeEntry, listVessels,
  getMissionDiff, getMissionLog,
} from '../api/client';
import type { MergeEntry, Vessel } from '../types/models';
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

type SortDir = 'asc' | 'desc';
type SortField = 'branchName' | 'targetBranch' | 'status' | 'priority' | 'createdUtc';

function formatTime(utc: string | null): string {
  if (!utc) return '-';
  const d = new Date(utc);
  const now = new Date();
  const diff = now.getTime() - d.getTime();
  if (diff < 60000) return 'just now';
  if (diff < 3600000) return `${Math.floor(diff / 60000)}m ago`;
  if (diff < 86400000) return `${Math.floor(diff / 3600000)}h ago`;
  return d.toLocaleDateString();
}

export default function MergeQueue() {
  const navigate = useNavigate();
  const [entries, setEntries] = useState<MergeEntry[]>([]);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Pagination (server-side)
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [totalPages, setTotalPages] = useState(1);
  const [totalRecords, setTotalRecords] = useState(0);

  // Enqueue modal
  const [showEnqueue, setShowEnqueue] = useState(false);
  const [enqueueForm, setEnqueueForm] = useState({
    branchName: '', targetBranch: 'main', missionId: '', vesselId: '', testCommand: '', priority: 0,
  });

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });

  // Confirm dialog
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

  // Diff viewer
  const [diffModal, setDiffModal] = useState<{ open: boolean; title: string; rawDiff: string; loading: boolean }>({ open: false, title: '', rawDiff: '', loading: false });

  // Log viewer
  const [logModal, setLogModal] = useState<{ open: boolean; title: string; missionId: string; content: string; totalLines: number; lineCount: number }>({ open: false, title: '', missionId: '', content: '', totalLines: 0, lineCount: 200 });

  // Selection
  const [selected, setSelected] = useState<string[]>([]);

  // Sorting
  const [sortField, setSortField] = useState<SortField>('createdUtc');
  const [sortDir, setSortDir] = useState<SortDir>('desc');

  // Column filters
  const [colFilters, setColFilters] = useState({ branchName: '', targetBranch: '', status: '', vesselId: '' });

  const vesselName = useCallback((id: string | null) => {
    if (!id) return '-';
    const v = vessels.find(v => v.id === id);
    return v?.name || id.substring(0, 8);
  }, [vessels]);

  const load = useCallback(async () => {
    try {
      setLoading(true);
      const result = await listMergeQueue({ pageNumber, pageSize });
      setEntries(result.objects || []);
      setTotalPages(result.totalPages || 1);
      setTotalRecords(result.totalRecords || 0);
      setSelected([]);
      setError('');
    } catch {
      setError('Failed to load merge queue.');
    } finally {
      setLoading(false);
    }
  }, [pageNumber, pageSize]);

  useEffect(() => { load(); }, [load]);

  useEffect(() => {
    listVessels({ pageSize: 1000 }).then(r => setVessels(r.objects || [])).catch(() => {});
  }, []);

  // Client-side column filter + sort
  const filtered = useMemo(() => {
    return entries.filter(e =>
      (!colFilters.branchName || e.branchName.toLowerCase().includes(colFilters.branchName.toLowerCase())) &&
      (!colFilters.targetBranch || e.targetBranch.toLowerCase().includes(colFilters.targetBranch.toLowerCase())) &&
      (!colFilters.status || (e.status ?? '').toLowerCase().includes(colFilters.status.toLowerCase())) &&
      (!colFilters.vesselId || e.vesselId === colFilters.vesselId)
    );
  }, [entries, colFilters]);

  const sorted = useMemo(() => {
    const arr = [...filtered];
    arr.sort((a, b) => {
      let va: string | number = '';
      let vb: string | number = '';
      switch (sortField) {
        case 'branchName': va = a.branchName.toLowerCase(); vb = b.branchName.toLowerCase(); break;
        case 'targetBranch': va = a.targetBranch.toLowerCase(); vb = b.targetBranch.toLowerCase(); break;
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
  function selectAll() { setSelected(sorted.map(e => e.id)); }
  function clearSelection() { setSelected([]); }

  // Enqueue
  async function handleEnqueue(e: React.FormEvent) {
    e.preventDefault();
    try {
      await enqueueMerge({
        branchName: enqueueForm.branchName,
        targetBranch: enqueueForm.targetBranch || 'main',
        missionId: enqueueForm.missionId || undefined,
        vesselId: enqueueForm.vesselId || undefined,
        testCommand: enqueueForm.testCommand || undefined,
        priority: enqueueForm.priority,
      });
      setShowEnqueue(false);
      load();
    } catch { setError('Enqueue failed.'); }
  }

  // Process all
  function handleProcessAll() {
    setConfirm({
      open: true,
      title: 'Process Merge Queue',
      message: 'Process all queued entries in the merge queue now?',
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try { await processAllMergeQueue(); load(); } catch { setError('Process all failed.'); }
      },
    });
  }

  // Process single
  function handleProcess(id: string) {
    setConfirm({
      open: true,
      title: 'Process Entry',
      message: `Process merge entry ${id} now?`,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try { await processMergeEntry(id); load(); } catch { setError('Process failed.'); }
      },
    });
  }

  // Cancel
  function handleCancel(id: string) {
    setConfirm({
      open: true,
      title: 'Cancel Entry',
      message: `Cancel merge entry ${id}?`,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try { await cancelMergeEntry(id); load(); } catch { setError('Cancel failed.'); }
      },
    });
  }

  // Delete
  function handleDelete(id: string) {
    setConfirm({
      open: true,
      title: 'Delete Entry',
      message: `Delete merge entry ${id}? This cannot be undone.`,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try { await deleteMergeEntry(id); load(); } catch { setError('Delete failed.'); }
      },
    });
  }

  function handleBulkDelete() {
    setConfirm({
      open: true,
      title: 'Delete Selected Entries',
      message: `Delete ${selected.length} selected merge queue entries? This cannot be undone.`,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        const ids = [...selected];
        setSelected([]);
        let failed = 0;
        for (const id of ids) {
          try { await deleteMergeEntry(id); } catch { failed++; }
        }
        if (failed > 0) setError(`Deleted ${ids.length - failed} entries, ${failed} failed.`);
        load();
      },
    });
  }

  // Mission diff/log handlers
  async function handleMissionDiff(missionId: string) {
    setDiffModal({ open: true, title: `Diff: Mission ${missionId.substring(0, 8)}...`, rawDiff: '', loading: true });
    try {
      const result = await getMissionDiff(missionId);
      setDiffModal(d => ({ ...d, rawDiff: result?.diff || '', loading: false }));
    } catch {
      setDiffModal(d => ({ ...d, rawDiff: '', loading: false }));
    }
  }

  const fetchLog = useCallback(async (missionId: string, lines: number) => {
    try {
      const result = await getMissionLog(missionId, lines);
      setLogModal(l => ({ ...l, content: result.log || 'No log output', totalLines: result.totalLines || 0 }));
    } catch (e: unknown) {
      setLogModal(l => ({ ...l, content: 'Log unavailable: ' + (e instanceof Error ? e.message : String(e)) }));
    }
  }, []);

  function handleMissionLog(missionId: string) {
    setLogModal({ open: true, title: `Log: Mission ${missionId.substring(0, 8)}...`, missionId, content: 'Loading...', totalLines: 0, lineCount: 200 });
    fetchLog(missionId, 200);
  }

  const handleLogRefresh = useCallback(() => {
    if (logModal.missionId) fetchLog(logModal.missionId, logModal.lineCount);
  }, [logModal.missionId, logModal.lineCount, fetchLog]);

  const handleLogLineCountChange = useCallback((lines: number) => {
    setLogModal(l => ({ ...l, lineCount: lines }));
    if (logModal.missionId) fetchLog(logModal.missionId, lines);
  }, [logModal.missionId, fetchLog]);

  return (
    <div>
      <div className="view-header">
        <div>
          <h2>Merge Queue</h2>
          <p className="text-dim view-subtitle">Completed missions awaiting merge. Review, test, approve, and manage the merge pipeline.</p>
        </div>
        <div className="view-actions">
          {selected.length > 0 && (
            <button className="btn btn-sm btn-danger" onClick={handleBulkDelete}>
              Delete Selected ({selected.length})
            </button>
          )}
          <button className="btn btn-sm" onClick={handleProcessAll} title="Process all queued entries">Process All</button>
          <button className="btn btn-primary btn-sm" onClick={() => {
            setEnqueueForm({ branchName: '', targetBranch: 'main', missionId: '', vesselId: '', testCommand: '', priority: 0 });
            setShowEnqueue(true);
          }}>+ Enqueue</button>
          <RefreshButton onRefresh={load} title="Refresh merge queue" />
        </div>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />

      {/* Enqueue Modal */}
      {showEnqueue && (
        <div className="modal-overlay" onClick={() => setShowEnqueue(false)}>
          <form className="modal" onClick={e => e.stopPropagation()} onSubmit={handleEnqueue}>
            <h3>Enqueue Merge</h3>
            <label>Branch Name<input value={enqueueForm.branchName} onChange={e => setEnqueueForm({ ...enqueueForm, branchName: e.target.value })} required /></label>
            <label>Target Branch<input value={enqueueForm.targetBranch} onChange={e => setEnqueueForm({ ...enqueueForm, targetBranch: e.target.value })} required /></label>
            <label>Mission ID (optional)<input value={enqueueForm.missionId} onChange={e => setEnqueueForm({ ...enqueueForm, missionId: e.target.value })} placeholder="msn_..." /></label>
            <label>Vessel
              <select value={enqueueForm.vesselId} onChange={e => setEnqueueForm({ ...enqueueForm, vesselId: e.target.value })}>
                <option value="">(none)</option>
                {vessels.map(v => <option key={v.id} value={v.id}>{v.name}</option>)}
              </select>
            </label>
            <label>Test Command (optional)<input value={enqueueForm.testCommand} onChange={e => setEnqueueForm({ ...enqueueForm, testCommand: e.target.value })} placeholder="npm test" /></label>
            <label>Priority<input type="number" value={enqueueForm.priority} onChange={e => setEnqueueForm({ ...enqueueForm, priority: Number(e.target.value) })} /></label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary">Enqueue</button>
              <button type="button" className="btn" onClick={() => setShowEnqueue(false)}>Cancel</button>
            </div>
          </form>
        </div>
      )}

      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message}
        onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />
      <DiffViewer
        open={diffModal.open}
        title={diffModal.title}
        rawDiff={diffModal.rawDiff}
        loading={diffModal.loading}
        onClose={() => setDiffModal({ open: false, title: '', rawDiff: '', loading: false })}
      />
      <LogViewer
        open={logModal.open}
        title={logModal.title}
        content={logModal.content}
        totalLines={logModal.totalLines}
        onClose={() => setLogModal({ open: false, title: '', missionId: '', content: '', totalLines: 0, lineCount: 200 })}
        onRefresh={handleLogRefresh}
        onLineCountChange={handleLogLineCountChange}
      />

      {loading && entries.length === 0 && <p className="text-dim">Loading...</p>}
      {!loading && entries.length === 0 && <p className="text-dim">Merge queue is empty.</p>}

      {entries.length > 0 && (
        <>
          <Pagination pageNumber={pageNumber} pageSize={pageSize} totalPages={totalPages}
            totalRecords={totalRecords}
            onPageChange={p => setPageNumber(p)} onPageSizeChange={s => { setPageSize(s); setPageNumber(1); }} />

          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th className="col-checkbox">
                    <input type="checkbox" checked={allSelected} onChange={e => e.target.checked ? selectAll() : clearSelection()} title="Select all entries" />
                  </th>
                  <th>ID</th>
                  <th className="sortable" onClick={() => handleSort('branchName')} title="Branch -- click to sort">
                    Branch{sortIcon('branchName')}
                  </th>
                  <th className="sortable" onClick={() => handleSort('targetBranch')} title="Target branch -- click to sort">
                    Target{sortIcon('targetBranch')}
                  </th>
                  <th className="sortable" onClick={() => handleSort('status')} title="Status -- click to sort">
                    Status{sortIcon('status')}
                  </th>
                  <th className="sortable" onClick={() => handleSort('priority')} title="Priority -- click to sort">
                    Priority{sortIcon('priority')}
                  </th>
                  <th>Mission</th>
                  <th>Vessel</th>
                  <th className="text-right">Actions</th>
                </tr>
                <tr className="column-filter-row">
                  <td></td>
                  <td></td>
                  <td><input type="text" className="col-filter" value={colFilters.branchName} onChange={e => setColFilters(f => ({ ...f, branchName: e.target.value }))} placeholder="Filter..." /></td>
                  <td><input type="text" className="col-filter" value={colFilters.targetBranch} onChange={e => setColFilters(f => ({ ...f, targetBranch: e.target.value }))} placeholder="Filter..." /></td>
                  <td><input type="text" className="col-filter" value={colFilters.status} onChange={e => setColFilters(f => ({ ...f, status: e.target.value }))} placeholder="Filter..." /></td>
                  <td></td>
                  <td></td>
                  <td>
                    <select className="col-filter" title="Filter by vessel" value={colFilters.vesselId} onChange={e => { setColFilters(f => ({ ...f, vesselId: e.target.value })); }}>
                      <option value="">All Vessels</option>
                      {vessels.map(v => <option key={v.id} value={v.id}>{v.name}</option>)}
                    </select>
                  </td>
                  <td></td>
                </tr>
              </thead>
              <tbody>
                {sorted.map(entry => (
                  <tr key={entry.id} className="clickable" onClick={() => navigate(`/merge-queue/${entry.id}`)}>
                    <td className="col-checkbox" onClick={e => e.stopPropagation()}>
                      <input type="checkbox" checked={selected.includes(entry.id)} onChange={() => toggleSelect(entry.id)} title="Select this entry" />
                    </td>
                    <td className="mono text-dim table-id-cell">
                      <span className="id-display">
                        <span className="id-value" title={entry.id}>{entry.id}</span>
                        <CopyButton text={entry.id} onClick={e => e.stopPropagation()} />
                      </span>
                    </td>
                    <td className="mono text-dim table-url-cell">
                      {entry.branchName ? (
                        <span className="id-display">
                          <span className="url-value" title={entry.branchName}>{entry.branchName}</span>
                          <CopyButton text={entry.branchName} onClick={e => e.stopPropagation()} title="Copy branch" />
                        </span>
                      ) : '-'}
                    </td>
                    <td className="mono text-dim table-url-cell">
                      {entry.targetBranch ? (
                        <span className="id-display">
                          <span className="url-value" title={entry.targetBranch}>{entry.targetBranch}</span>
                          <CopyButton text={entry.targetBranch} onClick={e => e.stopPropagation()} title="Copy branch" />
                        </span>
                      ) : '-'}
                    </td>
                    <td><StatusBadge status={entry.status} /></td>
                    <td>{entry.priority}</td>
                    <td onClick={e => e.stopPropagation()}>
                      {entry.missionId ? (
                        <a href="#" onClick={e => { e.preventDefault(); navigate(`/missions/${entry.missionId}`); }}>
                          {entry.missionId}
                        </a>
                      ) : '-'}
                    </td>
                    <td onClick={e => e.stopPropagation()}>
                      {entry.vesselId ? (
                        <a href="#" onClick={e => { e.preventDefault(); navigate(`/vessels/${entry.vesselId}`); }}>
                          {vesselName(entry.vesselId)}
                        </a>
                      ) : '-'}
                    </td>
                    <td className="text-right" onClick={e => e.stopPropagation()}>
                      <ActionMenu id={`merge-${entry.id}`} items={[
                        { label: 'View Detail', onClick: () => navigate(`/merge-queue/${entry.id}`) },
                        { label: 'Process', onClick: () => handleProcess(entry.id) },
                        { label: 'Cancel', onClick: () => handleCancel(entry.id) },
                        ...(entry.missionId ? [
                          { label: 'Mission Diff', onClick: () => handleMissionDiff(entry.missionId!) },
                          { label: 'Mission Log', onClick: () => handleMissionLog(entry.missionId!) },
                        ] : []),
                        { label: 'View JSON', onClick: () => setJsonData({ open: true, title: `Merge Entry: ${entry.id}`, data: entry }) },
                        { label: 'Delete', danger: true as const, onClick: () => handleDelete(entry.id) },
                      ]} />
                    </td>
                  </tr>
                ))}
                {sorted.length === 0 && (
                  <tr><td colSpan={12} className="text-dim">No entries match the current filters.</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
}
