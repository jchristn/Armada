import { useEffect, useState, useMemo, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { listVoyages, cancelVoyage, purgeVoyage, getVoyageStatus } from '../api/client';
import type { Voyage } from '../types/models';
import Pagination from '../components/shared/Pagination';
import ActionMenu from '../components/shared/ActionMenu';
import StatusBadge from '../components/shared/StatusBadge';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import CopyButton from '../components/shared/CopyButton';
import RefreshButton from '../components/shared/RefreshButton';
import ErrorModal from '../components/shared/ErrorModal';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';

type SortDir = 'asc' | 'desc';
type SortField = 'title' | 'status' | 'createdUtc';

export default function Voyages() {
  const navigate = useNavigate();
  const { t } = useLocale();
  const { pushToast } = useNotifications();
  const [voyages, setVoyages] = useState<Voyage[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Pagination (server-side)
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(25);
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
  const [colFilters, setColFilters] = useState({ title: '', status: '' });

  const load = useCallback(async () => {
    try {
      setLoading(true);
      const result = await listVoyages({ pageNumber, pageSize });
      setVoyages(result.objects || []);
      setTotalPages(result.totalPages || 1);
      setTotalRecords(result.totalRecords || 0);
      setError('');
    } catch {
      setError(t('Failed to load voyages.'));
    } finally {
      setLoading(false);
    }
  }, [pageNumber, pageSize, t]);

  useEffect(() => { load(); }, [load]);

  // Client-side filter + sort
  const filtered = useMemo(() => {
    return voyages.filter(v =>
      (!colFilters.title || v.title.toLowerCase().includes(colFilters.title.toLowerCase())) &&
      (!colFilters.status || (v.status ?? '').toLowerCase().includes(colFilters.status.toLowerCase()))
    );
  }, [voyages, colFilters]);

  const sorted = useMemo(() => {
    const arr = [...filtered];
    arr.sort((a, b) => {
      let va: string = '';
      let vb: string = '';
      switch (sortField) {
        case 'title': va = a.title.toLowerCase(); vb = b.title.toLowerCase(); break;
        case 'status': va = (a.status ?? '').toLowerCase(); vb = (b.status ?? '').toLowerCase(); break;
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
  function selectAll() { setSelected(sorted.map(v => v.id)); }
  function clearSelection() { setSelected([]); }

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
      message: t('Cancel {{count}} selected voyage(s)?', { count: selected.length }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        const ids = [...selected];
        setSelected([]);
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

  return (
    <div>
      <div className="view-header">
        <div>
          <h2>{t('Voyages')}</h2>
          <p className="text-dim view-subtitle">{t('Batches of related missions dispatched together')}</p>
        </div>
        <div className="view-actions">
          {selected.length > 0 && (
            <button className="btn btn-sm btn-danger" onClick={handleBulkCancel}>
              {t('Cancel Selected')} ({selected.length})
            </button>
          )}
          <button className="btn btn-primary btn-sm" onClick={() => navigate('/voyages/create')}>+ {t('Voyage')}</button>
          <RefreshButton onRefresh={load} title="Refresh voyage data" />
        </div>
      </div>

      <ErrorModal error={error} onClose={() => setError('')} />

      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message}
        onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />

      {loading && voyages.length === 0 && <p className="text-dim">{t('Loading...')}</p>}
      {!loading && voyages.length === 0 && <p className="text-dim">{t('No voyages found.')}</p>}

      {voyages.length > 0 && (
        <>
          <Pagination pageNumber={pageNumber} pageSize={pageSize} totalPages={totalPages}
            totalRecords={totalRecords}
            onPageChange={p => setPageNumber(p)} onPageSizeChange={s => { setPageSize(s); setPageNumber(1); }} />

          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th className="col-checkbox">
                    <input type="checkbox" checked={allSelected} onChange={e => e.target.checked ? selectAll() : clearSelection()} title={t('Select all voyages')} />
                  </th>
                  <th className="sortable" onClick={() => handleSort('title')} title={t('Voyage title -- click to sort')}>
                    {t('Title')}{sortIcon('title')}
                  </th>
                  <th>{t('ID')}</th>
                  <th className="sortable" onClick={() => handleSort('status')} title={t('Status -- click to sort')}>
                    {t('Status')}{sortIcon('status')}
                  </th>
                  <th>{t('Auto Push')}</th>
                  <th>{t('Auto Create PRs')}</th>
                  <th>{t('Landing Mode')}</th>
                  <th className="text-right">{t('Actions')}</th>
                </tr>
                <tr className="column-filter-row">
                  <td></td>
                  <td><input type="text" className="col-filter" value={colFilters.title} onChange={e => setColFilters(f => ({ ...f, title: e.target.value }))} placeholder={t('Search...')} /></td>
                  <td></td>
                  <td><input type="text" className="col-filter" value={colFilters.status} onChange={e => setColFilters(f => ({ ...f, status: e.target.value }))} placeholder={t('Search...')} /></td>
                  <td></td>
                  <td></td>
                  <td></td>
                  <td></td>
                </tr>
              </thead>
              <tbody>
                {sorted.map(v => (
                  <tr key={v.id} className="clickable" onClick={() => navigate(`/voyages/${v.id}`)}>
                    <td className="col-checkbox" onClick={e => e.stopPropagation()}>
                      <input type="checkbox" checked={selected.includes(v.id)} onChange={() => toggleSelect(v.id)} title={t('Select this voyage')} />
                    </td>
                    <td className="truncate-cell" title={v.title}>
                      <strong className="truncate-text">{v.title}</strong>
                    </td>
                    <td className="mono text-dim table-id-cell">
                      <span className="id-display">
                        <span className="id-value" title={v.id}>{v.id}</span>
                        <CopyButton text={v.id} onClick={e => e.stopPropagation()} />
                      </span>
                    </td>
                    <td><StatusBadge status={v.status} /></td>
                    <td>{v.autoPush != null ? (v.autoPush ? t('Yes') : t('No')) : '-'}</td>
                    <td>{v.autoCreatePullRequests != null ? (v.autoCreatePullRequests ? t('Yes') : t('No')) : '-'}</td>
                    <td className="text-dim">{v.landingMode || '-'}</td>
                    <td className="text-right" onClick={e => e.stopPropagation()}>
                      <ActionMenu id={`voyage-${v.id}`} items={[
                        { label: 'View Detail', onClick: () => navigate(`/voyages/${v.id}`) },
                        { label: 'View Status', onClick: () => handleViewStatus(v.id) },
                        { label: 'View JSON', onClick: () => setJsonData({ open: true, title: `Voyage: ${v.title}`, data: v }) },
                        { label: 'Cancel', danger: true, onClick: () => handleCancel(v.id, v.title) },
                        { label: 'Purge', danger: true, onClick: () => handlePurge(v.id, v.title) },
                      ]} />
                    </td>
                  </tr>
                ))}
                {sorted.length === 0 && (
                  <tr><td colSpan={8} className="text-dim">{t('No voyages match the current filters.')}</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
}
