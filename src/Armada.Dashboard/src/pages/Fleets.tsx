import { useEffect, useState, useMemo, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { listFleets, listVessels, listPipelines, createFleet, updateFleet, deleteFleet } from '../api/client';
import type { Fleet, Vessel, Pipeline } from '../types/models';
import Pagination from '../components/shared/Pagination';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import RecordDetailModal from '../components/shared/RecordDetailModal';
import StatusBadge from '../components/shared/StatusBadge';
import CopyButton from '../components/shared/CopyButton';
import RefreshButton from '../components/shared/RefreshButton';
import PageHeader from '../components/shared/PageHeader';
import ErrorModal from '../components/shared/ErrorModal';
import AutoRefreshSelect from '../components/shared/AutoRefreshSelect';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import { buildFleetDuplicatePayload } from '../lib/duplicates';
import { useResourceTable } from '../lib/useResourceTable';

interface FleetWithCount extends Fleet {
  _vesselCount: number;
  _vessels: Vessel[];
}

export default function Fleets() {
  const navigate = useNavigate();
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();
  const [fleets, setFleets] = useState<FleetWithCount[]>([]);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // Modal state
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState<Fleet | null>(null);
  const [form, setForm] = useState({ name: '', description: '', defaultPipelineId: '' });
  const [pipelines, setPipelines] = useState<Pipeline[]>([]);

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });

  // Row-click view modal
  const [viewRecord, setViewRecord] = useState<Record<string, unknown> | null>(null);

  // Confirm dialog
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

  const table = useResourceTable({
    rows: fleets,
    getId: (f) => f.id,
    columnValues: {
      name: (f) => f.name.toLowerCase(),
      description: (f) => f.description ?? '',
      _vesselCount: (f) => f._vesselCount,
      createdUtc: (f) => f.createdUtc,
    },
    initialSortField: 'name',
    initialSortDir: 'asc',
    initialPageSize: 25,
  });

  const load = useCallback(async () => {
    try {
      setLoading(true);
      const [fResult, vResult, pResult] = await Promise.all([listFleets({ pageSize: 9999 }), listVessels({ pageSize: 9999 }), listPipelines({ pageSize: 9999 })]);
      setPipelines(pResult.objects);
      const vesselsByFleet = new Map<string, Vessel[]>();
      for (const v of vResult.objects) {
        if (v.fleetId) {
          const list = vesselsByFleet.get(v.fleetId) || [];
          list.push(v);
          vesselsByFleet.set(v.fleetId, list);
        }
      }
      setFleets(fResult.objects.map(f => ({
        ...f,
        _vesselCount: vesselsByFleet.get(f.id)?.length ?? 0,
        _vessels: vesselsByFleet.get(f.id) ?? [],
      })));
      setVessels(vResult.objects);
      setError('');
    } catch {
      setError(t('Failed to load fleets.'));
    } finally {
      setLoading(false);
    }
  }, [t]);

  useEffect(() => { load(); }, [load]);

  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('fleets', load);

  // CRUD
  function openCreate() { setForm({ name: '', description: '', defaultPipelineId: '' }); setEditing(null); setShowForm(true); }
  function openEdit(f: Fleet) { setForm({ name: f.name, description: f.description ?? '', defaultPipelineId: f.defaultPipelineId ?? '' }); setEditing(f); setShowForm(true); }

  async function handleDuplicate(fleet: Fleet) {
    try {
      const created = await createFleet(buildFleetDuplicatePayload(fleet));
      pushToast('success', t('Fleet "{{name}}" duplicated.', { name: created.name }));
      navigate(`/fleets/${created.id}`);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Duplicate failed.'));
    }
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    try {
      const payload: Record<string, unknown> = { ...form };
      if (!payload.defaultPipelineId) delete payload.defaultPipelineId;
      if (editing) await updateFleet(editing.id, payload);
      else await createFleet(payload);
      setShowForm(false);
      pushToast('success', editing
        ? t('Fleet "{{name}}" saved.', { name: form.name })
        : t('Fleet "{{name}}" created.', { name: form.name }));
      load();
    } catch { setError(t('Save failed.')); }
  }

  function handleDelete(id: string, name: string) {
    setConfirm({
      open: true,
      title: t('Delete Fleet'),
      message: t('Delete fleet "{{name}}"? This cannot be undone.', { name }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await deleteFleet(id);
          pushToast('warning', t('Fleet "{{name}}" deleted.', { name }));
          load();
        } catch { setError(t('Delete failed.')); }
      },
    });
  }

  function handleBulkDelete() {
    setConfirm({
      open: true,
      title: t('Delete Selected Fleets'),
      message: t('Delete {{count}} selected fleet(s)? This cannot be undone.', { count: table.selected.length }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        const ids = [...table.selected];
        table.setSelected([]);
        let failed = 0;
        for (const id of ids) {
          try { await deleteFleet(id); } catch { failed++; }
        }
        const deleted = ids.length - failed;
        if (deleted > 0) {
          pushToast(failed > 0 ? 'warning' : 'success', failed > 0
            ? t('Deleted {{deleted}} fleets. {{failed}} failed.', { deleted, failed })
            : t('Deleted {{deleted}} fleets.', { deleted }));
        }
        if (failed > 0) setError(t('Deleted {{deleted}} fleets, {{failed}} failed.', { deleted: ids.length - failed, failed }));
        load();
      },
    });
  }

  return (
    <div>
      <PageHeader
        title={t('Fleets')}
        subtitle={t('Fleets are groups of vessels (repositories) useful for organizing and understanding relationships amongst code assets.')}
        actions={(
          <>
            <AutoRefreshSelect seconds={refreshSeconds} onChange={setRefreshSeconds} />
            <RefreshButton onRefresh={load} title={t('Refresh fleet data')} />
            {table.selected.length > 0 && (
              <button className="btn btn-sm btn-danger" onClick={handleBulkDelete}>
                {t('Delete Selected')} ({table.selected.length})
              </button>
            )}
            <button className="btn btn-primary btn-sm" onClick={openCreate}>+ {t('Fleet')}</button>
          </>
        )}
      />

      <ErrorModal error={error} onClose={() => setError('')} />

      {/* Create/Edit Modal */}
      {showForm && (
        <div className="modal-overlay" onClick={() => setShowForm(false)}>
          <form className="modal" onClick={e => e.stopPropagation()} onSubmit={handleSubmit}>
            <h3>{editing ? t('Edit Fleet') : t('Create Fleet')}</h3>
            <label>{t('Name')}<input value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} required /></label>
            <label>{t('Description')}<input value={form.description} onChange={e => setForm({ ...form, description: e.target.value })} /></label>
            <label>{t('Default Pipeline')}
              <select value={form.defaultPipelineId} onChange={e => setForm({ ...form, defaultPipelineId: e.target.value })}>
                <option value="">{t('None (WorkerOnly)')}</option>
                {pipelines.map(p => (
                  <option key={p.id} value={p.id}>{p.name} ({p.stages.map(s => s.personaName).join(' -> ')})</option>
                ))}
              </select>
            </label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary">{t('Save')}</button>
              <button type="button" className="btn" onClick={() => setShowForm(false)}>{t('Cancel')}</button>
            </div>
          </form>
        </div>
      )}

      {/* JSON Viewer */}
      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />

      {/* Row-click View Modal */}
      <RecordDetailModal
        open={!!viewRecord}
        title={typeof viewRecord?.name === 'string' ? viewRecord.name : t('Fleet')}
        subtitle={t('Fleet')}
        record={viewRecord}
        onClose={() => setViewRecord(null)}
        onEdit={() => { const r = viewRecord; setViewRecord(null); navigate(`/fleets/${(r as { id: string }).id}`); }}
        editLabel={t('Open Details')}
      />

      {/* Confirm Dialog */}
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message}
        onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />

      {loading && fleets.length === 0 && <p className="text-dim">{t('Loading...')}</p>}
      {!loading && fleets.length === 0 && <p className="text-dim">{t('No fleets configured.')}</p>}

      {fleets.length > 0 && (
        <>
          <Pagination pageNumber={table.currentPage} pageSize={table.pageSize} totalPages={table.totalPages}
            totalRecords={table.sorted.length}
            onPageChange={p => table.setPageNumber(p)} onPageSizeChange={s => { table.setPageSize(s); table.setPageNumber(1); }} />

          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th className="col-checkbox">
                    <input type="checkbox" checked={table.allSelected} onChange={e => e.target.checked ? table.selectAll() : table.clearSelection()} title={t('Select all fleets')} />
                  </th>
                  <th className="sortable" onClick={() => table.handleSort('name')} title={t('Fleet name -- click to sort')}>
                    {t('Name')}{table.sortIcon('name')}
                  </th>
                  <th>{t('ID')}</th>
                  <th className="sortable" onClick={() => table.handleSort('description')} title={t('Description -- click to sort')}>
                    {t('Description')}{table.sortIcon('description')}
                  </th>
                  <th className="sortable" onClick={() => table.handleSort('_vesselCount')} title={t('Vessel count -- click to sort')}>
                    {t('Vessels')}{table.sortIcon('_vesselCount')}
                  </th>
                  <th>{t('Active')}</th>
                  <th className="sortable" onClick={() => table.handleSort('createdUtc')} title={t('Created date -- click to sort')}>
                    {t('Created')}{table.sortIcon('createdUtc')}
                  </th>
                  <th className="text-right">{t('Actions')}</th>
                </tr>
                <tr className="column-filter-row">
                  <td></td>
                  <td><input type="text" className="col-filter" value={table.colFilters.name ?? ''} onChange={e => table.setColFilter('name', e.target.value)} placeholder={t('Filter...')} /></td>
                  <td></td>
                  <td><input type="text" className="col-filter" value={table.colFilters.description ?? ''} onChange={e => table.setColFilter('description', e.target.value)} placeholder={t('Filter...')} /></td>
                  <td></td>
                  <td></td>
                  <td></td>
                  <td></td>
                </tr>
              </thead>
              <tbody>
                {table.paginated.map(f => (
                  <tr key={f.id} className="clickable" onClick={() => setViewRecord(f as unknown as Record<string, unknown>)}>
                    <td className="col-checkbox" onClick={e => e.stopPropagation()}>
                      <input type="checkbox" checked={table.selected.includes(f.id)} onChange={() => table.toggleSelect(f.id)} title={t('Select this fleet')} />
                    </td>
                    <td><strong>{f.name}</strong></td>
                    <td className="mono text-dim table-id-cell">
                      <span className="id-display">
                        <span className="id-value" title={f.id}>{f.id}</span>
                        <CopyButton text={f.id} onClick={e => e.stopPropagation()} />
                      </span>
                    </td>
                    <td className="text-dim">{f.description || '-'}</td>
                    <td>{f._vesselCount}</td>
                    <td>{f.active !== false ? t('Yes') : t('No')}</td>
                    <td className="text-dim" title={formatDateTime(f.createdUtc)}>{formatRelativeTime(f.createdUtc)}</td>
                    <td className="text-right" onClick={e => e.stopPropagation()}>
                      <ActionMenu id={`fleet-${f.id}`} items={[
                        { label: 'View Detail', onClick: () => navigate(`/fleets/${f.id}`) },
                        { label: 'Edit', onClick: () => openEdit(f) },
                        { label: 'Duplicate', onClick: () => void handleDuplicate(f) },
                        { label: 'View JSON', onClick: () => setJsonData({ open: true, title: `${t('Fleet')}: ${f.name}`, data: f }) },
                        { label: 'Delete', danger: true, onClick: () => handleDelete(f.id, f.name) },
                      ]} />
                    </td>
                  </tr>
                ))}
                {table.paginated.length === 0 && (
                  <tr><td colSpan={8} className="text-dim">{t('No fleets match the current filters.')}</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
}
