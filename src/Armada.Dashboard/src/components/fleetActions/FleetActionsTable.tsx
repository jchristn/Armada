import { useCallback, useEffect, useState, type ReactNode } from 'react';
import { deleteFleetAction, enumerateFleetActions, getSettings } from '../../api/client';
import type { FleetAction } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import { useNotifications } from '../../context/NotificationContext';
import { useAuth } from '../../context/AuthContext';
import DataTable, { type DataTableColumn } from '../shared/DataTable';
import ActionMenu from '../shared/ActionMenu';
import ConfirmDialog from '../shared/ConfirmDialog';
import JsonViewer from '../shared/JsonViewer';
import CopyButton from '../shared/CopyButton';
import CodeStatusBadge from '../shared/CodeStatusBadge';
import { EmptyState, ErrorState, LoadingState } from '../shared/StateBlocks';
import FleetActionFormModal from './FleetActionFormModal';
import VesselPickerModal from './VesselPickerModal';
import RunActionModal from './RunActionModal';
import { KIND_DESCRIPTIONS, KIND_LABELS } from '../../lib/fleetActionLabels';
import { usePersistedPageSize } from '../../lib/usePersistedPageSize';

interface FleetActionsTableProps {
  /** Open the run flow (vessel picker then run modal) immediately, e.g. from the Home CTA. */
  startRunOnMount?: boolean;
  onRunFlowStarted?: () => void;
}

