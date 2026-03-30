import { useState, useEffect, useMemo, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { listPersonas, listPromptTemplates, createPersona, updatePersona, deletePersona } from '../api/client';
import type { Persona } from '../types/models';
import Pagination from '../components/shared/Pagination';
import ActionMenu from '../components/shared/ActionMenu';
import StatusBadge from '../components/shared/StatusBadge';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import CopyButton from '../components/shared/CopyButton';
import RefreshButton from '../components/shared/RefreshButton';
import ErrorModal from '../components/shared/ErrorModal';

type SortDir = 'asc' | 'desc';
type SortField = 'name' | 'description' | 'promptTemplateName' | 'isBuiltIn' | 'active' | 'createdUtc';

function formatTime(utc: string | null): string {
  if (!utc) return '-';
  const d = new Date(utc);
  const now = new Date();
  const diff = now.getTime() - d.getTime();
  if (diff < 60000) return 'just now';
  if (diff < 3600000) return `${Math.floor(diff / 60000)}m ago`;
  if (diff < 86400000) return `${Math.floor(diff / 3600000)}h ago`;
  return d.toLocaleDateString();
}

export default function Personas() {
  const navigate = useNavigate();
  const [personas, setPersonas] = useState<Persona[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Modal state
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState<Persona | null>(null);
  const [form, setForm] = useState({ name: '', description: '', promptTemplateName: '' });
  const [templateNames, setTemplateNames] = useState<string[]>([]);

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });

  // Confirm dialog
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

  // Sorting
  const [sortField, setSortField] = useState<SortField>('name');
  const [sortDir, setSortDir] = useState<SortDir>('asc');

  // Column filters
  const [colFilters, setColFilters] = useState({ name: '', description: '', promptTemplateName: '' });

  // Pagination
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(25);

  const load = useCallback(async () => {
    try {
      setLoading(true);
      const result = await listPersonas({ pageSize: 9999 });
      setPersonas(result.objects);
      const templateResult = await listPromptTemplates({ pageSize: 9999 });
      setTemplateNames(templateResult.objects.map(t => t.name));
      setError('');
    } catch {
      setError('Failed to load personas.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  // Filtered rows
  const filtered = useMemo(() => {
    return personas.filter(p =>
      (!colFilters.name || p.name.toLowerCase().includes(colFilters.name.toLowerCase())) &&
      (!colFilters.description || (p.description ?? '').toLowerCase().includes(colFilters.description.toLowerCase())) &&
      (!colFilters.promptTemplateName || p.promptTemplateName.toLowerCase().includes(colFilters.promptTemplateName.toLowerCase()))
    );
  }, [personas, colFilters]);

  // Sorted rows
  const sorted = useMemo(() => {
    const arr = [...filtered];
    arr.sort((a, b) => {
      let va: string = '';
      let vb: string = '';
      switch (sortField) {
        case 'description': va = (a.description ?? '').toLowerCase(); vb = (b.description ?? '').toLowerCase(); break;
        case 'promptTemplateName': va = a.promptTemplateName.toLowerCase(); vb = b.promptTemplateName.toLowerCase(); break;
        case 'isBuiltIn': va = a.isBuiltIn ? '1' : '0'; vb = b.isBuiltIn ? '1' : '0'; break;
        case 'active': va = a.active ? '1' : '0'; vb = b.active ? '1' : '0'; break;
        case 'createdUtc': va = a.createdUtc; vb = b.createdUtc; break;
        default: va = a.name.toLowerCase(); vb = b.name.toLowerCase();
      }
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
  function openCreate() { setForm({ name: '', description: '', promptTemplateName: '' }); setEditing(null); setShowForm(true); }
  function openEdit(p: Persona) { setForm({ name: p.name, description: p.description ?? '', promptTemplateName: p.promptTemplateName }); setEditing(p); setShowForm(true); }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    try {
      const payload: Record<string, unknown> = { name: form.name, promptTemplateName: form.promptTemplateName };
      if (form.description) payload.description = form.description;
      if (editing) await updatePersona(editing.name, payload);
      else await createPersona(payload as Partial<Persona>);
      setShowForm(false);
      load();
    } catch { setError('Save failed.'); }
  }

  function handleDelete(name: string) {
    setConfirm({
      open: true,
      title: 'Delete Persona',
      message: `Delete persona "${name}"? This cannot be undone.`,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try { await deletePersona(name); load(); } catch { setError('Delete failed.'); }
      },
    });
  }

  return (
    <div>
      <div className="view-header">
        <div>
          <h2>Personas</h2>
          <p className="text-dim view-subtitle">Named configurations that define how captains behave when executing missions.</p>
        </div>
        <div className="view-actions">
          <button className="btn btn-primary btn-sm" onClick={openCreate}>+ Persona</button>
          <RefreshButton onRefresh={load} title="Refresh persona data" />
        </div>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />

      {/* Create/Edit Modal */}
      {showForm && (
        <div className="modal-overlay" onClick={() => setShowForm(false)}>
          <form className="modal" onClick={e => e.stopPropagation()} onSubmit={handleSubmit}>
            <h3>{editing ? 'Edit Persona' : 'Create Persona'}</h3>
            <label>Name<input value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} required disabled={!!editing} /></label>
            <label>Description
              <textarea value={form.description} onChange={e => setForm({ ...form, description: e.target.value })} rows={3} placeholder="Optional description of this persona..." />
            </label>
            <label>Prompt Template Name
              <select value={form.promptTemplateName} onChange={e => setForm({ ...form, promptTemplateName: e.target.value })} required>
                <option value="">Select a template...</option>
                {templateNames.map(name => (
                  <option key={name} value={name}>{name}</option>
                ))}
              </select>
            </label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary">Save</button>
              <button type="button" className="btn" onClick={() => setShowForm(false)}>Cancel</button>
            </div>
          </form>
        </div>
      )}

      {/* JSON Viewer */}
      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />

      {/* Confirm Dialog */}
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message}
        onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />

      {loading && personas.length === 0 && <p className="text-dim">Loading...</p>}
      {!loading && personas.length === 0 && <p className="text-dim">No personas configured.</p>}

      {personas.length > 0 && (
        <>
          <Pagination pageNumber={currentPage} pageSize={pageSize} totalPages={totalPages}
            totalRecords={sorted.length}
            onPageChange={p => setPageNumber(p)} onPageSizeChange={s => { setPageSize(s); setPageNumber(1); }} />

          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th className="sortable" onClick={() => handleSort('name')} title="Persona name -- click to sort">
                    Name{sortIcon('name')}
                  </th>
                  <th>ID</th>
                  <th className="sortable" onClick={() => handleSort('description')} title="Description -- click to sort">
                    Description{sortIcon('description')}
                  </th>
                  <th className="sortable" onClick={() => handleSort('promptTemplateName')} title="Prompt template -- click to sort">
                    Prompt Template{sortIcon('promptTemplateName')}
                  </th>
                  <th className="sortable" onClick={() => handleSort('isBuiltIn')} title="Built-in -- click to sort">
                    Built-in{sortIcon('isBuiltIn')}
                  </th>
                  <th className="sortable" onClick={() => handleSort('active')} title="Active -- click to sort">
                    Active{sortIcon('active')}
                  </th>
                  <th className="sortable" onClick={() => handleSort('createdUtc')} title="Created date -- click to sort">
                    Created{sortIcon('createdUtc')}
                  </th>
                  <th className="text-right">Actions</th>
                </tr>
                <tr className="column-filter-row">
                  <td><input type="text" className="col-filter" value={colFilters.name} onChange={e => { setColFilters(f => ({ ...f, name: e.target.value })); setPageNumber(1); }} placeholder="Filter..." /></td>
                  <td></td>
                  <td><input type="text" className="col-filter" value={colFilters.description} onChange={e => { setColFilters(f => ({ ...f, description: e.target.value })); setPageNumber(1); }} placeholder="Filter..." /></td>
                  <td><input type="text" className="col-filter" value={colFilters.promptTemplateName} onChange={e => { setColFilters(f => ({ ...f, promptTemplateName: e.target.value })); setPageNumber(1); }} placeholder="Filter..." /></td>
                  <td></td>
                  <td></td>
                  <td></td>
                  <td></td>
                </tr>
              </thead>
              <tbody>
                {paginated.map(p => (
                  <tr key={p.name} className="clickable" onClick={() => openEdit(p)}>
                    <td><strong>{p.name}</strong></td>
                    <td className="mono text-dim table-id-cell">
                      <span className="id-display">
                        <span className="id-value" title={p.id}>{p.id}</span>
                        <CopyButton text={p.id} onClick={e => e.stopPropagation()} />
                      </span>
                    </td>
                    <td className="text-dim">{p.description ?? '-'}</td>
                    <td className="mono text-dim">{p.promptTemplateName}</td>
                    <td>{p.isBuiltIn ? <StatusBadge status="Built-in" /> : <span className="text-dim">-</span>}</td>
                    <td><StatusBadge status={p.active ? 'Active' : 'Inactive'} /></td>
                    <td className="text-dim">{formatTime(p.createdUtc)}</td>
                    <td className="text-right" onClick={e => e.stopPropagation()}>
                      <ActionMenu id={`persona-${p.name}`} items={[
                        { label: 'View Detail', onClick: () => navigate(`/personas/${encodeURIComponent(p.name)}`) },
                        { label: 'Edit', onClick: () => openEdit(p) },
                        { label: 'View JSON', onClick: () => setJsonData({ open: true, title: `Persona: ${p.name}`, data: p }) },
                        ...(!p.isBuiltIn ? [{ label: 'Delete', danger: true as const, onClick: () => handleDelete(p.name) }] : []),
                      ]} />
                    </td>
                  </tr>
                ))}
                {paginated.length === 0 && (
                  <tr><td colSpan={8} className="text-dim">No personas match the current filters.</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
}
