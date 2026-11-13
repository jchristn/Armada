import { useEffect, useState, useMemo, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { listFleets, listVessels, listPipelines, createFleet, updateFleet, deleteFleet, apiErrorMessage } from '../api/client';
import { listAll } from '../api/paging';
import type { Fleet, Vessel, Pipeline } from '../types/models';
import DataTable, { type DataTableColumn } from '../components/shared/DataTable';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import RecordDetailModal from '../components/shared/RecordDetailModal';
import StatusBadge from '../components/shared/StatusBadge';
import CopyButton from '../components/shared/CopyButton';
import PageHeader from '../components/shared/PageHeader';
import ErrorModal from '../components/shared/ErrorModal';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import { buildFleetDuplicatePayload } from '../lib/duplicates';
import { useResourceTable } from '../lib/useResourceTable';
import { useDeleteProgress } from '../lib/useDeleteProgress';

interface FleetWithCount extends Fleet {
  _vesselCount: number;
  _vessels: Vessel[];
}

export default function Fleets() {
  const navigate = useNavigate();
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();
  const deletion = useDeleteProgress();
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
      const [fResult, vResult, pResult] = await Promise.all([listAll(listFleets), listAll(listVessels), listAll(listPipelines)]);
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
    } catch (err: unknown) { setError(apiErrorMessage(err, t('Save failed.'))); }
  }

  function handleDelete(id: string, name: string) {
    setConfirm({
      open: true,
      title: t('Delete Fleet'),
      message: t('Delete fleet "{{name}}"? This cannot be undone.', { name }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        if (!(await deletion.run({ kind: 'fleet', action: () => deleteFleet(id), errorMessage: (err) => apiErrorMessage(err, t('Delete failed.')) }))) return;
        pushToast('warning', t('Fleet "{{name}}" deleted.', { name }));
        load();
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
        if (!(await deletion.run({
          kind: 'fleet',
          count: ids.length,
          action: async () => {
            failed = 0;
            for (const id of ids) {
              try { await deleteFleet(id); } catch { failed++; }
            }
          },
        }))) return;
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

  const columns: DataTableColumn<FleetWithCount>[] = [
    {
      key: 'id', label: t('ID'), headerTitle: t('Fleet ID (flt_...), used by the CLI, API and MCP tools.'), required: true, cellClassName: 'mono text-dim table-id-cell',
      render: (f) => (
        <span className="id-display">
          <span className="id-value" title={f.id}>{f.id}</span>
          <CopyButton text={f.id} onClick={e => e.stopPropagation()} />
        </span>
      ),
    },
    {
      key: 'name', label: t('Name'), required: true, sortKey: 'name', headerTitle: t('Fleet name. Click to sort; type in the box below to filter.'),
      filter: <input type="text" className="col-filter" aria-label={t('Name')} value={table.colFilters.name ?? ''} onChange={e => table.setColFilter('name', e.target.value)} placeholder={t('Filter...')} />,
      render: (f) => <strong>{f.name}</strong>,
    },
    {
      key: 'description', label: t('Description'), sortKey: 'description', headerTitle: t('Fleet description. Click to sort; type in the box below to filter.'),
      cellClassName: 'text-dim truncate-cell',
      clearFilter: () => table.setColFilter('description', ''),
      filter: <input type="text" className="col-filter" aria-label={t('Description')} value={table.colFilters.description ?? ''} onChange={e => table.setColFilter('description', e.target.value)} placeholder={t('Filter...')} />,
      render: (f) => f.description ? <span className="truncate-text" title={f.description}>{f.description}</span> : '-',
    },
    { key: 'vessels', label: t('Vessels'), sortKey: '_vesselCount', headerTitle: t('Number of vessels (repositories) in the fleet. Click to sort.'), render: (f) => f._vesselCount },
    { key: 'active', label: t('Active'), headerTitle: t('Whether the fleet is marked active. This is a label only; it does not stop dispatches to its vessels.'), render: (f) => (f.active !== false ? t('Yes') : t('No')) },
    {
      key: 'created', label: t('Created'), sortKey: 'createdUtc', headerTitle: t('When the fleet was created. Hover a cell for the exact time; click to sort.'),
      cellClassName: 'text-dim cell-nowrap', cellTitle: (f) => formatDateTime(f.createdUtc),
      render: (f) => formatRelativeTime(f.createdUtc),
    },
    {
      key: 'actions', label: t('Actions'), headerTitle: t('Open, edit, duplicate, inspect or delete a fleet.'), fixed: true, interactive: true, className: 'text-right',
      render: (f) => (
        <ActionMenu id={`fleet-${f.id}`} items={[
          { label: 'View Detail', title: 'Open the fleet page with its vessels and settings.', onClick: () => navigate(`/fleets/${f.id}`) },
          { label: 'Edit', title: 'Change the fleet name, description or default pipeline.', onClick: () => openEdit(f) },
          { label: 'Duplicate', title: 'Create a new fleet with the same description and default pipeline. Vessels are not copied.', onClick: () => void handleDuplicate(f) },
          { label: 'View JSON', title: 'Show the raw fleet record as JSON.', onClick: () => setJsonData({ open: true, title: `${t('Fleet')}: ${f.name}`, data: f }) },
          { label: 'Delete', title: 'Delete the fleet after you confirm. Its vessels are kept and left without a fleet.', danger: true, onClick: () => handleDelete(f.id, f.name) },
        ]} />
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title={t('Fleets')}
        subtitle={t('Fleets are groups of vessels (repositories) useful for organizing and understanding relationships amongst code assets.')}
        actions={(
          <>
            {table.selected.length > 0 && (
              <button className="btn btn-sm btn-danger" onClick={handleBulkDelete} title={t('Delete every checked fleet after you confirm. Their vessels are kept and left without a fleet.')}>
                {t('Delete Selected')} ({table.selected.length})
              </button>
            )}
            <button className="btn btn-primary btn-sm" onClick={openCreate} title={t('Create a new fleet to group related vessels.')}>+ {t('Fleet')}</button>
          </>
        )}
      />

      <ErrorModal error={error} onClose={() => setError('')} />

      {/* Create/Edit Modal */}
      {showForm && (
        <div className="modal-overlay" onClick={() => setShowForm(false)}>
          <form className="modal" onClick={e => e.stopPropagation()} onSubmit={handleSubmit}>
            <h3>{editing ? t('Edit Fleet') : t('Create Fleet')}</h3>
            <label title={t('Fleet name shown across Armada and used to pick the fleet in dispatch, e.g. Payments or Web Frontends. Required.')}>{t('Name')}<input title={t('Fleet name shown across Armada and used to pick the fleet in dispatch, e.g. Payments or Web Frontends. Required.')} value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} required /></label>
            <label title={t('Optional note about what the repositories in this fleet have in common. Shown in the fleet list.')}>{t('Description')}<input title={t('Optional note about what the repositories in this fleet have in common. Shown in the fleet list.')} value={form.description} onChange={e => setForm({ ...form, description: e.target.value })} /></label>
            <label title={t('Pipeline used for dispatches to vessels in this fleet when the vessel itself has no default pipeline. None means a single Worker stage.')}>{t('Default Pipeline')}
              <select title={t('Pipeline used for dispatches to vessels in this fleet when the vessel itself has no default pipeline. None means a single Worker stage.')} value={form.defaultPipelineId} onChange={e => setForm({ ...form, defaultPipelineId: e.target.value })}>
                <option value="">{t('None (WorkerOnly)')}</option>
                {pipelines.map(p => (
                  <option key={p.id} value={p.id}>{p.name} ({p.stages.map(s => s.personaName).join(' -> ')})</option>
                ))}
              </select>
            </label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary" title={editing ? t('Save your changes to this fleet.') : t('Create the fleet. Add vessels to it from the fleet page or the vessel form.')}>{t('Save')}</button>
              <button type="button" className="btn" onClick={() => setShowForm(false)} title={t('Close without saving.')}>{t('Cancel')}</button>
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
        secondaryAction={viewRecord
          ? { label: t('Edit'), title: t('Change the fleet name, description or default pipeline.'), onClick: () => { const r = viewRecord as unknown as Fleet; setViewRecord(null); openEdit(r); } }
          : undefined}
      />

      {/* Confirm Dialog */}
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message}
        onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />
      {deletion.dialog}

      <DataTable
        tableKey="fleets"
        columns={columns}
        rows={table.paginated}
        rowKey={(f) => f.id}
        onRowClick={(f) => setViewRecord(f as unknown as Record<string, unknown>)}
        sort={table.sortState}
        pagination={table.paginationProps}
        autoRefresh={{ seconds: refreshSeconds, onChange: setRefreshSeconds }}
        onRefresh={load}
        refreshTitle={t('Reload the fleet list and vessel counts now.')}
        selection={{
          isSelected: (f) => table.selected.includes(f.id),
          onToggle: (f) => table.toggleSelect(f.id),
          allSelected: table.allSelected,
          onToggleAll: (checked) => (checked ? table.selectAll() : table.clearSelection()),
          selectAllLabel: t('Select all fleets'),
          rowLabel: () => t('Select this fleet'),
        }}
        emptyMessage={t('No fleets match the current filters.')}
        placeholder={fleets.length > 0 ? undefined : <p className="text-dim">{loading ? t('Loading...') : t('No fleets configured.')}</p>}
      />
    </div>
  );
}
