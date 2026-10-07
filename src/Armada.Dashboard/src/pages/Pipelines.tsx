import { useEffect, useState, useMemo, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { listPipelines, listPersonas, createPipeline, updatePipeline, deletePipeline } from '../api/client';
import type { Pipeline, PipelineStage, ScopeEnum } from '../types/models';
import { useAuth } from '../context/AuthContext';
import { canEdit as canEditScoped, resolveCreateScope, type ScopeViewer } from '../lib/scoping';
import ScopeBadge from '../components/shared/ScopeBadge';
import ScopeSelect from '../components/shared/ScopeSelect';
import DataTable, { type DataTableColumn } from '../components/shared/DataTable';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import RecordDetailModal from '../components/shared/RecordDetailModal';
import BoolIcon from '../components/shared/BoolIcon';
import CopyButton from '../components/shared/CopyButton';
import PageHeader from '../components/shared/PageHeader';
import ErrorModal from '../components/shared/ErrorModal';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import { buildPipelineDuplicatePayload } from '../lib/duplicates';
import { useResourceTable } from '../lib/useResourceTable';

interface StageFormEntry {
  personaName: string;
  isOptional: boolean;
  requiresReview: boolean;
  reviewDenyAction: 'RetryStage' | 'FailPipeline';
}

function formatStages(stages: PipelineStage[]): string {
  if (!stages || stages.length === 0) return '-';
  const sorted = [...stages].sort((a, b) => a.order - b.order);
  return sorted.map(s => `${s.personaName}${s.requiresReview ? ' [review]' : ''}`).join(' -> ');
}

export default function Pipelines() {
  const navigate = useNavigate();
  const { isAdmin, isTenantAdmin, user } = useAuth();
  const viewer: ScopeViewer = { isAdmin, isTenantAdmin, tenantId: user?.user?.tenantId, userId: user?.user?.id };
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();
  const [pipelines, setPipelines] = useState<Pipeline[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Modal state
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState<Pipeline | null>(null);
  const [form, setForm] = useState<{ name: string; description: string; stages: StageFormEntry[]; scope: ScopeEnum }>({ name: '', description: '', stages: [{ personaName: '', isOptional: false, requiresReview: false, reviewDenyAction: 'RetryStage' }], scope: resolveCreateScope(viewer) });
  const [personaNames, setPersonaNames] = useState<string[]>([]);

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });

  // Row-click view modal
  const [viewRecord, setViewRecord] = useState<Record<string, unknown> | null>(null);

  // Confirm dialog
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

  const table = useResourceTable({
    rows: pipelines,
    getId: (p) => p.id,
    columnValues: {
      name: (p) => p.name.toLowerCase(),
      description: (p) => (p.description ?? '').toLowerCase(),
      stages: (p) => (p.stages ?? []).length,
      isBuiltIn: (p) => (p.isBuiltIn ? 1 : 0),
      active: (p) => (p.active ? 1 : 0),
      createdUtc: (p) => p.createdUtc,
    },
    initialSortField: 'name',
    initialSortDir: 'asc',
    initialPageSize: 25,
  });

  const load = useCallback(async () => {
    try {
      setLoading(true);
      const result = await listPipelines({ pageSize: 9999 });
      setPipelines(result.objects);
      const personaResult = await listPersonas({ pageSize: 9999 });
      setPersonaNames(personaResult.objects.map(p => p.name));
      setError('');
    } catch {
      setError(t('Failed to load pipelines.'));
    } finally {
      setLoading(false);
    }
  }, [t]);

  useEffect(() => { load(); }, [load]);
  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('pipelines', load);

  // CRUD
  function openCreate() {
    setForm({ name: '', description: '', stages: [{ personaName: '', isOptional: false, requiresReview: false, reviewDenyAction: 'RetryStage' }], scope: resolveCreateScope(viewer) });
    setEditing(null);
    setShowForm(true);
  }

  function openEdit(p: Pipeline) {
    const stages: StageFormEntry[] = (p.stages ?? [])
      .sort((a, b) => a.order - b.order)
      .map(s => ({ personaName: s.personaName, isOptional: s.isOptional, requiresReview: s.requiresReview, reviewDenyAction: s.reviewDenyAction }));
    if (stages.length === 0) stages.push({ personaName: '', isOptional: false, requiresReview: false, reviewDenyAction: 'RetryStage' });
    setForm({ name: p.name, description: p.description ?? '', stages, scope: p.scope });
    setEditing(p);
    setShowForm(true);
  }

  function addStage() {
    setForm(f => ({ ...f, stages: [...f.stages, { personaName: '', isOptional: false, requiresReview: false, reviewDenyAction: 'RetryStage' }] }));
  }

  function moveStage(index: number, direction: number) {
    setForm(f => {
      const stages = [...f.stages];
      const target = index + direction;
      if (target < 0 || target >= stages.length) return f;
      const temp = stages[index];
      stages[index] = stages[target];
      stages[target] = temp;
      return { ...f, stages };
    });
  }

  function removeStage(index: number) {
    setForm(f => {
      const stages = f.stages.filter((_, i) => i !== index);
      if (stages.length === 0) stages.push({ personaName: '', isOptional: false, requiresReview: false, reviewDenyAction: 'RetryStage' });
      return { ...f, stages };
    });
  }

  function updateStage(index: number, field: keyof StageFormEntry, value: string | boolean) {
    setForm(f => {
      const stages = [...f.stages];
      stages[index] = { ...stages[index], [field]: value };
      return { ...f, stages };
    });
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    try {
      const stagesPayload = form.stages
        .filter(s => s.personaName.trim() !== '')
        .map((s, i) => ({ personaName: s.personaName.trim(), isOptional: s.isOptional, requiresReview: s.requiresReview, reviewDenyAction: s.reviewDenyAction, order: i + 1 }));
      const payload = { name: form.name, description: form.description || null, stages: stagesPayload, scope: form.scope } as Partial<Pipeline>;
      if (editing) await updatePipeline(editing.name, payload as Partial<Pipeline>);
      else await createPipeline(payload);
      setShowForm(false);
      pushToast('success', editing
        ? t('Pipeline "{{name}}" saved.', { name: editing.name })
        : t('Pipeline "{{name}}" created.', { name: form.name }));
      load();
    } catch { setError(t('Save failed.')); }
  }

  function handleDelete(name: string) {
    setConfirm({
      open: true,
      title: t('Delete Pipeline'),
      message: t('Delete pipeline "{{name}}"? This cannot be undone.', { name }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await deletePipeline(name);
          pushToast('warning', t('Pipeline "{{name}}" deleted.', { name }));
          load();
        } catch { setError(t('Delete failed.')); }
      },
    });
  }

  async function handleDuplicate(pipeline: Pipeline) {
    try {
      const created = await createPipeline(buildPipelineDuplicatePayload(pipeline));
      pushToast('success', t('Pipeline "{{name}}" duplicated.', { name: created.name }));
      navigate(`/pipelines/${encodeURIComponent(created.name)}`);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Duplicate failed.'));
    }
  }

  const columns: DataTableColumn<Pipeline>[] = [
    {
      key: 'name', label: t('Name'), required: true, sortKey: 'name', headerTitle: t('Pipeline name -- click to sort'), cellClassName: 'cell-ident',
      filter: <input type="text" className="col-filter" aria-label={t('Name')} value={table.colFilters.name ?? ''} onChange={e => table.setColFilter('name', e.target.value)} placeholder={t('Search...')} />,
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
      filter: <input type="text" className="col-filter" aria-label={t('Description')} value={table.colFilters.description ?? ''} onChange={e => table.setColFilter('description', e.target.value)} placeholder={t('Search...')} />,
      render: (p) => p.description ? <span className="truncate-text" title={p.description}>{p.description}</span> : '-',
    },
    { key: 'stages', label: t('Stages'), sortKey: 'stages', headerTitle: t('Stage count -- click to sort'), cellClassName: 'cell-ident', render: (p) => formatStages(p.stages) },
    { key: 'visibility', label: t('Visibility'), render: (p) => <ScopeBadge scope={p.scope} /> },
    { key: 'isBuiltIn', label: t('Built-in'), sortKey: 'isBuiltIn', headerTitle: t('Built-in -- click to sort'), render: (p) => <BoolIcon value={!!p.isBuiltIn} falseVariant="dash" trueTitle={t('Built-in')} falseTitle={t('Not built-in')} /> },
    { key: 'active', label: t('Active'), sortKey: 'active', headerTitle: t('Active -- click to sort'), render: (p) => <BoolIcon value={p.active !== false} falseVariant="cross" trueTitle={t('Active')} falseTitle={t('Inactive')} /> },
    {
      key: 'created', label: t('Created'), sortKey: 'createdUtc', headerTitle: t('Created date -- click to sort'),
      cellClassName: 'text-dim cell-nowrap', cellTitle: (p) => formatDateTime(p.createdUtc),
      render: (p) => formatRelativeTime(p.createdUtc),
    },
    {
      key: 'actions', label: t('Actions'), fixed: true, interactive: true, className: 'text-right',
      render: (p) => (
        <ActionMenu id={`pipeline-${p.id}`} items={[
          { label: 'View Detail', onClick: () => navigate(`/pipelines/${encodeURIComponent(p.name)}`) },
          ...(canEditScoped(viewer, p) ? [{ label: 'Edit', onClick: () => openEdit(p) }] : []),
          { label: 'Duplicate', onClick: () => void handleDuplicate(p) },
          { label: 'View JSON', onClick: () => setJsonData({ open: true, title: `${t('Pipeline')}: ${p.name}`, data: p }) },
          ...(!p.isBuiltIn && canEditScoped(viewer, p) ? [{ label: 'Delete', danger: true as const, onClick: () => handleDelete(p.name) }] : []),
        ]} />
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title={t('Pipelines')}
        subtitle={t('Multi-stage workflows combining different personas')}
        actions={(
          <>
            <button className="btn btn-primary btn-sm" onClick={openCreate}>+ {t('Pipeline')}</button>
          </>
        )}
      />

      <ErrorModal error={error} onClose={() => setError('')} />

      {/* Create/Edit Modal */}
      {showForm && (
        <div className="modal-overlay" onClick={() => setShowForm(false)}>
          <form className="modal" onClick={e => e.stopPropagation()} onSubmit={handleSubmit} style={{ maxWidth: '720px', width: '90%' }}>
            <h3>{editing ? t('Edit Pipeline') : t('Create Pipeline')}</h3>
            <label>{t('Name')}<input value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} required /></label>
            <label>{t('Description')}<input value={form.description} onChange={e => setForm({ ...form, description: e.target.value })} /></label>
            <ScopeSelect viewer={viewer} value={form.scope} onChange={(scope) => setForm({ ...form, scope })} />
            <div style={{ marginTop: '1rem' }}>
              <strong>{t('Stages')}</strong>
              {form.stages.map((stage, i) => (
                <div key={i} style={{ display: 'flex', flexWrap: 'wrap', gap: '0.5rem', alignItems: 'center', marginBottom: '0.5rem', marginTop: '0.5rem' }}>
                  <span className="text-dim" style={{ minWidth: '1.5rem' }}>{i + 1}.</span>
                  <select aria-label={t('Select persona...')}
                    value={stage.personaName}
                    onChange={e => updateStage(i, 'personaName', e.target.value)}
                    required
                    style={{ flex: '0 1 200px', minWidth: '120px' }}
                  >
                    <option value="">{t('Select persona...')}</option>
                    {personaNames.map(name => (
                      <option key={name} value={name}>{name}</option>
                    ))}
                  </select>
                  <label style={{ display: 'inline-flex', alignItems: 'center', gap: '0.35rem', margin: 0, whiteSpace: 'nowrap', lineHeight: 1, cursor: 'pointer' }}>
                    <input
                      type="checkbox"
                      checked={stage.isOptional}
                      onChange={e => updateStage(i, 'isOptional', e.target.checked)}
                      style={{ width: 'auto', margin: 0, verticalAlign: 'middle' }}
                    />
                    <span style={{ verticalAlign: 'middle' }}>{t('Optional')}</span>
                  </label>
                  <label style={{ display: 'inline-flex', alignItems: 'center', gap: '0.35rem', margin: 0, whiteSpace: 'nowrap', lineHeight: 1, cursor: 'pointer' }}>
                    <input
                      type="checkbox"
                      checked={stage.requiresReview}
                      onChange={e => updateStage(i, 'requiresReview', e.target.checked)}
                      style={{ width: 'auto', margin: 0, verticalAlign: 'middle' }}
                    />
                    <span style={{ verticalAlign: 'middle' }}>{t('Review gate')}</span>
                  </label>
                  <select aria-label={t('Retry stage')}
                    value={stage.reviewDenyAction}
                    disabled={!stage.requiresReview}
                    onChange={e => updateStage(i, 'reviewDenyAction', e.target.value as 'RetryStage' | 'FailPipeline')}
                    style={{ flex: '0 1 150px', minWidth: '120px' }}
                  >
                    <option value="RetryStage">{t('Retry stage')}</option>
                    <option value="FailPipeline">{t('Fail pipeline')}</option>
                  </select>
                  <span style={{ width: '0.75rem', flexShrink: 0 }} />
                  <button type="button" className="btn btn-sm" onClick={() => moveStage(i, -1)} disabled={i === 0} title={t('Move up')} style={{ padding: '0.15rem 0.4rem', fontSize: '0.75rem' }}>{'\u25B2'}</button>
                  <button type="button" className="btn btn-sm" onClick={() => moveStage(i, 1)} disabled={i === form.stages.length - 1} title={t('Move down')} style={{ padding: '0.15rem 0.4rem', fontSize: '0.75rem' }}>{'\u25BC'}</button>
                  <span style={{ width: '0.5rem', flexShrink: 0 }} />
                  <button type="button" className="btn btn-sm btn-danger" onClick={() => removeStage(i)} title={t('Remove stage')} style={{ flexShrink: 0 }}>X</button>
                </div>
              ))}
              <button type="button" className="btn btn-sm" onClick={addStage} style={{ marginTop: '0.5rem' }}>+ {t('Stage')}</button>
            </div>
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
        title={typeof viewRecord?.name === 'string' ? viewRecord.name : t('Pipeline')}
        subtitle={t('Pipeline')}
        record={viewRecord}
        onClose={() => setViewRecord(null)}
        onEdit={() => { const r = viewRecord; setViewRecord(null); navigate(`/pipelines/${encodeURIComponent((r as { name: string }).name)}`); }}
        editLabel={t('Open Details')}
      />

      {/* Confirm Dialog */}
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message}
        onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />

      <DataTable
        tableKey="pipelines"
        columns={columns}
        rows={table.paginated}
        rowKey={(p) => p.id}
        onRowClick={(p) => setViewRecord(p as unknown as Record<string, unknown>)}
        sort={table.sortState}
        pagination={table.paginationProps}
        autoRefresh={{ seconds: refreshSeconds, onChange: setRefreshSeconds }}
        onRefresh={load}
        refreshTitle="Refresh pipeline data"
        emptyMessage={t('No pipelines match the current filters.')}
        placeholder={pipelines.length > 0 ? undefined : <p className="text-dim">{loading ? t('Loading...') : t('No pipelines configured.')}</p>}
      />
    </div>
  );
}
