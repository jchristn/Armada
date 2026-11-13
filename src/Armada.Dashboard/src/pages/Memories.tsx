import ActionMenu from '../components/shared/ActionMenu';
import DialogShell from '../components/shared/DialogShell';
import ScopeSelect from '../components/shared/ScopeSelect';
import { useAuth } from '../context/AuthContext';
import { resolveCreateScope } from '../lib/scoping';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { apiErrorMessage, enumerateMemories, deleteMemory, createMemory, listVessels } from '../api/client';
import { listAll } from '../api/paging';
import type { Memory, MemoryEnumerateQuery, MemorySortField, MemoryType, ScopeEnum, Vessel } from '../types/models';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import PageHeader from '../components/shared/PageHeader';
import DataTable, { type DataTableColumn, type DataTableSort } from '../components/shared/DataTable';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import ScopeBadge from '../components/shared/ScopeBadge';
import { MEMORY_TYPES } from '../lib/configuration';
import { usePersistedPageSize } from '../lib/usePersistedPageSize';
import { useDeleteProgress } from '../lib/useDeleteProgress';

const TYPES = MEMORY_TYPES;

type SortDir = 'asc' | 'desc';
type SortField = 'vessel' | 'type' | 'topic' | 'summary' | 'salience' | 'visibility' | 'updated';

/** Server sort column for each sortable header. */
const SORT_FIELDS: Record<SortField, MemorySortField> = {
  vessel: 'Vessel', type: 'Type', topic: 'Topic', summary: 'Summary', salience: 'Salience', visibility: 'Visibility', updated: 'Updated',
};

const MEMORY_TYPE_TIPS: Record<string, string> = {
  Episodic: 'Episodic: what happened and when, such as events, actions taken and key decisions.',
  Semantic: 'Semantic: a fact on its own, such as a preference or a rule about the code.',
  Procedural: 'Procedural: how to do something, such as a workflow or checklist.',
};

/**
 * Memories management surface. Lists the durable agent memories the Recorder distills from voyages
 * (episodic / semantic / procedural), with type and text filters, a JSON detail view, and delete for
 * pruning stale entries. Memories are primarily written by agents over MCP; operators curate here.
 */
