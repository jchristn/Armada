import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { createPlaybook, deletePlaybook, listPlaybooks, updatePlaybook } from '../api/client';
import type { Playbook, ScopeEnum } from '../types/models';
import { useAuth } from '../context/AuthContext';
import { canEdit as canEditScoped, resolveCreateScope, type ScopeViewer } from '../lib/scoping';
import ScopeBadge from '../components/shared/ScopeBadge';
import ScopeSelect from '../components/shared/ScopeSelect';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import ErrorModal from '../components/shared/ErrorModal';
import JsonViewer from '../components/shared/JsonViewer';
import RefreshButton from '../components/shared/RefreshButton';
import AutoRefreshSelect from '../components/shared/AutoRefreshSelect';
import PageHeader from '../components/shared/PageHeader';
import StatusBadge from '../components/shared/StatusBadge';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import { buildPlaybookDuplicatePayload } from '../lib/duplicates';

export default function Playbooks() {
  const navigate = useNavigate();
  const { isAdmin, isTenantAdmin, user } = useAuth();
  const viewer: ScopeViewer = { isAdmin, isTenantAdmin, tenantId: user?.user?.tenantId, userId: user?.user?.id };
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();
  const [playbooks, setPlaybooks] = useState<Playbook[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState<'all' | 'active' | 'inactive'>('all');
  const [colFilters, setColFilters] = useState({ fileName: '', description: '' });
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({
    open: false,
    title: '',
    message: '',
    onConfirm: () => {},
  });

  // Create/Edit modal
  const [showCreate, setShowCreate] = useState(false);
  const [editing, setEditing] = useState<Playbook | null>(null);
  const [saving, setSaving] = useState(false);
  const [createForm, setCreateForm] = useState<{ fileName: string; description: string; content: string; active: boolean; scope: ScopeEnum }>({
    fileName: 'NEW_PLAYBOOK.md',
    description: '',
    content: '# Playbook\n\nDescribe the rules the model must follow.\n',
    active: true,
    scope: resolveCreateScope(viewer),
  });

  // Any authenticated user may create their own playbooks; editing/deleting is gated per-row by ownership.
  const canManage = isAdmin || isTenantAdmin;

  function openCreate() {
    setEditing(null);
    setCreateForm({
      fileName: 'NEW_PLAYBOOK.md',
      description: '',
      content: '# Playbook\n\nDescribe the rules the model must follow.\n',
      active: true,
      scope: resolveCreateScope(viewer),
    });
    setShowCreate(true);
  }

  function openEdit(playbook: Playbook) {
    setEditing(playbook);
    setCreateForm({
      fileName: playbook.fileName,
      description: playbook.description || '',
      content: playbook.content,
      active: playbook.active,
      scope: playbook.scope,
    });
    setShowCreate(true);
  }

  async function handleCreate(event: React.FormEvent) {
    event.preventDefault();
    if (saving) return;
    setSaving(true);
    try {
      const payload = {
        fileName: createForm.fileName,
        description: createForm.description.trim() || null,
        content: createForm.content,
        active: createForm.active,
        scope: createForm.scope,
      };
      if (editing) {
        const updated = await updatePlaybook(editing.id, payload);
        setShowCreate(false);
        pushToast('success', t('Playbook "{{name}}" saved.', { name: updated.fileName }));
      } else {
        const created = await createPlaybook(payload);
        setShowCreate(false);
        pushToast('success', t('Playbook "{{name}}" created.', { name: created.fileName }));
      }
      await load();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Save failed.'));
    } finally {
      setSaving(false);
    }
  }

  async function load() {
    try {
      setLoading(true);
      const result = await listPlaybooks({ pageSize: 9999 });
      setPlaybooks(result.objects || []);
      setError('');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to load playbooks.'));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    load();
  }, []);

  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('playbooks', load);

  const filtered = playbooks.filter((playbook) => {
    const matchesSearch = search.trim().length === 0
      || playbook.fileName.toLowerCase().includes(search.toLowerCase())
      || (playbook.description || '').toLowerCase().includes(search.toLowerCase())
      || playbook.id.toLowerCase().includes(search.toLowerCase());

    const matchesStatus = statusFilter === 'all'
      || (statusFilter === 'active' && playbook.active)
      || (statusFilter === 'inactive' && !playbook.active);

    const matchesColumns = (!colFilters.fileName || (playbook.fileName ?? '').toLowerCase().includes(colFilters.fileName.toLowerCase()))
      && (!colFilters.description || (playbook.description ?? '').toLowerCase().includes(colFilters.description.toLowerCase()));

    return matchesSearch && matchesStatus && matchesColumns;
  });

  const activeCount = playbooks.filter((playbook) => playbook.active).length;
  const inactiveCount = playbooks.length - activeCount;
  const totalChars = playbooks.reduce((total, playbook) => total + playbook.content.length, 0);

  function handleDelete(playbook: Playbook) {
    setConfirm({
      open: true,
      title: t('Delete Playbook'),
      message: t('Delete "{{name}}"? Existing mission snapshots will remain, but this playbook will no longer be selectable.', { name: playbook.fileName }),
      onConfirm: async () => {
        setConfirm((current) => ({ ...current, open: false }));
        try {
          await deletePlaybook(playbook.id);
          pushToast('warning', t('Playbook "{{name}}" deleted.', { name: playbook.fileName }));
          await load();
        } catch (err: unknown) {
          setError(err instanceof Error ? err.message : t('Delete failed.'));
        }
      },
    });
  }

  async function handleDuplicate(playbook: Playbook) {
    try {
      const created = await createPlaybook({ ...buildPlaybookDuplicatePayload(playbook), scope: resolveCreateScope(viewer, playbook.scope) });
      pushToast('success', t('Playbook "{{name}}" duplicated.', { name: created.fileName }));
      navigate(`/playbooks/${created.id}`);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Duplicate failed.'));
    }
  }

  return (
    <div>
      <PageHeader
        title={t('Playbooks')}
        subtitle={t('Tenant-scoped markdown playbooks that can be attached to voyages and missions. Use them for durable engineering rules, architecture standards, or execution checklists.')}
        actions={(
          <>
            <AutoRefreshSelect seconds={refreshSeconds} onChange={setRefreshSeconds} />
            <RefreshButton onRefresh={load} title={t('Refresh playbooks')} />
            <button className="btn btn-primary" onClick={openCreate}>
              + {t('Playbook')}
            </button>
          </>
        )}
      />

      <ErrorModal error={error} onClose={() => setError('')} />
      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <ConfirmDialog
        open={confirm.open}
        title={confirm.title}
        message={confirm.message}
        onConfirm={confirm.onConfirm}
        onCancel={() => setConfirm((current) => ({ ...current, open: false }))}
      />

      {showCreate && (
        <div className="modal-overlay" onClick={() => setShowCreate(false)}>
          <form className="modal modal-large" onClick={(event) => event.stopPropagation()} onSubmit={handleCreate}>
            <h3>{editing ? t('Edit Playbook') : t('Create Playbook')}</h3>
            <label>{t('File Name')}
              <input type="text" value={createForm.fileName} onChange={(event) => setCreateForm({ ...createForm, fileName: event.target.value })} placeholder={t('CSHARP_BACKEND_ARCHITECTURE.md')} required />
            </label>
            <label>{t('Description')}
              <input type="text" value={createForm.description} onChange={(event) => setCreateForm({ ...createForm, description: event.target.value })} placeholder={t('Optional summary shown during playbook selection')} />
            </label>
            <label>{t('Markdown Content')}
              <textarea rows={16} value={createForm.content} onChange={(event) => setCreateForm({ ...createForm, content: event.target.value })} spellCheck={false} />
            </label>
            <ScopeSelect viewer={viewer} value={createForm.scope} onChange={(scope) => setCreateForm({ ...createForm, scope })} />
            <label className="checkbox-row">
              <input type="checkbox" checked={createForm.active} onChange={(event) => setCreateForm({ ...createForm, active: event.target.checked })} />
              <span>{t('Active and selectable during dispatch')}</span>
            </label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary" disabled={saving}>{saving ? t('Saving...') : editing ? t('Save Changes') : t('Create Playbook')}</button>
              <button type="button" className="btn" onClick={() => setShowCreate(false)} disabled={saving}>{t('Cancel')}</button>
            </div>
          </form>
        </div>
      )}

      <div className="playbook-overview-grid">
        <div className="card playbook-overview-card">
          <span>{t('Total Playbooks')}</span>
          <strong>{playbooks.length}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Active')}</span>
          <strong>{activeCount}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Inactive')}</span>
          <strong>{inactiveCount}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Stored Markdown')}</span>
          <strong>{totalChars.toLocaleString()} {t('chars')}</strong>
        </div>
      </div>

      <div className="card" style={{ padding: '1rem', marginBottom: '1rem' }}>
        <div className="playbook-filter-row">
          <input
            type="text"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder={t('Search by filename, description, or ID...')}
          />
          <select value={statusFilter} onChange={(event) => setStatusFilter(event.target.value as 'all' | 'active' | 'inactive')}>
            <option value="all">{t('All statuses')}</option>
            <option value="active">{t('Active only')}</option>
            <option value="inactive">{t('Inactive only')}</option>
          </select>
        </div>
      </div>

      {loading && playbooks.length === 0 ? (
        <p className="text-dim">{t('Loading...')}</p>
      ) : filtered.length === 0 ? (
        <div className="playbook-empty-state">
          <strong>{t('No playbooks match the current filters.')}</strong>
          <span>{canManage ? t('Create a playbook to start standardizing dispatch behavior.') : t('Ask a tenant administrator to create playbooks for shared guidance.')}</span>
        </div>
      ) : (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>{t('File')}</th>
                <th>{t('Description')}</th>
                <th>{t('Visibility')}</th>
                <th>{t('Status')}</th>
                <th>{t('Content')}</th>
                <th>{t('Last Updated')}</th>
                <th className="text-right">{t('Actions')}</th>
              </tr>
              <tr className="column-filter-row">
                <td><input type="text" className="col-filter" value={colFilters.fileName} onChange={e => setColFilters(f => ({ ...f, fileName: e.target.value }))} placeholder={t('Filter...')} /></td>
                <td><input type="text" className="col-filter" value={colFilters.description} onChange={e => setColFilters(f => ({ ...f, description: e.target.value }))} placeholder={t('Filter...')} /></td>
                <td></td>
                <td></td>
                <td></td>
                <td></td>
                <td></td>
              </tr>
            </thead>
            <tbody>
              {filtered.map((playbook) => {
                const canEditRow = canEditScoped(viewer, playbook);
                return (
                <tr key={playbook.id} className="clickable" onClick={() => canEditRow ? openEdit(playbook) : navigate(`/playbooks/${playbook.id}`)}>
                  <td>
                    <strong>{playbook.fileName}</strong>
                    <div className="mono text-dim" style={{ fontSize: '0.78rem' }}>{playbook.id}</div>
                  </td>
                  <td className="text-dim">{playbook.description || '-'}</td>
                  <td>
                    <ScopeBadge scope={playbook.scope} />
                  </td>
                  <td>
                    <StatusBadge status={playbook.active ? 'Active' : 'Inactive'} />
                  </td>
                  <td className="text-dim">
                    {playbook.content.length.toLocaleString()} {t('chars')}
                  </td>
                  <td className="text-dim" title={formatDateTime(playbook.lastUpdateUtc)}>
                    {formatRelativeTime(playbook.lastUpdateUtc)}
                  </td>
                  <td className="text-right" onClick={(event) => event.stopPropagation()}>
                    <ActionMenu
                      id={`playbook-${playbook.id}`}
                      items={[
                        { label: 'Open', onClick: () => navigate(`/playbooks/${playbook.id}`) },
                        ...(canEditRow ? [{ label: 'Edit', onClick: () => openEdit(playbook) }] : []),
                        { label: 'Duplicate', onClick: () => void handleDuplicate(playbook) },
                        { label: 'View JSON', onClick: () => setJsonData({ open: true, title: playbook.fileName, data: playbook }) },
                        ...(canEditRow ? [{ label: 'Delete', danger: true as const, onClick: () => handleDelete(playbook) }] : []),
                      ]}
                    />
                  </td>
                </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
