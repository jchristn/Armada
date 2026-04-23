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
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';

type SortDir = 'asc' | 'desc';
type SortField = 'name' | 'description' | 'category' | 'isBuiltIn' | 'contentLength' | 'active' | 'lastUpdateUtc';

const CATEGORY_OPTIONS = ['all', 'mission', 'persona', 'structure', 'commit', 'landing', 'agent'] as const;

export default function PromptTemplates() {
  const navigate = useNavigate();
  const { t: translate, formatRelativeTime, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();
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
      setError(translate('Failed to load prompt templates.'));
    } finally {
      setLoading(false);
    }
  }, [translate]);

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
      title: translate('Reset to Default'),
      message: translate('Reset template "{{name}}" to its built-in default content? Any custom edits will be lost.', { name }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await resetPromptTemplate(name);
          pushToast('success', translate('Template "{{name}}" reset to default.', { name }));
          load();
        } catch { setError(translate('Reset failed.')); }
      },
    });
  }

  return (
    <div>
      <div className="view-header">
        <div>
          <h2>{translate('Prompt Templates')}</h2>
          <p className="text-dim view-subtitle">{translate('Prompt templates define the instructions and structure used when generating prompts for captains and missions.')}</p>
        </div>
        <div className="view-actions">
          <RefreshButton onRefresh={load} title={translate('Refresh prompt template data')} />
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
              {cat === 'all' ? translate('All') : translate(cat)}
            </button>
          ))}
        </div>
      )}

      {loading && templates.length === 0 && <p className="text-dim">{translate('Loading...')}</p>}
      {!loading && templates.length === 0 && <p className="text-dim">{translate('No prompt templates found.')}</p>}

      {templates.length > 0 && (
        <>
          <Pagination pageNumber={currentPage} pageSize={pageSize} totalPages={totalPages}
            totalRecords={sorted.length}
            onPageChange={p => setPageNumber(p)} onPageSizeChange={s => { setPageSize(s); setPageNumber(1); }} />

          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th className="sortable" onClick={() => handleSort('name')} title={translate('Template name -- click to sort')}>
                    {translate('Name')}{sortIcon('name')}
                  </th>
                  <th className="sortable" onClick={() => handleSort('description')} title={translate('Description -- click to sort')}>
                    {translate('Description')}{sortIcon('description')}
                  </th>
                  <th className="sortable" onClick={() => handleSort('category')} title={translate('Category -- click to sort')}>
                    {translate('Category')}{sortIcon('category')}
                  </th>
                  <th className="sortable" onClick={() => handleSort('isBuiltIn')} title={translate('Built-in -- click to sort')}>
                    {translate('Built-in')}{sortIcon('isBuiltIn')}
                  </th>
                  <th className="sortable" onClick={() => handleSort('contentLength')} title={translate('Content length -- click to sort')}>
                    {translate('Content Length')}{sortIcon('contentLength')}
                  </th>
                  <th className="sortable" onClick={() => handleSort('active')} title={translate('Active -- click to sort')}>
                    {translate('Active')}{sortIcon('active')}
                  </th>
                  <th className="sortable" onClick={() => handleSort('lastUpdateUtc')} title={translate('Last updated -- click to sort')}>
                    {translate('Last Updated')}{sortIcon('lastUpdateUtc')}
                  </th>
                  <th className="text-right">{translate('Actions')}</th>
                </tr>
                <tr className="column-filter-row">
                  <td><input type="text" className="col-filter" value={colFilters.name} onChange={e => { setColFilters(f => ({ ...f, name: e.target.value })); setPageNumber(1); }} placeholder={translate('Filter...')} /></td>
                  <td><input type="text" className="col-filter" value={colFilters.description} onChange={e => { setColFilters(f => ({ ...f, description: e.target.value })); setPageNumber(1); }} placeholder={translate('Filter...')} /></td>
                  <td></td>
                  <td></td>
                  <td></td>
                  <td></td>
                  <td></td>
                  <td></td>
                </tr>
              </thead>
              <tbody>
                {paginated.map(template => (
                  <tr key={template.id} className="clickable" onClick={() => navigate(`/prompt-templates/${encodeURIComponent(template.name)}`)}>
                    <td><strong>{template.name}</strong></td>
                    <td className="text-dim">{template.description || '-'}</td>
                    <td><StatusBadge status={template.category} /></td>
                    <td>{template.isBuiltIn ? <StatusBadge status="Built-in" /> : '-'}</td>
                    <td className="mono text-dim">{(template.content ?? '').length.toLocaleString()} {translate('chars')}</td>
                    <td><StatusBadge status={template.active !== false ? 'Active' : 'Inactive'} /></td>
                    <td className="text-dim" title={formatDateTime(template.lastUpdateUtc)}>{formatRelativeTime(template.lastUpdateUtc)}</td>
                    <td className="text-right" onClick={e => e.stopPropagation()}>
                      <ActionMenu id={`template-${template.id}`} items={[
                        { label: 'Edit', onClick: () => navigate(`/prompt-templates/${encodeURIComponent(template.name)}`) },
                        { label: 'View JSON', onClick: () => setJsonData({ open: true, title: `${translate('Template')}: ${template.name}`, data: template }) },
                        ...(template.isBuiltIn ? [{ label: 'Reset to Default', danger: true as const, onClick: () => handleResetToDefault(template.name) }] : []),
                      ]} />
                    </td>
                  </tr>
                ))}
                {paginated.length === 0 && (
                  <tr><td colSpan={8} className="text-dim">{translate('No prompt templates match the current filters.')}</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
}
