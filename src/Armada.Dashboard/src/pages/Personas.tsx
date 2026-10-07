import { useState, useEffect, useMemo, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { listPersonas, listPromptTemplates, createPersona, updatePersona, deletePersona } from '../api/client';
import type { Persona, ScopeEnum } from '../types/models';
import { useAuth } from '../context/AuthContext';
import { canEdit as canEditScoped, resolveCreateScope, type ScopeViewer } from '../lib/scoping';
import ScopeBadge from '../components/shared/ScopeBadge';
import ScopeSelect from '../components/shared/ScopeSelect';
import DataTable, { type DataTableColumn } from '../components/shared/DataTable';
import ActionMenu from '../components/shared/ActionMenu';
import StatusBadge from '../components/shared/StatusBadge';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import RecordDetailModal from '../components/shared/RecordDetailModal';
import CopyButton from '../components/shared/CopyButton';
import PageHeader from '../components/shared/PageHeader';
import ErrorModal from '../components/shared/ErrorModal';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import { buildPersonaDuplicatePayload } from '../lib/duplicates';
import { useResourceTable } from '../lib/useResourceTable';

export default function Personas() {
  const navigate = useNavigate();
  const { isAdmin, isTenantAdmin, user } = useAuth();
  const viewer: ScopeViewer = { isAdmin, isTenantAdmin, tenantId: user?.user?.tenantId, userId: user?.user?.id };
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();
  const [personas, setPersonas] = useState<Persona[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Modal state
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState<Persona | null>(null);
  const [form, setForm] = useState<{ name: string; description: string; promptTemplateName: string; scope: ScopeEnum }>({ name: '', description: '', promptTemplateName: '', scope: resolveCreateScope(viewer) });
  const [templateNames, setTemplateNames] = useState<string[]>([]);

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });

  // Row-click view modal
  const [viewRecord, setViewRecord] = useState<Record<string, unknown> | null>(null);

  // Confirm dialog
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

  const table = useResourceTable({
    rows: personas,
    getId: (p) => p.id,
    columnValues: {
      name: (p) => p.name.toLowerCase(),
      description: (p) => (p.description ?? '').toLowerCase(),
      promptTemplateName: (p) => p.promptTemplateName.toLowerCase(),
      isBuiltIn: (p) => (p.isBuiltIn ? '1' : '0'),
      active: (p) => (p.active ? '1' : '0'),
      createdUtc: (p) => p.createdUtc,
    },
    initialSortField: 'name',
    initialSortDir: 'asc',
    initialPageSize: 25,
  });

  const load = useCallback(async () => {
    try {
      setLoading(true);
      const result = await listPersonas({ pageSize: 9999 });
      setPersonas(result.objects);
      const templateResult = await listPromptTemplates({ pageSize: 9999 });
      setTemplateNames(templateResult.objects.map(t => t.name));
      setError('');
    } catch {
      setError(t('Failed to load personas.'));
    } finally {
      setLoading(false);
    }
  }, [t]);

  useEffect(() => { load(); }, [load]);
  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('personas', load);

  // CRUD
  function openCreate() { setForm({ name: '', description: '', promptTemplateName: '', scope: resolveCreateScope(viewer) }); setEditing(null); setShowForm(true); }
  function openEdit(p: Persona) { setForm({ name: p.name, description: p.description ?? '', promptTemplateName: p.promptTemplateName, scope: p.scope }); setEditing(p); setShowForm(true); }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    try {
      const payload: Record<string, unknown> = { name: form.name, promptTemplateName: form.promptTemplateName, scope: form.scope };
      if (form.description) payload.description = form.description;
      if (editing) await updatePersona(editing.name, payload);
      else await createPersona(payload as Partial<Persona>);
      setShowForm(false);
      pushToast('success', editing
        ? t('Persona "{{name}}" saved.', { name: editing.name })
        : t('Persona "{{name}}" created.', { name: form.name }));
      load();
    } catch { setError(t('Save failed.')); }
  }

  function handleDelete(name: string) {
    setConfirm({
      open: true,
      title: t('Delete Persona'),
      message: t('Delete persona "{{name}}"? This cannot be undone.', { name }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await deletePersona(name);
          pushToast('warning', t('Persona "{{name}}" deleted.', { name }));
          load();
        } catch { setError(t('Delete failed.')); }
      },
    });
  }

  async function handleDuplicate(persona: Persona) {
    try {
      const created = await createPersona(buildPersonaDuplicatePayload(persona));
      pushToast('success', t('Persona "{{name}}" duplicated.', { name: created.name }));
      navigate(`/personas/${encodeURIComponent(created.name)}`);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Duplicate failed.'));
    }
  }

  const columns: DataTableColumn<Persona>[] = [
    {
      key: 'name', label: t('Name'), required: true, sortKey: 'name', headerTitle: t('Persona name -- click to sort'), cellClassName: 'cell-ident',
      filter: <input type="text" className="col-filter" aria-label={t('Name')} value={table.colFilters.name ?? ''} onChange={e => table.setColFilter('name', e.target.value)} placeholder={t('Filter...')} />,
      render: (p) => <strong>{p.name}</strong>,
    },
    {
      key: 'id', label: t('ID'), required: true, cellClassName: 'mono text-dim table-id-cell',
      render: (p) => (
        <span className="id-display">
          <span className="id-value" title={p.id}>{p.id}</span>
          <CopyButton text={p.id} onClick={e => e.stopPropagation()} />
        </span>
      ),
    },
    {
      key: 'description', label: t('Description'), sortKey: 'description', headerTitle: t('Description -- click to sort'), cellClassName: 'text-dim truncate-cell',
      clearFilter: () => table.setColFilter('description', ''),
      filter: <input type="text" className="col-filter" aria-label={t('Description')} value={table.colFilters.description ?? ''} onChange={e => table.setColFilter('description', e.target.value)} placeholder={t('Filter...')} />,
      render: (p) => p.description ? <span className="truncate-text" title={p.description}>{p.description}</span> : '-',
    },
    {
      key: 'promptTemplateName', label: t('Prompt Template'), sortKey: 'promptTemplateName', headerTitle: t('Prompt template -- click to sort'), cellClassName: 'mono text-dim cell-ident',
      clearFilter: () => table.setColFilter('promptTemplateName', ''),
      filter: <input type="text" className="col-filter" aria-label={t('Prompt Template')} value={table.colFilters.promptTemplateName ?? ''} onChange={e => table.setColFilter('promptTemplateName', e.target.value)} placeholder={t('Filter...')} />,
      render: (p) => p.promptTemplateName,
    },
    { key: 'visibility', label: t('Visibility'), render: (p) => <ScopeBadge scope={p.scope} /> },
    { key: 'isBuiltIn', label: t('Built-in'), sortKey: 'isBuiltIn', headerTitle: t('Built-in -- click to sort'), render: (p) => (p.isBuiltIn ? <StatusBadge status="Built-in" /> : <span className="text-dim">-</span>) },
    { key: 'active', label: t('Active'), sortKey: 'active', headerTitle: t('Active -- click to sort'), render: (p) => <StatusBadge status={p.active ? 'Active' : 'Inactive'} /> },
    {
      key: 'created', label: t('Created'), sortKey: 'createdUtc', headerTitle: t('Created date -- click to sort'),
      cellClassName: 'text-dim cell-nowrap', cellTitle: (p) => formatDateTime(p.createdUtc),
      render: (p) => formatRelativeTime(p.createdUtc),
    },
    {
      key: 'actions', label: t('Actions'), fixed: true, interactive: true, className: 'text-right',
      render: (p) => (
        <ActionMenu id={`persona-${p.name}`} items={[
          { label: 'View Detail', onClick: () => navigate(`/personas/${encodeURIComponent(p.name)}`) },
          ...(canEditScoped(viewer, p) ? [{ label: 'Edit', onClick: () => openEdit(p) }] : []),
          { label: 'Duplicate', onClick: () => void handleDuplicate(p) },
          { label: 'Edit Backing Prompt', onClick: () => navigate(`/prompt-templates/${encodeURIComponent(p.promptTemplateName)}`) },
          { label: 'View JSON', onClick: () => setJsonData({ open: true, title: `${t('Persona')}: ${p.name}`, data: p }) },
          ...(!p.isBuiltIn && canEditScoped(viewer, p) ? [{ label: 'Delete', danger: true as const, onClick: () => handleDelete(p.name) }] : []),
        ]} />
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title={t('Personas')}
        subtitle={t('Named configurations that define how captains behave when executing missions.')}
        actions={(
          <>
            <button className="btn btn-primary btn-sm" onClick={openCreate}>+ {t('Persona')}</button>
          </>
        )}
      />

      <ErrorModal error={error} onClose={() => setError('')} />

      {/* Create/Edit Modal */}
      {showForm && (
        <div className="modal-overlay" onClick={() => setShowForm(false)}>
          <form className="modal" onClick={e => e.stopPropagation()} onSubmit={handleSubmit}>
            <h3>{editing ? t('Edit Persona') : t('Create Persona')}</h3>
            <label>{t('Name')}<input value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} required disabled={!!editing} /></label>
            <label>{t('Description')}
              <textarea value={form.description} onChange={e => setForm({ ...form, description: e.target.value })} rows={3} placeholder={t('Optional description of this persona...')} />
            </label>
            <label>{t('Prompt Template Name')}
              <select value={form.promptTemplateName} onChange={e => setForm({ ...form, promptTemplateName: e.target.value })} required>
                <option value="">{t('Select a template...')}</option>
                {templateNames.map(name => (
                  <option key={name} value={name}>{name}</option>
                ))}
              </select>
            </label>
            <ScopeSelect viewer={viewer} value={form.scope} onChange={(scope) => setForm({ ...form, scope })} />
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary">{t('Save')}</button>
              <button type="button" className="btn" onClick={() => setShowForm(false)}>{t('Cancel')}</button>
            </div>
          </form>
        </div>
      )}

      {/* JSON Viewer */}
      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />

      {/* Row-click View Modal */}
      <RecordDetailModal
        open={!!viewRecord}
        title={typeof viewRecord?.name === 'string' ? viewRecord.name : t('Persona')}
        subtitle={t('Persona')}
        record={viewRecord}
        onClose={() => setViewRecord(null)}
        onEdit={() => { const r = viewRecord; setViewRecord(null); navigate(`/personas/${encodeURIComponent((r as { name: string }).name)}`); }}
        editLabel={t('Open Details')}
      />

      {/* Confirm Dialog */}
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message}
        onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />

      <DataTable
        tableKey="personas"
        columns={columns}
        rows={table.paginated}
        rowKey={(p) => p.name}
        onRowClick={(p) => setViewRecord(p as unknown as Record<string, unknown>)}
        sort={table.sortState}
        pagination={table.paginationProps}
        autoRefresh={{ seconds: refreshSeconds, onChange: setRefreshSeconds }}
        onRefresh={load}
        refreshTitle={t('Refresh persona data')}
        emptyMessage={t('No personas match the current filters.')}
        placeholder={personas.length > 0 ? undefined : <p className="text-dim">{loading ? t('Loading...') : t('No personas configured.')}</p>}
      />
    </div>
  );
}