export default function Memories() {
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const navigate = useNavigate();
  const { pushToast } = useNotifications();
  const deletion = useDeleteProgress();
  const { isAdmin, isTenantAdmin, user } = useAuth();
  const viewer = { isAdmin, isTenantAdmin, tenantId: user?.user?.tenantId, userId: user?.user?.id };
  const [draft, setDraft] = useState<Partial<Memory> | null>(null);
  const [tagsText, setTagsText] = useState('');
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState('');

  function openDuplicate(memory: Memory) {
    setSaveError('');
    setTagsText(memory.tags.join(', '));
    // No key: POST upserts by key, so reusing the source key would replace the original.
    setDraft({ type: memory.type, topic: memory.topic, summary: memory.summary, content: memory.content,
      salience: memory.salience, vesselId: memory.vesselId || memory.sourceVesselId, tags: [...memory.tags], scope: resolveCreateScope(viewer), sourceKind: 'Manual' });
  }

  async function saveDuplicate(event: React.FormEvent) {
    event.preventDefault();
    if (!draft || saving) return;
    setSaving(true);
    try {
      await createMemory({ ...draft, tags: tagsText.split(',').map(tag => tag.trim()).filter(Boolean) });
      setDraft(null);
      pushToast('success', t('Memory duplicated.'));
      await load();
    } catch (error) { setSaveError(error instanceof Error ? error.message : t('Save failed.')); }
    finally { setSaving(false); }
  }

  const [memories, setMemories] = useState<Memory[]>([]);
  const [loading, setLoading] = useState(false);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = usePersistedPageSize('memories', 25);
  const [totalPages, setTotalPages] = useState(1);
  const [totalRecords, setTotalRecords] = useState(0);
  const [typeFilter, setTypeFilter] = useState<'' | MemoryType>('');
  const [search, setSearch] = useState('');
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });
  const [confirm, setConfirm] = useState<{ open: boolean; id: string }>({ open: false, id: '' });
  const [vessels, setVessels] = useState<Vessel[]>([]);
  // Sort and column filters are sent to POST /api/v1/memories/enumerate; the server filters, sorts and pages.
  const [sortField, setSortField] = useState<SortField>('updated');
  const [sortDir, setSortDir] = useState<SortDir>('desc');
  const [colFilters, setColFilters] = useState({ vessel: '', topic: '', summary: '', salience: '', visibility: '' });

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const query: MemoryEnumerateQuery = {
        pageNumber,
        pageSize,
        sortBy: SORT_FIELDS[sortField],
        order: sortDir === 'asc' ? 'CreatedAscending' : 'CreatedDescending',
      };
      if (typeFilter) query.type = typeFilter;
      if (search.trim()) query.search = search.trim();
      if (colFilters.vessel.trim()) query.vesselName = colFilters.vessel.trim();
      if (colFilters.topic.trim()) query.topicContains = colFilters.topic.trim();
      if (colFilters.summary.trim()) query.summaryContains = colFilters.summary.trim();
      if (colFilters.visibility) query.scope = colFilters.visibility as ScopeEnum;
      const minSalience = colFilters.salience.trim() === '' ? NaN : Number(colFilters.salience);
      if (!Number.isNaN(minSalience)) query.minSalience = minSalience;
      const result = await enumerateMemories(query);
      setMemories(result.objects ?? []);
      setTotalPages(result.totalPages ?? 1);
      setTotalRecords(result.totalRecords ?? 0);
    } catch {
      pushToast('error', t('Failed to load memories.'));
    } finally {
      setLoading(false);
    }
  }, [pageNumber, pageSize, sortField, sortDir, typeFilter, search, colFilters, pushToast, t]);

  useEffect(() => { void load(); }, [load]);

  useEffect(() => {
    listAll(listVessels).then(r => setVessels(r.objects || [])).catch(() => {});
  }, []);

  const vesselMap = useMemo(() => new Map(vessels.map(v => [v.id, v.name])), [vessels]);
  const memoryVesselId = (m: Memory) => m.vesselId || m.sourceVesselId || null;
  const vesselName = useCallback((id: string | null) => (id ? vesselMap.get(id) || id : '-'), [vesselMap]);

  const handleDelete = async () => {
    const id = confirm.id;
    setConfirm({ open: false, id: '' });
    if (!(await deletion.run({ kind: 'memory', action: () => deleteMemory(id), errorMessage: (err) => apiErrorMessage(err, t('Failed to delete memory.')) }))) return;
    pushToast('success', t('Memory deleted.'));
    void load();
  };

  const memorySummary = (m: Memory) => m.summary || m.content;

  function handleSort(field: SortField) {
    if (sortField === field) setSortDir(d => d === 'asc' ? 'desc' : 'asc');
    else { setSortField(field); setSortDir('asc'); }
    setPageNumber(1);
  }

  function setColFilter(key: keyof typeof colFilters, value: string) {
    setColFilters(f => ({ ...f, [key]: value }));
    setPageNumber(1);
  }

  const sort: DataTableSort = { field: sortField, dir: sortDir, onSort: (f) => handleSort(f as SortField) };

  const columns: DataTableColumn<Memory>[] = [
    {
      key: 'vessel', label: t('Vessel'), sortKey: 'vessel', headerTitle: t('Repository this memory is about, or that the work came from. Click a name to open that vessel; hover for its ID. A dash means it applies to no single vessel.'), cellClassName: 'text-dim', interactive: true,
      clearFilter: () => setColFilter('vessel', ''),
      filter: <input type="text" className="col-filter" aria-label={t('Filter by vessel')} title={t('Show only memories whose vessel name contains this text. Not case sensitive.')} value={colFilters.vessel} onChange={e => setColFilter('vessel', e.target.value)} placeholder={t('Search...')} />,
      render: (m) => {
        const vesselId = memoryVesselId(m);
        return vesselId ? (
          <span className="cell-clip" title={vesselId}>
            <a href="#" onClick={e => { e.preventDefault(); navigate(`/vessels/${vesselId}`); }}>{vesselName(vesselId)}</a>
          </span>
        ) : '-';
      },
    },
    {
      key: 'type', label: t('Type'), sortKey: 'type', headerTitle: t('Episodic memories record what happened, semantic ones record facts, procedural ones record how to do something.'), cellClassName: 'cell-nowrap',
      clearFilter: () => { setTypeFilter(''); setPageNumber(1); },
      filter: (
        <select
          className="col-filter"
          aria-label={t('Filter by type')}
          title={t('Show memories of every type, or only episodic, semantic or procedural ones.')}
          value={typeFilter}
          onChange={e => { setTypeFilter(e.target.value as '' | MemoryType); setPageNumber(1); }}
        >
          <option value="">{t('All types')}</option>
          {TYPES.map(ty => <option key={ty} value={ty}>{t(ty)}</option>)}
        </select>
      ),
      render: (m) => <span className={`tag tag-${m.type.toLowerCase()}`} title={t(MEMORY_TYPE_TIPS[m.type] ?? m.type)}>{t(m.type)}</span>,
    },
    {
      key: 'topic', label: t('Topic'), sortKey: 'topic', headerTitle: t('Group within the type that related memories share, for example code-style or deployment. A dash means none.'), cellClassName: 'text-dim',
      clearFilter: () => setColFilter('topic', ''),
      filter: <input type="text" className="col-filter" aria-label={t('Filter by topic')} title={t('Show only memories whose topic contains this text. Not case sensitive.')} value={colFilters.topic} onChange={e => setColFilter('topic', e.target.value)} placeholder={t('Search...')} />,
      render: (m) => m.topic || '-',
    },
    {
      // One line; the full summary (or content) is in the tooltip.
      key: 'summary', label: t('Summary'), required: true, sortKey: 'summary', headerTitle: t('One-line summary of the memory (or its content when there is no summary). Hover for the full text; click a row for every field.'), cellClassName: 'truncate-cell', cellTitle: memorySummary,
      clearFilter: () => setColFilter('summary', ''),
      filter: <input type="text" className="col-filter" aria-label={t('Filter by summary')} title={t('Show only memories whose summary (or content) contains this text. Not case sensitive.')} value={colFilters.summary} onChange={e => setColFilter('summary', e.target.value)} placeholder={t('Search...')} />,
      render: (m) => <span className="truncate-text">{memorySummary(m)}</span>,
    },
    {
      key: 'salience', label: t('Salience'), sortKey: 'salience', headerTitle: t('Importance from 0.00 to 1.00 (default 0.50). Higher memories are recalled first when agents search memory.'), cellClassName: 'text-dim',
      clearFilter: () => setColFilter('salience', ''),
      filter: <input type="number" min={0} max={1} step={0.05} className="col-filter" aria-label={t('Minimum salience')} title={t('Show only memories with at least this salience.')} value={colFilters.salience} onChange={e => setColFilter('salience', e.target.value)} placeholder={t('Min')} />,
      render: (m) => m.salience.toFixed(2),
    },
    {
      key: 'visibility', label: t('Visibility'), sortKey: 'visibility', headerTitle: t('Tenant-wide memories are recalled for everyone in the tenant; personal ones only for their owner.'),
      clearFilter: () => setColFilter('visibility', ''),
      filter: (
        <select className="col-filter" aria-label={t('Filter by visibility')} title={t('Show only tenant-wide or only personal memories.')} value={colFilters.visibility} onChange={e => setColFilter('visibility', e.target.value)}>
          <option value="">{t('All')}</option>
          <option value="TenantWide">{t('Tenant-wide')}</option>
          <option value="UserSpecific">{t('Personal')}</option>
        </select>
      ),
      render: (m) => <ScopeBadge scope={m.scope} />,
    },
    {
      key: 'updated', label: t('Updated'), sortKey: 'updated', headerTitle: t('When this memory was last written or revised. Hover a value for the exact time.'), cellClassName: 'text-dim cell-nowrap', cellTitle: (m) => formatDateTime(m.lastUpdateUtc),
      render: (m) => formatRelativeTime(m.lastUpdateUtc),
    },
    {
      key: 'actions', label: t('Actions'), headerTitle: t('Copy the memory ID, or delete the memory.'), fixed: true, interactive: true, className: 'text-right',
      render: (m) => (
        <ActionMenu id={`memory-${m.id}`} items={[
          { label: 'Duplicate', title: 'Edit a new memory before saving. Its ID, key and agent history are not reused.', onClick: () => openDuplicate(m) },
          { label: 'View JSON', onClick: () => setJsonData({ open: true, title: `${t('Memory')}: ${m.id}`, data: m }) },
          { label: 'Delete', danger: true, onClick: () => setConfirm({ open: true, id: m.id }) },
        ]} />
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title={t('Memory')}
        subtitle={t('Durable memories distilled from voyages: episodic, semantic, and procedural.')}
      />

      <div className="filter-bar" style={{ display: 'flex', gap: '0.5rem', alignItems: 'center', margin: '0.5rem 0' }}>
        <input
          type="text"
          placeholder={t('Search content, topic, tags...')}
          title={t('Show only memories whose content, topic or tags contain this text.')}
          value={search}
          onChange={e => { setSearch(e.target.value); setPageNumber(1); }}
        />
      </div>

      {draft && <DialogShell open title={t('Duplicate Memory')} onClose={() => setDraft(null)} dismissible={!saving}>
        <form onSubmit={saveDuplicate}>
          {saveError && <p role="alert" className="text-danger">{saveError}</p>}
          <label>{t('Type')}<select value={draft.type} onChange={e => setDraft({ ...draft, type: e.target.value as MemoryType })}>{TYPES.map(type => <option key={type}>{type}</option>)}</select></label>
          <label>{t('Topic')}<input value={draft.topic ?? ''} onChange={e => setDraft({ ...draft, topic: e.target.value })} /></label>
          <label>{t('Summary')}<input value={draft.summary ?? ''} onChange={e => setDraft({ ...draft, summary: e.target.value })} /></label>
          <label>{t('Content')}<textarea required value={draft.content ?? ''} onChange={e => setDraft({ ...draft, content: e.target.value })} /></label>
          <label>{t('Salience')}<input type="number" min={0} max={1} step={0.01} required value={draft.salience} onChange={e => setDraft({ ...draft, salience: Number(e.target.value) })} /></label>
          <label>{t('Tags')}<input value={tagsText} onChange={e => setTagsText(e.target.value)} /></label>
          <ScopeSelect viewer={viewer} value={draft.scope ?? 'UserSpecific'} onChange={scope => setDraft({ ...draft, scope })} />
          <div className="modal-actions"><button type="submit" className="btn btn-primary" disabled={saving}>{t('Save Copy')}</button><button type="button" className="btn" disabled={saving} onClick={() => setDraft(null)}>{t('Cancel')}</button></div>
        </form>
      </DialogShell>}

      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />

      <ConfirmDialog
        open={confirm.open}
        title={t('Delete Memory')}
        message={t('Delete this memory? This cannot be undone.')}
        onConfirm={() => void handleDelete()}
        onCancel={() => setConfirm({ open: false, id: '' })}
      />
      {deletion.dialog}

      <DataTable
        tableKey="memories"
        columns={columns}
        rows={memories}
        sort={sort}
        rowKey={(m) => m.id}
        onRowClick={(m) => setJsonData({ open: true, title: `${t('Memory')}: ${m.id}`, data: m })}
        pagination={{
          pageNumber, pageSize, totalPages, totalRecords,
          onPageChange: setPageNumber,
          onPageSizeChange: (s) => { setPageSize(s); setPageNumber(1); },
        }}
        onRefresh={load}
        placeholder={memories.length > 0 ? undefined : loading
          ? <p className="text-dim">{t('Loading...')}</p>
          : <p className="text-dim">{t('No memories recorded yet.')}</p>}
        emptyMessage={t('No memories match the current filters.')}
      />
    </div>
  );
}
