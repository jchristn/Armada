import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { createPlaybook, deletePlaybook, listPlaybooks, updatePlaybook } from '../api/client';
import { listAll } from '../api/paging';
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
import CopyButton from '../components/shared/CopyButton';
import DataTable, { type DataTableColumn } from '../components/shared/DataTable';
import PageHeader from '../components/shared/PageHeader';
import StatusBadge from '../components/shared/StatusBadge';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import { buildPlaybookDuplicatePayload } from '../lib/duplicates';
import { useDeleteProgress } from '../lib/useDeleteProgress';

const PLAYBOOK_FILE_TIP = 'File name for the playbook, which must end in .md, for example CSHARP_BACKEND_ARCHITECTURE.md. Captains see this name when the playbook is attached as a file. Required.';
const PLAYBOOK_DESCRIPTION_TIP = 'Optional one-line summary shown when people pick playbooks during dispatch. It is not sent to the captain.';
const PLAYBOOK_CONTENT_TIP = 'The guidance itself, in markdown: rules, standards or checklists the captain should follow. Depending on how it is attached, it is pasted into the prompt or given as a file.';
const PLAYBOOK_ACTIVE_TIP = 'On: the playbook can be selected during dispatch. Off: it is hidden from selection and rejected if requested; missions that already used it are not affected.';

