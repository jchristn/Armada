import { useEffect, useState, useMemo, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { listVoyages, cancelVoyage, purgeVoyage, getVoyageStatus } from '../api/client';
import type { Voyage } from '../types/models';
import DataTable, { type DataTableColumn } from '../components/shared/DataTable';
import ActionMenu from '../components/shared/ActionMenu';
import StatusBadge from '../components/shared/StatusBadge';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import RecordDetailModal from '../components/shared/RecordDetailModal';
import CopyButton from '../components/shared/CopyButton';
import UserScopeFilter from '../components/shared/UserScopeFilter';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import PageHeader from '../components/shared/PageHeader';
import ErrorModal from '../components/shared/ErrorModal';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import { useResourceTable } from '../lib/useResourceTable';
import { findLandingMode, getVoyageLandingModes } from '../lib/vesselForm';

export default function Voyages() {
  const navigate = useNavigate();
  const { t } = useLocale();
  const landingModes = getVoyageLandingModes(t);
  const { pushToast } = useNotifications();
  const [voyages, setVoyages] = useState<Voyage[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Pagination (server-side)
  const [pageNumber, setPageNumber] = useState(1);
  const [userScope, setUserScope] = useState('');
  const [pageSize, setPageSize] = useState(25);
  const [totalPages, setTotalPages] = useState(1);
  const [totalRecords, setTotalRecords] = useState(0);

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });

  // Row-click view modal
  const [viewRecord, setViewRecord] = useState<Record<string, unknown> | null>(null);

  // Confirm dialog
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

  const table = useResourceTable({
    rows: voyages,
    getId: (v) => v.id,
    columnValues: {
      title: (v) => v.title.toLowerCase(),
      status: (v) => (v.status ?? '').toLowerCase(),
      createdUtc: (v) => v.createdUtc,
    },
    initialSortField: 'createdUtc',
    initialSortDir: 'desc',
    initialPageSize: 25,
  });

  const load = useCallback(async () => {
    try {
      setLoading(true);
      const result = await listVoyages({ pageNumber, pageSize, filters: userScope ? { userId: userScope } : undefined });
      setVoyages(result.objects || []);
      setTotalPages(result.totalPages || 1);
      setTotalRecords(result.totalRecords || 0);
      setError('');
    } catch {
      setError(t('Failed to load voyages.'));
    } finally {
      setLoading(false);
    }
  }, [pageNumber, pageSize, userScope, t]);

  useEffect(() => { load(); }, [load]);

  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('voyages', load);

  // Actions
  function handleCancel(id: string, title: string) {
    setConfirm({
      open: true,
      title: t('Cancel Voyage'),
      message: t('Cancel voyage "{{title}}"? All pending missions will be cancelled.', { title }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await cancelVoyage(id);
          pushToast('warning', t('Voyage "{{title}}" cancelled.', { title }));
          load();
        } catch { setError(t('Cancel failed.')); }
      },
    });
  }

  function handlePurge(id: string, title: string) {
    setConfirm({
      open: true,
      title: t('Purge Voyage'),
      message: t('Purge voyage "{{title}}"? This will permanently remove the voyage and all associated missions. This cannot be undone.', { title }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await purgeVoyage(id);
          pushToast('warning', t('Voyage "{{title}}" purged.', { title }));
          load();
        } catch { setError(t('Purge failed.')); }
      },
    });
  }

  function handleBulkCancel() {
    setConfirm({
      open: true,
      title: t('Cancel Selected Voyages'),
      message: t('Cancel {{count}} selected voyage(s)?', { count: table.selected.length }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        const ids = [...table.selected];
        table.setSelected([]);
        let failed = 0;
        for (const id of ids) {
          try { await cancelVoyage(id); } catch { failed++; }
        }
        const success = ids.length - failed;
        if (success > 0) {
          pushToast(failed > 0 ? 'warning' : 'success', failed > 0
            ? t('Cancelled {{success}} voyages. {{failed}} failed.', { success, failed })
            : t('Cancelled {{success}} voyages.', { success }));
        }
        if (failed > 0) setError(t('Cancelled {{success}} voyages, {{failed}} failed.', { success: ids.length - failed, failed }));
        load();
      },
    });
  }

  async function handleViewStatus(id: string) {
    try {
      const status = await getVoyageStatus(id);
      setJsonData({ open: true, title: t('Voyage Status'), data: status });
    } catch { setError(t('Failed to load voyage status.')); }
  }

  const columns: DataTableColumn<Voyage>[] = [
    {
      key: 'title', label: t('Title'), required: true, sortKey: 'title', headerTitle: t('Voyage title -- click to sort'),
      cellClassName: 'cell-title', cellTitle: (v) => v.title,
      filter: <input type="text" className="col-filter" aria-label={t('Title')} value={table.colFilters.title ?? ''} onChange={e => table.setColFilter('title', e.target.value)} placeholder={t('Search...')} />,
      render: (v) => <strong className="line-clamp-2">{v.title}</strong>,
    },
    {
      key: 'id', label: t('ID'), required: true, cellClassName: 'mono text-dim table-id-cell',
      render: (v) => (
        <span className="id-display">
          <span className="id-value" title={v.id}>{v.id}</span>
          <CopyButton text={v.id} onClick={e => e.stopPropagation()} />
        </span>
      ),
    },
    {
      key: 'status', label: t('Status'), sortKey: 'status', headerTitle: t('Status -- click to sort'), cellClassName: 'cell-nowrap',
      clearFilter: () => table.setColFilter('status', ''),
      filter: <input type="text" className="col-filter" aria-label={t('Status')} value={table.colFilters.status ?? ''} onChange={e => table.setColFilter('status', e.target.value)} placeholder={t('Search...')} />,
      render: (v) => <StatusBadge status={v.status} />,
    },
    {
      key: 'landingMode', label: t('Landing Mode'),
      headerTitle: t('How the voyage\'s missions land; Default uses the vessel\'s mode, then the global setting'),
      cellClassName: 'text-dim cell-nowrap',
      // One line: the mode (or "Default" when inherited); the short summary and full explanation are in the tooltip.
      render: (v) => {
        const info = findLandingMode(landingModes, v.landingMode);
        return <span className="cell-one-line" title={`${info.short} -- ${info.description}`}>{v.landingMode || t('Default')}</span>;
      },
    },
    {
      key: 'actions', label: t('Actions'), fixed: true, interactive: true, className: 'text-right',
      render: (v) => (
        <ActionMenu id={`voyage-${v.id}`} items={[
          { label: 'View Detail', onClick: () => navigate(`/voyages/${v.id}`) },
          { label: 'View Status', onClick: () => handleViewStatus(v.id) },
          { label: 'View JSON', onClick: () => setJsonData({ open: true, title: `${t('Voyage')}: ${v.title}`, data: v }) },
          { label: 'Cancel', danger: true, onClick: () => handleCancel(v.id, v.title) },
          { label: 'Purge', danger: true, onClick: () => handlePurge(v.id, v.title) },
        ]} />
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title={t('Voyages')}
        subtitle={t('Batches of related missions dispatched together')}
        actions={(
          <>
            <UserScopeFilter value={userScope} onChange={(id) => { setUserScope(id); setPageNumber(1); }} />
            {table.selected.length > 0 && (
              <button className="btn btn-sm btn-danger" onClick={handleBulkCancel}>
                {t('Cancel Selected')} ({table.selected.length})
              </button>
            )}
            <button className="btn btn-primary btn-sm" onClick={() => navigate('/voyages/create')}>+ {t('Voyage')}</button>
          </>
        )}
      />

      <ErrorModal error={error} onClose={() => setError('')} />

      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <RecordDetailModal
        open={!!viewRecord}
        title={typeof viewRecord?.title === 'string' ? viewRecord.title : t('Voyage')}
        subtitle={t('Voyage')}
        record={viewRecord}
        onClose={() => setViewRecord(null)}
        onEdit={() => { const r = viewRecord; setViewRecord(null); navigate(`/voyages/${(r as { id: string }).id}`); }}
        editLabel={t('Open Details')}
      />
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message}
        onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />

      <DataTable
        tableKey="voyages"
        columns={columns}
        rows={table.sorted}
        rowKey={(v) => v.id}
        onRowClick={(v) => setViewRecord(v as unknown as Record<string, unknown>)}
        sort={table.sortState}
        pagination={{
          pageNumber, pageSize, totalPages, totalRecords,
          onPageChange: (p) => setPageNumber(p),
          onPageSizeChange: (size) => { setPageSize(size); setPageNumber(1); },
        }}
        autoRefresh={{ seconds: refreshSeconds, onChange: setRefreshSeconds }}
        onRefresh={load}
        refreshTitle="Refresh voyage data"
        selection={{
          isSelected: (v) => table.selected.includes(v.id),
          onToggle: (v) => table.toggleSelect(v.id),
          allSelected: table.allSelected,
          onToggleAll: (checked) => (checked ? table.selectAll() : table.clearSelection()),
          selectAllLabel: t('Select all voyages'),
          rowLabel: () => t('Select this voyage'),
        }}
        emptyMessage={t('No voyages match the current filters.')}
        placeholder={voyages.length > 0 ? undefined : <p className="text-dim">{loading ? t('Loading...') : t('No voyages found.')}</p>}
      />
    </div>
  );
}
