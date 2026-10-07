import { useEffect, useState, useMemo, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { listEvents, deleteEventsBatch, listCaptains, listVessels } from '../api/client';
import type { ArmadaEvent, Captain, Vessel } from '../types/models';
import DataTable, { type DataTableColumn } from '../components/shared/DataTable';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import RecordDetailModal from '../components/shared/RecordDetailModal';
import CopyButton from '../components/shared/CopyButton';
import PageHeader from '../components/shared/PageHeader';
import ErrorModal from '../components/shared/ErrorModal';
import UserScopeFilter from '../components/shared/UserScopeFilter';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import { entityRoute } from '../lib/routing';

type SortDir = 'asc' | 'desc';
type SortField = 'eventType' | 'entityType' | 'createdUtc';

export default function Events() {
  const navigate = useNavigate();
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();
  const [events, setEvents] = useState<ArmadaEvent[]>([]);
  const [viewRecord, setViewRecord] = useState<Record<string, unknown> | null>(null);
  const [captains, setCaptains] = useState<Captain[]>([]);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Pagination (server-side)
  const [pageNumber, setPageNumber] = useState(1);
  const [userScope, setUserScope] = useState('');
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
      const result = await listEvents({ pageNumber, pageSize, filters: userScope ? { userId: userScope } : undefined });
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
  }, [pageNumber, pageSize, userScope, t]);

  useEffect(() => { load(); }, [load]);

  useEffect(() => {
    listCaptains({ pageSize: 1000 }).then(r => setCaptains(r.objects || [])).catch(() => {});
    listVessels({ pageSize: 1000 }).then(r => setVessels(r.objects || [])).catch(() => {});
  }, []);

  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('events', load);

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

  function colFilterInput(key: 'eventType' | 'entityType' | 'message', label: string) {
    return <input type="text" className="col-filter" aria-label={label} value={colFilters[key]} onChange={e => setColFilters(f => ({ ...f, [key]: e.target.value }))} placeholder={t('Filter...')} />;
  }

  // Mission and Voyage repeat what Entity ID usually already links to, so they start hidden (column chooser).
  const columns: DataTableColumn<ArmadaEvent>[] = [
    {
      key: 'id', label: t('ID'), required: true, cellClassName: 'mono text-dim table-id-cell',
      render: (evt) => (
        <span className="id-display">
          <span className="id-value" title={evt.id}>{evt.id}</span>
          <CopyButton text={evt.id} onClick={e => e.stopPropagation()} />
        </span>
      ),
    },
    {
      key: 'eventType', label: t('Event Type'), sortKey: 'eventType', headerTitle: t('Event type -- click to sort'),
      clearFilter: () => setColFilters(f => ({ ...f, eventType: '' })), filter: colFilterInput('eventType', t('Event Type')),
      render: (evt) => <span className="cell-clip" title={evt.eventType}><span>{evt.eventType}</span></span>,
    },
    {
      key: 'entityType', label: t('Entity Type'), sortKey: 'entityType', headerTitle: t('Entity type -- click to sort'), cellClassName: 'text-dim',
      clearFilter: () => setColFilters(f => ({ ...f, entityType: '' })), filter: colFilterInput('entityType', t('Entity Type')),
      render: (evt) => evt.entityType || '-',
    },
    {
      key: 'entityId', label: t('Entity ID'), cellClassName: 'mono text-dim table-id-cell', interactive: true,
      render: (evt) => {
        if (!evt.entityId) return '-';
        const entRoute = entityRoute(evt.entityType, evt.entityId);
        return (
          <span className="id-display">
            {entRoute ? (
              <a href="#" className="id-value" title={evt.entityId} onClick={e => { e.preventDefault(); navigate(entRoute); }}>{evt.entityId}</a>
            ) : (
              <span className="id-value" title={evt.entityId}>{evt.entityId}</span>
            )}
            <CopyButton text={evt.entityId} onClick={e => e.stopPropagation()} />
          </span>
        );
      },
    },
    {
      key: 'captain', label: t('Captain'), interactive: true,
      render: (evt) => evt.captainId ? <a href="#" onClick={e => { e.preventDefault(); navigate(`/captains/${evt.captainId}`); }}>{captainName(evt.captainId)}</a> : '-',
    },
    {
      key: 'mission', label: t('Mission'), defaultHidden: true, cellClassName: 'mono text-dim', interactive: true,
      render: (evt) => evt.missionId ? <span className="cell-clip"><a href="#" title={evt.missionId} onClick={e => { e.preventDefault(); navigate(`/missions/${evt.missionId}`); }}>{evt.missionId}</a></span> : '-',
    },
    {
      key: 'vessel', label: t('Vessel'), interactive: true,
      render: (evt) => evt.vesselId ? <a href="#" onClick={e => { e.preventDefault(); navigate(`/vessels/${evt.vesselId}`); }}>{vesselName(evt.vesselId)}</a> : '-',
    },
    {
      key: 'voyage', label: t('Voyage'), defaultHidden: true, cellClassName: 'mono text-dim', interactive: true,
      render: (evt) => evt.voyageId ? <span className="cell-clip"><a href="#" title={evt.voyageId} onClick={e => { e.preventDefault(); navigate(`/voyages/${evt.voyageId}`); }}>{evt.voyageId}</a></span> : '-',
    },
    {
      key: 'message', label: t('Message'), cellClassName: 'truncate-cell', cellTitle: (evt) => evt.message ?? undefined,
      clearFilter: () => setColFilters(f => ({ ...f, message: '' })), filter: colFilterInput('message', t('Message')),
      render: (evt) => <span className="truncate-text">{evt.message}</span>,
    },
    {
      key: 'created', label: t('Created'), sortKey: 'createdUtc', headerTitle: t('Created -- click to sort'),
      cellClassName: 'text-dim cell-nowrap', cellTitle: (evt) => formatDateTime(evt.createdUtc),
      render: (evt) => formatRelativeTime(evt.createdUtc),
    },
    {
      key: 'actions', label: t('Actions'), fixed: true, interactive: true, className: 'text-right',
      render: (evt) => (
        <ActionMenu id={`event-${evt.id}`} items={[
          { label: 'View Detail', onClick: () => navigate(`/events/${evt.id}`) },
          { label: 'View JSON', onClick: () => setJsonData({ open: true, title: `${t('Event')}: ${evt.id}`, data: evt }) },
          { label: 'Delete', danger: true, onClick: () => handleDeleteSingle(evt.id) },
        ]} />
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title={t('Events')}
        subtitle={t('System event log capturing state changes, completions, failures, and other notable occurrences.')}
        actions={(
          <>
            <UserScopeFilter value={userScope} onChange={(id) => { setUserScope(id); setPageNumber(1); }} />
            {selected.length > 0 && (
              <button className="btn btn-sm btn-danger" onClick={handleBulkDelete}>
                {t('Delete Selected')} ({selected.length})
              </button>
            )}
          </>
        )}
      />

      <ErrorModal error={error} onClose={() => setError('')} />

      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <RecordDetailModal
        open={!!viewRecord}
        title={viewRecord ? `${(viewRecord as { eventType?: string }).eventType || t('Event')}` : t('Event')}
        subtitle={viewRecord ? String((viewRecord as { id?: string }).id ?? '') : ''}
        record={viewRecord}
        onClose={() => setViewRecord(null)}
        onEdit={() => { const r = viewRecord; setViewRecord(null); if (r) navigate(`/events/${(r as { id: string }).id}`); }}
        editLabel={t('Open Details')}
      />
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message}
        onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />

      <DataTable
        tableKey="events"
        columns={columns}
        rows={sorted}
        rowKey={(evt) => evt.id}
        onRowClick={(evt) => setViewRecord(evt as unknown as Record<string, unknown>)}
        sort={{ field: sortField, dir: sortDir, onSort: (field) => handleSort(field as SortField) }}
        pagination={{
          pageNumber, pageSize, totalPages, totalRecords,
          onPageChange: (p) => setPageNumber(p),
          onPageSizeChange: (size) => { setPageSize(size); setPageNumber(1); },
        }}
        autoRefresh={{ seconds: refreshSeconds, onChange: setRefreshSeconds }}
        onRefresh={load}
        refreshTitle={t('Refresh event data')}
        selection={{
          isSelected: (evt) => selected.includes(evt.id),
          onToggle: (evt) => toggleSelect(evt.id),
          allSelected,
          onToggleAll: (checked) => (checked ? selectAll() : clearSelection()),
          selectAllLabel: t('Select all events'),
          rowLabel: () => t('Select this event'),
        }}
        className="table-dense"
        emptyMessage={t('No events match the current filters.')}
        placeholder={events.length > 0 ? undefined : <p className="text-dim">{loading ? t('Loading...') : t('No events found.')}</p>}
      />
    </div>
  );
}