/** Actions tab: server-paged table of saved fleet actions with create/edit/duplicate/run/delete. */
export default function FleetActionsTable({ startRunOnMount, onRunFlowStarted }: FleetActionsTableProps) {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const { isTenantAdmin } = useAuth();

  const [rows, setRows] = useState<FleetAction[]>([]);
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = usePersistedPageSize('fleet-actions', 25);
  const [order, setOrder] = useState<'CreatedDescending' | 'CreatedAscending'>('CreatedDescending');
  const [totalPages, setTotalPages] = useState(1);
  const [totalRecords, setTotalRecords] = useState(0);
  const [totalMs, setTotalMs] = useState<number | undefined>(undefined);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [defaultTimeout, setDefaultTimeout] = useState(300);

  const [form, setForm] = useState<{ open: boolean; mode: 'create' | 'edit'; source: FleetAction | null }>({ open: false, mode: 'create', source: null });
  const [json, setJson] = useState<FleetAction | null>(null);
  const [confirmDelete, setConfirmDelete] = useState<FleetAction | null>(null);
  const [runFlow, setRunFlow] = useState<{ stage: 'pick' | 'run' | null; actionId: string | null; vesselIds: string[] }>({ stage: null, actionId: null, vesselIds: [] });

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const result = await enumerateFleetActions({ pageNumber, pageSize, order });
      setRows(result.objects || []);
      setTotalPages(Math.max(1, result.totalPages || 1));
      setTotalRecords(result.totalRecords || 0);
      setTotalMs(result.totalMs);
      setError('');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to load fleet actions.'));
    } finally {
      setLoading(false);
    }
  }, [pageNumber, pageSize, order, t]);

  useEffect(() => { void load(); }, [load]);

  useEffect(() => {
    getSettings()
      .then((s) => {
        const fa = (s as { fleetActions?: { defaultTimeoutSeconds?: number } }).fleetActions;
        if (fa?.defaultTimeoutSeconds) setDefaultTimeout(fa.defaultTimeoutSeconds);
      })
      .catch(() => undefined);
  }, []);

  useEffect(() => {
    if (startRunOnMount) {
      setRunFlow({ stage: 'pick', actionId: null, vesselIds: [] });
      onRunFlowStarted?.();
    }
  }, [startRunOnMount, onRunFlowStarted]);

  function openCreate() { setForm({ open: true, mode: 'create', source: null }); }
  function openEdit(a: FleetAction) { setForm({ open: true, mode: 'edit', source: a }); }
  function openDuplicate(a: FleetAction) { setForm({ open: true, mode: 'create', source: a }); }

  async function handleDelete(a: FleetAction) {
    setConfirmDelete(null);
    try {
      await deleteFleetAction(a.id);
      pushToast('warning', a.isBuiltIn
        ? t('Built-in action "{{name}}" hidden.', { name: a.name })
        : t('Fleet action "{{name}}" deleted.', { name: a.name }));
      void load();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Delete failed.'));
    }
  }

  const toggleOrder = () => {
    setOrder((o) => (o === 'CreatedDescending' ? 'CreatedAscending' : 'CreatedDescending'));
    setPageNumber(1);
  };

  const columns: DataTableColumn<FleetAction>[] = [
    {
      key: 'name', label: t('Name'), required: true, cellClassName: 'cell-nowrap',
      render: (a) => (
        <>
          <strong>{a.name}</strong>
          {!a.active && <span className="tag cancelled" style={{ marginLeft: '0.4rem' }}>{t('Hidden')}</span>}
        </>
      ),
    },
    {
      // ID and description used to be stacked under the name; each is a one-line column now.
      key: 'id', label: t('ID'), required: true, cellClassName: 'mono text-dim table-id-cell',
      render: (a) => (
        <span className="id-display">
          <span className="id-value" title={a.id}>{a.id}</span>
          <CopyButton text={a.id} onClick={(e) => e.stopPropagation()} />
        </span>
      ),
    },
    {
      key: 'description', label: t('Description'), cellClassName: 'text-dim truncate-cell',
      cellTitle: (a) => a.description || undefined,
      render: (a) => (a.description ? <span className="truncate-text">{a.description}</span> : '-'),
    },
    {
      key: 'kind', label: t('Kind'), cellClassName: 'cell-nowrap',
      render: (a) => (
        <CodeStatusBadge
          label={t(KIND_LABELS[a.kind])}
          tone={a.kind === 'Command' ? 'warning' : 'info'}
          icon={a.kind === 'Command' ? 'alert' : 'info'}
          title={t(KIND_DESCRIPTIONS[a.kind])}
        />
      ),
    },
    {
      key: 'source', label: t('Source'), cellClassName: 'cell-nowrap',
      render: (a) => (a.isBuiltIn
        ? <CodeStatusBadge label={t('Built-in')} tone="skipped" icon="lock" title={t('Seeded by Armada. Editable; deleting hides it permanently.')} />
        : <span className="text-dim">{t('Custom')}</span>),
    },
    {
      key: 'timeout', label: t('Timeout'), className: 'text-right', cellClassName: 'mono nowrap',
      render: (a) => (a.kind === 'Command' ? t('{{value}} s', { value: a.timeoutSeconds.toLocaleString() }) : <span className="text-dim">-</span>),
    },
    { key: 'concurrency', label: t('Concurrency'), className: 'text-right', cellClassName: 'mono', render: (a) => a.defaultConcurrency.toLocaleString() },
    {
      key: 'created', label: t('Created'), sortKey: 'created', headerTitle: t('Sort by created date'), cellClassName: 'text-dim nowrap',
      cellTitle: (a) => formatDateTime(a.createdUtc), render: (a) => formatRelativeTime(a.createdUtc),
    },
    {
      key: 'updated', label: t('Updated'), cellClassName: 'text-dim nowrap',
      cellTitle: (a) => formatDateTime(a.lastUpdateUtc), render: (a) => formatRelativeTime(a.lastUpdateUtc),
    },
    {
      key: 'actions', label: t('Actions'), fixed: true, interactive: true, className: 'text-right',
      render: (a) => (
        <ActionMenu
          id={`fleet-action-${a.id}`}
          items={[
            { label: 'Run', onClick: () => setRunFlow({ stage: 'pick', actionId: a.id, vesselIds: [] }), disabled: !isTenantAdmin },
            { label: 'Edit', onClick: () => openEdit(a), disabled: !isTenantAdmin },
            { label: 'Duplicate', onClick: () => openDuplicate(a), disabled: !isTenantAdmin },
            { label: 'View JSON', onClick: () => setJson(a) },
            { label: 'Delete', danger: true, onClick: () => setConfirmDelete(a), disabled: !isTenantAdmin },
          ]}
        />
      ),
    },
  ];

  let placeholder: ReactNode = undefined;
  if (rows.length === 0 && totalRecords === 0) {
    if (loading && !error) placeholder = <LoadingState />;
    else if (error) placeholder = <></>;
    else {
      placeholder = (
        <EmptyState
          title={t('No fleet actions yet')}
          actions={isTenantAdmin ? <button type="button" className="btn btn-primary btn-sm" onClick={openCreate}>+ {t('Action')}</button> : undefined}
        >
          <p>{t('Armada seeds five built-in actions into each tenant the first time actions are listed: Fast-forward default branch, Prune merged branches, Build, Update outdated dependencies, and Add a test project.')}</p>
          <p>{t('Deleted built-ins are hidden for good and never re-seeded. Create your own action to get started.')}</p>
        </EmptyState>
      );
    }
  }

  return (
    <div>
      <div className="table-toolbar">
        <div className="table-toolbar-left text-dim">
          {t('Reusable commands and mission prompts you can run across many vessels.')}
        </div>
        <div className="table-toolbar-right">
          {isTenantAdmin && (
            <button type="button" className="btn btn-sm" onClick={() => setRunFlow({ stage: 'pick', actionId: null, vesselIds: [] })}>
              {t('Run action...')}
            </button>
          )}
          {isTenantAdmin && <button type="button" className="btn btn-primary btn-sm" onClick={openCreate}>+ {t('Action')}</button>}
        </div>
      </div>

      {error && <ErrorState message={error} onRetry={() => void load()} />}

      <DataTable
        tableKey="fleet-actions"
        columns={columns}
        rows={rows}
        rowKey={(a) => a.id}
        onRowClick={(a) => (isTenantAdmin ? openEdit(a) : setJson(a))}
        sort={{ field: 'created', dir: order === 'CreatedAscending' ? 'asc' : 'desc', onSort: toggleOrder }}
        pagination={{
          pageNumber,
          pageSize,
          totalPages,
          totalRecords,
          totalMs,
          onPageChange: setPageNumber,
          onPageSizeChange: (size) => { setPageSize(size); setPageNumber(1); },
        }}
        onRefresh={load}
        refreshTitle="Refresh fleet actions"
        emptyMessage={t('No actions on this page.')}
        placeholder={placeholder}
      />

      <FleetActionFormModal
        open={form.open}
        mode={form.mode}
        source={form.source}
        defaultTimeoutSeconds={defaultTimeout}
        onClose={() => setForm((f) => ({ ...f, open: false }))}
        onSaved={(saved) => {
          setForm((f) => ({ ...f, open: false }));
          pushToast('success', t('Fleet action "{{name}}" saved.', { name: saved.name }));
          void load();
        }}
      />

      <JsonViewer open={json !== null} title={json ? t('Fleet action: {{name}}', { name: json.name }) : ''} id={json?.id} data={json} onClose={() => setJson(null)} />

      <ConfirmDialog
        open={confirmDelete !== null}
        title={confirmDelete?.isBuiltIn ? t('Hide built-in action') : t('Delete fleet action')}
        message={confirmDelete
          ? (confirmDelete.isBuiltIn
            ? t('"{{name}}" is a built-in action. Deleting it hides it permanently (a soft delete) and Armada will not seed it again. Past runs keep their snapshot.', { name: confirmDelete.name })
            : t('Delete "{{name}}"? Past runs keep their snapshot of the definition. This cannot be undone.', { name: confirmDelete.name }))
          : ''}
        confirmLabel={confirmDelete?.isBuiltIn ? t('Hide') : t('Delete')}
        danger
        onConfirm={() => { if (confirmDelete) void handleDelete(confirmDelete); }}
        onCancel={() => setConfirmDelete(null)}
      />

      <VesselPickerModal
        open={runFlow.stage === 'pick'}
        title={t('Choose vessels to run on')}
        onClose={() => setRunFlow({ stage: null, actionId: null, vesselIds: [] })}
        onPicked={(ids) => setRunFlow((f) => ({ ...f, stage: 'run', vesselIds: ids }))}
      />
      <RunActionModal
        open={runFlow.stage === 'run'}
        vesselIds={runFlow.vesselIds}
        initialActionId={runFlow.actionId}
        onClose={() => setRunFlow({ stage: null, actionId: null, vesselIds: [] })}
      />
    </div>
  );
}
