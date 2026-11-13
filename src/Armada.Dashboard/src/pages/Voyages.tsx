import { useEffect, useState, useMemo, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { listVoyages, cancelVoyage, purgeVoyage, getVoyageStatus } from '../api/client';
import type { Voyage } from '../types/models';
import DataTable, { type DataTableColumn } from '../components/shared/DataTable';
import ActionMenu from '../components/shared/ActionMenu';
import StatusBadge from '../components/shared/StatusBadge';
import EvaluationBadge from '../components/shared/EvaluationBadge';
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
import { usePersistedPageSize } from '../lib/usePersistedPageSize';
import { useDeleteProgress } from '../lib/useDeleteProgress';

export default function Voyages() {
  const navigate = useNavigate();
  const { t } = useLocale();
  const deletion = useDeleteProgress();
  const landingModes = getVoyageLandingModes(t);
  const { pushToast } = useNotifications();
  const [voyages, setVoyages] = useState<Voyage[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [deleting, setDeleting] = useState(false);

  // Pagination (server-side)
  const [pageNumber, setPageNumber] = useState(1);
  const [userScope, setUserScope] = useState('');
  const [pageSize, setPageSize] = usePersistedPageSize('voyages', 25);
  const [totalPages, setTotalPages] = useState(1);
  const [totalRecords, setTotalRecords] = useState(0);

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });

  // Row-click view modal
  const [viewRecord, setViewRecord] = useState<Record<string, unknown> | null>(null);

  // Confirm dialog
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; confirmLabel?: string; danger?: boolean; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

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

  function handleDelete(id: string, title: string) {
    setConfirm({
      open: true,
      title: t('Delete Voyage'),
      confirmLabel: 'Delete',
      danger: true,
      message: t('Delete voyage "{{title}}"? This will permanently remove the voyage and all associated missions. This cannot be undone.', { title }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        if (!(await deletion.run({ kind: 'voyagePurge', action: () => purgeVoyage(id) }))) return;
        pushToast('warning', t('Voyage "{{title}}" deleted.', { title }));
        load();
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

  function handleBulkDelete() {
    const ids = [...table.selected];
    setConfirm({
      open: true,
      title: t('Delete Multiple Voyages'),
      message: t('Delete {{count}} selected voyages and all their associated missions? This cannot be undone. Active voyages or voyages with active missions cannot be deleted.', { count: ids.length }),
      confirmLabel: 'Delete',
      danger: true,
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        setDeleting(true);
        const failedIds: string[] = [];
        try {
          await deletion.run({
            kind: 'voyagePurge',
            count: ids.length,
            action: async () => {
              failedIds.length = 0;
              for (const id of ids) {
                try { await purgeVoyage(id); } catch { failedIds.push(id); }
              }
            },
          });
          table.setSelected(failedIds);
          const success = ids.length - failedIds.length;
          if (success > 0) {
            pushToast(failedIds.length > 0 ? 'warning' : 'success', failedIds.length > 0
              ? t('Deleted {{success}} voyages. {{failed}} failed.', { success, failed: failedIds.length })
              : t('Deleted {{success}} voyages.', { success }));
          }
          await load();
          if (failedIds.length > 0) setError(t('Deleted {{success}} voyages, {{failed}} failed. Failed voyages remain selected.', { success, failed: failedIds.length }));
        } finally {
          setDeleting(false);
        }
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
      key: 'id', label: t('ID'), required: true, headerTitle: t('Unique voyage ID (vyg_ prefix). Use it with the REST API, MCP tools and the armada CLI.'), cellClassName: 'mono text-dim table-id-cell',
      render: (v) => (
        <span className="id-display">
          <span className="id-value" title={v.id}>{v.id}</span>
          <CopyButton text={v.id} onClick={e => e.stopPropagation()} />
        </span>
      ),
    },
    {
      key: 'title', label: t('Title'), required: true, sortKey: 'title', headerTitle: t('Name of the voyage, a batch of related missions dispatched together. Click to sort.'),
      cellClassName: 'cell-title', cellTitle: (v) => v.title,
      filter: <input type="text" className="col-filter" aria-label={t('Title')} title={t('Show only voyages on this page whose title contains this text. Not case sensitive.')} value={table.colFilters.title ?? ''} onChange={e => table.setColFilter('title', e.target.value)} placeholder={t('Search...')} />,
      render: (v) => <><strong className="line-clamp-2">{v.title}</strong>{v.isEvaluation && <EvaluationBadge isEvaluation />}</>,
    },
    {
      key: 'status', label: t('Status'), sortKey: 'status', headerTitle: t('Open while missions are being set up, InProgress while any run, then Complete, Failed (some missions failed) or Cancelled. Click to sort.'), cellClassName: 'cell-nowrap',
      clearFilter: () => table.setColFilter('status', ''),
      filter: <input type="text" className="col-filter" aria-label={t('Status')} title={t('Show only voyages on this page whose status contains this text, for example InProgress.')} value={table.colFilters.status ?? ''} onChange={e => table.setColFilter('status', e.target.value)} placeholder={t('Search...')} />,
      render: (v) => <StatusBadge status={v.status} />,
    },
    {
      key: 'landingMode', label: t('Landing Mode'),
      headerTitle: t('How this voyage\'s missions land: LocalMerge, MergeAndPush, PullRequest, MergeQueue or None. Default means each vessel\'s own mode, then the server default.'),
      cellClassName: 'text-dim cell-nowrap',
      // One line: the mode (or "Default" when inherited); the short summary and full explanation are in the tooltip.
      render: (v) => {
        const info = findLandingMode(landingModes, v.landingMode);
        return <span className="cell-one-line" title={`${info.short}. ${info.description}`}>{v.landingMode || t('Default')}</span>;
      },
    },
    {
      key: 'actions', label: t('Actions'), headerTitle: t('Open, view status or JSON, cancel or delete a voyage.'), fixed: true, interactive: true, className: 'text-right',
      render: (v) => (
        <ActionMenu id={`voyage-${v.id}`} items={[
          { label: 'View Detail', title: 'Open the voyage page with its missions, progress and landing settings.', onClick: () => navigate(`/voyages/${v.id}`) },
          { label: 'View Status', title: 'Show the voyage\'s progress summary from the server as raw JSON.', onClick: () => handleViewStatus(v.id) },
          { label: 'View JSON', title: 'Show the raw voyage record as the API returns it.', onClick: () => setJsonData({ open: true, title: `${t('Voyage')}: ${v.title}`, data: v }) },
          { label: 'Cancel', danger: true, title: 'Cancel the voyage and its pending and assigned missions. Missions already running are not stopped. The records are kept.', onClick: () => handleCancel(v.id, v.title) },
          { label: 'Delete', danger: true, title: 'Permanently delete the voyage and all of its missions. Refused while the voyage or any mission is still active. Cannot be undone.', onClick: () => handleDelete(v.id, v.title) },
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
              <button className="btn btn-sm btn-danger" onClick={handleBulkCancel} disabled={deleting} title={t('Cancel every selected voyage and its pending and assigned missions. You confirm first; the records are kept.')}>
                {t('Cancel Selected')} ({table.selected.length})
              </button>
            )}
            {table.selected.length > 1 && (
              <button className="btn btn-sm btn-danger" onClick={handleBulkDelete} disabled={deleting} title={t('Permanently delete the selected voyages and all their missions. You confirm first; active voyages or missions cannot be deleted.')}>
                {deleting ? t('Deleting...') : t('Delete Multiple')} ({table.selected.length})
              </button>
            )}
            <button className="btn btn-primary btn-sm" onClick={() => navigate('/voyages/create')} title={t('Create a voyage: a named batch of missions for one vessel that are dispatched together.')}>+ {t('Voyage')}</button>
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
      {deletion.dialog}
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message}
        confirmLabel={confirm.confirmLabel} danger={confirm.danger}
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
        refreshTitle="Reload the voyage list from the server now."
        selection={{
          isSelected: (v) => table.selected.includes(v.id),
          onToggle: (v) => table.toggleSelect(v.id),
          allSelected: table.allSelected,
          onToggleAll: (checked) => (checked ? table.selectAll() : table.clearSelection()),
          selectAllLabel: t('Select all voyages'),
          rowLabel: () => t('Select this voyage for Cancel Selected or Delete Multiple.'),
        }}
        emptyMessage={t('No voyages match the current filters.')}
        placeholder={voyages.length > 0 ? undefined : <p className="text-dim">{loading ? t('Loading...') : t('No voyages found.')}</p>}
      />
    </div>
  );
}
