import { useEffect, useState, useMemo, useCallback } from 'react';
import { listCredentials, createCredential, updateCredential, deleteCredential, listUsers, listTenants } from '../../api/client';
import type { Credential, UserMaster, TenantMetadata } from '../../types/models';
import DataTable, { type DataTableColumn } from '../../components/shared/DataTable';
import ActionMenu from '../../components/shared/ActionMenu';
import ConfirmDialog from '../../components/shared/ConfirmDialog';
import JsonViewer from '../../components/shared/JsonViewer';
import CopyButton from '../../components/shared/CopyButton';
import { useAutoRefresh } from '../../lib/useAutoRefresh';
import { useAuth } from '../../context/AuthContext';
import ErrorModal from '../../components/shared/ErrorModal';
import { useLocale } from '../../context/LocaleContext';
import { useNotifications } from '../../context/NotificationContext';
import { useProxySessionContext } from '../../lib/useProxySessionContext';

type SortField = 'name' | 'userId' | 'active' | 'createdUtc';
type SortDir = 'asc' | 'desc';

export default function Credentials() {
  const { user, isAdmin, isTenantAdmin } = useAuth();
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();
  const proxyContext = useProxySessionContext();
  const [items, setItems] = useState<Credential[]>([]);
  const [users, setUsers] = useState<UserMaster[]>([]);
  const [tenants, setTenants] = useState<TenantMetadata[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [showForm, setShowForm] = useState(false);
  const [newToken, setNewToken] = useState<string | null>(null);
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
  const remoteProxyMode = Boolean(proxyContext?.selectedInstanceId);

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
    } catch { setError(t('Failed to load credentials.')); }
    finally { setLoading(false); }
  }, [isAdmin, t, user?.tenant]);

  useEffect(() => { load(); }, [load]);
  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('credentials', load);

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

  const allSelected = selected.length > 0 && selected.length === filtered.length;
  function toggleSelect(id: string) { setSelected(s => s.includes(id) ? s.filter(x => x !== id) : [...s, id]); }

  function openCreate() {
    if (remoteProxyMode) return;
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
    if (remoteProxyMode) {
      setJsonData({ open: true, title: `${t('Credential')}: ${credential.name || credential.id}`, data: credential });
      return;
    }
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
        });
      } else {
        const created = await createCredential({
          userId: form.userId,
          tenantId: form.tenantId,
          name: form.name || null,
        });
        setNewToken(created.bearerToken);
      }
      setShowForm(false);
      setEditing(null);
      pushToast('success', editing
        ? t('Credential "{{name}}" saved.', { name: form.name || editing.id })
        : t('Credential created.'));
      load();
    } catch {
      setError(editing ? t('Update failed.') : t('Create failed.'));
    }
  }

  function handleDelete(id: string, name: string) {
    setConfirm({
      open: true, title: t('Delete Credential'),
      message: t('Delete credential "{{name}}"? This cannot be undone.', { name: name || id }),
      resourceName: name || id,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await deleteCredential(id);
          pushToast('warning', t('Credential "{{name}}" deleted.', { name: name || id }));
          load();
        } catch { setError(t('Delete failed.')); }
      },
    });
  }

  function handleBulkDelete() {
    setConfirm({
      open: true, title: t('Delete Selected Credentials'),
      message: t('Delete {{count}} credential(s)?', { count: selected.length }),
      resourceName: `${selected.length} credential(s)`,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        const ids = [...selected]; setSelected([]);
        let failed = 0;
        for (const id of ids) { try { await deleteCredential(id); } catch { failed++; } }
        const success = ids.length - failed;
        if (success > 0) {
          pushToast(failed > 0 ? 'warning' : 'success', failed > 0
            ? t('Deleted {{success}} credentials. {{failed}} failed.', { success, failed })
            : t('Deleted {{success}} credentials.', { success }));
        }
        if (failed > 0) setError(t('Deleted {{success}}, {{failed}} failed.', { success: ids.length - failed, failed }));
        load();
      },
    });
  }

  const columns: DataTableColumn<Credential>[] = [
    {
      key: 'name', label: t('Name'), required: true, sortKey: 'name',
      filter: <input type="text" className="col-filter" aria-label={t('Filter by name')} value={colFilters.name} onChange={e => { setColFilters(f => ({ ...f, name: e.target.value })); setPageNumber(1); }} placeholder={t('Search...')} />,
      render: (c) => <strong>{c.name || '-'}</strong>,
    },
    {
      key: 'id', label: t('ID'), required: true, cellClassName: 'mono text-dim table-id-cell',
      render: (c) => (
        <span className="id-display">
          <span className="id-value" title={c.id}>{c.id}</span>
          <CopyButton text={c.id} onClick={e => e.stopPropagation()} />
        </span>
      ),
    },
    {
      key: 'user', label: t('User'), sortKey: 'userId', cellClassName: 'text-dim',
      clearFilter: () => setColFilters(f => ({ ...f, userId: '' })),
      filter: (
        <select aria-label={t('All users')}
          className="col-filter"
          value={colFilters.userId}
          onChange={e => { setColFilters(f => ({ ...f, userId: e.target.value })); setPageNumber(1); }}
        >
          <option value="">{t('All users')}</option>
          {users.map(u => (
            <option key={u.id} value={u.id}>{u.email}</option>
          ))}
        </select>
      ),
      render: (c) => userName(c.userId),
    },
    {
      key: 'tenant', label: t('Tenant'), cellClassName: 'text-dim',
      clearFilter: () => setColFilters(f => ({ ...f, tenantId: '' })),
      filter: (
        <select aria-label={t('All tenants')}
          className="col-filter"
          value={colFilters.tenantId}
          onChange={e => { setColFilters(f => ({ ...f, tenantId: e.target.value })); setPageNumber(1); }}
        >
          <option value="">{t('All tenants')}</option>
          {tenants.map(tn => (
            <option key={tn.id} value={tn.id}>{tn.name}</option>
          ))}
        </select>
      ),
      render: (c) => tenantName(c.tenantId),
    },
    {
      key: 'bearerToken', label: t('Bearer Token'), cellClassName: 'mono text-dim table-url-cell',
      render: (c) => <span className="url-value" title={t('Tokens are shown once, when the credential is created.')}>{c.bearerToken}</span>,
    },
    { key: 'active', label: t('Active'), sortKey: 'active', render: (c) => (c.active ? t('Yes') : t('No')) },
    {
      key: 'createdUtc', label: t('Created'), sortKey: 'createdUtc', cellClassName: 'text-dim cell-nowrap',
      cellTitle: (c) => formatDateTime(c.createdUtc), render: (c) => formatRelativeTime(c.createdUtc),
    },
    {
      key: 'actions', label: t('Actions'), fixed: true, interactive: true, className: 'text-right',
      render: (c) => (
        <ActionMenu id={c.id} items={[
          ...(remoteProxyMode ? [] : [{ label: 'Edit', onClick: () => openEdit(c) }]),
          { label: 'View JSON', onClick: () => setJsonData({ open: true, title: `${t('Credential')}: ${c.name || c.id}`, data: c }) },
          ...(remoteProxyMode ? [] : [{ label: 'Delete', danger: true, onClick: () => handleDelete(c.id, c.name ?? '') }]),
        ]} />
      ),
    },
  ];

  return (
    <div>
      <div className="view-header">
        <div>
          <h2>{t('Credentials')}</h2>
          <p className="text-dim view-subtitle">{isAdmin ? t('Manage API bearer tokens across all tenants.') : isTenantAdmin ? t('Manage API bearer tokens within your tenant.') : t('Manage your API bearer tokens.')}</p>
        </div>
        <div className="view-actions">
          {!remoteProxyMode && selected.length > 0 && <button className="btn btn-sm btn-danger" onClick={handleBulkDelete}>{t('Delete Selected')} ({selected.length})</button>}
          {!remoteProxyMode && <button className="btn btn-primary btn-sm" onClick={openCreate}>+ {t('Credential')}</button>}
        </div>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />
      {remoteProxyMode && (
        <div className="alert alert-warning" style={{ marginBottom: '1rem' }}>
          {t(
            'This page is connected through Armada.Proxy for {{instanceId}}. Credential create, edit, and delete actions are blocked in remote mode.',
            { instanceId: proxyContext?.selectedInstanceId ?? t('the selected deployment') },
          )}
        </div>
      )}

      {newToken && (
        <div className="modal-overlay" onClick={() => setNewToken(null)}>
          <div className="modal" role="dialog" aria-modal="true" onClick={e => e.stopPropagation()}>
            <h3>{t('Credential created')}</h3>
            <p>{t('Copy this bearer token now. It is shown only once; later reads show it masked.')}</p>
            <div className="id-display mono">
              <span className="url-value">{newToken}</span>
              <CopyButton text={newToken} title={t('Copy token')} />
            </div>
            <div className="modal-actions">
              <button type="button" className="btn btn-primary" onClick={() => setNewToken(null)}>{t('Done')}</button>
            </div>
          </div>
        </div>
      )}

      {showForm && (
        <div className="modal-overlay" onClick={() => setShowForm(false)}>
          <form className="modal" onClick={e => e.stopPropagation()} onSubmit={handleSubmit}>
            <h3>{editing ? t('Edit Credential') : t('Create Credential')}</h3>
            <label>{t('User')}
              <select value={form.userId} onChange={e => setForm({ ...form, userId: e.target.value })} required disabled={!!editing || (!isAdmin && !isTenantAdmin)}>
                <option value="">{t('Select user...')}</option>
                {users.map(u => <option key={u.id} value={u.id}>{u.email}</option>)}
              </select>
            </label>
            {isAdmin ? (
              <label>{t('Tenant')}
                <select value={form.tenantId} onChange={e => setForm({ ...form, tenantId: e.target.value })} required disabled={!!editing}>
                  <option value="">{t('Select tenant...')}</option>
                  {tenants.map(t => <option key={t.id} value={t.id}>{t.name}</option>)}
                </select>
              </label>
            ) : (
              <label>{t('Tenant')}<input value={tenantName(form.tenantId)} disabled /></label>
            )}
            <label>{t('Name (optional)')}<input value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} placeholder={t('e.g., CI/CD Token')} /></label>
            {editing && (
              <label className="checkbox-label">
                <input type="checkbox" checked={form.active} onChange={e => setForm({ ...form, active: e.target.checked })} />
                {t('Active')}
              </label>
            )}
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary">{editing ? t('Save') : t('Create')}</button>
              <button type="button" className="btn" onClick={() => setShowForm(false)}>{t('Cancel')}</button>
            </div>
          </form>
        </div>
      )}

      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message} resourceName={confirm.resourceName} danger requireDeleteConfirm onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />

      <DataTable
        tableKey="credentials"
        columns={columns}
        rows={paginated}
        rowKey={(c) => c.id}
        onRowClick={(c) => (remoteProxyMode
          ? setJsonData({ open: true, title: `${t('Credential')}: ${c.name || c.id}`, data: c })
          : openEdit(c))}
        sort={{ field: sortField, dir: sortDir, onSort: (f) => handleSort(f as SortField) }}
        pagination={{
          pageNumber: currentPage, pageSize, totalPages, totalRecords: sorted.length,
          onPageChange: (p) => setPageNumber(p),
          onPageSizeChange: (s) => { setPageSize(s); setPageNumber(1); },
        }}
        autoRefresh={{ seconds: refreshSeconds, onChange: setRefreshSeconds }}
        onRefresh={load}
        refreshTitle="Refresh credentials"
        selection={{
          isSelected: (c) => selected.includes(c.id),
          onToggle: (c) => toggleSelect(c.id),
          allSelected,
          onToggleAll: (checked) => (checked ? setSelected(filtered.map(c => c.id)) : setSelected([])),
          selectAllLabel: t('Select all credentials'),
          rowLabel: () => t('Select this credential'),
        }}
        emptyMessage={t('No credentials match filters.')}
        placeholder={items.length > 0 ? undefined : loading
          ? <p className="text-dim">{t('Loading...')}</p>
          : <p className="text-dim">{t('No credentials found.')}</p>}
      />
    </div>
  );
}
