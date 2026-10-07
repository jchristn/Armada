import { useEffect, useState, useMemo, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  listMergeQueue, enqueueMerge, deleteMergeEntry, processMergeEntry,
  processAllMergeQueue, cancelMergeEntry, listVessels,
  getMissionDiff, getMissionLog,
} from '../api/client';
import type { MergeEntry, Vessel } from '../types/models';
import DataTable, { type DataTableColumn } from '../components/shared/DataTable';
import ActionMenu from '../components/shared/ActionMenu';
import StatusBadge from '../components/shared/StatusBadge';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import RecordDetailModal from '../components/shared/RecordDetailModal';
import DiffViewer from '../components/shared/DiffViewer';
import PageHeader from '../components/shared/PageHeader';
import LogViewer from '../components/shared/LogViewer';
import ErrorModal from '../components/shared/ErrorModal';
import CopyButton from '../components/shared/CopyButton';
import UserScopeFilter from '../components/shared/UserScopeFilter';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import { buildEnqueueMergeRequest, emptyEnqueueMergeForm, type EnqueueMergeForm } from '../lib/mergeQueueForm';

type SortDir = 'asc' | 'desc';
type SortField = 'branchName' | 'targetBranch' | 'status' | 'priority' | 'createdUtc';

export default function MergeQueue() {
  const navigate = useNavigate();
  const { t } = useLocale();
  const { pushToast } = useNotifications();
  const [entries, setEntries] = useState<MergeEntry[]>([]);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Pagination (server-side)
  const [pageNumber, setPageNumber] = useState(1);
  const [userScope, setUserScope] = useState('');
  const [pageSize, setPageSize] = useState(25);
  const [totalPages, setTotalPages] = useState(1);
  const [totalRecords, setTotalRecords] = useState(0);

  // Enqueue modal
  const [showEnqueue, setShowEnqueue] = useState(false);
  const [enqueueForm, setEnqueueForm] = useState<EnqueueMergeForm>(emptyEnqueueMergeForm);

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });

  // View detail modal
  const [viewRecord, setViewRecord] = useState<Record<string, unknown> | null>(null);

  // Confirm dialog
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

  // Diff viewer
  const [diffModal, setDiffModal] = useState<{ open: boolean; title: string; rawDiff: string; loading: boolean }>({ open: false, title: '', rawDiff: '', loading: false });

  // Log viewer
  const [logModal, setLogModal] = useState<{ open: boolean; title: string; missionId: string; content: string; totalLines: number; lineCount: number }>({ open: false, title: '', missionId: '', content: '', totalLines: 0, lineCount: 200 });

  // Selection
  const [selected, setSelected] = useState<string[]>([]);

  // Sorting
  const [sortField, setSortField] = useState<SortField>('createdUtc');
  const [sortDir, setSortDir] = useState<SortDir>('desc');

  // Column filters
  const [colFilters, setColFilters] = useState({ branchName: '', targetBranch: '', status: '', vesselId: '' });

  const vesselName = useCallback((id: string | null) => {
    if (!id) return '-';
    const v = vessels.find(v => v.id === id);
    return v?.name || id.substring(0, 8);
  }, [vessels]);

  const load = useCallback(async () => {
    try {
      setLoading(true);
      const result = await listMergeQueue({ pageNumber, pageSize, filters: userScope ? { userId: userScope } : undefined });
      setEntries(result.objects || []);
      setTotalPages(result.totalPages || 1);
      setTotalRecords(result.totalRecords || 0);
      setSelected([]);
      setError('');
    } catch {
      setError(t('Failed to load merge queue.'));
    } finally {
      setLoading(false);
    }
  }, [pageNumber, pageSize, userScope, t]);

  useEffect(() => { load(); }, [load]);

  useEffect(() => {
    listVessels({ pageSize: 1000 }).then(r => setVessels(r.objects || [])).catch(() => {});
  }, []);

  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('mergequeue', load);

  // Client-side column filter + sort
  const filtered = useMemo(() => {
    return entries.filter(e =>
      (!colFilters.branchName || e.branchName.toLowerCase().includes(colFilters.branchName.toLowerCase())) &&
      (!colFilters.targetBranch || e.targetBranch.toLowerCase().includes(colFilters.targetBranch.toLowerCase())) &&
      (!colFilters.status || (e.status ?? '').toLowerCase().includes(colFilters.status.toLowerCase())) &&
      (!colFilters.vesselId || e.vesselId === colFilters.vesselId)
    );
  }, [entries, colFilters]);

  const sorted = useMemo(() => {
    const arr = [...filtered];
    arr.sort((a, b) => {
      let va: string | number = '';
      let vb: string | number = '';
      switch (sortField) {
        case 'branchName': va = a.branchName.toLowerCase(); vb = b.branchName.toLowerCase(); break;
        case 'targetBranch': va = a.targetBranch.toLowerCase(); vb = b.targetBranch.toLowerCase(); break;
        case 'status': va = (a.status ?? '').toLowerCase(); vb = (b.status ?? '').toLowerCase(); break;
        case 'priority': va = a.priority; vb = b.priority; break;
        case 'createdUtc': va = a.createdUtc; vb = b.createdUtc; break;
      }
      if (va < vb) return sortDir === 'asc' ? -1 : 1;
      if (va > vb) return sortDir === 'asc' ? 1 : -1;
      return 0;
    });
    return arr;
  }, [filtered, sortField, sortDir]);

  function handleSort(field: SortField) {
    if (sortField === field) setSortDir(d => d === 'asc' ? 'desc' : 'asc');
    else { setSortField(field); setSortDir('asc'); }
  }


  // Selection
  const allSelected = selected.length > 0 && selected.length === sorted.length;
  function toggleSelect(id: string) {
    setSelected(s => s.includes(id) ? s.filter(x => x !== id) : [...s, id]);
  }
  function selectAll() { setSelected(sorted.map(e => e.id)); }
  function clearSelection() { setSelected([]); }

  // Enqueue
  async function handleEnqueue(e: React.FormEvent) {
    e.preventDefault();
    try {
      await enqueueMerge(buildEnqueueMergeRequest(enqueueForm));
      setShowEnqueue(false);
      pushToast('success', t('Merge entry enqueued.'));
      load();
    } catch { setError(t('Enqueue failed.')); }
  }

  // Process all
  function handleProcessAll() {
    setConfirm({
      open: true,
      title: t('Process Merge Queue'),
      message: t('Process all queued entries in the merge queue now?'),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await processAllMergeQueue();
          pushToast('success', t('Merge queue processing started.'));
          load();
        } catch { setError(t('Process all failed.')); }
      },
    });
  }

  // Process single
  function handleProcess(id: string) {
    setConfirm({
      open: true,
      title: t('Process Entry'),
      message: t('Process merge entry {{id}} now?', { id }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await processMergeEntry(id);
          pushToast('success', t('Merge entry {{id}} processing started.', { id }));
          load();
        } catch { setError(t('Process failed.')); }
      },
    });
  }

  // Cancel
  function handleCancel(id: string) {
    setConfirm({
      open: true,
      title: t('Cancel Entry'),
      message: t('Cancel merge entry {{id}}?', { id }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await cancelMergeEntry(id);
          pushToast('warning', t('Merge entry {{id}} cancelled.', { id }));
          load();
        } catch { setError(t('Cancel failed.')); }
      },
    });
  }

  // Delete
  function handleDelete(id: string) {
    setConfirm({
      open: true,
      title: t('Delete Entry'),
      message: t('Delete merge entry {{id}}? This cannot be undone.', { id }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await deleteMergeEntry(id);
          pushToast('warning', t('Merge entry {{id}} deleted.', { id }));
          load();
        } catch { setError(t('Delete failed.')); }
      },
    });
  }

  function handleBulkDelete() {
    setConfirm({
      open: true,
      title: t('Delete Selected Entries'),
      message: t('Delete {{count}} selected merge queue entries? This cannot be undone.', { count: selected.length }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        const ids = [...selected];
        setSelected([]);
        let failed = 0;
        for (const id of ids) {
          try { await deleteMergeEntry(id); } catch { failed++; }
        }
        const deleted = ids.length - failed;
        if (deleted > 0) {
          pushToast(failed > 0 ? 'warning' : 'success', failed > 0
            ? t('Deleted {{deleted}} merge entries. {{failed}} failed.', { deleted, failed })
            : t('Deleted {{deleted}} merge entries.', { deleted }));
        }
        if (failed > 0) setError(t('Deleted {{deleted}} entries, {{failed}} failed.', { deleted: ids.length - failed, failed }));
        load();
      },
    });
  }

  // Mission diff/log handlers
  async function handleMissionDiff(missionId: string) {
    setDiffModal({ open: true, title: `${t('Diff')}: ${t('Mission')} ${missionId.substring(0, 8)}...`, rawDiff: '', loading: true });
    try {
      const result = await getMissionDiff(missionId);
      setDiffModal(d => ({ ...d, rawDiff: result?.diff || '', loading: false }));
    } catch {
      setDiffModal(d => ({ ...d, rawDiff: '', loading: false }));
    }
  }

  const fetchLog = useCallback(async (missionId: string, lines: number) => {
    try {
      const result = await getMissionLog(missionId, lines);
      setLogModal(l => ({ ...l, content: result.log || t('No log output'), totalLines: result.totalLines || 0 }));
    } catch (e: unknown) {
      setLogModal(l => ({ ...l, content: t('Log unavailable: {{message}}', { message: e instanceof Error ? e.message : String(e) }) }));
    }
  }, [t]);

  function handleMissionLog(missionId: string) {
    setLogModal({ open: true, title: `${t('Log')}: ${t('Mission')} ${missionId.substring(0, 8)}...`, missionId, content: t('Loading...'), totalLines: 0, lineCount: 200 });
    fetchLog(missionId, 200);
  }

  const handleLogRefresh = useCallback(() => {
    if (logModal.missionId) fetchLog(logModal.missionId, logModal.lineCount);
  }, [logModal.missionId, logModal.lineCount, fetchLog]);

  const handleLogLineCountChange = useCallback((lines: number) => {
    setLogModal(l => ({ ...l, lineCount: lines }));
    if (logModal.missionId) fetchLog(logModal.missionId, lines);
  }, [logModal.missionId, fetchLog]);

  function branchCell(branch: string) {
    return branch ? (
      <span className="id-display">
        <span className="url-value" title={branch}>{branch}</span>
        <CopyButton text={branch} onClick={e => e.stopPropagation()} title={t('Copy branch')} />
      </span>
    ) : '-';
  }

  const columns: DataTableColumn<MergeEntry>[] = [
    {
      key: 'id', label: t('ID'), required: true, cellClassName: 'mono text-dim table-id-cell',
      render: (entry) => (
        <span className="id-display">
          <span className="id-value" title={entry.id}>{entry.id}</span>
          <CopyButton text={entry.id} onClick={e => e.stopPropagation()} />
        </span>
      ),
    },
    {
      key: 'branchName', label: t('Branch'), required: true, sortKey: 'branchName', headerTitle: t('Branch -- click to sort'),
      cellClassName: 'mono text-dim table-url-cell',
      filter: <input type="text" className="col-filter" aria-label={t('Filter by branch')} value={colFilters.branchName} onChange={e => setColFilters(f => ({ ...f, branchName: e.target.value }))} placeholder={t('Filter...')} />,
      render: (entry) => branchCell(entry.branchName),
    },
    {
      key: 'targetBranch', label: t('Target'), sortKey: 'targetBranch', headerTitle: t('Target branch -- click to sort'),
      cellClassName: 'mono text-dim table-url-cell',
      clearFilter: () => setColFilters(f => ({ ...f, targetBranch: '' })),
      filter: <input type="text" className="col-filter" aria-label={t('Filter by target branch')} value={colFilters.targetBranch} onChange={e => setColFilters(f => ({ ...f, targetBranch: e.target.value }))} placeholder={t('Filter...')} />,
      render: (entry) => branchCell(entry.targetBranch),
    },
    {
      key: 'status', label: t('Status'), sortKey: 'status', headerTitle: t('Status -- click to sort'), cellClassName: 'cell-nowrap',
      clearFilter: () => setColFilters(f => ({ ...f, status: '' })),
      filter: <input type="text" className="col-filter" aria-label={t('Filter by status')} value={colFilters.status} onChange={e => setColFilters(f => ({ ...f, status: e.target.value }))} placeholder={t('Filter...')} />,
      render: (entry) => <StatusBadge status={entry.status} />,
    },
    { key: 'priority', label: t('Priority'), sortKey: 'priority', headerTitle: t('Priority -- click to sort'), render: (entry) => entry.priority },
    {
      key: 'mission', label: t('Mission'), interactive: true, cellClassName: 'mono',
      // One line: mission IDs truncate with an ellipsis; the full ID is in the tooltip.
      render: (entry) => entry.missionId ? (
        <span className="cell-clip">
          <a href="#" title={entry.missionId} onClick={e => { e.preventDefault(); navigate(`/missions/${entry.missionId}`); }}>
            {entry.missionId}
          </a>
        </span>
      ) : '-',
    },
    {
      key: 'vessel', label: t('Vessel'), interactive: true,
      clearFilter: () => setColFilters(f => ({ ...f, vesselId: '' })),
      filter: (
        <select aria-label={t('Filter by vessel')} className="col-filter" title={t('Filter by vessel')} value={colFilters.vesselId} onChange={e => { setColFilters(f => ({ ...f, vesselId: e.target.value })); }}>
          <option value="">{t('All Vessels')}</option>
          {vessels.map(v => <option key={v.id} value={v.id}>{v.name}</option>)}
        </select>
      ),
      render: (entry) => entry.vesselId ? (
        <a href="#" onClick={e => { e.preventDefault(); navigate(`/vessels/${entry.vesselId}`); }}>
          {vesselName(entry.vesselId)}
        </a>
      ) : '-',
    },
    {
      key: 'actions', label: t('Actions'), fixed: true, interactive: true, className: 'text-right',
      render: (entry) => (
        <ActionMenu id={`merge-${entry.id}`} items={[
          { label: 'View Detail', onClick: () => navigate(`/merge-queue/${entry.id}`) },
          { label: 'Process', onClick: () => handleProcess(entry.id) },
          { label: 'Cancel', onClick: () => handleCancel(entry.id) },
          ...(entry.missionId ? [
            { label: 'Mission Diff', onClick: () => handleMissionDiff(entry.missionId!) },
            { label: 'Mission Log', onClick: () => handleMissionLog(entry.missionId!) },
          ] : []),
          { label: 'View JSON', onClick: () => setJsonData({ open: true, title: `${t('Merge Entry')}: ${entry.id}`, data: entry }) },
          { label: 'Delete', danger: true as const, onClick: () => handleDelete(entry.id) },
        ]} />
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title={t('Merge Queue')}
        subtitle={t('Completed missions awaiting merge. Review, test, approve, and manage the merge pipeline.')}
        actions={(
          <>
            <UserScopeFilter value={userScope} onChange={(id) => { setUserScope(id); setPageNumber(1); }} />
            {selected.length > 0 && (
              <button className="btn btn-sm btn-danger" onClick={handleBulkDelete}>
                {t('Delete Selected')} ({selected.length})
              </button>
            )}
            <button className="btn btn-sm" onClick={handleProcessAll} title={t('Process all queued entries')}>{t('Process All')}</button>
            <button className="btn btn-primary btn-sm" onClick={() => {
              setEnqueueForm(emptyEnqueueMergeForm());
              setShowEnqueue(true);
            }}>+ {t('Enqueue')}</button>
          </>
        )}
      />

      <ErrorModal error={error} onClose={() => setError('')} />

      {/* Enqueue Modal */}
      {showEnqueue && (
        <div className="modal-overlay" onClick={() => setShowEnqueue(false)}>
          <form className="modal" onClick={e => e.stopPropagation()} onSubmit={handleEnqueue}>
            <h3>{t('Enqueue Merge')}</h3>
            <label>{t('Branch Name')}<input value={enqueueForm.branchName} onChange={e => setEnqueueForm({ ...enqueueForm, branchName: e.target.value })} required /></label>
            <label>{t('Target Branch')}<input value={enqueueForm.targetBranch} onChange={e => setEnqueueForm({ ...enqueueForm, targetBranch: e.target.value })} required /></label>
            <label>{t('Mission ID (optional)')}<input value={enqueueForm.missionId} onChange={e => setEnqueueForm({ ...enqueueForm, missionId: e.target.value })} placeholder="msn_..." /></label>
            <label>{t('Vessel')}
              <select value={enqueueForm.vesselId} onChange={e => setEnqueueForm({ ...enqueueForm, vesselId: e.target.value })}>
                <option value="">{t('(none)')}</option>
                {vessels.map(v => <option key={v.id} value={v.id}>{v.name}</option>)}
              </select>
            </label>
            <label>{t('Test Command (optional)')}<input value={enqueueForm.testCommand} onChange={e => setEnqueueForm({ ...enqueueForm, testCommand: e.target.value })} placeholder="npm test" /></label>
            <label>{t('Priority')}<input type="number" value={enqueueForm.priority} onChange={e => setEnqueueForm({ ...enqueueForm, priority: Number(e.target.value) })} /></label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary">{t('Enqueue')}</button>
              <button type="button" className="btn" onClick={() => setShowEnqueue(false)}>{t('Cancel')}</button>
            </div>
          </form>
        </div>
      )}

      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <RecordDetailModal
        open={!!viewRecord}
        title={viewRecord ? `${t('Merge Entry')}: ${String(viewRecord.branchName || viewRecord.id || '')}` : ''}
        subtitle={viewRecord ? String(viewRecord.targetBranch || '') : undefined}
        record={viewRecord}
        onClose={() => setViewRecord(null)}
      />
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message}
        onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />
      <DiffViewer
        open={diffModal.open}
        title={diffModal.title}
        rawDiff={diffModal.rawDiff}
        loading={diffModal.loading}
        onClose={() => setDiffModal({ open: false, title: '', rawDiff: '', loading: false })}
      />
      <LogViewer
        open={logModal.open}
        title={logModal.title}
        content={logModal.content}
        totalLines={logModal.totalLines}
        onClose={() => setLogModal({ open: false, title: '', missionId: '', content: '', totalLines: 0, lineCount: 200 })}
        onRefresh={handleLogRefresh}
        onLineCountChange={handleLogLineCountChange}
      />

      <DataTable
        tableKey="mergequeue"
        columns={columns}
        rows={sorted}
        rowKey={(entry) => entry.id}
        onRowClick={(entry) => setViewRecord(entry as unknown as Record<string, unknown>)}
        sort={{ field: sortField, dir: sortDir, onSort: (f) => handleSort(f as SortField) }}
        pagination={{
          pageNumber, pageSize, totalPages, totalRecords,
          onPageChange: (p) => setPageNumber(p),
          onPageSizeChange: (s) => { setPageSize(s); setPageNumber(1); },
        }}
        autoRefresh={{ seconds: refreshSeconds, onChange: setRefreshSeconds }}
        onRefresh={load}
        refreshTitle={t('Refresh merge queue')}
        selection={{
          isSelected: (entry) => selected.includes(entry.id),
          onToggle: (entry) => toggleSelect(entry.id),
          allSelected,
          onToggleAll: (checked) => (checked ? selectAll() : clearSelection()),
          selectAllLabel: t('Select all entries'),
          rowLabel: () => t('Select this entry'),
        }}
        emptyMessage={t('No entries match the current filters.')}
        placeholder={entries.length > 0 ? undefined : loading
          ? <p className="text-dim">{t('Loading...')}</p>
          : <p className="text-dim">{t('Merge queue is empty.')}</p>}
      />
    </div>
  );
}
