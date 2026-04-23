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
import Pagination from '../components/shared/Pagination';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import CopyButton from '../components/shared/CopyButton';
import RefreshButton from '../components/shared/RefreshButton';
import ErrorModal from '../components/shared/ErrorModal';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';

const SIGNAL_TYPES = ['Nudge', 'Mail', 'Assignment', 'Progress', 'Completion', 'Error'] as const;

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
  const [confirmAction, setConfirmAction] = useState<{ message: string; action: () => void } | null>(null);

  const captainName = useCallback((id: string | null) => {
    if (!id) return t('Admiral');
    const c = captains.find(c => c.id === id);
    return c?.name || id;
  }, [captains, t]);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const filters: Record<string, string> = {};
      if (filterType) filters.type = filterType;
      if (filterToCaptain) filters.toCaptainId = filterToCaptain;
      if (filterUnreadOnly) filters.unreadOnly = 'true';
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
  }, [page, pageSize, filterType, filterToCaptain, filterUnreadOnly, t]);

  useEffect(() => { load(); }, [load]);

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

  function sortIcon(field: string) {
    if (sortField !== field) return '';
    return sortDir === 'asc' ? ' \u25B2' : ' \u25BC';
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
      await sendSignal({
        type: sendForm.type || 'Nudge',
        payload: sendForm.payload || undefined,
        toCaptainId: sendForm.toCaptainId || undefined,
      });
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
        <select value={filterType} onChange={e => { setFilterType(e.target.value); setPage(1); }} style={{ width: 'auto', padding: '6px 10px', fontSize: 13 }}>
          <option value="">{t('All Types')}</option>
          {SIGNAL_TYPES.map(signalType => <option key={signalType} value={signalType}>{t(signalType)}</option>)}
        </select>
        <select value={filterToCaptain} onChange={e => { setFilterToCaptain(e.target.value); setPage(1); }} style={{ width: 'auto', padding: '6px 10px', fontSize: 13 }}>
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
      </div>

      {/* Pagination */}
      {totalPages > 0 && (
        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <Pagination pageNumber={page} totalPages={totalPages} totalRecords={totalRecords} totalMs={totalMs}
            pageSize={pageSize} onPageChange={setPage} onPageSizeChange={handlePageSizeChange} />
          <RefreshButton onRefresh={load} title={t('Refresh signals')} />
        </div>
      )}

      {/* Table */}
      {sorted.length > 0 ? (
        <div style={{ overflowX: 'auto' }}>
          <table className="table">
            <thead>
              <tr>
                <th style={{ width: 32 }}>
                  <input type="checkbox" checked={selected.length > 0 && selected.length === sorted.length} onChange={e => e.target.checked ? selectAll() : clearSelection()} title={t('Select all signals')} style={{ width: 'auto' }} />
                </th>
                <th style={{ cursor: 'pointer' }} onClick={() => handleSort('id')}>{t('ID')}{sortIcon('id')}</th>
                <th style={{ cursor: 'pointer' }} onClick={() => handleSort('type')}>{t('Type')}{sortIcon('type')}</th>
                <th style={{ cursor: 'pointer' }} onClick={() => handleSort('fromCaptainId')}>{t('From')}{sortIcon('fromCaptainId')}</th>
                <th style={{ cursor: 'pointer' }} onClick={() => handleSort('toCaptainId')}>{t('To')}{sortIcon('toCaptainId')}</th>
                <th>{t('Read')}</th>
                <th>{t('Payload')}</th>
                <th style={{ cursor: 'pointer' }} onClick={() => handleSort('createdUtc')}>{t('Time')}{sortIcon('createdUtc')}</th>
                <th>{t('Actions')}</th>
              </tr>
              <tr>
                <td />
                <td />
                <td><input type="text" placeholder={t('Filter...')} value={colFilters.type} onChange={e => setColFilters({ ...colFilters, type: e.target.value })} style={{ padding: '2px 6px', fontSize: 11, width: '100%' }} /></td>
                <td><input type="text" placeholder={t('Filter...')} value={colFilters.from} onChange={e => setColFilters({ ...colFilters, from: e.target.value })} style={{ padding: '2px 6px', fontSize: 11, width: '100%' }} /></td>
                <td><input type="text" placeholder={t('Filter...')} value={colFilters.to} onChange={e => setColFilters({ ...colFilters, to: e.target.value })} style={{ padding: '2px 6px', fontSize: 11, width: '100%' }} /></td>
                <td />
                <td><input type="text" placeholder={t('Filter...')} value={colFilters.payload} onChange={e => setColFilters({ ...colFilters, payload: e.target.value })} style={{ padding: '2px 6px', fontSize: 11, width: '100%' }} /></td>
                <td />
                <td />
              </tr>
            </thead>
            <tbody>
              {sorted.map(sig => (
                <tr key={sig.id} className="clickable" onClick={() => navigate(`/signals/${sig.id}`)}>
                  <td onClick={e => e.stopPropagation()}><input type="checkbox" checked={selected.includes(sig.id)} onChange={() => toggleSelection(sig.id)} style={{ width: 'auto' }} /></td>
                  <td className="mono table-id-cell" style={{ color: 'var(--primary)' }}>
                    <span className="id-display">
                      <span className="id-value">{sig.id}</span>
                      <CopyButton text={sig.id} onClick={e => e.stopPropagation()} />
                    </span>
                  </td>
                  <td><span className={`status status-${(sig.type || '').toLowerCase()}`}>{sig.type}</span></td>
                  <td onClick={e => e.stopPropagation()}>
                    {sig.fromCaptainId
                      ? <a href={`/captains/${sig.fromCaptainId}`} onClick={e => { e.preventDefault(); e.stopPropagation(); navigate(`/captains/${sig.fromCaptainId}`); }}>{captainName(sig.fromCaptainId)}</a>
                      : t('Admiral')}
                  </td>
                  <td onClick={e => e.stopPropagation()}>
                    {sig.toCaptainId
                      ? <a href={`/captains/${sig.toCaptainId}`} onClick={e => { e.preventDefault(); e.stopPropagation(); navigate(`/captains/${sig.toCaptainId}`); }}>{captainName(sig.toCaptainId)}</a>
                      : t('Admiral')}
                  </td>
                  <td>{sig.read ? t('Yes') : t('No')}</td>
                  <td style={{ maxWidth: 300, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{sig.payload || '-'}</td>
                  <td className="text-muted" title={formatDateTime(sig.createdUtc)} style={{ whiteSpace: 'nowrap' }}>{formatRelativeTime(sig.createdUtc)}</td>
                  <td onClick={e => e.stopPropagation()}>
                    <ActionMenu id={`signal-${sig.id}`} items={[
                      { label: 'View Detail', onClick: () => navigate(`/signals/${sig.id}`) },
                      ...(!sig.read ? [{ label: 'Mark Read', onClick: () => handleMarkRead(sig.id) }] : []),
                      { label: 'View JSON', onClick: () => setJsonView({ title: `${t('Signal')}: ${sig.id}`, data: sig }) },
                      { label: 'Delete', danger: true, onClick: () => setConfirmAction({ message: t('Delete signal {{id}}?', { id: sig.id }), action: async () => { try { await deleteSignalsBatch([sig.id]); pushToast('warning', t('Signal {{id}} deleted.', { id: sig.id })); setConfirmAction(null); load(); } catch { setError(t('Delete failed.')); setConfirmAction(null); } } }) },
                    ]} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <p className="text-muted" style={{ padding: 20 }}>{loading ? t('Loading...') : t('No signals found.')}</p>
      )}

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

      {/* Confirm Dialog */}
      <ConfirmDialog open={confirmAction !== null} message={confirmAction?.message ?? ''} onConfirm={() => confirmAction?.action()} onCancel={() => setConfirmAction(null)} />
    </div>
  );
}
