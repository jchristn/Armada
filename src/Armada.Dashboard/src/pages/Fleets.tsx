import { useEffect, useState, useMemo, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { listFleets, listVessels, createFleet, updateFleet, deleteFleet } from '../api/client';
import type { Fleet, Vessel } from '../types/models';
import Pagination from '../components/shared/Pagination';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import StatusBadge from '../components/shared/StatusBadge';
import CopyButton from '../components/shared/CopyButton';
import RefreshButton from '../components/shared/RefreshButton';
import ErrorModal from '../components/shared/ErrorModal';

type SortDir = 'asc' | 'desc';
type SortField = 'name' | 'createdUtc' | '_vesselCount' | 'description';

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

interface FleetWithCount extends Fleet {
  _vesselCount: number;
  _vessels: Vessel[];
}

export default function Fleets() {
  const navigate = useNavigate();
  const [fleets, setFleets] = useState<FleetWithCount[]>([]);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Modal state
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState<Fleet | null>(null);
  const [form, setForm] = useState({ name: '', description: '' });

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });

  // Confirm dialog
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

  // Selection
  const [selected, setSelected] = useState<string[]>([]);

  // Sorting
  const [sortField, setSortField] = useState<SortField>('name');
  const [sortDir, setSortDir] = useState<SortDir>('asc');

  // Column filters
  const [colFilters, setColFilters] = useState({ name: '', description: '' });

  // Pagination
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(25);

  const load = useCallback(async () => {
    try {
      setLoading(true);
      const [fResult, vResult] = await Promise.all([listFleets({ pageSize: 9999 }), listVessels({ pageSize: 9999 })]);
      const vesselsByFleet = new Map<string, Vessel[]>();
      for (const v of vResult.objects) {
        if (v.fleetId) {
          const list = vesselsByFleet.get(v.fleetId) || [];
          list.push(v);
          vesselsByFleet.set(v.fleetId, list);
        }
      }
      setFleets(fResult.objects.map(f => ({
        ...f,
        _vesselCount: vesselsByFleet.get(f.id)?.length ?? 0,
        _vessels: vesselsByFleet.get(f.id) ?? [],
      })));
      setVessels(vResult.objects);
      setError('');
    } catch {
      setError('Failed to load fleets.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  // Filtered rows
  const filtered = useMemo(() => {
    return fleets.filter(f =>
      (!colFilters.name || f.name.toLowerCase().includes(colFilters.name.toLowerCase())) &&
      (!colFilters.description || (f.description ?? '').toLowerCase().includes(colFilters.description.toLowerCase()))
    );
  }, [fleets, colFilters]);

  // Sorted rows
  const sorted = useMemo(() => {
    const arr = [...filtered];
    arr.sort((a, b) => {
      let va: string | number = '';
      let vb: string | number = '';
      if (sortField === '_vesselCount') { va = a._vesselCount; vb = b._vesselCount; }
      else if (sortField === 'createdUtc') { va = a.createdUtc; vb = b.createdUtc; }
      else if (sortField === 'description') { va = a.description ?? ''; vb = b.description ?? ''; }
      else { va = a.name.toLowerCase(); vb = b.name.toLowerCase(); }
      if (va < vb) return sortDir === 'asc' ? -1 : 1;
      if (va > vb) return sortDir === 'asc' ? 1 : -1;
      return 0;
    });
    return arr;
  }, [filtered, sortField, sortDir]);

  // Paginated
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
  function selectAll() { setSelected(filtered.map(f => f.id)); }
  function clearSelection() { setSelected([]); }

  // CRUD
  function openCreate() { setForm({ name: '', description: '' }); setEditing(null); setShowForm(true); }
  function openEdit(f: Fleet) { setForm({ name: f.name, description: f.description ?? '' }); setEditing(f); setShowForm(true); }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    try {
      if (editing) await updateFleet(editing.id, form);
      else await createFleet(form);
      setShowForm(false);
      load();
    } catch { setError('Save failed.'); }
  }

  function handleDelete(id: string, name: string) {
    setConfirm({
      open: true,
      title: 'Delete Fleet',
      message: `Delete fleet "${name}"? This cannot be undone.`,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try { await deleteFleet(id); load(); } catch { setError('Delete failed.'); }
      },
    });
  }

  function handleBulkDelete() {
    setConfirm({
      open: true,
      title: 'Delete Selected Fleets',
      message: `Delete ${selected.length} selected fleet(s)? This cannot be undone.`,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        const ids = [...selected];
        setSelected([]);
        let failed = 0;
        for (const id of ids) {
          try { await deleteFleet(id); } catch { failed++; }
        }
        if (failed > 0) setError(`Deleted ${ids.length - failed} fleets, ${failed} failed.`);
        load();
      },
    });
  }

  return (
    <div>
      <div className="view-header">
        <div>
          <h2>Fleets</h2>
          <p className="text-dim view-subtitle">Fleets are groups of vessels (repositories) useful for organizing and understanding relationships amongst code assets.</p>
        </div>
        <div className="view-actions">
          {selected.length > 0 && (
            <button className="btn btn-sm btn-danger" onClick={handleBulkDelete}>
              Delete Selected ({selected.length})
            </button>
          )}
          <button className="btn btn-primary btn-sm" onClick={openCreate}>+ Fleet</button>
          <RefreshButton onRefresh={load} title="Refresh fleet data" />
        </div>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />

      {/* Create/Edit Modal */}
      {showForm && (
        <div className="modal-overlay" onClick={() => setShowForm(false)}>
          <form className="modal" onClick={e => e.stopPropagation()} onSubmit={handleSubmit}>
            <h3>{editing ? 'Edit Fleet' : 'Create Fleet'}</h3>
            <label>Name<input value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} required /></label>
            <label>Description<input value={form.description} onChange={e => setForm({ ...form, description: e.target.value })} /></label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary">Save</button>
              <button type="button" className="btn" onClick={() => setShowForm(false)}>Cancel</button>
            </div>
          </form>
        </div>
      )}

      {/* JSON Viewer */}
      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />

      {/* Confirm Dialog */}
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message}
        onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />

      {loading && fleets.length === 0 && <p className="text-dim">Loading...</p>}
      {!loading && fleets.length === 0 && <p className="text-dim">No fleets configured.</p>}

      {fleets.length > 0 && (
        <>
          <Pagination pageNumber={currentPage} pageSize={pageSize} totalPages={totalPages}
            totalRecords={sorted.length}
            onPageChange={p => setPageNumber(p)} onPageSizeChange={s => { setPageSize(s); setPageNumber(1); }} />

          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th className="col-checkbox">
                    <input type="checkbox" checked={allSelected} onChange={e => e.target.checked ? selectAll() : clearSelection()} title="Select all fleets" />
                  </th>
                  <th className="sortable" onClick={() => handleSort('name')} title="Fleet name -- click to sort">
                    Name{sortIcon('name')}
                  </th>
                  <th>ID</th>
                  <th className="sortable" onClick={() => handleSort('description')} title="Description -- click to sort">
                    Description{sortIcon('description')}
                  </th>
                  <th className="sortable" onClick={() => handleSort('_vesselCount')} title="Vessel count -- click to sort">
                    Vessels{sortIcon('_vesselCount')}
                  </th>
                  <th>Active</th>
                  <th className="sortable" onClick={() => handleSort('createdUtc')} title="Created date -- click to sort">
                    Created{sortIcon('createdUtc')}
                  </th>
                  <th className="text-right">Actions</th>
                </tr>
                <tr className="column-filter-row">
                  <td></td>
                  <td><input type="text" className="col-filter" value={colFilters.name} onChange={e => { setColFilters(f => ({ ...f, name: e.target.value })); setPageNumber(1); }} placeholder="Filter..." /></td>
                  <td></td>
                  <td><input type="text" className="col-filter" value={colFilters.description} onChange={e => { setColFilters(f => ({ ...f, description: e.target.value })); setPageNumber(1); }} placeholder="Filter..." /></td>
                  <td></td>
                  <td></td>
                  <td></td>
                  <td></td>
                </tr>
              </thead>
              <tbody>
                {paginated.map(f => (
                  <tr key={f.id} className="clickable" onClick={() => openEdit(f)}>
                    <td className="col-checkbox" onClick={e => e.stopPropagation()}>
                      <input type="checkbox" checked={selected.includes(f.id)} onChange={() => toggleSelect(f.id)} title="Select this fleet" />
                    </td>
                    <td><strong>{f.name}</strong></td>
                    <td className="mono text-dim table-id-cell">
                      <span className="id-display">
                        <span className="id-value" title={f.id}>{f.id}</span>
                        <CopyButton text={f.id} onClick={e => e.stopPropagation()} />
                      </span>
                    </td>
                    <td className="text-dim">{f.description || '-'}</td>
                    <td>{f._vesselCount}</td>
                    <td>{f.active !== false ? 'Yes' : 'No'}</td>
                    <td className="text-dim">{formatTime(f.createdUtc)}</td>
                    <td className="text-right" onClick={e => e.stopPropagation()}>
                      <ActionMenu id={`fleet-${f.id}`} items={[
                        { label: 'View Detail', onClick: () => navigate(`/fleets/${f.id}`) },
                        { label: 'Edit', onClick: () => openEdit(f) },
                        { label: 'View JSON', onClick: () => setJsonData({ open: true, title: `Fleet: ${f.name}`, data: f }) },
                        { label: 'Delete', danger: true, onClick: () => handleDelete(f.id, f.name) },
                      ]} />
                    </td>
                  </tr>
                ))}
                {paginated.length === 0 && (
                  <tr><td colSpan={8} className="text-dim">No fleets match the current filters.</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
}
