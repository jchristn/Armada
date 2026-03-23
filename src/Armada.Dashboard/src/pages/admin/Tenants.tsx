import { useEffect, useState, useMemo, useCallback } from 'react';
import { listTenants, createTenant, updateTenant, deleteTenant } from '../../api/client';
import type { TenantMetadata } from '../../types/models';
import Pagination from '../../components/shared/Pagination';
import ActionMenu from '../../components/shared/ActionMenu';
import ConfirmDialog from '../../components/shared/ConfirmDialog';
import JsonViewer from '../../components/shared/JsonViewer';
import CopyButton from '../../components/shared/CopyButton';
import RefreshButton from '../../components/shared/RefreshButton';
import { useAuth } from '../../context/AuthContext';
import ErrorModal from '../../components/shared/ErrorModal';

function formatTime(utc: string | null): string {
  if (!utc) return '-';
  const d = new Date(utc);
  const diff = Date.now() - d.getTime();
  if (diff < 60000) return 'just now';
  if (diff < 3600000) return `${Math.floor(diff / 60000)}m ago`;
  if (diff < 86400000) return `${Math.floor(diff / 3600000)}h ago`;
  return d.toLocaleDateString();
}

type SortField = 'name' | 'active' | 'createdUtc';
type SortDir = 'asc' | 'desc';

