import { useEffect, useState, useCallback, useMemo } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  listSignals,
  sendSignal,
  markSignalRead,
  deleteSignalsBatch,
  listCaptains,
} from '../api/client';
import type { Signal, Captain, SendSignalRequest } from '../types/models';
import DataTable, { type DataTableColumn } from '../components/shared/DataTable';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import RecordDetailModal from '../components/shared/RecordDetailModal';
import CopyButton from '../components/shared/CopyButton';
import UserScopeFilter from '../components/shared/UserScopeFilter';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import ErrorModal from '../components/shared/ErrorModal';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import { SIGNAL_TYPES, buildSendSignalRequest, signalListFilters } from '../lib/signals';

type SortDir = 'asc' | 'desc';

export default function Signals() {
  const navigate = useNavigate();
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();

  // Data
  const [signals, setSignals] = useState<Signal[]>([]);
  const [captains, setCaptains] = useState<Captain[]>([]);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  // Pagination
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [totalPages, setTotalPages] = useState(0);
  const [totalRecords, setTotalRecords] = useState(0);
  const [totalMs, setTotalMs] = useState(0);

  // Filters
  const [filterType, setFilterType] = useState('');
  const [userScope, setUserScope] = useState('');
  const [filterToCaptain, setFilterToCaptain] = useState('');
  const [filterUnreadOnly, setFilterUnreadOnly] = useState(false);

  // Column filters
  const [colFilters, setColFilters] = useState({ type: '', from: '', to: '', payload: '' });

  // Sorting
  const [sortField, setSortField] = useState<string>('');
  const [sortDir, setSortDir] = useState<SortDir>('asc');

  // Selection
  const [selected, setSelected] = useState<string[]>([]);

  // Modals
  const [showSendModal, setShowSendModal] = useState(false);
  const [sendForm, setSendForm] = useState<SendSignalRequest>({ type: 'Nudge', payload: '', toCaptainId: '' });
  const [sendLoading, setSendLoading] = useState(false);
  const [jsonView, setJsonView] = useState<{ title: string; data: unknown } | null>(null);
  const [viewRecord, setViewRecord] = useState<Record<string, unknown> | null>(null);
  const [confirmAction, setConfirmAction] = useState<{ message: string; action: () => void } | null>(null);

  const captainName = useCallback((id: string | null) => {
    if (!id) return t('Admiral');
    const c = captains.find(c => c.id === id);
    return c?.name || id;
  }, [captains, t]);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const filters = signalListFilters({ type: filterType, toCaptainId: filterToCaptain, unreadOnly: filterUnreadOnly, userId: userScope });
      const result = await listSignals({ pageNumber: page, pageSize, filters });
      setSignals(result.objects || []);
      setTotalPages(result.totalPages || 0);
      setTotalRecords(result.totalRecords || 0);
      setTotalMs(result.totalMs || 0);
      setSelected([]);
    } catch {
      setError(t('Failed to load signals.'));
    } finally {
      setLoading(false);
    }
  }, [page, pageSize, filterType, filterToCaptain, filterUnreadOnly, userScope, t]);

  useEffect(() => { load(); }, [load]);

  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('signals', load);

  useEffect(() => {
    listCaptains({ pageSize: 1000 }).then(r => setCaptains(r.objects || [])).catch(() => {});
  }, []);

  // Column filtering
  const filtered = useMemo(() => {
    let rows = signals;
    if (colFilters.type) rows = rows.filter(s => (s.type || '').toLowerCase().includes(colFilters.type.toLowerCase()));
    if (colFilters.from) rows = rows.filter(s => (s.fromCaptainId ? captainName(s.fromCaptainId) : t('Admiral')).toLowerCase().includes(colFilters.from.toLowerCase()));
    if (colFilters.to) rows = rows.filter(s => (s.toCaptainId ? captainName(s.toCaptainId) : t('Admiral')).toLowerCase().includes(colFilters.to.toLowerCase()));
    if (colFilters.payload) rows = rows.filter(s => (s.payload || '').toLowerCase().includes(colFilters.payload.toLowerCase()));
    return rows;
  }, [signals, colFilters, captainName, t]);

  // Sorting
  const sorted = useMemo(() => {
    if (!sortField) return filtered;
    return [...filtered].sort((a, b) => {
      const av = (a as unknown as Record<string, unknown>)[sortField];
      const bv = (b as unknown as Record<string, unknown>)[sortField];
      const as = String(av ?? '');
      const bs = String(bv ?? '');
      const cmp = as.localeCompare(bs);
      return sortDir === 'asc' ? cmp : -cmp;
    });
  }, [filtered, sortField, sortDir]);

  function handleSort(field: string) {
    if (sortField === field) {
      setSortDir(d => d === 'asc' ? 'desc' : 'asc');
    } else {
      setSortField(field);
      setSortDir('asc');
    }
  }

  // Selection
  function toggleSelection(id: string) {
    setSelected(prev => prev.includes(id) ? prev.filter(x => x !== id) : [...prev, id]);
  }
  function selectAll() { setSelected(sorted.map(s => s.id)); }
  function clearSelection() { setSelected([]); }

  // Actions
  async function handleSend(e: React.FormEvent) {
    e.preventDefault();
    setSendLoading(true);
    try {
      await sendSignal(buildSendSignalRequest(sendForm));
      setShowSendModal(false);
      pushToast('success', t('Signal sent.'));
      load();
    } catch {
      setError(t('Failed to send signal.'));
    } finally {
      setSendLoading(false);
    }
  }

  async function handleMarkRead(id: string) {
    try {
      await markSignalRead(id);
      pushToast('success', t('Signal marked as read.'));
      load();
    } catch {
      setError(t('Failed to mark signal as read.'));
    }
  }

  async function handleBulkDelete() {
    setConfirmAction({
      message: t('Delete {{count}} selected signal(s)?', { count: selected.length }),
      action: async () => {
        try {
          await deleteSignalsBatch(selected);
          pushToast('warning', t('Deleted {{count}} signal(s).', { count: selected.length }));
          setConfirmAction(null);
          load();
        } catch {
          setError(t('Bulk delete failed.'));
          setConfirmAction(null);
        }
      }
    });
  }

  function handlePageSizeChange(newSize: number) {
    setPageSize(newSize);
    setPage(1);
  }

  function resetFilters() {
    setFilterType('');
    setFilterToCaptain('');
    setFilterUnreadOnly(false);
    setPage(1);
  }

  function colFilterInput(key: 'type' | 'from' | 'to' | 'payload', label: string) {
    return <input type="text" className="col-filter" aria-label={label} placeholder={t('Filter...')} value={colFilters[key]} onChange={e => setColFilters({ ...colFilters, [key]: e.target.value })} />;
  }

  function captainLink(id: string | null) {
    return id
      ? <a href={`/captains/${id}`} onClick={e => { e.preventDefault(); e.stopPropagation(); navigate(`/captains/${id}`); }}>{captainName(id)}</a>
      : t('Admiral');
  }

  const columns: DataTableColumn<Signal>[] = [
    {
      key: 'id', label: t('ID'), required: true, sortKey: 'id', cellClassName: 'mono table-id-cell',
      render: (sig) => (
        <span className="id-display" style={{ color: 'var(--primary)' }}>
          <span className="id-value" title={sig.id}>{sig.id}</span>
          <CopyButton text={sig.id} onClick={e => e.stopPropagation()} />
        </span>
      ),
    },
    {
      key: 'type', label: t('Type'), sortKey: 'type', cellClassName: 'cell-nowrap',
      clearFilter: () => setColFilters(f => ({ ...f, type: '' })), filter: colFilterInput('type', t('Type')),
      render: (sig) => <span className={`status status-${(sig.type || '').toLowerCase()}`}>{sig.type}</span>,
    },
    {
      key: 'from', label: t('From'), sortKey: 'fromCaptainId', interactive: true,
      clearFilter: () => setColFilters(f => ({ ...f, from: '' })), filter: colFilterInput('from', t('From')),
      render: (sig) => captainLink(sig.fromCaptainId),
    },
    {
      key: 'to', label: t('To'), sortKey: 'toCaptainId', interactive: true,
      clearFilter: () => setColFilters(f => ({ ...f, to: '' })), filter: colFilterInput('to', t('To')),
      render: (sig) => captainLink(sig.toCaptainId),
    },
    { key: 'read', label: t('Read'), render: (sig) => (sig.read ? t('Yes') : t('No')) },
    {
      key: 'payload', label: t('Payload'), cellClassName: 'truncate-cell', cellTitle: (sig) => sig.payload || undefined,
      clearFilter: () => setColFilters(f => ({ ...f, payload: '' })), filter: colFilterInput('payload', t('Payload')),
      render: (sig) => <span className="truncate-text">{sig.payload || '-'}</span>,
    },
    {
      key: 'created', label: t('Time'), sortKey: 'createdUtc', cellClassName: 'text-muted cell-nowrap', cellTitle: (sig) => formatDateTime(sig.createdUtc),
      render: (sig) => formatRelativeTime(sig.createdUtc),
    },
    {
      key: 'actions', label: t('Actions'), fixed: true, interactive: true,
      render: (sig) => (
        <ActionMenu id={`signal-${sig.id}`} items={[
          { label: 'View Detail', onClick: () => navigate(`/signals/${sig.id}`) },
          ...(!sig.read ? [{ label: 'Mark Read', onClick: () => handleMarkRead(sig.id) }] : []),
          { label: 'View JSON', onClick: () => setJsonView({ title: `${t('Signal')}: ${sig.id}`, data: sig }) },
          { label: 'Delete', danger: true, onClick: () => setConfirmAction({ message: t('Delete signal {{id}}?', { id: sig.id }), action: async () => { try { await deleteSignalsBatch([sig.id]); pushToast('warning', t('Signal {{id}} deleted.', { id: sig.id })); setConfirmAction(null); load(); } catch { setError(t('Delete failed.')); setConfirmAction(null); } } }) },
        ]} />
      ),
    },
  ];

  return (
    <div>
      {/* Header */}
      <div className="page-header">
        <div>
          <h2>{t('Signals')}</h2>
          <p className="text-muted" style={{ fontSize: 13, marginTop: 4 }}>{t('Messages exchanged between the admiral and captains. View signal payloads and delivery status.')}</p>
        </div>
        <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
          {selected.length > 0 && (
            <button className="btn-sm btn-danger" onClick={handleBulkDelete}>
              {t('Delete Selected')} ({selected.length})
            </button>
          )}
          <button className="btn-primary" onClick={() => { setSendForm({ type: 'Nudge', payload: '', toCaptainId: '' }); setShowSendModal(true); }}>
            + {t('Signal')}
          </button>
        </div>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />

      {/* Filters */}
      <div style={{ display: 'flex', gap: 8, marginBottom: 12, alignItems: 'center', flexWrap: 'wrap' }}>
        <select aria-label={t('All Types')} value={filterType} onChange={e => { setFilterType(e.target.value); setPage(1); }} style={{ width: 'auto', padding: '6px 10px', fontSize: 13 }}>
          <option value="">{t('All Types')}</option>
          {SIGNAL_TYPES.map(signalType => <option key={signalType} value={signalType}>{t(signalType)}</option>)}
        </select>
        <select aria-label={t('All Captains')} value={filterToCaptain} onChange={e => { setFilterToCaptain(e.target.value); setPage(1); }} style={{ width: 'auto', padding: '6px 10px', fontSize: 13 }}>
          <option value="">{t('All Captains')}</option>
          {captains.map(c => <option key={c.id} value={c.id}>{c.name || c.id}</option>)}
        </select>
        <label style={{ display: 'flex', alignItems: 'center', gap: 4, fontSize: 13, cursor: 'pointer' }}>
          <input type="checkbox" checked={filterUnreadOnly} onChange={e => { setFilterUnreadOnly(e.target.checked); setPage(1); }} style={{ width: 'auto' }} />
          {t('Unread Only')}
        </label>
        {(filterType || filterToCaptain || filterUnreadOnly) && (
          <button className="btn-sm" onClick={resetFilters}>{t('Clear Filters')}</button>
        )}
        <UserScopeFilter value={userScope} onChange={(id) => { setUserScope(id); setPage(1); }} />
      </div>

      <DataTable
        tableKey="signals"
        columns={columns}
        rows={sorted}
        rowKey={(sig) => sig.id}
        onRowClick={(sig) => setViewRecord(sig as unknown as Record<string, unknown>)}
        sort={{ field: sortField, dir: sortDir, onSort: handleSort }}
        pagination={{
          pageNumber: page, pageSize, totalPages: Math.max(1, totalPages), totalRecords, totalMs,
          onPageChange: setPage,
          onPageSizeChange: handlePageSizeChange,
        }}
        autoRefresh={{ seconds: refreshSeconds, onChange: setRefreshSeconds }}
        onRefresh={load}
        refreshTitle={t('Refresh signals')}
        selection={{
          isSelected: (sig) => selected.includes(sig.id),
          onToggle: (sig) => toggleSelection(sig.id),
          allSelected: selected.length > 0 && selected.length === sorted.length,
          onToggleAll: (checked) => (checked ? selectAll() : clearSelection()),
          selectAllLabel: t('Select all signals'),
          rowLabel: () => t('Select this signal'),
        }}
        placeholder={sorted.length > 0 ? undefined : <p className="text-muted" style={{ padding: 20 }}>{loading ? t('Loading...') : t('No signals found.')}</p>}
      />

      {/* Send Signal Modal */}
      {showSendModal && (
        <div className="modal-overlay" onClick={() => setShowSendModal(false)}>
          <form className="modal" onClick={e => e.stopPropagation()} onSubmit={handleSend}>
            <h3>{t('Send Signal')}</h3>
            <label>
              {t('Type')}
              <select value={sendForm.type} onChange={e => setSendForm({ ...sendForm, type: e.target.value })} style={{ marginTop: 4 }}>
                {SIGNAL_TYPES.map(signalType => <option key={signalType} value={signalType}>{t(signalType)}</option>)}
              </select>
            </label>
            <label>
              {t('Payload')}
              <textarea value={sendForm.payload || ''} onChange={e => setSendForm({ ...sendForm, payload: e.target.value })} rows={4} style={{ marginTop: 4, resize: 'vertical' }} />
            </label>
            <label>
              {t('To Captain (optional)')}
              <select value={sendForm.toCaptainId || ''} onChange={e => setSendForm({ ...sendForm, toCaptainId: e.target.value || undefined })} style={{ marginTop: 4 }}>
                <option value="">{t('Admiral (broadcast)')}</option>
                {captains.map(c => <option key={c.id} value={c.id}>{c.name || c.id}</option>)}
              </select>
            </label>
            <div className="modal-actions">
              <button type="button" className="btn-sm" onClick={() => setShowSendModal(false)}>{t('Cancel')}</button>
              <button type="submit" className="btn-primary" disabled={sendLoading}>{sendLoading ? t('Sending...') : t('Send')}</button>
            </div>
          </form>
        </div>
      )}

      {/* JSON Viewer */}
      <JsonViewer open={jsonView !== null} title={jsonView?.title ?? ''} data={jsonView?.data ?? null} onClose={() => setJsonView(null)} />

      {/* View Detail Modal */}
      <RecordDetailModal
        open={!!viewRecord}
        title={viewRecord ? `${t('Signal')}: ${String(viewRecord.type || viewRecord.id || '')}` : ''}
        record={viewRecord}
        onClose={() => setViewRecord(null)}
      />

      {/* Confirm Dialog */}
      <ConfirmDialog open={confirmAction !== null} message={confirmAction?.message ?? ''} onConfirm={() => confirmAction?.action()} onCancel={() => setConfirmAction(null)} />
    </div>
  );
}