export default function Playbooks() {
  const navigate = useNavigate();
  const { isAdmin, isTenantAdmin, user } = useAuth();
  const viewer: ScopeViewer = { isAdmin, isTenantAdmin, tenantId: user?.user?.tenantId, userId: user?.user?.id };
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const deletion = useDeleteProgress();
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
      const result = await listAll(listPlaybooks);
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
        if (!(await deletion.run({ kind: 'playbook', action: () => deletePlaybook(playbook.id), errorMessage: (err) => err instanceof Error ? err.message : t('Delete failed.') }))) return;
        pushToast('warning', t('Playbook "{{name}}" deleted.', { name: playbook.fileName }));
        await load();
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

  const emptyState = (
    <div className="playbook-empty-state">
      <strong>{t('No playbooks match the current filters.')}</strong>
      <span>{canManage ? t('Create a playbook to start standardizing dispatch behavior.') : t('Ask a tenant administrator to create playbooks for shared guidance.')}</span>
    </div>
  );

  const columns: DataTableColumn<Playbook>[] = [
    {
      key: 'id', label: t('ID'), required: true, headerTitle: t('Unique playbook ID, used when selecting playbooks through the API or MCP tools. Use the copy button to copy it.'), cellClassName: 'mono text-dim table-id-cell',
      render: (playbook) => (
        <span className="id-display">
          <span className="id-value" title={playbook.id}>{playbook.id}</span>
          <CopyButton text={playbook.id} onClick={e => e.stopPropagation()} />
        </span>
      ),
    },
    {
      key: 'fileName', label: t('File'), required: true, headerTitle: t('The playbook\'s file name (ends in .md). When a playbook is attached as a file, the captain reads it under this name.'),
      clearFilter: () => setColFilters(f => ({ ...f, fileName: '' })),
      filter: <input type="text" className="col-filter" aria-label={t('File')} title={t('Show only playbooks whose file name contains this text.')} value={colFilters.fileName} onChange={e => setColFilters(f => ({ ...f, fileName: e.target.value }))} placeholder={t('Filter...')} />,
      render: (playbook) => <strong>{playbook.fileName}</strong>,
    },
    {
      key: 'description', label: t('Description'), headerTitle: t('Summary shown when people pick playbooks during dispatch.'), cellClassName: 'text-dim',
      clearFilter: () => setColFilters(f => ({ ...f, description: '' })),
      filter: <input type="text" className="col-filter" aria-label={t('Description')} title={t('Show only playbooks whose description contains this text.')} value={colFilters.description} onChange={e => setColFilters(f => ({ ...f, description: e.target.value }))} placeholder={t('Filter...')} />,
      render: (playbook) => playbook.description || '-',
    },
    { key: 'visibility', label: t('Visibility'), headerTitle: t('Tenant-wide playbooks are usable by everyone in the tenant and edited by admins; personal ones only by their owner.'), cellClassName: 'cell-nowrap', render: (playbook) => <ScopeBadge scope={playbook.scope} /> },
    { key: 'status', label: t('Status'), headerTitle: t('Active playbooks can be selected during dispatch. Inactive ones are hidden from selection and rejected if requested.'), cellClassName: 'cell-nowrap', render: (playbook) => <StatusBadge status={playbook.active ? 'Active' : 'Inactive'} /> },
    { key: 'content', label: t('Content'), headerTitle: t('Size of the playbook text in characters. Inlined playbooks add this much to every mission prompt they are attached to.'), cellClassName: 'text-dim cell-nowrap', render: (playbook) => <>{playbook.content.length.toLocaleString()} {t('chars')}</> },
    {
      key: 'lastUpdated', label: t('Last Updated'), headerTitle: t('When this playbook was last changed. Hover a value for the exact time.'), cellClassName: 'text-dim cell-nowrap',
      cellTitle: (playbook) => formatDateTime(playbook.lastUpdateUtc),
      render: (playbook) => formatRelativeTime(playbook.lastUpdateUtc),
    },
    {
      key: 'actions', label: t('Actions'), headerTitle: t('Open the menu in a row to open, edit, duplicate or delete that playbook.'), fixed: true, interactive: true, className: 'text-right',
      render: (playbook) => {
        const canEditRow = canEditScoped(viewer, playbook);
        return (
          <ActionMenu
            id={`playbook-${playbook.id}`}
            items={[
              { label: 'Open', title: 'Open this playbook\'s page to read or edit its markdown.', onClick: () => navigate(`/playbooks/${playbook.id}`) },
              ...(canEditRow ? [{ label: 'Edit', title: 'Change this playbook\'s file name, description, content, visibility and active state.', onClick: () => openEdit(playbook) }] : []),
              { label: 'Duplicate', title: 'Create a copy of this playbook under a new file name, then open it.', onClick: () => void handleDuplicate(playbook) },
              { label: 'View JSON', title: 'Show this playbook\'s full record as JSON, as the API returns it.', onClick: () => setJsonData({ open: true, title: playbook.fileName, data: playbook }) },
              ...(canEditRow ? [{ label: 'Delete', danger: true as const, title: 'Permanently delete this playbook. Missions that already used it keep their copy. You confirm first.', onClick: () => handleDelete(playbook) }] : []),
            ]}
          />
        );
      },
    },
  ];

  return (
    <div>
      <PageHeader
        title={t('Playbooks')}
        subtitle={t('Tenant-scoped markdown playbooks that can be attached to voyages and missions. Use them for durable engineering rules, architecture standards, or execution checklists.')}
        actions={(
          <>
            <button className="btn btn-primary" onClick={openCreate} title={t('Create a playbook: a markdown document of rules or checklists that can be attached to voyages and missions.')}>
              + {t('Playbook')}
            </button>
          </>
        )}
      />

      <ErrorModal error={error} onClose={() => setError('')} />
      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      {deletion.dialog}
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
            <label title={t(PLAYBOOK_FILE_TIP)}>{t('File Name')}
              <input type="text" title={t(PLAYBOOK_FILE_TIP)} value={createForm.fileName} onChange={(event) => setCreateForm({ ...createForm, fileName: event.target.value })} placeholder={t('CSHARP_BACKEND_ARCHITECTURE.md')} required />
            </label>
            <label title={t(PLAYBOOK_DESCRIPTION_TIP)}>{t('Description')}
              <input type="text" title={t(PLAYBOOK_DESCRIPTION_TIP)} value={createForm.description} onChange={(event) => setCreateForm({ ...createForm, description: event.target.value })} placeholder={t('Optional summary shown during playbook selection')} />
            </label>
            <label title={t(PLAYBOOK_CONTENT_TIP)}>{t('Markdown Content')}
              <textarea rows={16} title={t(PLAYBOOK_CONTENT_TIP)} value={createForm.content} onChange={(event) => setCreateForm({ ...createForm, content: event.target.value })} spellCheck={false} />
            </label>
            <ScopeSelect viewer={viewer} value={createForm.scope} onChange={(scope) => setCreateForm({ ...createForm, scope })} />
            <label className="checkbox-row" title={t(PLAYBOOK_ACTIVE_TIP)}>
              <input type="checkbox" title={t(PLAYBOOK_ACTIVE_TIP)} checked={createForm.active} onChange={(event) => setCreateForm({ ...createForm, active: event.target.checked })} />
              <span>{t('Active and selectable during dispatch')}</span>
            </label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary" disabled={saving} title={editing ? t('Save changes to this playbook. Missions dispatched after saving get the new text; earlier missions keep their copy.') : t('Create this playbook so it can be selected during dispatch.')}>{saving ? t('Saving...') : editing ? t('Save Changes') : t('Create Playbook')}</button>
              <button type="button" className="btn" onClick={() => setShowCreate(false)} disabled={saving}>{t('Cancel')}</button>
            </div>
          </form>
        </div>
      )}

      <div className="playbook-overview-grid">
        <div className="card playbook-overview-card" title={t('All playbooks you can see, active and inactive.')}>
          <span>{t('Total Playbooks')}</span>
          <strong>{playbooks.length}</strong>
        </div>
        <div className="card playbook-overview-card" title={t('Playbooks that can be selected during dispatch.')}>
          <span>{t('Active')}</span>
          <strong>{activeCount}</strong>
        </div>
        <div className="card playbook-overview-card" title={t('Playbooks hidden from dispatch selection. Turn one back on by editing it.')}>
          <span>{t('Inactive')}</span>
          <strong>{inactiveCount}</strong>
        </div>
        <div className="card playbook-overview-card" title={t('Total characters of markdown across all playbooks.')}>
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
            title={t('Show only playbooks whose file name, description or ID contains this text.')}
            placeholder={t('Search by filename, description, or ID...')}
          />
          <select aria-label={t('All statuses')} title={t('Show all playbooks, or only active or only inactive ones.')} value={statusFilter} onChange={(event) => setStatusFilter(event.target.value as 'all' | 'active' | 'inactive')}>
            <option value="all">{t('All statuses')}</option>
            <option value="active">{t('Active only')}</option>
            <option value="inactive">{t('Inactive only')}</option>
          </select>
        </div>
      </div>

      <DataTable
        tableKey="playbooks"
        columns={columns}
        rows={filtered}
        rowKey={(playbook) => playbook.id}
        onRowClick={(playbook) => (canEditScoped(viewer, playbook) ? openEdit(playbook) : navigate(`/playbooks/${playbook.id}`))}
        autoRefresh={{ seconds: refreshSeconds, onChange: setRefreshSeconds }}
        onRefresh={load}
        refreshTitle={t('Refresh playbooks')}
        emptyMessage={emptyState}
        placeholder={playbooks.length > 0 ? undefined : loading ? <p className="text-dim">{t('Loading...')}</p> : emptyState}
      />
    </div>
  );
}
