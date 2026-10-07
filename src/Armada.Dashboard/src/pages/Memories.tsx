import { useCallback, useEffect, useState } from 'react';
import { listMemories, deleteMemory } from '../api/client';
import type { Memory, MemoryType } from '../types/models';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import PageHeader from '../components/shared/PageHeader';
import DataTable, { type DataTableColumn } from '../components/shared/DataTable';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import ScopeBadge from '../components/shared/ScopeBadge';
import CopyButton from '../components/shared/CopyButton';
import { MEMORY_TYPES } from '../lib/configuration';

const TYPES = MEMORY_TYPES;

/**
 * Memories management surface. Lists the durable agent memories the Recorder distills from voyages
 * (episodic / semantic / procedural), with type and text filters, a JSON detail view, and delete for
 * pruning stale entries. Memories are primarily written by agents over MCP; operators curate here.
 */
export default function Memories() {
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();

  const [memories, setMemories] = useState<Memory[]>([]);
  const [loading, setLoading] = useState(false);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [totalPages, setTotalPages] = useState(1);
  const [totalRecords, setTotalRecords] = useState(0);
  const [typeFilter, setTypeFilter] = useState<'' | MemoryType>('');
  const [search, setSearch] = useState('');
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });
  const [confirm, setConfirm] = useState<{ open: boolean; id: string }>({ open: false, id: '' });

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const filters: Record<string, string> = {};
      if (typeFilter) filters.type = typeFilter;
      if (search.trim()) filters.search = search.trim();
      const result = await listMemories({ pageNumber, pageSize, filters });
      setMemories(result.objects ?? []);
      setTotalPages(result.totalPages ?? 1);
      setTotalRecords(result.totalRecords ?? 0);
    } catch {
      pushToast('error', t('Failed to load memories.'));
    } finally {
      setLoading(false);
    }
  }, [pageNumber, pageSize, typeFilter, search, pushToast, t]);

  useEffect(() => { void load(); }, [load]);

  const handleDelete = async () => {
    const id = confirm.id;
    setConfirm({ open: false, id: '' });
    try {
      await deleteMemory(id);
      pushToast('success', t('Memory deleted.'));
      void load();
    } catch {
      pushToast('error', t('Failed to delete memory.'));
    }
  };

  const memorySummary = (m: Memory) => m.summary || m.content;

  const columns: DataTableColumn<Memory>[] = [
    {
      key: 'type', label: t('Type'), cellClassName: 'cell-nowrap',
      render: (m) => <span className={`tag tag-${m.type.toLowerCase()}`}>{t(m.type)}</span>,
    },
    { key: 'topic', label: t('Topic'), cellClassName: 'text-dim', render: (m) => m.topic || '-' },
    {
      // One line; the full summary (or content) is in the tooltip.
      key: 'summary', label: t('Summary'), required: true, cellClassName: 'truncate-cell', cellTitle: memorySummary,
      render: (m) => <span className="truncate-text">{memorySummary(m)}</span>,
    },
    { key: 'salience', label: t('Salience'), cellClassName: 'text-dim', render: (m) => m.salience.toFixed(2) },
    {
      key: 'vessel', label: t('Vessel'), cellClassName: 'mono text-dim',
      render: (m) => {
        const vesselId = m.vesselId || m.sourceVesselId;
        return vesselId ? <span className="cell-clip" title={vesselId}><span>{vesselId}</span></span> : '-';
      },
    },
    { key: 'visibility', label: t('Visibility'), render: (m) => <ScopeBadge scope={m.scope} /> },
    {
      key: 'updated', label: t('Updated'), cellClassName: 'text-dim cell-nowrap', cellTitle: (m) => formatDateTime(m.lastUpdateUtc),
      render: (m) => formatRelativeTime(m.lastUpdateUtc),
    },
    {
      key: 'actions', label: t('Actions'), fixed: true, interactive: true, className: 'text-right',
      render: (m) => (
        <span className="id-display">
          <CopyButton text={m.id} />
          <button className="btn-danger btn-sm" onClick={() => setConfirm({ open: true, id: m.id })}>{t('Delete')}</button>
        </span>
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
        <select
          aria-label={t('Filter by type')}
          value={typeFilter}
          onChange={e => { setTypeFilter(e.target.value as '' | MemoryType); setPageNumber(1); }}
        >
          <option value="">{t('All types')}</option>
          {TYPES.map(ty => <option key={ty} value={ty}>{t(ty)}</option>)}
        </select>
        <input
          type="text"
          placeholder={t('Search content, topic, tags...')}
          value={search}
          onChange={e => { setSearch(e.target.value); setPageNumber(1); }}
        />
      </div>

      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />

      <ConfirmDialog
        open={confirm.open}
        title={t('Delete Memory')}
        message={t('Delete this memory? This cannot be undone.')}
        onConfirm={() => void handleDelete()}
        onCancel={() => setConfirm({ open: false, id: '' })}
      />

      <DataTable
        tableKey="memories"
        columns={columns}
        rows={memories}
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
      />
    </div>
  );
}
