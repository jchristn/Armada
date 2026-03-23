import { useEffect, useState, useMemo, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { listDocks, deleteDock, listCaptains, listVessels } from '../api/client';
import type { Dock, Captain, Vessel } from '../types/models';
import Pagination from '../components/shared/Pagination';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import CopyButton from '../components/shared/CopyButton';
import RefreshButton from '../components/shared/RefreshButton';
import ErrorModal from '../components/shared/ErrorModal';

type SortDir = 'asc' | 'desc';
type SortField = 'branchName' | 'active' | 'createdUtc';

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

export default function Docks() {
  const navigate = useNavigate();
  const [docks, setDocks] = useState<Dock[]>([]);
  const [captains, setCaptains] = useState<Captain[]>([]);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Pagination (server-side)
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [totalPages, setTotalPages] = useState(1);
  const [totalRecords, setTotalRecords] = useState(0);

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });

  // Confirm dialog
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

  // Selection
  const [selected, setSelected] = useState<string[]>([]);

  // Sorting
  const [sortField, setSortField] = useState<SortField>('createdUtc');
  const [sortDir, setSortDir] = useState<SortDir>('desc');

  // Column filters
  const [colFilters, setColFilters] = useState({ branchName: '', worktreePath: '' });

  const captainName = useCallback((id: string | null) => {
    if (!id) return '-';
    const c = captains.find(c => c.id === id);
    return c?.name || id.substring(0, 8);
  }, [captains]);

  const vesselName = useCallback((id: string | null) => {
    if (!id) return '-';
    const v = vessels.find(v => v.id === id);
    return v?.name || id.substring(0, 8);
  }, [vessels]);

  const load = useCallback(async () => {
    try {
      setLoading(true);
      const result = await listDocks({ pageNumber, pageSize });
      setDocks(result.objects || []);
      setTotalPages(result.totalPages || 1);
      setTotalRecords(result.totalRecords || 0);
      setSelected([]);
      setError('');
    } catch {
      setError('Failed to load docks.');
    } finally {
      setLoading(false);
    }
  }, [pageNumber, pageSize]);

  useEffect(() => { load(); }, [load]);

  useEffect(() => {
    listCaptains({ pageSize: 1000 }).then(r => setCaptains(r.objects || [])).catch(() => {});
    listVessels({ pageSize: 1000 }).then(r => setVessels(r.objects || [])).catch(() => {});
  }, []);

  // Client-side column filter + sort
  const filtered = useMemo(() => {
    return docks.filter(d =>
      (!colFilters.branchName || (d.branchName ?? '').toLowerCase().includes(colFilters.branchName.toLowerCase())) &&
      (!colFilters.worktreePath || (d.worktreePath ?? '').toLowerCase().includes(colFilters.worktreePath.toLowerCase()))
    );
  }, [docks, colFilters]);

  const sorted = useMemo(() => {
    const arr = [...filtered];
    arr.sort((a, b) => {
      let va: string | number = '';
      let vb: string | number = '';
      switch (sortField) {
        case 'branchName': va = (a.branchName ?? '').toLowerCase(); vb = (b.branchName ?? '').toLowerCase(); break;
        case 'active': va = a.active ? 1 : 0; vb = b.active ? 1 : 0; break;
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
  function selectAll() { setSelected(sorted.map(d => d.id)); }
  function clearSelection() { setSelected([]); }

  // Delete
  function handleDelete(id: string) {
    setConfirm({
      open: true,
      title: 'Delete Dock',
      message: `Delete dock ${id}? This will clean up the git worktree and cannot be undone.`,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try { await deleteDock(id); load(); } catch { setError('Delete failed.'); }
      },
    });
  }

  function handleBulkDelete() {
    setConfirm({
      open: true,
      title: 'Delete Selected Docks',
      message: `Delete ${selected.length} selected dock(s)? This will clean up the git worktrees and cannot be undone.`,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        const ids = [...selected];
        setSelected([]);
        let failed = 0;
        for (const id of ids) {
          try { await deleteDock(id); } catch { failed++; }
        }
        if (failed > 0) setError(`Deleted ${ids.length - failed} docks, ${failed} failed.`);
        load();
      },
    });
  }

  return (
    <div>
      <div className="view-header">
        <div>
          <h2>Docks</h2>
          <p className="text-dim view-subtitle">Git worktrees provisioned for captains. Docks are system-managed and track branch activity.</p>
        </div>
        <div className="view-actions">
          {selected.length > 0 && (
            <button className="btn btn-sm btn-danger" onClick={handleBulkDelete}>
              Delete Selected ({selected.length})
            </button>
          )}
          <RefreshButton onRefresh={load} title="Refresh dock data" />
        </div>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />

      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message}
        onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />

      {loading && docks.length === 0 && <p className="text-dim">Loading...</p>}
      {!loading && docks.length === 0 && <p className="text-dim">No docks found.</p>}

      {docks.length > 0 && (
        <>
          <Pagination pageNumber={pageNumber} pageSize={pageSize} totalPages={totalPages}
            totalRecords={totalRecords}
            onPageChange={p => setPageNumber(p)} onPageSizeChange={s => { setPageSize(s); setPageNumber(1); }} />

          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th className="col-checkbox">
                    <input type="checkbox" checked={allSelected} onChange={e => e.target.checked ? selectAll() : clearSelection()} title="Select all docks" />
                  </th>
                  <th>ID</th>
                  <th>Vessel</th>
                  <th>Captain</th>
                  <th className="sortable" onClick={() => handleSort('branchName')} title="Branch name -- click to sort">
                    Branch{sortIcon('branchName')}
                  </th>
                  <th>Worktree Path</th>
                  <th className="sortable" onClick={() => handleSort('active')} title="Active status -- click to sort">
                    Active{sortIcon('active')}
                  </th>
                  <th className="sortable" onClick={() => handleSort('createdUtc')} title="Created -- click to sort">
                    Created{sortIcon('createdUtc')}
                  </th>
                  <th className="text-right">Actions</th>
                </tr>
                <tr className="column-filter-row">
                  <td></td>
                  <td></td>
                  <td></td>
                  <td></td>
                  <td><input type="text" className="col-filter" value={colFilters.branchName} onChange={e => setColFilters(f => ({ ...f, branchName: e.target.value }))} placeholder="Filter..." /></td>
                  <td><input type="text" className="col-filter" value={colFilters.worktreePath} onChange={e => setColFilters(f => ({ ...f, worktreePath: e.target.value }))} placeholder="Filter..." /></td>
                  <td></td>
                  <td></td>
                  <td></td>
                </tr>
              </thead>
              <tbody>
                {sorted.map(d => (
                  <tr key={d.id} className="clickable" onClick={() => navigate(`/docks/${d.id}`)}>
                    <td className="col-checkbox" onClick={e => e.stopPropagation()}>
                      <input type="checkbox" checked={selected.includes(d.id)} onChange={() => toggleSelect(d.id)} title="Select this dock" />
                    </td>
                    <td className="mono text-dim table-id-cell">
                      <span className="id-display">
                        <span className="id-value" title={d.id}>{d.id}</span>
                        <CopyButton text={d.id} onClick={e => e.stopPropagation()} />
                      </span>
                    </td>
                    <td onClick={e => e.stopPropagation()}>
                      {d.vesselId ? (
                        <a href="#" onClick={e => { e.preventDefault(); navigate(`/vessels/${d.vesselId}`); }}>{vesselName(d.vesselId)}</a>
                      ) : '-'}
                    </td>
                    <td onClick={e => e.stopPropagation()}>
                      {d.captainId ? (
                        <a href="#" onClick={e => { e.preventDefault(); navigate(`/captains/${d.captainId}`); }}>{captainName(d.captainId)}</a>
                      ) : '-'}
                    </td>
                    <td className="mono text-dim table-url-cell">
                      {d.branchName ? (
                        <span className="id-display">
                          <span className="url-value" title={d.branchName}>{d.branchName}</span>
                          <CopyButton text={d.branchName} onClick={e => e.stopPropagation()} title="Copy branch" />
                        </span>
                      ) : '-'}
                    </td>
                    <td className="mono text-dim" style={{ maxWidth: 200, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }} title={d.worktreePath || ''}>
                      {d.worktreePath || '-'}
                    </td>
                    <td>{d.active ? 'Yes' : 'No'}</td>
                    <td className="text-dim">{formatTime(d.createdUtc)}</td>
                    <td className="text-right" onClick={e => e.stopPropagation()}>
                      <ActionMenu id={`dock-${d.id}`} items={[
                        { label: 'View Detail', onClick: () => navigate(`/docks/${d.id}`) },
                        { label: 'View JSON', onClick: () => setJsonData({ open: true, title: `Dock: ${d.id}`, data: d }) },
                        { label: 'Delete', danger: true, onClick: () => handleDelete(d.id) },
                      ]} />
                    </td>
                  </tr>
                ))}
                {sorted.length === 0 && (
                  <tr><td colSpan={9} className="text-dim">No docks match the current filters.</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
}
