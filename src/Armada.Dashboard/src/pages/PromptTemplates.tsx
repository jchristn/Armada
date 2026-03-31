import { useEffect, useState, useMemo, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { listPromptTemplates, resetPromptTemplate } from '../api/client';
import type { PromptTemplate } from '../types/models';
import Pagination from '../components/shared/Pagination';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import StatusBadge from '../components/shared/StatusBadge';
import RefreshButton from '../components/shared/RefreshButton';
import ErrorModal from '../components/shared/ErrorModal';

type SortDir = 'asc' | 'desc';
type SortField = 'name' | 'description' | 'category' | 'isBuiltIn' | 'contentLength' | 'active' | 'lastUpdateUtc';

const CATEGORY_OPTIONS = ['all', 'mission', 'persona', 'structure', 'commit', 'landing', 'agent'] as const;

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

export default function PromptTemplates() {
  const navigate = useNavigate();
  const [templates, setTemplates] = useState<PromptTemplate[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });

  // Confirm dialog
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

  // Sorting
  const [sortField, setSortField] = useState<SortField>('category');
  const [sortDir, setSortDir] = useState<SortDir>('asc');

  // Category tab bar filter
  const [categoryFilter, setCategoryFilter] = useState('all');

  // Column filters
  const [colFilters, setColFilters] = useState({ name: '', description: '' });

  // Pagination
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(25);

  const load = useCallback(async () => {
    try {
      setLoading(true);
      const result = await listPromptTemplates({ pageSize: 9999 });
      setTemplates(result.objects);
      setError('');
    } catch {
      setError('Failed to load prompt templates.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  // Filtered rows
  const filtered = useMemo(() => {
    return templates.filter(t =>
      (!colFilters.name || t.name.toLowerCase().includes(colFilters.name.toLowerCase())) &&
      (!colFilters.description || (t.description ?? '').toLowerCase().includes(colFilters.description.toLowerCase())) &&
      (categoryFilter === 'all' || t.category.toLowerCase() === categoryFilter.toLowerCase())
    );
  }, [templates, colFilters, categoryFilter]);

  // Sorted rows
  const sorted = useMemo(() => {
    const arr = [...filtered];
    arr.sort((a, b) => {
      let va: string | number = '';
      let vb: string | number = '';
      if (sortField === 'name') { va = a.name.toLowerCase(); vb = b.name.toLowerCase(); }
      else if (sortField === 'description') { va = (a.description ?? '').toLowerCase(); vb = (b.description ?? '').toLowerCase(); }
      else if (sortField === 'category') { va = a.category.toLowerCase(); vb = b.category.toLowerCase(); }
      else if (sortField === 'isBuiltIn') { va = a.isBuiltIn ? 1 : 0; vb = b.isBuiltIn ? 1 : 0; }
      else if (sortField === 'contentLength') { va = (a.content ?? '').length; vb = (b.content ?? '').length; }
      else if (sortField === 'active') { va = a.active ? 1 : 0; vb = b.active ? 1 : 0; }
      else if (sortField === 'lastUpdateUtc') { va = a.lastUpdateUtc ?? ''; vb = b.lastUpdateUtc ?? ''; }
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

  // Actions
  function handleResetToDefault(name: string) {
    setConfirm({
      open: true,
      title: 'Reset to Default',
      message: `Reset template "${name}" to its built-in default content? Any custom edits will be lost.`,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try { await resetPromptTemplate(name); load(); } catch { setError('Reset failed.'); }
      },
    });
  }

  return (
    <div>
      <div className="view-header">
        <div>
          <h2>Prompt Templates</h2>
          <p className="text-dim view-subtitle">Prompt templates define the instructions and structure used when generating prompts for captains and missions.</p>
        </div>
        <div className="view-actions">
          <RefreshButton onRefresh={load} title="Refresh prompt template data" />
        </div>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />

      {/* JSON Viewer */}
      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />

      {/* Confirm Dialog */}
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message}
        onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />

      {/* Category tab bar */}
      {templates.length > 0 && (
        <div style={{ display: 'flex', gap: '0.25rem', marginBottom: '1rem', flexWrap: 'wrap' }}>
          {CATEGORY_OPTIONS.map(cat => (
            <button
              key={cat}
              className={`btn btn-sm${categoryFilter === cat ? ' btn-primary' : ''}`}
              onClick={() => { setCategoryFilter(cat); setPageNumber(1); }}
              style={{ textTransform: 'capitalize', padding: '0.25rem 0.75rem', fontSize: '0.85rem' }}
            >
              {cat === 'all' ? 'All' : cat}
            </button>
          ))}
        </div>
      )}

      {loading && templates.length === 0 && <p className="text-dim">Loading...</p>}
      {!loading && templates.length === 0 && <p className="text-dim">No prompt templates found.</p>}

      {templates.length > 0 && (
        <>
          <Pagination pageNumber={currentPage} pageSize={pageSize} totalPages={totalPages}
            totalRecords={sorted.length}
            onPageChange={p => setPageNumber(p)} onPageSizeChange={s => { setPageSize(s); setPageNumber(1); }} />

          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th className="sortable" onClick={() => handleSort('name')} title="Template name -- click to sort">
                    Name{sortIcon('name')}
                  </th>
                  <th className="sortable" onClick={() => handleSort('description')} title="Description -- click to sort">
                    Description{sortIcon('description')}
                  </th>
                  <th className="sortable" onClick={() => handleSort('category')} title="Category -- click to sort">
                    Category{sortIcon('category')}
                  </th>
                  <th className="sortable" onClick={() => handleSort('isBuiltIn')} title="Built-in -- click to sort">
                    Built-in{sortIcon('isBuiltIn')}
                  </th>
                  <th className="sortable" onClick={() => handleSort('contentLength')} title="Content length -- click to sort">
                    Content Length{sortIcon('contentLength')}
                  </th>
                  <th className="sortable" onClick={() => handleSort('active')} title="Active -- click to sort">
                    Active{sortIcon('active')}
                  </th>
                  <th className="sortable" onClick={() => handleSort('lastUpdateUtc')} title="Last updated -- click to sort">
                    Last Updated{sortIcon('lastUpdateUtc')}
                  </th>
                  <th className="text-right">Actions</th>
                </tr>
                <tr className="column-filter-row">
                  <td><input type="text" className="col-filter" value={colFilters.name} onChange={e => { setColFilters(f => ({ ...f, name: e.target.value })); setPageNumber(1); }} placeholder="Filter..." /></td>
                  <td><input type="text" className="col-filter" value={colFilters.description} onChange={e => { setColFilters(f => ({ ...f, description: e.target.value })); setPageNumber(1); }} placeholder="Filter..." /></td>
                  <td></td>
                  <td></td>
                  <td></td>
                  <td></td>
                  <td></td>
                  <td></td>
                </tr>
              </thead>
              <tbody>
                {paginated.map(t => (
                  <tr key={t.id} className="clickable" onClick={() => navigate(`/prompt-templates/${encodeURIComponent(t.name)}`)}>
                    <td><strong>{t.name}</strong></td>
                    <td className="text-dim">{t.description || '-'}</td>
                    <td><StatusBadge status={t.category} /></td>
                    <td>{t.isBuiltIn ? <StatusBadge status="Built-in" /> : '-'}</td>
                    <td className="mono text-dim">{(t.content ?? '').length.toLocaleString()} chars</td>
                    <td><StatusBadge status={t.active !== false ? 'Active' : 'Inactive'} /></td>
                    <td className="text-dim">{formatTime(t.lastUpdateUtc)}</td>
                    <td className="text-right" onClick={e => e.stopPropagation()}>
                      <ActionMenu id={`template-${t.id}`} items={[
                        { label: 'Edit', onClick: () => navigate(`/prompt-templates/${encodeURIComponent(t.name)}`) },
                        { label: 'View JSON', onClick: () => setJsonData({ open: true, title: `Template: ${t.name}`, data: t }) },
                        ...(t.isBuiltIn ? [{ label: 'Reset to Default', danger: true as const, onClick: () => handleResetToDefault(t.name) }] : []),
                      ]} />
                    </td>
                  </tr>
                ))}
                {paginated.length === 0 && (
                  <tr><td colSpan={8} className="text-dim">No prompt templates match the current filters.</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
}
