import { useEffect, useState, useMemo, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { createPromptTemplate, listPromptTemplates, resetPromptTemplate } from '../api/client';
import type { PromptTemplate } from '../types/models';
import { useAuth } from '../context/AuthContext';
import { canEdit as canEditScoped, type ScopeViewer } from '../lib/scoping';
import ScopeBadge from '../components/shared/ScopeBadge';
import DataTable, { type DataTableColumn } from '../components/shared/DataTable';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import RecordDetailModal from '../components/shared/RecordDetailModal';
import StatusBadge from '../components/shared/StatusBadge';
import PageHeader from '../components/shared/PageHeader';
import ErrorModal from '../components/shared/ErrorModal';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import { buildPromptTemplateDuplicatePayload } from '../lib/duplicates';

type SortDir = 'asc' | 'desc';
type SortField = 'name' | 'description' | 'category' | 'isBuiltIn' | 'contentLength' | 'active' | 'lastUpdateUtc';

const CATEGORY_OPTIONS = ['all', 'mission', 'persona', 'structure', 'commit', 'landing', 'agent', 'import'] as const;

export default function PromptTemplates() {
  const navigate = useNavigate();
  const { isAdmin, isTenantAdmin, user } = useAuth();
  const viewer: ScopeViewer = { isAdmin, isTenantAdmin, tenantId: user?.user?.tenantId, userId: user?.user?.id };
  const { t: translate, formatRelativeTime, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();
  const [templates, setTemplates] = useState<PromptTemplate[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });

  // Row-click view modal
  const [viewRecord, setViewRecord] = useState<Record<string, unknown> | null>(null);

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
  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('prompttemplates', load);

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

  async function handleDuplicate(template: PromptTemplate) {
    try {
      const created = await createPromptTemplate(buildPromptTemplateDuplicatePayload(template));
      pushToast('success', translate('Template "{{name}}" duplicated.', { name: created.name }));
      navigate(`/prompt-templates/${encodeURIComponent(created.name)}`);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : translate('Duplicate failed.'));
    }
  }

  const columns: DataTableColumn<PromptTemplate>[] = [
    {
      key: 'name', label: translate('Name'), required: true, sortKey: 'name', headerTitle: translate('Template name -- click to sort'),
      filter: <input type="text" className="col-filter" aria-label={translate('Filter by name')} value={colFilters.name} onChange={e => { setColFilters(f => ({ ...f, name: e.target.value })); setPageNumber(1); }} placeholder={translate('Filter...')} />,
      render: (template) => <strong>{template.name}</strong>,
    },
    {
      key: 'description', label: translate('Description'), sortKey: 'description', headerTitle: translate('Description -- click to sort'),
      // One line; the full description is in the tooltip.
      cellClassName: 'text-dim truncate-cell', cellTitle: (template) => template.description || undefined,
      clearFilter: () => setColFilters(f => ({ ...f, description: '' })),
      filter: <input type="text" className="col-filter" aria-label={translate('Filter by description')} value={colFilters.description} onChange={e => { setColFilters(f => ({ ...f, description: e.target.value })); setPageNumber(1); }} placeholder={translate('Filter...')} />,
      render: (template) => <span className="truncate-text">{template.description || '-'}</span>,
    },
    { key: 'category', label: translate('Category'), sortKey: 'category', headerTitle: translate('Category -- click to sort'), render: (template) => <StatusBadge status={template.category} /> },
    { key: 'visibility', label: translate('Visibility'), render: (template) => <ScopeBadge scope={template.scope} /> },
    {
      key: 'isBuiltIn', label: translate('Built-in'), sortKey: 'isBuiltIn', headerTitle: translate('Built-in -- click to sort'),
      render: (template) => (template.isBuiltIn ? <StatusBadge status="Built-in" /> : '-'),
    },
    {
      key: 'contentLength', label: translate('Content Length'), sortKey: 'contentLength', headerTitle: translate('Content length -- click to sort'),
      cellClassName: 'mono text-dim cell-nowrap',
      render: (template) => `${(template.content ?? '').length.toLocaleString()} ${translate('chars')}`,
    },
    {
      key: 'active', label: translate('Active'), sortKey: 'active', headerTitle: translate('Active -- click to sort'),
      render: (template) => <StatusBadge status={template.active !== false ? 'Active' : 'Inactive'} />,
    },
    {
      key: 'lastUpdateUtc', label: translate('Last Updated'), sortKey: 'lastUpdateUtc', headerTitle: translate('Last updated -- click to sort'),
      cellClassName: 'text-dim cell-nowrap', cellTitle: (template) => formatDateTime(template.lastUpdateUtc),
      render: (template) => formatRelativeTime(template.lastUpdateUtc),
    },
    {
      key: 'actions', label: translate('Actions'), fixed: true, interactive: true, className: 'text-right',
      render: (template) => (
        <ActionMenu id={`template-${template.id}`} items={[
          canEditScoped(viewer, template)
            ? { label: 'Edit', onClick: () => navigate(`/prompt-templates/${encodeURIComponent(template.name)}`) }
            : { label: 'Open', onClick: () => navigate(`/prompt-templates/${encodeURIComponent(template.name)}`) },
          { label: 'Duplicate', onClick: () => void handleDuplicate(template) },
          { label: 'View JSON', onClick: () => setJsonData({ open: true, title: `${translate('Template')}: ${template.name}`, data: template }) },
          ...(template.isBuiltIn && canEditScoped(viewer, template) ? [{ label: 'Reset to Default', danger: true as const, onClick: () => handleResetToDefault(template.name) }] : []),
        ]} />
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title={translate('Prompt Templates')}
        subtitle={translate('Prompt templates define the instructions and structure used when generating prompts for captains and missions.')}
        actions={(
          <>
            <button className="btn btn-primary btn-sm" onClick={() => navigate('/prompt-templates/create')}>
              + {translate('Prompt Template')}
            </button>
          </>
        )}
      />

      <ErrorModal error={error} onClose={() => setError('')} />

      {/* JSON Viewer */}
      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />

      {/* Row-click View Modal */}
      <RecordDetailModal
        open={!!viewRecord}
        title={typeof viewRecord?.name === 'string' ? viewRecord.name : translate('Prompt Template')}
        subtitle={translate('Prompt Template')}
        record={viewRecord}
        sizeClassName="modal-large modal-prompt-template"
        onClose={() => setViewRecord(null)}
        onEdit={() => { const r = viewRecord; setViewRecord(null); navigate(`/prompt-templates/${encodeURIComponent((r as { name: string }).name)}`); }}
        editLabel={translate('Open Details')}
      />

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

      <DataTable
        tableKey="prompttemplates"
        columns={columns}
        rows={paginated}
        rowKey={(template) => template.id}
        onRowClick={(template) => setViewRecord(template as unknown as Record<string, unknown>)}
        sort={{ field: sortField, dir: sortDir, onSort: (f) => handleSort(f as SortField) }}
        pagination={{
          pageNumber: currentPage, pageSize, totalPages, totalRecords: sorted.length,
          onPageChange: (p) => setPageNumber(p),
          onPageSizeChange: (s) => { setPageSize(s); setPageNumber(1); },
        }}
        autoRefresh={{ seconds: refreshSeconds, onChange: setRefreshSeconds }}
        onRefresh={load}
        refreshTitle={translate('Refresh prompt template data')}
        emptyMessage={translate('No prompt templates match the current filters.')}
        placeholder={templates.length > 0 ? undefined : loading
          ? <p className="text-dim">{translate('Loading...')}</p>
          : <p className="text-dim">{translate('No prompt templates found.')}</p>}
      />
    </div>
  );
}
