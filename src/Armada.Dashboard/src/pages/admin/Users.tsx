import { useEffect, useState, useMemo, useCallback } from 'react';
import { listUsers, createUser, updateUser, deleteUser, listTenants } from '../../api/client';
import type { UserMaster, TenantMetadata, UserUpsertRequest } from '../../types/models';
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

type SortField = 'email' | 'firstName' | 'isAdmin' | 'active' | 'createdUtc';
type SortDir = 'asc' | 'desc';

export default function Users() {
  const { user, isAdmin, isTenantAdmin } = useAuth();
  const [items, setItems] = useState<UserMaster[]>([]);
  const [tenants, setTenants] = useState<TenantMetadata[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState<UserMaster | null>(null);
  const [form, setForm] = useState({
    email: '',
    firstName: '',
    lastName: '',
    password: '',
    confirmPassword: '',
    isAdmin: false,
    isTenantAdmin: false,
    tenantId: '',
    active: true,
  });
  const [selected, setSelected] = useState<string[]>([]);
  const [sortField, setSortField] = useState<SortField>('email');
  const [sortDir, setSortDir] = useState<SortDir>('asc');
  const [colFilters, setColFilters] = useState({ email: '', firstName: '', tenantId: '' });
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; resourceName?: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

  const tenantName = useCallback((id: string) => tenants.find(t => t.id === id)?.name ?? id, [tenants]);

  const load = useCallback(async () => {
    try {
      setLoading(true);
      const userPromise = listUsers();
      const tenantPromise = isAdmin
        ? listTenants()
        : Promise.resolve({
            objects: user?.tenant ? [user.tenant] : [],
          } as { objects: TenantMetadata[] });
      const [uResult, tResult] = await Promise.all([userPromise, tenantPromise]);
      setItems(uResult.objects);
      setTenants(tResult.objects);
      setError('');
    } catch { setError('Failed to load users.'); }
    finally { setLoading(false); }
  }, [isAdmin, user?.tenant]);

  useEffect(() => { load(); }, [load]);

  const filtered = useMemo(() =>
    items.filter(u =>
      (!colFilters.email || u.email.toLowerCase().includes(colFilters.email.toLowerCase())) &&
      (!colFilters.firstName || `${u.firstName ?? ''} ${u.lastName ?? ''}`.toLowerCase().includes(colFilters.firstName.toLowerCase())) &&
      (!colFilters.tenantId || u.tenantId === colFilters.tenantId)
    ),
    [items, colFilters, tenantName]
  );

  const sorted = useMemo(() => {
    const arr = [...filtered];
    arr.sort((a, b) => {
      let va: string | number = '', vb: string | number = '';
      if (sortField === 'isAdmin') { va = a.isAdmin ? 1 : 0; vb = b.isAdmin ? 1 : 0; }
      else if (sortField === 'active') { va = a.active ? 1 : 0; vb = b.active ? 1 : 0; }
      else if (sortField === 'createdUtc') { va = a.createdUtc; vb = b.createdUtc; }
      else if (sortField === 'firstName') { va = `${a.firstName ?? ''} ${a.lastName ?? ''}`.toLowerCase(); vb = `${b.firstName ?? ''} ${b.lastName ?? ''}`.toLowerCase(); }
      else { va = a.email.toLowerCase(); vb = b.email.toLowerCase(); }
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

  function openCreate() {
    setForm({
      email: '',
      firstName: '',
      lastName: '',
      password: '',
      confirmPassword: '',
      isAdmin: false,
      isTenantAdmin: false,
      tenantId: tenants[0]?.id ?? user?.tenant?.id ?? '',
      active: true,
    });
    setEditing(null);
    setShowForm(true);
  }
  function openEdit(u: UserMaster) {
    setForm({
      email: u.email,
      firstName: u.firstName ?? '',
      lastName: u.lastName ?? '',
      password: '',
      confirmPassword: '',
      isAdmin: u.isAdmin,
      isTenantAdmin: u.isTenantAdmin,
      tenantId: u.tenantId,
      active: u.active,
    });
    setEditing(u);
    setShowForm(true);
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    try {
      if (!editing && !form.password.trim()) {
        setError('Password is required when creating a user.');
        return;
      }
      if (form.password !== form.confirmPassword) {
        setError('Passwords do not match.');
        return;
      }

      const payload: UserUpsertRequest = {
        email: form.email,
        firstName: form.firstName || null,
        lastName: form.lastName || null,
        tenantId: form.tenantId,
        isAdmin: form.isAdmin,
        isTenantAdmin: form.isTenantAdmin,
        active: form.active,
        ...(form.password.trim() ? { password: form.password } : {}),
      };

      if (editing) await updateUser(editing.id, payload);
      else await createUser(payload);
      setShowForm(false);
      setError('');
      load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Save failed.');
    }
  }

  function handleDelete(id: string, email: string) {
    setConfirm({
      open: true, title: 'Delete User',
      message: `Delete user "${email}"? This cannot be undone.`,
      resourceName: email,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try { await deleteUser(id); load(); } catch { setError('Delete failed.'); }
      },
    });
  }

  function handleBulkDelete() {
    setConfirm({
      open: true, title: 'Delete Selected Users',
      message: `Delete ${selected.length} user(s)?`,
      resourceName: `${selected.length} user(s)`,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        const ids = [...selected]; setSelected([]);
        let failed = 0;
        for (const id of ids) { try { await deleteUser(id); } catch { failed++; } }
        if (failed > 0) setError(`Deleted ${ids.length - failed}, ${failed} failed.`);
        load();
      },
    });
  }

  return (
    <div>
      <div className="view-header">
        <div>
          <h2>Users</h2>
          <p className="text-dim view-subtitle">
            {isAdmin
              ? 'Manage user accounts across all tenants.'
              : isTenantAdmin
                ? 'Manage user accounts within your tenant.'
                : 'View and update your own user account.'}
          </p>
        </div>
        <div className="view-actions">
          {(isAdmin || isTenantAdmin) && selected.length > 0 && <button className="btn btn-sm btn-danger" onClick={handleBulkDelete}>Delete Selected ({selected.length})</button>}
          {(isAdmin || isTenantAdmin) && <button className="btn btn-primary btn-sm" onClick={openCreate}>+ User</button>}
          <RefreshButton onRefresh={load} title="Refresh users" />
        </div>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />

      {showForm && (
        <div className="modal-overlay" onClick={() => setShowForm(false)}>
          <form className="modal" onClick={e => e.stopPropagation()} onSubmit={handleSubmit}>
            <h3>{editing ? 'Edit User' : 'Create User'}</h3>
            <label>Email<input type="email" value={form.email} onChange={e => setForm({ ...form, email: e.target.value })} required /></label>
            <label>First Name<input value={form.firstName} onChange={e => setForm({ ...form, firstName: e.target.value })} /></label>
            <label>Last Name<input value={form.lastName} onChange={e => setForm({ ...form, lastName: e.target.value })} /></label>
            <label>
              {editing ? 'New Password' : 'Password'}
              <input
                type="password"
                value={form.password}
                onChange={e => setForm({ ...form, password: e.target.value })}
                required={!editing}
                placeholder={editing ? 'Leave blank to keep current password' : 'Enter password'}
              />
            </label>
            <label>
              {editing ? 'Confirm New Password' : 'Confirm Password'}
              <input
                type="password"
                value={form.confirmPassword}
                onChange={e => setForm({ ...form, confirmPassword: e.target.value })}
                required={!editing || !!form.password}
                placeholder={editing ? 'Repeat new password' : 'Repeat password'}
              />
            </label>
            <label>Tenant
              <select
                value={form.tenantId}
                onChange={e => setForm({ ...form, tenantId: e.target.value })}
                required
                disabled={!isAdmin}
              >
                {isAdmin && <option value="">Select tenant...</option>}
                {(isAdmin ? tenants : tenants.filter(t => t.id === form.tenantId || t.id === user?.tenant?.id)).map(t => (
                  <option key={t.id} value={t.id}>{t.name}</option>
                ))}
              </select>
            </label>
            {isAdmin && <label className="checkbox-label"><input type="checkbox" checked={form.isAdmin} onChange={e => setForm({ ...form, isAdmin: e.target.checked })} /> Global Admin</label>}
            {isTenantAdmin && <label className="checkbox-label"><input type="checkbox" checked={form.isTenantAdmin} onChange={e => setForm({ ...form, isTenantAdmin: e.target.checked })} /> Tenant Admin</label>}
            {editing && <label className="checkbox-label"><input type="checkbox" checked={form.active} onChange={e => setForm({ ...form, active: e.target.checked })} /> Active</label>}
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
      {!loading && items.length === 0 && <p className="text-dim">No users found.</p>}

      {items.length > 0 && (
        <>
          <Pagination pageNumber={currentPage} pageSize={pageSize} totalPages={totalPages} totalRecords={sorted.length}
            onPageChange={p => setPageNumber(p)} onPageSizeChange={s => { setPageSize(s); setPageNumber(1); }} />
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th className="col-checkbox"><input type="checkbox" checked={allSelected} onChange={e => e.target.checked ? setSelected(filtered.map(u => u.id)) : setSelected([])} /></th>
                  <th className="sortable" onClick={() => handleSort('email')}>Email{sortIcon('email')}</th>
                  <th>ID</th>
                  <th className="sortable" onClick={() => handleSort('firstName')}>Name{sortIcon('firstName')}</th>
                  <th>Tenant</th>
                  <th className="sortable" onClick={() => handleSort('isAdmin')}>Global Admin{sortIcon('isAdmin')}</th>
                  <th>Tenant Admin</th>
                  <th className="sortable" onClick={() => handleSort('active')}>Active{sortIcon('active')}</th>
                  <th className="sortable" onClick={() => handleSort('createdUtc')}>Created{sortIcon('createdUtc')}</th>
                  <th className="text-right">Actions</th>
                </tr>
                <tr className="column-filter-row">
                  <td></td>
                  <td><input type="text" className="col-filter" value={colFilters.email} onChange={e => { setColFilters(f => ({ ...f, email: e.target.value })); setPageNumber(1); }} placeholder="Filter..." /></td>
                  <td></td>
                  <td><input type="text" className="col-filter" value={colFilters.firstName} onChange={e => { setColFilters(f => ({ ...f, firstName: e.target.value })); setPageNumber(1); }} placeholder="Filter..." /></td>
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
                  <td></td><td></td><td></td><td></td><td></td>
                </tr>
              </thead>
              <tbody>
                {paginated.map(u => (
                  <tr key={u.id} className="clickable" onClick={() => openEdit(u)}>
                    <td className="col-checkbox" onClick={e => e.stopPropagation()}><input type="checkbox" checked={selected.includes(u.id)} onChange={() => toggleSelect(u.id)} /></td>
                    <td><strong>{u.email}</strong></td>
                    <td className="mono text-dim table-id-cell">
                      <span className="id-display">
                        <span className="id-value" title={u.id}>{u.id}</span>
                        <CopyButton text={u.id} onClick={e => e.stopPropagation()} />
                      </span>
                    </td>
                    <td>{[u.firstName, u.lastName].filter(Boolean).join(' ') || '-'}</td>
                    <td className="text-dim">{tenantName(u.tenantId)}</td>
                    <td>{u.isAdmin ? 'Yes' : 'No'}</td>
                    <td>{u.isTenantAdmin ? 'Yes' : 'No'}</td>
                    <td>{u.active ? 'Yes' : 'No'}</td>
                    <td className="text-dim" title={new Date(u.createdUtc).toLocaleString()}>{formatTime(u.createdUtc)}</td>
                    <td className="text-right" onClick={e => e.stopPropagation()}>
                      <ActionMenu id={u.id} items={[
                        { label: 'Edit', onClick: () => openEdit(u) },
                        { label: 'View JSON', onClick: () => setJsonData({ open: true, title: `User: ${u.email}`, data: u }) },
                        ...((isAdmin || isTenantAdmin) ? [{ label: 'Delete', danger: true as const, onClick: () => handleDelete(u.id, u.email) }] : []),
                      ]} />
                    </td>
                  </tr>
                ))}
                {paginated.length === 0 && <tr><td colSpan={10} className="text-dim">No users match filters.</td></tr>}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
}
