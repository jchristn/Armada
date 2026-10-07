import { useEffect, useState, useMemo, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { listDocks, deleteDock, listCaptains, listVessels } from '../api/client';
import type { Dock, Captain, Vessel } from '../types/models';
import DataTable, { type DataTableColumn } from '../components/shared/DataTable';
import ActionMenu from '../components/shared/ActionMenu';
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

export default function Docks() {
  const navigate = useNavigate();
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();
  const [docks, setDocks] = useState<Dock[]>([]);
  const [captains, setCaptains] = useState<Captain[]>([]);
  const [vessels, setVessels] = useState<Vessel[]>([]);
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

  // View detail modal
  const [viewRecord, setViewRecord] = useState<Record<string, unknown> | null>(null);

  // Confirm dialog
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

  const table = useResourceTable({
    rows: docks,
    getId: (d) => d.id,
    columnValues: {
      branchName: (d) => (d.branchName ?? '').toLowerCase(),
      worktreePath: (d) => (d.worktreePath ?? '').toLowerCase(),
      active: (d) => (d.active ? 1 : 0),
      createdUtc: (d) => d.createdUtc,
    },
    initialSortField: 'createdUtc',
    initialSortDir: 'desc',
    initialPageSize: 25,
  });

  const captainName = useCallback((id: string | null) => {
    if (!id) return '-';
    const c = captains.find(c => c.id === id);
    return c?.name || id.substring(0, 8);
  }, [captains]);

  const vesselName = useCallback((id: string | null) => {
    if (!id) return '-';
    const v = vessels.find(v => v.id === id);
    return v?.name || id.substring(0, 8);
  }, [vessels]);

  const load = useCallback(async () => {
    try {
      setLoading(true);
      const result = await listDocks({ pageNumber, pageSize, filters: userScope ? { userId: userScope } : undefined });
      setDocks(result.objects || []);
      setTotalPages(result.totalPages || 1);
      setTotalRecords(result.totalRecords || 0);
      table.setSelected([]);
      setError('');
    } catch {
      setError(t('Failed to load docks.'));
    } finally {
      setLoading(false);
    }
  }, [pageNumber, pageSize, userScope, t]);

  useEffect(() => { load(); }, [load]);
  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('docks', load);

  useEffect(() => {
    listCaptains({ pageSize: 1000 }).then(r => setCaptains(r.objects || [])).catch(() => {});
    listVessels({ pageSize: 1000 }).then(r => setVessels(r.objects || [])).catch(() => {});
  }, []);

  // Delete
  function handleDelete(id: string) {
    setConfirm({
      open: true,
      title: t('Delete Dock'),
      message: t('Delete dock {{id}}? This will clean up the git worktree and cannot be undone.', { id }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await deleteDock(id);
          pushToast('warning', t('Dock {{id}} deleted.', { id }));
          load();
        } catch { setError(t('Delete failed.')); }
      },
    });
  }

  function handleBulkDelete() {
    setConfirm({
      open: true,
      title: t('Delete Selected Docks'),
      message: t('Delete {{count}} selected dock(s)? This will clean up the git worktrees and cannot be undone.', { count: table.selected.length }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        const ids = [...table.selected];
        table.setSelected([]);
        let failed = 0;
        for (const id of ids) {
          try { await deleteDock(id); } catch { failed++; }
        }
        const deleted = ids.length - failed;
        if (deleted > 0) {
          pushToast(failed > 0 ? 'warning' : 'success', failed > 0
            ? t('Deleted {{deleted}} docks. {{failed}} failed.', { deleted, failed })
            : t('Deleted {{deleted}} docks.', { deleted }));
        }
        if (failed > 0) setError(t('Deleted {{deleted}} docks, {{failed}} failed.', { deleted: ids.length - failed, failed }));
        load();
      },
    });
  }

  const columns: DataTableColumn<Dock>[] = [
    {
      key: 'id', label: t('ID'), required: true, cellClassName: 'mono text-dim table-id-cell',
      render: (d) => (
        <span className="id-display">
          <span className="id-value" title={d.id}>{d.id}</span>
          <CopyButton text={d.id} onClick={e => e.stopPropagation()} />
        </span>
      ),
    },
    {
      key: 'vessel', label: t('Vessel'), interactive: true,
      render: (d) => d.vesselId ? <a href="#" onClick={e => { e.preventDefault(); navigate(`/vessels/${d.vesselId}`); }}>{vesselName(d.vesselId)}</a> : '-',
    },
    {
      key: 'captain', label: t('Captain'), interactive: true,
      render: (d) => d.captainId ? <a href="#" onClick={e => { e.preventDefault(); navigate(`/captains/${d.captainId}`); }}>{captainName(d.captainId)}</a> : '-',
    },
    {
      key: 'branchName', label: t('Branch'), sortKey: 'branchName', headerTitle: t('Branch name -- click to sort'),
      cellClassName: 'mono text-dim table-url-cell',
      clearFilter: () => table.setColFilter('branchName', ''),
      filter: <input type="text" className="col-filter" aria-label={t('Branch')} value={table.colFilters.branchName ?? ''} onChange={e => table.setColFilter('branchName', e.target.value)} placeholder={t('Filter...')} />,
      render: (d) => d.branchName ? (
        <span className="id-display">
          <span className="url-value" title={d.branchName}>{d.branchName}</span>
          <CopyButton text={d.branchName} onClick={e => e.stopPropagation()} title={t('Copy branch')} />
        </span>
      ) : '-',
    },
    {
      key: 'worktreePath', label: t('Worktree Path'), cellClassName: 'mono text-dim', cellTitle: (d) => d.worktreePath || '',
      clearFilter: () => table.setColFilter('worktreePath', ''),
      filter: <input type="text" className="col-filter" aria-label={t('Worktree Path')} value={table.colFilters.worktreePath ?? ''} onChange={e => table.setColFilter('worktreePath', e.target.value)} placeholder={t('Filter...')} />,
      render: (d) => <span className="cell-clip"><span>{d.worktreePath || '-'}</span></span>,
    },
    { key: 'active', label: t('Active'), sortKey: 'active', headerTitle: t('Active status -- click to sort'), render: (d) => (d.active ? t('Yes') : t('No')) },
    {
      key: 'created', label: t('Created'), sortKey: 'createdUtc', headerTitle: t('Created -- click to sort'),
      cellClassName: 'text-dim cell-nowrap', cellTitle: (d) => formatDateTime(d.createdUtc),
      render: (d) => formatRelativeTime(d.createdUtc),
    },
    {
      key: 'actions', label: t('Actions'), fixed: true, interactive: true, className: 'text-right',
      render: (d) => (
        <ActionMenu id={`dock-${d.id}`} items={[
          { label: 'View Detail', onClick: () => navigate(`/docks/${d.id}`) },
          { label: 'View JSON', onClick: () => setJsonData({ open: true, title: `${t('Dock')}: ${d.id}`, data: d }) },
          { label: 'Delete', danger: true, onClick: () => handleDelete(d.id) },
        ]} />
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title={t('Docks')}
        subtitle={t('Git worktrees provisioned for captains. Docks are system-managed and track branch activity.')}
        actions={(
          <>
            <UserScopeFilter value={userScope} onChange={(id) => { setUserScope(id); setPageNumber(1); }} />
            {table.selected.length > 0 && (
              <button className="btn btn-sm btn-danger" onClick={handleBulkDelete}>
                {t('Delete Selected')} ({table.selected.length})
              </button>
            )}
          </>
        )}
      />

      <ErrorModal error={error} onClose={() => setError('')} />

      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <RecordDetailModal
        open={!!viewRecord}
        title={viewRecord ? `${t('Dock')}: ${String(viewRecord.branchName || viewRecord.id || '')}` : ''}
        subtitle={viewRecord ? String(viewRecord.worktreePath || '') : undefined}
        record={viewRecord}
        onClose={() => setViewRecord(null)}
      />
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message}
        onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />

      <DataTable
        tableKey="docks"
        columns={columns}
        rows={table.sorted}
        rowKey={(d) => d.id}
        onRowClick={(d) => setViewRecord(d as unknown as Record<string, unknown>)}
        sort={table.sortState}
        pagination={{
          pageNumber, pageSize, totalPages, totalRecords,
          onPageChange: (p) => setPageNumber(p),
          onPageSizeChange: (size) => { setPageSize(size); setPageNumber(1); },
        }}
        autoRefresh={{ seconds: refreshSeconds, onChange: setRefreshSeconds }}
        onRefresh={load}
        refreshTitle={t('Refresh dock data')}
        selection={{
          isSelected: (d) => table.selected.includes(d.id),
          onToggle: (d) => table.toggleSelect(d.id),
          allSelected: table.allSelected,
          onToggleAll: (checked) => (checked ? table.selectAll() : table.clearSelection()),
          selectAllLabel: t('Select all docks'),
          rowLabel: () => t('Select this dock'),
        }}
        emptyMessage={t('No docks match the current filters.')}
        placeholder={docks.length > 0 ? undefined : <p className="text-dim">{loading ? t('Loading...') : t('No docks found.')}</p>}
      />
    </div>
  );
}
