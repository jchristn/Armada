import { useEffect, useState, useMemo, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { listPipelines, listPersonas, createPipeline, updatePipeline, deletePipeline } from '../api/client';
import type { Pipeline, PipelineStage } from '../types/models';
import Pagination from '../components/shared/Pagination';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import StatusBadge from '../components/shared/StatusBadge';
import CopyButton from '../components/shared/CopyButton';
import RefreshButton from '../components/shared/RefreshButton';
import ErrorModal from '../components/shared/ErrorModal';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';

type SortDir = 'asc' | 'desc';
type SortField = 'name' | 'description' | 'stages' | 'isBuiltIn' | 'active' | 'createdUtc';

interface StageFormEntry {
  personaName: string;
  isOptional: boolean;
}

function formatStages(stages: PipelineStage[]): string {
  if (!stages || stages.length === 0) return '-';
  const sorted = [...stages].sort((a, b) => a.order - b.order);
  return sorted.map(s => s.personaName).join(' -> ');
}

export default function Pipelines() {
  const navigate = useNavigate();
  const { t, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const [pipelines, setPipelines] = useState<Pipeline[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Modal state
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState<Pipeline | null>(null);
  const [form, setForm] = useState<{ name: string; description: string; stages: StageFormEntry[] }>({ name: '', description: '', stages: [{ personaName: '', isOptional: false }] });
  const [personaNames, setPersonaNames] = useState<string[]>([]);

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });

  // Confirm dialog
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

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

  // Filtered rows
  const filtered = useMemo(() => {
    return pipelines.filter(p =>
      (!colFilters.name || p.name.toLowerCase().includes(colFilters.name.toLowerCase())) &&
      (!colFilters.description || (p.description ?? '').toLowerCase().includes(colFilters.description.toLowerCase()))
    );
  }, [pipelines, colFilters]);

  // Sorted rows
  const sorted = useMemo(() => {
    const arr = [...filtered];
    arr.sort((a, b) => {
      let va: string | number | boolean = '';
      let vb: string | number | boolean = '';
      if (sortField === 'stages') { va = (a.stages ?? []).length; vb = (b.stages ?? []).length; }
      else if (sortField === 'isBuiltIn') { va = a.isBuiltIn ? 1 : 0; vb = b.isBuiltIn ? 1 : 0; }
      else if (sortField === 'active') { va = a.active ? 1 : 0; vb = b.active ? 1 : 0; }
      else if (sortField === 'createdUtc') { va = a.createdUtc; vb = b.createdUtc; }
      else if (sortField === 'description') { va = (a.description ?? '').toLowerCase(); vb = (b.description ?? '').toLowerCase(); }
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

  // CRUD
  function openCreate() {
    setForm({ name: '', description: '', stages: [{ personaName: '', isOptional: false }] });
    setEditing(null);
    setShowForm(true);
  }

  function openEdit(p: Pipeline) {
    const stages: StageFormEntry[] = (p.stages ?? [])
      .sort((a, b) => a.order - b.order)
      .map(s => ({ personaName: s.personaName, isOptional: s.isOptional }));
    if (stages.length === 0) stages.push({ personaName: '', isOptional: false });
    setForm({ name: p.name, description: p.description ?? '', stages });
    setEditing(p);
    setShowForm(true);
  }

  function addStage() {
    setForm(f => ({ ...f, stages: [...f.stages, { personaName: '', isOptional: false }] }));
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
      if (stages.length === 0) stages.push({ personaName: '', isOptional: false });
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
        .map((s, i) => ({ personaName: s.personaName.trim(), isOptional: s.isOptional, order: i + 1 }));
      const payload = { name: form.name, description: form.description || null, stages: stagesPayload } as Partial<Pipeline>;
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

  return (
    <div>
      <div className="view-header">
        <div>
          <h2>{t('Pipelines')}</h2>
          <p className="text-dim view-subtitle">{t('Multi-stage workflows combining different personas')}</p>
        </div>
        <div className="view-actions">
          <button className="btn btn-primary btn-sm" onClick={openCreate}>+ {t('Pipeline')}</button>
          <RefreshButton onRefresh={load} title="Refresh pipeline data" />
        </div>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />

      {/* Create/Edit Modal */}
      {showForm && (
        <div className="modal-overlay" onClick={() => setShowForm(false)}>
          <form className="modal" onClick={e => e.stopPropagation()} onSubmit={handleSubmit} style={{ maxWidth: '720px', width: '90%' }}>
            <h3>{editing ? t('Edit Pipeline') : t('Create Pipeline')}</h3>
            <label>{t('Name')}<input value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} required /></label>
            <label>{t('Description')}<input value={form.description} onChange={e => setForm({ ...form, description: e.target.value })} /></label>
            <div style={{ marginTop: '1rem' }}>
              <strong>{t('Stages')}</strong>
              {form.stages.map((stage, i) => (
                <div key={i} style={{ display: 'flex', gap: '0.5rem', alignItems: 'center', marginBottom: '0.5rem', marginTop: '0.5rem' }}>
                  <span className="text-dim" style={{ minWidth: '1.5rem' }}>{i + 1}.</span>
                  <select
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

      {/* Confirm Dialog */}
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message}
        onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />

      {loading && pipelines.length === 0 && <p className="text-dim">{t('Loading...')}</p>}
      {!loading && pipelines.length === 0 && <p className="text-dim">{t('No pipelines configured.')}</p>}

      {pipelines.length > 0 && (
        <>
          <Pagination pageNumber={currentPage} pageSize={pageSize} totalPages={totalPages}
            totalRecords={sorted.length}
            onPageChange={p => setPageNumber(p)} onPageSizeChange={s => { setPageSize(s); setPageNumber(1); }} />

          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th className="sortable" onClick={() => handleSort('name')} title={t('Pipeline name -- click to sort')}>
                    {t('Name')}{sortIcon('name')}
                  </th>
                  <th>{t('ID')}</th>
                  <th className="sortable" onClick={() => handleSort('description')} title={t('Description -- click to sort')}>
                    {t('Description')}{sortIcon('description')}
                  </th>
                  <th className="sortable" onClick={() => handleSort('stages')} title={t('Stage count -- click to sort')}>
                    {t('Stages')}{sortIcon('stages')}
                  </th>
                  <th className="sortable" onClick={() => handleSort('isBuiltIn')} title={t('Built-in -- click to sort')}>
                    {t('Built-in')}{sortIcon('isBuiltIn')}
                  </th>
                  <th className="sortable" onClick={() => handleSort('active')} title={t('Active -- click to sort')}>
                    {t('Active')}{sortIcon('active')}
                  </th>
                  <th className="sortable" onClick={() => handleSort('createdUtc')} title={t('Created date -- click to sort')}>
                    {t('Created')}{sortIcon('createdUtc')}
                  </th>
                  <th className="text-right">{t('Actions')}</th>
                </tr>
                <tr className="column-filter-row">
                  <td><input type="text" className="col-filter" value={colFilters.name} onChange={e => { setColFilters(f => ({ ...f, name: e.target.value })); setPageNumber(1); }} placeholder={t('Search...')} /></td>
                  <td></td>
                  <td><input type="text" className="col-filter" value={colFilters.description} onChange={e => { setColFilters(f => ({ ...f, description: e.target.value })); setPageNumber(1); }} placeholder={t('Search...')} /></td>
                  <td></td>
                  <td></td>
                  <td></td>
                  <td></td>
                  <td></td>
                </tr>
              </thead>
              <tbody>
                {paginated.map(p => (
                  <tr key={p.id} className="clickable" onClick={() => openEdit(p)}>
                    <td><strong>{p.name}</strong></td>
                    <td className="mono text-dim table-id-cell">
                      <span className="id-display">
                        <span className="id-value" title={p.id}>{p.id}</span>
                        <CopyButton text={p.id} onClick={e => e.stopPropagation()} />
                      </span>
                    </td>
                    <td className="text-dim">{p.description || '-'}</td>
                    <td>{formatStages(p.stages)}</td>
                    <td>{p.isBuiltIn ? <StatusBadge status="Built-in" /> : <span className="text-dim">-</span>}</td>
                    <td><StatusBadge status={p.active !== false ? 'Active' : 'Inactive'} /></td>
                    <td className="text-dim">{formatRelativeTime(p.createdUtc)}</td>
                    <td className="text-right" onClick={e => e.stopPropagation()}>
                      <ActionMenu id={`pipeline-${p.id}`} items={[
                        { label: 'View Detail', onClick: () => navigate(`/pipelines/${encodeURIComponent(p.name)}`) },
                        { label: 'Edit', onClick: () => openEdit(p) },
                        { label: 'View JSON', onClick: () => setJsonData({ open: true, title: `Pipeline: ${p.name}`, data: p }) },
                        ...(!p.isBuiltIn ? [{ label: 'Delete', danger: true as const, onClick: () => handleDelete(p.name) }] : []),
                      ]} />
                    </td>
                  </tr>
                ))}
                {paginated.length === 0 && (
                  <tr><td colSpan={8} className="text-dim">{t('No pipelines match the current filters.')}</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
}
