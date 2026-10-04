import { useCallback, useEffect, useState } from 'react';
import { deleteFleetAction, enumerateFleetActions, getSettings } from '../../api/client';
import type { FleetAction } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import { useNotifications } from '../../context/NotificationContext';
import { useAuth } from '../../context/AuthContext';
import Pagination from '../shared/Pagination';
import ActionMenu from '../shared/ActionMenu';
import ConfirmDialog from '../shared/ConfirmDialog';
import JsonViewer from '../shared/JsonViewer';
import RefreshButton from '../shared/RefreshButton';
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

  return (
    <div>
      <div className="table-toolbar">
        <div className="table-toolbar-left text-dim">
          {t('Reusable commands and mission prompts you can run across many vessels.')}
        </div>
        <div className="table-toolbar-right">
          <RefreshButton onRefresh={load} title={t('Refresh fleet actions')} />
          {isTenantAdmin && (
            <button type="button" className="btn btn-sm" onClick={() => setRunFlow({ stage: 'pick', actionId: null, vesselIds: [] })}>
              {t('Run action...')}
            </button>
          )}
          {isTenantAdmin && <button type="button" className="btn btn-primary btn-sm" onClick={openCreate}>+ {t('Action')}</button>}
        </div>
      </div>

      {error && <ErrorState message={error} onRetry={() => void load()} />}

      {loading && rows.length === 0 && !error && <LoadingState />}

      {!loading && !error && rows.length === 0 && totalRecords === 0 && (
        <EmptyState
          title={t('No fleet actions yet')}
          actions={isTenantAdmin ? <button type="button" className="btn btn-primary btn-sm" onClick={openCreate}>+ {t('Action')}</button> : undefined}
        >
          <p>{t('Armada seeds five built-in actions into each tenant the first time actions are listed: Fast-forward default branch, Prune merged branches, Build, Update outdated dependencies, and Add a test project.')}</p>
          <p>{t('Deleted built-ins are hidden for good and never re-seeded. Create your own action to get started.')}</p>
        </EmptyState>
      )}

      {(rows.length > 0 || totalRecords > 0) && (
        <>
          <Pagination
            pageNumber={pageNumber}
            pageSize={pageSize}
            totalPages={totalPages}
            totalRecords={totalRecords}
            totalMs={totalMs}
            onPageChange={setPageNumber}
            onPageSizeChange={(s) => { setPageSize(s); setPageNumber(1); }}
          />
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th scope="col">{t('Name')}</th>
                  <th scope="col">{t('Kind')}</th>
                  <th scope="col">{t('Source')}</th>
                  <th scope="col" className="text-right">{t('Timeout')}</th>
                  <th scope="col" className="text-right">{t('Concurrency')}</th>
                  <th
                    scope="col"
                    className="sortable"
                    aria-sort={order === 'CreatedAscending' ? 'ascending' : 'descending'}
                  >
                    <button type="button" className="th-sort-btn" onClick={toggleOrder} title={t('Sort by created date')}>
                      {t('Created')} <span aria-hidden="true">{order === 'CreatedAscending' ? '\u25B2' : '\u25BC'}</span>
                    </button>
                  </th>
                  <th scope="col">{t('Updated')}</th>
                  <th scope="col" className="text-right">{t('Actions')}</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((a) => (
                  <tr key={a.id} className="clickable" onClick={() => (isTenantAdmin ? openEdit(a) : setJson(a))}>
                    <td>
                      <strong>{a.name}</strong>
                      {!a.active && <span className="tag cancelled" style={{ marginLeft: '0.4rem' }}>{t('Hidden')}</span>}
                      <div className="text-dim cell-subline id-display">
                        <span className="mono id-value" title={a.id}>{a.id}</span>
                        <CopyButton text={a.id} onClick={(e) => e.stopPropagation()} />
                      </div>
                      {a.description && <div className="text-dim cell-subline">{a.description}</div>}
                    </td>
                    <td>
                      <CodeStatusBadge
                        label={t(KIND_LABELS[a.kind])}
                        tone={a.kind === 'Command' ? 'warning' : 'info'}
                        icon={a.kind === 'Command' ? 'alert' : 'info'}
                        title={t(KIND_DESCRIPTIONS[a.kind])}
                      />
                    </td>
                    <td>
                      {a.isBuiltIn
                        ? <CodeStatusBadge label={t('Built-in')} tone="skipped" icon="lock" title={t('Seeded by Armada. Editable; deleting hides it permanently.')} />
                        : <span className="text-dim">{t('Custom')}</span>}
                    </td>
                    <td className="text-right mono">{a.kind === 'Command' ? t('{{value}} s', { value: a.timeoutSeconds.toLocaleString() }) : <span className="text-dim">-</span>}</td>
                    <td className="text-right mono">{a.defaultConcurrency.toLocaleString()}</td>
                    <td className="text-dim nowrap" title={formatDateTime(a.createdUtc)}>{formatRelativeTime(a.createdUtc)}</td>
                    <td className="text-dim nowrap" title={formatDateTime(a.lastUpdateUtc)}>{formatRelativeTime(a.lastUpdateUtc)}</td>
                    <td className="text-right" onClick={(e) => e.stopPropagation()}>
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
                    </td>
                  </tr>
                ))}
                {rows.length === 0 && (
                  <tr><td colSpan={8} className="text-dim">{t('No actions on this page.')}</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </>
      )}

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
