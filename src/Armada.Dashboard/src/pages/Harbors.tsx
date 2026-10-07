import { useEffect, useMemo, useState } from 'react';
import {
  listHarbors,
  createHarbor,
  updateHarbor,
  deleteHarbor,
  enableHarbor,
  disableHarbor,
} from '../api/client';
import type { Harbor } from '../types/models';
import { useAuth } from '../context/AuthContext';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import ErrorModal from '../components/shared/ErrorModal';
import JsonViewer from '../components/shared/JsonViewer';
import CopyButton from '../components/shared/CopyButton';
import DataTable, { type DataTableColumn } from '../components/shared/DataTable';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import PageHeader from '../components/shared/PageHeader';
import StatusBadge from '../components/shared/StatusBadge';

interface HarborForm {
  name: string;
  maxConcurrentJobs: string;
  enabled: boolean;
}

const EMPTY_FORM: HarborForm = { name: 'New Harbor', maxConcurrentJobs: '4', enabled: true };

export default function Harbors() {
  const { isAdmin, isTenantAdmin } = useAuth();
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();
  const [harbors, setHarbors] = useState<Harbor[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState('all');
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({
    open: false, title: '', message: '', onConfirm: () => {},
  });

  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState<Harbor | null>(null);
  const [saving, setSaving] = useState(false);
  const [form, setForm] = useState<HarborForm>(EMPTY_FORM);
  const [detail, setDetail] = useState<{ open: boolean; harbor: Harbor | null }>({ open: false, harbor: null });

  const canManage = isAdmin || isTenantAdmin;

  function openCreate() {
    setEditing(null);
    setForm(EMPTY_FORM);
    setShowForm(true);
  }

  function openEdit(harbor: Harbor) {
    setEditing(harbor);
    setForm({ name: harbor.name, maxConcurrentJobs: String(harbor.maxConcurrentJobs ?? 4), enabled: harbor.enabled });
    setShowForm(true);
  }

  async function handleSave(e: React.FormEvent) {
    e.preventDefault();
    if (saving) return;
    setSaving(true);
    try {
      const payload: Partial<Harbor> = {
        name: form.name,
        maxConcurrentJobs: Number.parseInt(form.maxConcurrentJobs, 10) || 4,
        enabled: form.enabled,
      };
      if (editing) {
        const updated = await updateHarbor(editing.id, payload);
        setShowForm(false);
        pushToast('success', t('Harbor "{{name}}" saved.', { name: updated.name }));
      } else {
        const created = await createHarbor(payload);
        setShowForm(false);
        pushToast('success', t('Harbor "{{name}}" registered.', { name: created.name }));
      }
      await load();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Save failed.'));
    } finally {
      setSaving(false);
    }
  }

  async function load() {
    try {
      setLoading(true);
      const result = await listHarbors();
      setHarbors(result || []);
      setError('');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to load harbors.'));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { load(); }, []);

  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('harbors', load);

  const filtered = useMemo(() => harbors.filter((harbor) => {
    const matchesSearch = search.trim().length === 0
      || harbor.name.toLowerCase().includes(search.toLowerCase())
      || harbor.id.toLowerCase().includes(search.toLowerCase())
      || (harbor.osPlatform || '').toLowerCase().includes(search.toLowerCase());
    const matchesStatus = statusFilter === 'all' || harbor.connectionStatus === statusFilter;
    return matchesSearch && matchesStatus;
  }), [harbors, search, statusFilter]);

  async function handleToggle(harbor: Harbor) {
    try {
      if (harbor.enabled) await disableHarbor(harbor.id);
      else await enableHarbor(harbor.id);
      pushToast('success', t('Harbor "{{name}}" {{state}}.', { name: harbor.name, state: harbor.enabled ? t('disabled') : t('enabled') }));
      await load();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Update failed.'));
    }
  }

  function handleDelete(harbor: Harbor) {
    setConfirm({
      open: true,
      title: t('Delete Harbor'),
      message: t('Delete "{{name}}"? Its registration is removed; running work on it is not affected until it reconnects.', { name: harbor.name }),
      onConfirm: async () => {
        setConfirm((current) => ({ ...current, open: false }));
        try {
          await deleteHarbor(harbor.id);
          pushToast('warning', t('Harbor "{{name}}" deleted.', { name: harbor.name }));
          await load();
        } catch (err: unknown) {
          setError(err instanceof Error ? err.message : t('Delete failed.'));
        }
      },
    });
  }

  const emptyState = (
    <div className="playbook-empty-state">
      <strong>{t('No harbors match the current filters.')}</strong>
      <span>{canManage ? t('Install the Harbor app on a host and connect it, or pre-register one here.') : t('Ask a tenant administrator to connect a Harbor.')}</span>
    </div>
  );

  // The ID used to sit under the name, and the capability list wrapped; both are one line now.
  const columns: DataTableColumn<Harbor>[] = [
    {
      key: 'name', label: t('Harbor'), required: true, cellClassName: 'cell-nowrap',
      render: (harbor) => (
        <>
          <strong>{harbor.name}</strong>
          {!harbor.enabled && <span className="text-dim"> ({t('disabled')})</span>}
        </>
      ),
    },
    {
      key: 'id', label: t('ID'), required: true, cellClassName: 'mono text-dim table-id-cell',
      render: (harbor) => (
        <span className="id-display">
          <span className="id-value" title={harbor.id}>{harbor.id}</span>
          <CopyButton text={harbor.id} onClick={e => e.stopPropagation()} />
        </span>
      ),
    },
    { key: 'status', label: t('Status'), cellClassName: 'cell-nowrap', render: (harbor) => <StatusBadge status={harbor.connectionStatus} /> },
    {
      key: 'capabilities', label: t('Capabilities'), cellClassName: 'text-dim',
      render: (harbor) => {
        if (harbor.capabilities.length === 0) return '-';
        const text = harbor.capabilities.map((c) => c.name).join(', ');
        return <span className="cell-one-line" title={text}>{text}</span>;
      },
    },
    { key: 'capacity', label: t('Capacity'), cellClassName: 'text-dim', render: (harbor) => harbor.maxConcurrentJobs },
    {
      key: 'lastSeen', label: t('Last Seen'), cellClassName: 'text-dim cell-nowrap',
      cellTitle: (harbor) => (harbor.lastSeenUtc ? formatDateTime(harbor.lastSeenUtc) : ''),
      render: (harbor) => (harbor.lastSeenUtc ? formatRelativeTime(harbor.lastSeenUtc) : t('Never')),
    },
    {
      key: 'actions', label: t('Actions'), fixed: true, interactive: true, className: 'text-right',
      render: (harbor) => (
        <ActionMenu
          id={`harbor-${harbor.id}`}
          items={[
            { label: 'Details', onClick: () => setDetail({ open: true, harbor }) },
            ...(canManage ? [{ label: 'Edit', onClick: () => openEdit(harbor) }] : []),
            ...(canManage ? [{ label: harbor.enabled ? 'Disable' : 'Enable', onClick: () => handleToggle(harbor) }] : []),
            { label: 'View JSON', onClick: () => setJsonData({ open: true, title: harbor.name, data: harbor }) },
            ...(canManage ? [{ label: 'Delete', danger: true as const, onClick: () => handleDelete(harbor) }] : []),
          ]}
        />
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title={t('Harbors')}
        subtitle={t('Detached host runners that execute captains, git, and worktrees on the machines where your repositories and tool logins live.')}
        actions={(
          <>
            {canManage && (
              <button className="btn btn-primary" onClick={openCreate}>+ {t('Harbor')}</button>
            )}
          </>
        )}
      />

      <ErrorModal error={error} onClose={() => setError('')} />
      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      <ConfirmDialog
        open={confirm.open}
        title={confirm.title}
        message={confirm.message}
        onConfirm={confirm.onConfirm}
        onCancel={() => setConfirm((current) => ({ ...current, open: false }))}
      />

      {showForm && (
        <div className="modal-overlay" onClick={() => setShowForm(false)}>
          <form className="modal" onClick={(e) => e.stopPropagation()} onSubmit={handleSave}>
            <h3>{editing ? t('Edit Harbor') : t('Register Harbor')}</h3>
            <label>{t('Name')}
              <input type="text" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} required />
            </label>
            <label>{t('Max Concurrent Jobs')}
              <input type="number" min={1} value={form.maxConcurrentJobs} onChange={(e) => setForm({ ...form, maxConcurrentJobs: e.target.value })} />
            </label>
            <label className="checkbox-row">
              <input type="checkbox" checked={form.enabled} onChange={(e) => setForm({ ...form, enabled: e.target.checked })} />
              <span>{t('Enabled for routing')}</span>
            </label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary" disabled={saving}>{saving ? t('Saving...') : editing ? t('Save Changes') : t('Register Harbor')}</button>
              <button type="button" className="btn" onClick={() => setShowForm(false)} disabled={saving}>{t('Cancel')}</button>
            </div>
          </form>
        </div>
      )}

      {detail.open && detail.harbor && (
        <div className="modal-overlay" onClick={() => setDetail({ open: false, harbor: null })}>
          <div className="modal modal-large" onClick={(e) => e.stopPropagation()}>
            <h3>{detail.harbor.name}</h3>
            <dl className="detail-list">
              <dt>{t('Status')}</dt>
              <dd><StatusBadge status={detail.harbor.connectionStatus} /></dd>
              <dt>{t('Enabled')}</dt>
              <dd>{detail.harbor.enabled ? t('Yes') : t('No')}</dd>
              <dt>{t('Capacity')}</dt>
              <dd>{detail.harbor.maxConcurrentJobs}</dd>
              <dt>{t('Platform')}</dt>
              <dd>{[detail.harbor.osPlatform, detail.harbor.architecture].filter(Boolean).join(' / ') || '-'}</dd>
              <dt>{t('Protocol')}</dt>
              <dd>{detail.harbor.protocolVersion || '-'}</dd>
              <dt>{t('Last Seen')}</dt>
              <dd>{detail.harbor.lastSeenUtc ? formatDateTime(detail.harbor.lastSeenUtc) : t('Never')}</dd>
              <dt>{t('Capabilities')}</dt>
              <dd>
                {detail.harbor.capabilities.length === 0
                  ? '-'
                  : detail.harbor.capabilities.map((c) => (
                      <span key={c.name} className="tag" style={{ marginRight: '0.35rem' }}>{c.name}{c.available ? '' : t(' (unavailable)')}</span>
                    ))}
              </dd>
            </dl>
            <div className="modal-actions">
              <button type="button" className="btn" onClick={() => setDetail({ open: false, harbor: null })}>{t('Close')}</button>
            </div>
          </div>
        </div>
      )}

      <div className="playbook-overview-grid">
        <div className="card playbook-overview-card">
          <span>{t('Total Harbors')}</span>
          <strong>{harbors.length}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Connected')}</span>
          <strong>{harbors.filter((h) => h.connectionStatus === 'Connected').length}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Disconnected')}</span>
          <strong>{harbors.filter((h) => h.connectionStatus === 'Disconnected').length}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Enabled')}</span>
          <strong>{harbors.filter((h) => h.enabled).length}</strong>
        </div>
      </div>

      <div className="card" style={{ padding: '1rem', marginBottom: '1rem' }}>
        <div className="playbook-filter-row">
          <input type="text" value={search} onChange={(e) => setSearch(e.target.value)} placeholder={t('Search by name, platform, or ID...')} />
          <select aria-label={t('All statuses')} value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)}>
            <option value="all">{t('All statuses')}</option>
            <option value="Connected">{t('Connected')}</option>
            <option value="Degraded">{t('Degraded')}</option>
            <option value="Disconnected">{t('Disconnected')}</option>
            <option value="Unknown">{t('Unknown')}</option>
          </select>
        </div>
      </div>

      <DataTable
        tableKey="harbors"
        columns={columns}
        rows={filtered}
        rowKey={(harbor) => harbor.id}
        onRowClick={(harbor) => setDetail({ open: true, harbor })}
        autoRefresh={{ seconds: refreshSeconds, onChange: setRefreshSeconds }}
        onRefresh={load}
        refreshTitle="Refresh harbors"
        emptyMessage={emptyState}
        placeholder={harbors.length > 0 ? undefined : loading ? <p className="text-dim">{t('Loading...')}</p> : emptyState}
      />
    </div>
  );
}
