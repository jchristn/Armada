import DialogShell from '../components/shared/DialogShell';
import PersonaPriorityEditor from '../components/shared/PersonaPriorityEditor';
import { personaRoutingForm, validatePersonaPriorities } from '../lib/personaPriorities';
import { useState, useEffect, useMemo, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { listPersonas, listPromptTemplates, createPersona, updatePersona, deletePersona, apiErrorMessage } from '../api/client';
import { listAll } from '../api/paging';
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
import { useDeleteProgress } from '../lib/useDeleteProgress';

const PERSONA_NAME_TIP = 'Unique persona name that missions, pipelines and captains refer to, for example Reviewer or Test Engineer. Required, and it cannot be changed after creation.';
const PERSONA_DESCRIPTION_TIP = 'Optional note on what this persona is for. Shown in lists and pickers; it is not sent to the captain.';
const PERSONA_TEMPLATE_TIP = 'The prompt template whose text tells the captain how to act in this role. Required. Manage templates on the Prompts tab.';

export default function Personas() {
  const navigate = useNavigate();
  const { isAdmin, isTenantAdmin, user } = useAuth();
  const viewer: ScopeViewer = { isAdmin, isTenantAdmin, tenantId: user?.user?.tenantId, userId: user?.user?.id };
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const deletion = useDeleteProgress();
  const { pushToast } = useNotifications();
  const [personas, setPersonas] = useState<Persona[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Modal state
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState<Persona | null>(null);
  const [form, setForm] = useState<{ name: string; description: string; promptTemplateName: string; scope: ScopeEnum }>({ name: '', description: '', promptTemplateName: '', scope: resolveCreateScope(viewer) });
  const [templateNames, setTemplateNames] = useState<string[]>([]);

  const [routing, setRouting] = useState(personaRoutingForm());

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
      const result = await listAll(listPersonas);
      setPersonas(result.objects);
      const templateResult = await listAll(listPromptTemplates);
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
  function openCreate() { setRouting(personaRoutingForm()); setForm({ name: '', description: '', promptTemplateName: '', scope: resolveCreateScope(viewer) }); setEditing(null); setShowForm(true); }
  function openEdit(p: Persona) { setRouting(personaRoutingForm(p)); setForm({ name: p.name, description: p.description ?? '', promptTemplateName: p.promptTemplateName, scope: p.scope }); setEditing(p); setShowForm(true); }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    try {
      const validation = validatePersonaPriorities(routing.captainPriorities);
      if (validation) throw new Error(t(validation));
      const payload: Record<string, unknown> = { ...routing, name: form.name, promptTemplateName: form.promptTemplateName, scope: form.scope };
      if (form.description) payload.description = form.description;
      // This modal does not edit the default captain; send the stored one so saving never clears it.
      if (editing) payload.defaultCaptainId = editing.defaultCaptainId ?? null;
      if (editing) await updatePersona(editing.name, payload);
      else await createPersona(payload as Partial<Persona>);
      setShowForm(false);
      pushToast('success', editing
        ? t('Persona "{{name}}" saved.', { name: editing.name })
        : t('Persona "{{name}}" created.', { name: form.name }));
      load();
    } catch (err: unknown) { setError(apiErrorMessage(err, t('Save failed.'))); }
  }

  function handleDelete(name: string) {
    setConfirm({
      open: true,
      title: t('Delete Persona'),
      message: t('Delete persona "{{name}}"? This cannot be undone.', { name }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        if (!(await deletion.run({ kind: 'persona', action: () => deletePersona(name) }))) return;
        pushToast('warning', t('Persona "{{name}}" deleted.', { name }));
        load();
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
      key: 'id', label: t('ID'), required: true, headerTitle: t('Unique persona ID. Personas are usually referred to by name; use the copy button to copy the ID.'), cellClassName: 'mono text-dim table-id-cell',
      render: (p) => (
        <span className="id-display">
          <span className="id-value" title={p.id}>{p.id}</span>
          <CopyButton text={p.id} onClick={e => e.stopPropagation()} />
        </span>
      ),
    },
    {
      key: 'name', label: t('Name'), required: true, sortKey: 'name', headerTitle: t('Persona name, the role a mission or pipeline stage asks for, such as Worker or Judge. Click to sort.'), cellClassName: 'cell-ident',
      filter: <input type="text" className="col-filter" aria-label={t('Name')} title={t('Show only personas whose name contains this text (not case sensitive).')} value={table.colFilters.name ?? ''} onChange={e => table.setColFilter('name', e.target.value)} placeholder={t('Filter...')} />,
      render: (p) => <strong>{p.name}</strong>,
    },
    {
      key: 'description', label: t('Description'), sortKey: 'description', headerTitle: t('What this persona does. Hover a value for the full text; click to sort.'), cellClassName: 'text-dim truncate-cell',
      clearFilter: () => table.setColFilter('description', ''),
      filter: <input type="text" className="col-filter" aria-label={t('Description')} title={t('Show only personas whose description contains this text.')} value={table.colFilters.description ?? ''} onChange={e => table.setColFilter('description', e.target.value)} placeholder={t('Filter...')} />,
      render: (p) => p.description ? <span className="truncate-text" title={p.description}>{p.description}</span> : '-',
    },
    {
      key: 'promptTemplateName', label: t('Prompt Template'), sortKey: 'promptTemplateName', headerTitle: t('The prompt template that holds this persona\'s role instructions for the captain. Click to sort.'), cellClassName: 'mono text-dim cell-ident',
      clearFilter: () => table.setColFilter('promptTemplateName', ''),
      filter: <input type="text" className="col-filter" aria-label={t('Prompt Template')} title={t('Show only personas whose prompt template name contains this text.')} value={table.colFilters.promptTemplateName ?? ''} onChange={e => table.setColFilter('promptTemplateName', e.target.value)} placeholder={t('Filter...')} />,
      render: (p) => p.promptTemplateName,
    },
    { key: 'visibility', label: t('Visibility'), headerTitle: t('Tenant-wide personas are usable by everyone in the tenant and edited by admins; personal ones only by their owner.'), render: (p) => <ScopeBadge scope={p.scope} /> },
    { key: 'isBuiltIn', label: t('Built-in'), sortKey: 'isBuiltIn', headerTitle: t('Built-in personas ship with Armada and cannot be deleted. Click to sort.'), render: (p) => (p.isBuiltIn ? <span className="tag built-in" title={t('Ships with Armada. It cannot be deleted.')}>{t('Built-in')}</span> : <span className="text-dim">-</span>) },
    { key: 'active', label: t('Active'), sortKey: 'active', headerTitle: t('Marks the persona as in use or retired. It is a label only: dispatch does not skip inactive personas. Click to sort.'), render: (p) => <StatusBadge status={p.active ? 'Active' : 'Inactive'} /> },
    {
      key: 'created', label: t('Created'), sortKey: 'createdUtc', headerTitle: t('When this persona was created. Hover a value for the exact time; click to sort.'),
      cellClassName: 'text-dim cell-nowrap', cellTitle: (p) => formatDateTime(p.createdUtc),
      render: (p) => formatRelativeTime(p.createdUtc),
    },
    {
      key: 'actions', label: t('Actions'), headerTitle: t('Open the menu in a row to view, edit, duplicate or delete that persona, or edit its prompt template.'), fixed: true, interactive: true, className: 'text-right',
      render: (p) => (
        <ActionMenu id={`persona-${p.name}`} items={[
          { label: 'View Detail', title: 'Open this persona\'s page with its settings, routing and prompt preview.', onClick: () => navigate(`/personas/${encodeURIComponent(p.name)}`) },
          ...(canEditScoped(viewer, p) ? [{ label: 'Edit', title: 'Change this persona\'s description, prompt template, visibility and captain priorities.', onClick: () => openEdit(p) }] : []),
          { label: 'Duplicate', title: 'Create a copy of this persona under a new name, including its captain priorities, then open it.', onClick: () => void handleDuplicate(p) },
          { label: 'Edit Backing Prompt', title: 'Open the prompt template this persona uses. Changes there affect every persona that uses that template.', onClick: () => navigate(`/prompt-templates/${encodeURIComponent(p.promptTemplateName)}`) },
          { label: 'View JSON', title: 'Show this persona\'s full record as JSON, as the API returns it.', onClick: () => setJsonData({ open: true, title: `${t('Persona')}: ${p.name}`, data: p }) },
          ...(!p.isBuiltIn && canEditScoped(viewer, p) ? [{ label: 'Delete', danger: true as const, title: 'Permanently delete this persona. Missions and pipelines that name it can no longer use it. You confirm first.', onClick: () => handleDelete(p.name) }] : []),
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
            <button className="btn btn-primary btn-sm" onClick={openCreate} title={t('Create a persona: a named role, such as Reviewer, that pairs a prompt template with captain routing.')}>+ {t('Persona')}</button>
          </>
        )}
      />

      <ErrorModal error={error} onClose={() => setError('')} />

      {/* Create/Edit Modal */}
      <DialogShell open={showForm} title={editing ? t('Edit Persona') : t('Create Persona')} onClose={() => setShowForm(false)} size="lg" className="persona-edit-dialog"
        footer={<><button type="submit" form="persona-edit-form" className="btn btn-primary" title={editing ? t('Save changes to this persona. Missions assigned after saving use the new settings.') : t('Create this persona.')}>{t('Save')}</button>
          <button type="button" className="btn" onClick={() => setShowForm(false)}>{t('Cancel')}</button></>}>
        <form id="persona-edit-form" className="persona-edit-layout" onSubmit={handleSubmit}>
          <div className="persona-edit-settings">
            <label title={t(PERSONA_NAME_TIP)}>{t('Name')}<input value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} title={t(PERSONA_NAME_TIP)} required disabled={!!editing} /></label>
            <label title={t(PERSONA_DESCRIPTION_TIP)}>{t('Description')}
              <textarea title={t(PERSONA_DESCRIPTION_TIP)} value={form.description} onChange={e => setForm({ ...form, description: e.target.value })} rows={3} placeholder={t('Optional description of this persona...')} />
            </label>
            <label title={t(PERSONA_TEMPLATE_TIP)}>{t('Prompt Template Name')}
              <select title={t(PERSONA_TEMPLATE_TIP)} value={form.promptTemplateName} onChange={e => setForm({ ...form, promptTemplateName: e.target.value })} required>
                <option value="">{t('Select a template...')}</option>
                {templateNames.map(name => (
                  <option key={name} value={name}>{name}</option>
                ))}
              </select>
            </label>
            <ScopeSelect viewer={viewer} value={form.scope} onChange={(scope) => setForm({ ...form, scope })} />
          </div>
          <PersonaPriorityEditor value={routing} onChange={setRouting} />
        </form>
      </DialogShell>

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
        secondaryAction={viewRecord && canEditScoped(viewer, viewRecord as unknown as Persona)
          ? { label: t('Edit'), title: t('Change this persona\'s description, prompt template, visibility and captain priorities.'), onClick: () => { const r = viewRecord as unknown as Persona; setViewRecord(null); openEdit(r); } }
          : undefined}
      />

      {/* Confirm Dialog */}
      {deletion.dialog}
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
