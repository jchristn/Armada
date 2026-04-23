import { useEffect, useState, useMemo, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { listEvents, deleteEventsBatch, listCaptains, listVessels } from '../api/client';
import type { ArmadaEvent, Captain, Vessel } from '../types/models';
import Pagination from '../components/shared/Pagination';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import CopyButton from '../components/shared/CopyButton';
import RefreshButton from '../components/shared/RefreshButton';
import ErrorModal from '../components/shared/ErrorModal';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';

type SortDir = 'asc' | 'desc';
type SortField = 'eventType' | 'entityType' | 'createdUtc';

function entityRoute(entityId: string | null): string | null {
  if (!entityId) return null;
  if (entityId.startsWith('flt_')) return `/fleets/${entityId}`;
  if (entityId.startsWith('vsl_')) return `/vessels/${entityId}`;
  if (entityId.startsWith('cpt_')) return `/captains/${entityId}`;
  if (entityId.startsWith('msn_')) return `/missions/${entityId}`;
  if (entityId.startsWith('vyg_')) return `/voyages/${entityId}`;
  if (entityId.startsWith('sig_')) return `/signals/${entityId}`;
  if (entityId.startsWith('evt_')) return `/events/${entityId}`;
  if (entityId.startsWith('dck_')) return `/docks/${entityId}`;
  if (entityId.startsWith('mrg_')) return `/merge-queue`;
  return null;
}

export default function Events() {
  const navigate = useNavigate();
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();
  const [events, setEvents] = useState<ArmadaEvent[]>([]);
  const [captains, setCaptains] = useState<Captain[]>([]);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Pagination (server-side)
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(50);
  const [totalPages, setTotalPages] = useState(1);
  const [totalRecords, setTotalRecords] = useState(0);

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });

  // Confirm dialog
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

  // Selection
  const [selected, setSelected] = useState<string[]>([]);

  // Sorting
  const [sortField, setSortField] = useState<SortField>('createdUtc');
  const [sortDir, setSortDir] = useState<SortDir>('desc');

  // Column filters
  const [colFilters, setColFilters] = useState({ eventType: '', entityType: '', message: '' });

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
      const result = await listEvents({ pageNumber, pageSize });
      setEvents(result.objects || []);
      setTotalPages(result.totalPages || 1);
      setTotalRecords(result.totalRecords || 0);
      setSelected([]);
      setError('');
    } catch {
      setError(t('Failed to load events.'));
    } finally {
      setLoading(false);
    }
  }, [pageNumber, pageSize, t]);

  useEffect(() => { load(); }, [load]);

  useEffect(() => {
    listCaptains({ pageSize: 1000 }).then(r => setCaptains(r.objects || [])).catch(() => {});
    listVessels({ pageSize: 1000 }).then(r => setVessels(r.objects || [])).catch(() => {});
  }, []);

  // Client-side column filter + sort
  const filtered = useMemo(() => {
    return events.filter(e =>
      (!colFilters.eventType || (e.eventType ?? '').toLowerCase().includes(colFilters.eventType.toLowerCase())) &&
      (!colFilters.entityType || (e.entityType ?? '').toLowerCase().includes(colFilters.entityType.toLowerCase())) &&
      (!colFilters.message || (e.message ?? '').toLowerCase().includes(colFilters.message.toLowerCase()))
    );
  }, [events, colFilters]);

  const sorted = useMemo(() => {
    const arr = [...filtered];
    arr.sort((a, b) => {
      let va: string = '';
      let vb: string = '';
      switch (sortField) {
        case 'eventType': va = (a.eventType ?? '').toLowerCase(); vb = (b.eventType ?? '').toLowerCase(); break;
        case 'entityType': va = (a.entityType ?? '').toLowerCase(); vb = (b.entityType ?? '').toLowerCase(); break;
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

  function sortIcon(field: SortField) {
    if (sortField !== field) return '';
    return sortDir === 'asc' ? ' \u25B2' : ' \u25BC';
  }

  // Selection
  const allSelected = selected.length > 0 && selected.length === sorted.length;
  function toggleSelect(id: string) {
    setSelected(s => s.includes(id) ? s.filter(x => x !== id) : [...s, id]);
  }
  function selectAll() { setSelected(sorted.map(e => e.id)); }
  function clearSelection() { setSelected([]); }

  // Delete
  function handleDeleteSingle(id: string) {
    setConfirm({
      open: true,
      title: t('Delete Event'),
      message: t('Delete event {{id}}?', { id }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await deleteEventsBatch([id]);
          pushToast('warning', t('Event {{id}} deleted.', { id }));
          load();
        } catch { setError(t('Delete failed.')); }
      },
    });
  }

  function handleBulkDelete() {
    setConfirm({
      open: true,
      title: t('Delete Selected Events'),
      message: t('Delete {{count}} selected event(s)?', { count: selected.length }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await deleteEventsBatch(selected);
          pushToast('warning', t('Deleted {{count}} event(s).', { count: selected.length }));
          setSelected([]);
          load();
        } catch { setError(t('Bulk delete failed.')); }
      },
    });
  }

  return (
    <div>
      <div className="view-header">
        <div>
          <h2>{t('Events')}</h2>
          <p className="text-dim view-subtitle">{t('System event log capturing state changes, completions, failures, and other notable occurrences.')}</p>
        </div>
        <div className="view-actions">
          {selected.length > 0 && (
            <button className="btn btn-sm btn-danger" onClick={handleBulkDelete}>
              {t('Delete Selected')} ({selected.length})
            </button>
          )}
          <RefreshButton onRefresh={load} title={t('Refresh event data')} />
        </div>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />

      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message}
        onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />

      {loading && events.length === 0 && <p className="text-dim">{t('Loading...')}</p>}
      {!loading && events.length === 0 && <p className="text-dim">{t('No events found.')}</p>}

      {events.length > 0 && (
        <>
          <Pagination pageNumber={pageNumber} pageSize={pageSize} totalPages={totalPages}
            totalRecords={totalRecords}
            onPageChange={p => setPageNumber(p)} onPageSizeChange={s => { setPageSize(s); setPageNumber(1); }} />

          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th className="col-checkbox">
                    <input type="checkbox" checked={allSelected} onChange={e => e.target.checked ? selectAll() : clearSelection()} title={t('Select all events')} />
                  </th>
                  <th>{t('ID')}</th>
                  <th className="sortable" onClick={() => handleSort('eventType')} title={t('Event type -- click to sort')}>
                    {t('Event Type')}{sortIcon('eventType')}
                  </th>
                  <th className="sortable" onClick={() => handleSort('entityType')} title={t('Entity type -- click to sort')}>
                    {t('Entity Type')}{sortIcon('entityType')}
                  </th>
                  <th>{t('Entity ID')}</th>
                  <th>{t('Captain')}</th>
                  <th>{t('Mission')}</th>
                  <th>{t('Vessel')}</th>
                  <th>{t('Voyage')}</th>
                  <th>{t('Message')}</th>
                  <th className="sortable" onClick={() => handleSort('createdUtc')} title={t('Created -- click to sort')}>
                    {t('Created')}{sortIcon('createdUtc')}
                  </th>
                  <th className="text-right">{t('Actions')}</th>
                </tr>
                <tr className="column-filter-row">
                  <td></td>
                  <td></td>
                  <td><input type="text" className="col-filter" value={colFilters.eventType} onChange={e => setColFilters(f => ({ ...f, eventType: e.target.value }))} placeholder={t('Filter...')} /></td>
                  <td><input type="text" className="col-filter" value={colFilters.entityType} onChange={e => setColFilters(f => ({ ...f, entityType: e.target.value }))} placeholder={t('Filter...')} /></td>
                  <td></td>
                  <td></td>
                  <td></td>
                  <td></td>
                  <td></td>
                  <td><input type="text" className="col-filter" value={colFilters.message} onChange={e => setColFilters(f => ({ ...f, message: e.target.value }))} placeholder={t('Filter...')} /></td>
                  <td></td>
                  <td></td>
                </tr>
              </thead>
              <tbody>
                {sorted.map(evt => {
                  const entRoute = entityRoute(evt.entityId);
                  return (
                    <tr key={evt.id} className="clickable" onClick={() => navigate(`/events/${evt.id}`)}>
                      <td className="col-checkbox" onClick={e => e.stopPropagation()}>
                        <input type="checkbox" checked={selected.includes(evt.id)} onChange={() => toggleSelect(evt.id)} title={t('Select this event')} />
                      </td>
                      <td className="mono text-dim table-id-cell">
                        <span className="id-display">
                          <span className="id-value" title={evt.id}>{evt.id}</span>
                          <CopyButton text={evt.id} onClick={e => e.stopPropagation()} />
                        </span>
                      </td>
                      <td style={{ whiteSpace: 'nowrap' }}>{evt.eventType}</td>
                      <td className="text-dim">{evt.entityType || '-'}</td>
                      <td className="mono text-dim table-id-cell" onClick={e => e.stopPropagation()}>
                        {evt.entityId ? (
                          <span className="id-display">
                            {entRoute ? (
                              <a href="#" className="id-value" onClick={e => { e.preventDefault(); navigate(entRoute); }}>{evt.entityId}</a>
                            ) : (
                              <span className="id-value">{evt.entityId}</span>
                            )}
                            <CopyButton text={evt.entityId} onClick={e => e.stopPropagation()} />
                          </span>
                        ) : '-'}
                      </td>
                      <td onClick={e => e.stopPropagation()}>
                        {evt.captainId ? (
                          <a href="#" onClick={e => { e.preventDefault(); navigate(`/captains/${evt.captainId}`); }}>{captainName(evt.captainId)}</a>
                        ) : '-'}
                      </td>
                      <td className="mono text-dim" onClick={e => e.stopPropagation()}>
                        {evt.missionId ? (
                          <a href="#" onClick={e => { e.preventDefault(); navigate(`/missions/${evt.missionId}`); }}>{evt.missionId.substring(0, 8)}...</a>
                        ) : '-'}
                      </td>
                      <td onClick={e => e.stopPropagation()}>
                        {evt.vesselId ? (
                          <a href="#" onClick={e => { e.preventDefault(); navigate(`/vessels/${evt.vesselId}`); }}>{vesselName(evt.vesselId)}</a>
                        ) : '-'}
                      </td>
                      <td className="mono text-dim" onClick={e => e.stopPropagation()}>
                        {evt.voyageId ? (
                          <a href="#" onClick={e => { e.preventDefault(); navigate(`/voyages/${evt.voyageId}`); }}>{evt.voyageId.substring(0, 8)}...</a>
                        ) : '-'}
                      </td>
                      <td style={{ maxWidth: 250, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }} title={evt.message}>
                        {evt.message}
                      </td>
                      <td className="text-dim" style={{ whiteSpace: 'nowrap' }} title={formatDateTime(evt.createdUtc)}>{formatRelativeTime(evt.createdUtc)}</td>
                      <td className="text-right" onClick={e => e.stopPropagation()}>
                        <ActionMenu id={`event-${evt.id}`} items={[
                          { label: 'View Detail', onClick: () => navigate(`/events/${evt.id}`) },
                          { label: 'View JSON', onClick: () => setJsonData({ open: true, title: `${t('Event')}: ${evt.id}`, data: evt }) },
                          { label: 'Delete', danger: true, onClick: () => handleDeleteSingle(evt.id) },
                        ]} />
                      </td>
                    </tr>
                  );
                })}
                {sorted.length === 0 && (
                  <tr><td colSpan={12} className="text-dim">{t('No events match the current filters.')}</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
}
