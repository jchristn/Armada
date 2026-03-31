import { useEffect, useState, useMemo, useCallback } from 'react';
import { listCredentials, createCredential, updateCredential, deleteCredential, listUsers, listTenants } from '../../api/client';
import type { Credential, UserMaster, TenantMetadata } from '../../types/models';
import Pagination from '../../components/shared/Pagination';
import ActionMenu from '../../components/shared/ActionMenu';
import ConfirmDialog from '../../components/shared/ConfirmDialog';
import JsonViewer from '../../components/shared/JsonViewer';
import CopyButton, { copyToClipboard } from '../../components/shared/CopyButton';
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

type SortField = 'name' | 'userId' | 'active' | 'createdUtc';
type SortDir = 'asc' | 'desc';

export default function Credentials() {
  const { user, isAdmin, isTenantAdmin } = useAuth();
  const [items, setItems] = useState<Credential[]>([]);
  const [users, setUsers] = useState<UserMaster[]>([]);
  const [tenants, setTenants] = useState<TenantMetadata[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState<Credential | null>(null);
  const [form, setForm] = useState({ userId: '', tenantId: '', name: '', active: true });
  const [selected, setSelected] = useState<string[]>([]);
  const [sortField, setSortField] = useState<SortField>('name');
  const [sortDir, setSortDir] = useState<SortDir>('asc');
  const [colFilters, setColFilters] = useState({ name: '', userId: '', tenantId: '' });
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; resourceName?: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

  const userName = useCallback((id: string) => {
    const u = users.find(u => u.id === id);
    return u?.email ?? id;
  }, [users]);

  const tenantName = useCallback((id: string) => tenants.find(t => t.id === id)?.name ?? id, [tenants]);

  const load = useCallback(async () => {
    try {
      setLoading(true);
      const tenantPromise = isAdmin
        ? listTenants()
        : Promise.resolve({
            objects: user?.tenant ? [user.tenant] : [],
          } as { objects: TenantMetadata[] });
      const [cResult, uResult, tResult] = await Promise.all([listCredentials(), listUsers(), tenantPromise]);
      setItems(cResult.objects);
      setUsers(uResult.objects);
      setTenants(tResult.objects);
      setError('');
    } catch { setError('Failed to load credentials.'); }
    finally { setLoading(false); }
  }, [isAdmin, user?.tenant]);

  useEffect(() => { load(); }, [load]);

  const filtered = useMemo(() =>
    items.filter(c =>
      (!colFilters.name || (c.name ?? '').toLowerCase().includes(colFilters.name.toLowerCase())) &&
      (!colFilters.userId || c.userId === colFilters.userId) &&
      (!colFilters.tenantId || c.tenantId === colFilters.tenantId)
    ),
    [items, colFilters, userName]
  );

  const sorted = useMemo(() => {
    const arr = [...filtered];
    arr.sort((a, b) => {
      let va: string | number = '', vb: string | number = '';
      if (sortField === 'active') { va = a.active ? 1 : 0; vb = b.active ? 1 : 0; }
      else if (sortField === 'createdUtc') { va = a.createdUtc; vb = b.createdUtc; }
      else if (sortField === 'userId') { va = userName(a.userId).toLowerCase(); vb = userName(b.userId).toLowerCase(); }
      else { va = (a.name ?? '').toLowerCase(); vb = (b.name ?? '').toLowerCase(); }
      if (va < vb) return sortDir === 'asc' ? -1 : 1;
      if (va > vb) return sortDir === 'asc' ? 1 : -1;
      return 0;
    });
    return arr;
  }, [filtered, sortField, sortDir, userName]);

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

  function openCreate() {
    setForm({
      userId: users[0]?.id ?? user?.user?.id ?? '',
      tenantId: tenants[0]?.id ?? user?.tenant?.id ?? '',
      name: '',
      active: true,
    });
    setEditing(null);
    setShowForm(true);
  }

  function openEdit(credential: Credential) {
    setForm({
      userId: credential.userId,
      tenantId: credential.tenantId,
      name: credential.name ?? '',
      active: credential.active,
    });
    setEditing(credential);
    setShowForm(true);
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    try {
      if (editing) {
        await updateCredential(editing.id, {
          id: editing.id,
          userId: editing.userId,
          tenantId: editing.tenantId,
          name: form.name || null,
          active: form.active,
          bearerToken: editing.bearerToken,
        });
      } else {
        await createCredential({
          userId: form.userId,
          tenantId: form.tenantId,
          name: form.name || null,
        });
      }
      setShowForm(false);
      setEditing(null);
      load();
    } catch {
      setError(editing ? 'Update failed.' : 'Create failed.');
    }
  }

  function handleDelete(id: string, name: string) {
    setConfirm({
      open: true, title: 'Delete Credential',
      message: `Delete credential "${name || id}"? This cannot be undone.`,
      resourceName: name || id,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try { await deleteCredential(id); load(); } catch { setError('Delete failed.'); }
      },
    });
  }

  function handleBulkDelete() {
    setConfirm({
      open: true, title: 'Delete Selected Credentials',
      message: `Delete ${selected.length} credential(s)?`,
      resourceName: `${selected.length} credential(s)`,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        const ids = [...selected]; setSelected([]);
        let failed = 0;
        for (const id of ids) { try { await deleteCredential(id); } catch { failed++; } }
        if (failed > 0) setError(`Deleted ${ids.length - failed}, ${failed} failed.`);
        load();
      },
    });
  }

  return (
    <div>
      <div className="view-header">
        <div>
          <h2>Credentials</h2>
          <p className="text-dim view-subtitle">{isAdmin ? 'Manage API bearer tokens across all tenants.' : isTenantAdmin ? 'Manage API bearer tokens within your tenant.' : 'Manage your API bearer tokens.'}</p>
        </div>
        <div className="view-actions">
          {selected.length > 0 && <button className="btn btn-sm btn-danger" onClick={handleBulkDelete}>Delete Selected ({selected.length})</button>}
          <button className="btn btn-primary btn-sm" onClick={openCreate}>+ Credential</button>
          <RefreshButton onRefresh={load} title="Refresh credentials" />
        </div>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />

      {showForm && (
        <div className="modal-overlay" onClick={() => setShowForm(false)}>
          <form className="modal" onClick={e => e.stopPropagation()} onSubmit={handleSubmit}>
            <h3>{editing ? 'Edit Credential' : 'Create Credential'}</h3>
            <label>User
              <select value={form.userId} onChange={e => setForm({ ...form, userId: e.target.value })} required disabled={!!editing || (!isAdmin && !isTenantAdmin)}>
                <option value="">Select user...</option>
                {users.map(u => <option key={u.id} value={u.id}>{u.email}</option>)}
              </select>
            </label>
            {isAdmin ? (
              <label>Tenant
                <select value={form.tenantId} onChange={e => setForm({ ...form, tenantId: e.target.value })} required disabled={!!editing}>
                  <option value="">Select tenant...</option>
                  {tenants.map(t => <option key={t.id} value={t.id}>{t.name}</option>)}
                </select>
              </label>
            ) : (
              <label>Tenant<input value={tenantName(form.tenantId)} disabled /></label>
            )}
            <label>Name (optional)<input value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} placeholder="e.g., CI/CD Token" /></label>
            {editing && (
              <label className="checkbox-label">
                <input type="checkbox" checked={form.active} onChange={e => setForm({ ...form, active: e.target.checked })} />
                Active
              </label>
            )}
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary">{editing ? 'Save' : 'Create'}</button>
              <button type="button" className="btn" onClick={() => setShowForm(false)}>Cancel</button>
            </div>
          </form>
        </div>
      )}

      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message} resourceName={confirm.resourceName} danger requireDeleteConfirm onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />

      {loading && items.length === 0 && <p className="text-dim">Loading...</p>}
      {!loading && items.length === 0 && <p className="text-dim">No credentials found.</p>}

      {items.length > 0 && (
        <>
          <Pagination pageNumber={currentPage} pageSize={pageSize} totalPages={totalPages} totalRecords={sorted.length}
            onPageChange={p => setPageNumber(p)} onPageSizeChange={s => { setPageSize(s); setPageNumber(1); }} />
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th className="col-checkbox"><input type="checkbox" checked={allSelected} onChange={e => e.target.checked ? setSelected(filtered.map(c => c.id)) : setSelected([])} /></th>
                  <th className="sortable" onClick={() => handleSort('name')}>Name{sortIcon('name')}</th>
                  <th>ID</th>
                  <th className="sortable" onClick={() => handleSort('userId')}>User{sortIcon('userId')}</th>
                  <th>Tenant</th>
                  <th>Bearer Token</th>
                  <th className="sortable" onClick={() => handleSort('active')}>Active{sortIcon('active')}</th>
                  <th className="sortable" onClick={() => handleSort('createdUtc')}>Created{sortIcon('createdUtc')}</th>
                  <th className="text-right">Actions</th>
                </tr>
                <tr className="column-filter-row">
                  <td></td>
                  <td><input type="text" className="col-filter" value={colFilters.name} onChange={e => { setColFilters(f => ({ ...f, name: e.target.value })); setPageNumber(1); }} placeholder="Filter..." /></td>
                  <td></td>
                  <td>
                    <select
                      className="col-filter"
                      value={colFilters.userId}
                      onChange={e => { setColFilters(f => ({ ...f, userId: e.target.value })); setPageNumber(1); }}
                    >
                      <option value="">All users</option>
                      {users.map(u => (
                        <option key={u.id} value={u.id}>{u.email}</option>
                      ))}
                    </select>
                  </td>
                  <td>
                    <select
                      className="col-filter"
                      value={colFilters.tenantId}
                      onChange={e => { setColFilters(f => ({ ...f, tenantId: e.target.value })); setPageNumber(1); }}
                    >
                      <option value="">All tenants</option>
                      {tenants.map(t => (
                        <option key={t.id} value={t.id}>{t.name}</option>
                      ))}
                    </select>
                  </td>
                  <td></td><td></td><td></td><td></td>
                </tr>
              </thead>
              <tbody>
                {paginated.map(c => (
                  <tr
                    key={c.id}
                    className="clickable"
                    onClick={() => openEdit(c)}
                  >
                    <td className="col-checkbox" onClick={e => e.stopPropagation()}><input type="checkbox" checked={selected.includes(c.id)} onChange={() => toggleSelect(c.id)} /></td>
                    <td><strong>{c.name || '-'}</strong></td>
                     <td className="mono text-dim table-id-cell">
                       <span className="id-display">
                         <span className="id-value" title={c.id}>{c.id}</span>
                         <CopyButton text={c.id} onClick={e => e.stopPropagation()} />
                       </span>
                     </td>
                    <td className="text-dim">{userName(c.userId)}</td>
                    <td className="text-dim">{tenantName(c.tenantId)}</td>
                     <td className="mono text-dim table-url-cell">
                       <span className="id-display">
                         <span className="url-value" title={c.bearerToken}>{c.bearerToken}</span>
                         <CopyButton text={c.bearerToken} title="Copy token" onClick={e => e.stopPropagation()} />
                       </span>
                     </td>
                    <td>{c.active ? 'Yes' : 'No'}</td>
                    <td className="text-dim" title={new Date(c.createdUtc).toLocaleString()}>{formatTime(c.createdUtc)}</td>
                    <td className="text-right" onClick={e => e.stopPropagation()}>
                      <ActionMenu id={c.id} items={[
                        { label: 'Edit', onClick: () => openEdit(c) },
                        { label: 'Copy Token', onClick: () => { copyToClipboard(c.bearerToken).catch(() => {}); } },
                        { label: 'View JSON', onClick: () => setJsonData({ open: true, title: `Credential: ${c.name || c.id}`, data: c }) },
                        { label: 'Delete', danger: true, onClick: () => handleDelete(c.id, c.name ?? '') },
                      ]} />
                    </td>
                  </tr>
                ))}
                {paginated.length === 0 && <tr><td colSpan={9} className="text-dim">No credentials match filters.</td></tr>}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
}