export default function Tenants() {
  const { user, isAdmin } = useAuth();
  const [items, setItems] = useState<TenantMetadata[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState<TenantMetadata | null>(null);
  const [form, setForm] = useState({ name: '', active: true });
  const [selected, setSelected] = useState<string[]>([]);
  const [sortField, setSortField] = useState<SortField>('name');
  const [sortDir, setSortDir] = useState<SortDir>('asc');
  const [colFilters, setColFilters] = useState({ name: '' });
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; resourceName?: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

  const load = useCallback(async () => {
    try {
      setLoading(true);
      if (isAdmin) {
        const result = await listTenants();
        setItems(result.objects);
      } else {
        setItems(user?.tenant ? [user.tenant] : []);
      }
      setError('');
    } catch { setError('Failed to load tenants.'); }
    finally { setLoading(false); }
  }, [isAdmin, user?.tenant]);

  useEffect(() => { load(); }, [load]);

  const filtered = useMemo(() =>
    items.filter(t => !colFilters.name || t.name.toLowerCase().includes(colFilters.name.toLowerCase())),
    [items, colFilters]
  );

  const sorted = useMemo(() => {
    const arr = [...filtered];
    arr.sort((a, b) => {
      let va: string | number | boolean = '', vb: string | number | boolean = '';
      if (sortField === 'active') { va = a.active ? 1 : 0; vb = b.active ? 1 : 0; }
      else if (sortField === 'createdUtc') { va = a.createdUtc; vb = b.createdUtc; }
      else { va = a.name.toLowerCase(); vb = b.name.toLowerCase(); }
      if (va < vb) return sortDir === 'asc' ? -1 : 1;
      if (va > vb) return sortDir === 'asc' ? 1 : -1;
      return 0;
    });
    return arr;
  }, [filtered, sortField, sortDir]);

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
  function sortIcon(field: SortField) { return sortField !== field ? '' : sortDir === 'asc' ? ' \u25B2' : ' \u25BC'; }

  const allSelected = selected.length > 0 && selected.length === filtered.length;
  function toggleSelect(id: string) { setSelected(s => s.includes(id) ? s.filter(x => x !== id) : [...s, id]); }

  function openCreate() { setForm({ name: '', active: true }); setEditing(null); setShowForm(true); }
  function openEdit(t: TenantMetadata) { setForm({ name: t.name, active: t.active }); setEditing(t); setShowForm(true); }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    try {
      if (editing) await updateTenant(editing.id, form);
      else await createTenant(form);
      setShowForm(false);
      load();
    } catch { setError('Save failed.'); }
  }

  function handleDelete(id: string, name: string) {
    setConfirm({
      open: true, title: 'Delete Tenant',
      message: `Delete tenant "${name}"? This is destructive and cannot be undone.`,
      resourceName: name,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try { await deleteTenant(id); load(); } catch { setError('Delete failed.'); }
      },
    });
  }

  function handleBulkDelete() {
    setConfirm({
      open: true, title: 'Delete Selected Tenants',
      message: `Delete ${selected.length} tenant(s)? This cannot be undone.`,
      resourceName: `${selected.length} tenant(s)`,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        const ids = [...selected]; setSelected([]);
        let failed = 0;
        for (const id of ids) { try { await deleteTenant(id); } catch { failed++; } }
        if (failed > 0) setError(`Deleted ${ids.length - failed}, ${failed} failed.`);
        load();
      },
    });
  }

  return (
    <div>
      <div className="view-header">
        <div>
          <h2>Tenants</h2>
          <p className="text-dim view-subtitle">
            {isAdmin
              ? 'Manage tenants in the system. Each tenant is an isolated organizational unit.'
              : 'View your tenant information.'}
          </p>
        </div>
        <div className="view-actions">
          {isAdmin && selected.length > 0 && <button className="btn btn-sm btn-danger" onClick={handleBulkDelete}>Delete Selected ({selected.length})</button>}
          {isAdmin && <button className="btn btn-primary btn-sm" onClick={openCreate}>+ Tenant</button>}
          <RefreshButton onRefresh={load} title="Refresh tenants" />
        </div>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />

      {showForm && (
        <div className="modal-overlay" onClick={() => setShowForm(false)}>
          <form className="modal" onClick={e => e.stopPropagation()} onSubmit={handleSubmit}>
            <h3>{editing ? 'Edit Tenant' : 'Create Tenant'}</h3>
            <label>Name<input value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} required /></label>
            {editing && (
              <label className="checkbox-label"><input type="checkbox" checked={form.active} onChange={e => setForm({ ...form, active: e.target.checked })} /> Active</label>
            )}
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary">Save</button>
              <button type="button" className="btn" onClick={() => setShowForm(false)}>Cancel</button>
            </div>
          </form>
        </div>
      )}

      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message} resourceName={confirm.resourceName} danger requireDeleteConfirm onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />

      {loading && items.length === 0 && <p className="text-dim">Loading...</p>}
      {!loading && items.length === 0 && <p className="text-dim">No tenants found.</p>}

      {items.length > 0 && (
        <>
          <Pagination pageNumber={currentPage} pageSize={pageSize} totalPages={totalPages} totalRecords={sorted.length}
            onPageChange={p => setPageNumber(p)} onPageSizeChange={s => { setPageSize(s); setPageNumber(1); }} />
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th className="col-checkbox"><input type="checkbox" checked={allSelected} onChange={e => e.target.checked ? setSelected(filtered.map(t => t.id)) : setSelected([])} /></th>
                  <th className="sortable" onClick={() => handleSort('name')}>Name{sortIcon('name')}</th>
                  <th>ID</th>
                  <th className="sortable" onClick={() => handleSort('active')}>Active{sortIcon('active')}</th>
                  <th className="sortable" onClick={() => handleSort('createdUtc')}>Created{sortIcon('createdUtc')}</th>
                  <th>Last Updated</th>
                  <th className="text-right">Actions</th>
                </tr>
                <tr className="column-filter-row">
                  <td></td>
                  <td><input type="text" className="col-filter" value={colFilters.name} onChange={e => { setColFilters({ name: e.target.value }); setPageNumber(1); }} placeholder="Filter..." /></td>
                  <td></td><td></td><td></td><td></td><td></td>
                </tr>
              </thead>
              <tbody>
                {paginated.map(t => (
                  <tr
                    key={t.id}
                    className="clickable"
                    onClick={() => isAdmin
                      ? openEdit(t)
                      : setJsonData({ open: true, title: `Tenant: ${t.name}`, data: t })}
                  >
                    <td className="col-checkbox" onClick={e => e.stopPropagation()}><input type="checkbox" checked={selected.includes(t.id)} onChange={() => toggleSelect(t.id)} /></td>
                    <td><strong>{t.name}</strong></td>
                    <td className="mono text-dim table-id-cell">
                      <span className="id-display">
                        <span className="id-value" title={t.id}>{t.id}</span>
                        <CopyButton text={t.id} onClick={e => e.stopPropagation()} />
                      </span>
                    </td>
                    <td>{t.active ? 'Yes' : 'No'}</td>
                    <td className="text-dim" title={new Date(t.createdUtc).toLocaleString()}>{formatTime(t.createdUtc)}</td>
                    <td className="text-dim" title={new Date(t.lastUpdateUtc).toLocaleString()}>{formatTime(t.lastUpdateUtc)}</td>
                    <td className="text-right" onClick={e => e.stopPropagation()}>
                      <ActionMenu id={t.id} items={[
                        ...(isAdmin ? [{ label: 'Edit', onClick: () => openEdit(t) }] : []),
                        { label: 'View JSON', onClick: () => setJsonData({ open: true, title: `Tenant: ${t.name}`, data: t }) },
                        ...(isAdmin ? [{ label: 'Delete', danger: true as const, onClick: () => handleDelete(t.id, t.name) }] : []),
                      ]} />
                    </td>
                  </tr>
                ))}
                {paginated.length === 0 && <tr><td colSpan={7} className="text-dim">No tenants match filters.</td></tr>}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
}
