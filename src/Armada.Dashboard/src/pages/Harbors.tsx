import { duplicateDisplayName } from '../lib/duplicates';
import { useEffect, useMemo, useState } from 'react';
import {
  enumerateHarbors,
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
import HarborMetricsPanel from '../components/harbors/HarborMetricsPanel';
import { useDeleteProgress } from '../lib/useDeleteProgress';
import TruncationNote from '../components/shared/TruncationNote';

interface HarborForm {
  name: string;
  maxConcurrentJobs: string;
  enabled: boolean;
}

const STATUS_TIPS: Record<string, string> = {
  Connected: 'Connected: the Harbor has an open link and is sending heartbeats, so it can take work.',
  Degraded: 'Degraded: the link is open but heartbeats are late, or the Harbor is finishing work before it stops.',
  Disconnected: 'Disconnected: the Harbor has no open link. Work routed to it waits until it reconnects.',
  Unknown: 'Unknown: registered here but has never connected, so its state is not known yet.',
};
const NAME_TIP = 'Name for this Harbor, usually the machine it runs on, for example joel-macbook. Shown wherever work is placed on it. Required.';
const CAPACITY_TIP = 'Most jobs (captain runs and other host work) this Harbor runs at once, 1 or more. Default 4. The Harbor app reports its own value when it connects, which replaces this one.';
const ENABLED_TIP = 'On: new missions can be sent to this Harbor. Off: it keeps its existing docks but gets no new missions.';

const EMPTY_FORM: HarborForm = { name: 'New Harbor', maxConcurrentJobs: '4', enabled: true };

export default function Harbors() {
  const { isAdmin, isTenantAdmin } = useAuth();
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();
  const deletion = useDeleteProgress();
  const [harbors, setHarbors] = useState<Harbor[]>([]);
  const [harborTotal, setHarborTotal] = useState(0);
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
  const [chartHarborId, setChartHarborId] = useState('');

  const canManage = isAdmin || isTenantAdmin;
  // The page's chart section follows the chosen Harbor, or the first one listed.
  const chartHarbor = harbors.find((h) => h.id === chartHarborId) ?? harbors[0] ?? null;

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

  function openDuplicate(harbor: Harbor) {
    openEdit(harbor);
    setEditing(null);
    setForm(current => ({ ...current, name: duplicateDisplayName(harbor.name) }));
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
      // One bounded page of the largest size; the note below says when more Harbors exist than this view holds.
      const result = await enumerateHarbors();
      setHarbors(result?.objects ?? []);
      setHarborTotal(result?.totalRecords ?? (result?.objects?.length ?? 0));
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
        if (!(await deletion.run({ kind: 'harbor', action: () => deleteHarbor(harbor.id), errorMessage: (err) => (err instanceof Error ? err.message : t('Delete failed.')) }))) return;
        pushToast('warning', t('Harbor "{{name}}" deleted.', { name: harbor.name }));
        await load();
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
      key: 'id', label: t('ID'), required: true, headerTitle: t('Unique Harbor ID (hbr_ prefix). Use the copy button to copy it.'), cellClassName: 'mono text-dim table-id-cell',
      render: (harbor) => (
        <span className="id-display">
          <span className="id-value" title={harbor.id}>{harbor.id}</span>
          <CopyButton text={harbor.id} onClick={e => e.stopPropagation()} />
        </span>
      ),
    },
    {
      key: 'name', label: t('Harbor'), required: true, headerTitle: t('Harbor name. A Harbor is the app on a developer machine that runs captains, git and worktrees there. (disabled) means it gets no new missions.'), cellClassName: 'cell-nowrap',
      render: (harbor) => (
        <>
          <strong>{harbor.name}</strong>
          {!harbor.enabled && <span className="text-dim"> ({t('disabled')})</span>}
        </>
      ),
    },
    { key: 'status', label: t('Status'), headerTitle: t('Connection as the Admiral sees it: Connected, Degraded (heartbeats late), Disconnected (no link) or Unknown (never connected).'), cellClassName: 'cell-nowrap', render: (harbor) => <span className={`tag ${(harbor.connectionStatus || '').toLowerCase()}`} title={t(STATUS_TIPS[harbor.connectionStatus] ?? harbor.connectionStatus)}>{t(harbor.connectionStatus)}</span> },
    {
      key: 'capabilities', label: t('Capabilities'), headerTitle: t('Agent runtimes and tools this Harbor reported when it connected, such as git or claude. Work needing a runtime goes only to Harbors that have it.'), cellClassName: 'text-dim',
      render: (harbor) => {
        if (harbor.capabilities.length === 0) return '-';
        const text = harbor.capabilities.map((c) => c.name).join(', ');
        return <span className="cell-one-line" title={text}>{text}</span>;
      },
    },
    { key: 'capacity', label: t('Capacity'), headerTitle: t('Most jobs this Harbor runs at the same time.'), cellClassName: 'text-dim', render: (harbor) => harbor.maxConcurrentJobs },
    {
      key: 'lastSeen', label: t('Last Seen'), headerTitle: t('When the Harbor last sent a heartbeat or message. Never means it has not connected yet.'), cellClassName: 'text-dim cell-nowrap',
      cellTitle: (harbor) => (harbor.lastSeenUtc ? formatDateTime(harbor.lastSeenUtc) : ''),
      render: (harbor) => (harbor.lastSeenUtc ? formatRelativeTime(harbor.lastSeenUtc) : t('Never')),
    },
    {
      key: 'actions', label: t('Actions'), headerTitle: t('Open the menu in a row to view details, edit, enable or disable, or delete that Harbor.'), fixed: true, interactive: true, className: 'text-right',
      render: (harbor) => (
        <ActionMenu
          id={`harbor-${harbor.id}`}
          items={[
            { label: 'Details', title: 'Show this Harbor\'s platform, capabilities and activity charts.', onClick: () => setDetail({ open: true, harbor }) },
            ...(canManage ? [{ label: 'Duplicate', title: 'Edit a new copy before saving.', onClick: () => openDuplicate(harbor) }] : []),
              ...(canManage ? [{ label: 'Edit', title: 'Change this Harbor\'s name, capacity and whether it gets new missions.', onClick: () => openEdit(harbor) }] : []),
            ...(canManage ? [{ label: harbor.enabled ? 'Disable' : 'Enable', title: harbor.enabled ? 'Stop sending new missions to this Harbor. Running work and existing docks are kept.' : 'Let this Harbor receive new missions again.', onClick: () => handleToggle(harbor) }] : []),
            { label: 'View JSON', title: 'Show this Harbor\'s full record as JSON, as the API returns it.', onClick: () => setJsonData({ open: true, title: harbor.name, data: harbor }) },
            ...(canManage ? [{ label: 'Delete', danger: true as const, title: 'Remove this Harbor\'s registration. If the Harbor app connects again it registers itself anew. You confirm first.', onClick: () => handleDelete(harbor) }] : []),
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
              <button className="btn btn-primary" onClick={openCreate} title={t('Pre-register a Harbor to reserve its name and capacity before it connects. Harbors also register themselves the first time they connect.')}>+ {t('Harbor')}</button>
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
      {deletion.dialog}

      {showForm && (
        <div className="modal-overlay" onClick={() => setShowForm(false)}>
          <form className="modal" onClick={(e) => e.stopPropagation()} onSubmit={handleSave}>
            <h3>{editing ? t('Edit Harbor') : t('Register Harbor')}</h3>
            <label title={t(NAME_TIP)}>{t('Name')}
              <input type="text" title={t(NAME_TIP)} value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} required />
            </label>
            <label title={t(CAPACITY_TIP)}>{t('Max Concurrent Jobs')}
              <input type="number" min={1} title={t(CAPACITY_TIP)} value={form.maxConcurrentJobs} onChange={(e) => setForm({ ...form, maxConcurrentJobs: e.target.value })} />
            </label>
            <label className="checkbox-row" title={t(ENABLED_TIP)}>
              <input type="checkbox" title={t(ENABLED_TIP)} checked={form.enabled} onChange={(e) => setForm({ ...form, enabled: e.target.checked })} />
              <span>{t('Enabled for routing')}</span>
            </label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary" disabled={saving} title={editing ? t('Save changes to this Harbor.') : t('Register this Harbor. It shows as Unknown until the Harbor app connects with the same ID.')}>{saving ? t('Saving...') : editing ? t('Save Changes') : t('Register Harbor')}</button>
              <button type="button" className="btn" onClick={() => setShowForm(false)} disabled={saving}>{t('Cancel')}</button>
            </div>
          </form>
        </div>
      )}

      {detail.open && detail.harbor && (
        <div className="modal-overlay" onClick={() => setDetail({ open: false, harbor: null })}>
          <div className="modal modal-large harbor-detail-modal" onClick={(e) => e.stopPropagation()}>
            <h3>{detail.harbor.name}</h3>
            <dl className="detail-list">
              <dt title={t('Connection as the Admiral sees it.')}>{t('Status')}</dt>
              <dd><span className={`tag ${(detail.harbor.connectionStatus || '').toLowerCase()}`} title={t(STATUS_TIPS[detail.harbor.connectionStatus] ?? detail.harbor.connectionStatus)}>{t(detail.harbor.connectionStatus)}</span></dd>
              <dt title={t(ENABLED_TIP)}>{t('Enabled')}</dt>
              <dd>{detail.harbor.enabled ? t('Yes') : t('No')}</dd>
              <dt title={t('Most jobs this Harbor runs at the same time.')}>{t('Capacity')}</dt>
              <dd>{detail.harbor.maxConcurrentJobs}</dd>
              <dt title={t('Operating system and processor the Harbor reported, for example macOS / Arm64.')}>{t('Platform')}</dt>
              <dd>{[detail.harbor.osPlatform, detail.harbor.architecture].filter(Boolean).join(' / ') || '-'}</dd>
              <dt title={t('Version of the Harbor link protocol the Harbor app speaks. A dash means it has never connected.')}>{t('Protocol')}</dt>
              <dd>{detail.harbor.protocolVersion || '-'}</dd>
              <dt title={t('When the Harbor last sent a heartbeat or message.')}>{t('Last Seen')}</dt>
              <dd>{detail.harbor.lastSeenUtc ? formatDateTime(detail.harbor.lastSeenUtc) : t('Never')}</dd>
              <dt title={t('Runtimes and tools the Harbor reported. Unavailable ones were found but cannot be used right now.')}>{t('Capabilities')}</dt>
              <dd>
                {detail.harbor.capabilities.length === 0
                  ? '-'
                  : detail.harbor.capabilities.map((c) => (
                      <span key={c.name} className="tag" style={{ marginRight: '0.35rem' }} title={c.available ? t('{{name}} is available on this Harbor.', { name: c.name }) : t('{{name}} was reported but is not usable right now, so work needing it is not sent here.', { name: c.name })}>{c.name}{c.available ? '' : t(' (unavailable)')}</span>
                    ))}
              </dd>
            </dl>
            <HarborMetricsPanel harborId={detail.harbor.id} />
            <div className="modal-actions">
              {canManage && (
                <button type="button" className="btn btn-primary" onClick={() => { const h = detail.harbor as Harbor; setDetail({ open: false, harbor: null }); openEdit(h); }} title={t('Change this Harbor\'s name, capacity and whether it gets new missions.')}>{t('Edit')}</button>
              )}
              <button type="button" className="btn" onClick={() => setDetail({ open: false, harbor: null })}>{t('Close')}</button>
            </div>
          </div>
        </div>
      )}

      <div className="playbook-overview-grid">
        <div className="card playbook-overview-card" title={t('All registered Harbors, connected or not.')}>
          <span>{t('Total Harbors')}</span>
          <strong>{Math.max(harborTotal, harbors.length)}</strong>
        </div>
        <div className="card playbook-overview-card" title={t('Harbors with an open link and recent heartbeats.')}>
          <span>{t('Connected')}</span>
          <strong>{harbors.filter((h) => h.connectionStatus === 'Connected').length}</strong>
        </div>
        <div className="card playbook-overview-card" title={t('Harbors with no open link right now.')}>
          <span>{t('Disconnected')}</span>
          <strong>{harbors.filter((h) => h.connectionStatus === 'Disconnected').length}</strong>
        </div>
        <div className="card playbook-overview-card" title={t('Harbors that can receive new missions.')}>
          <span>{t('Enabled')}</span>
          <strong>{harbors.filter((h) => h.enabled).length}</strong>
        </div>
      </div>

      <div className="card" style={{ padding: '1rem', marginBottom: '1rem' }}>
        <div className="playbook-filter-row">
          <input type="text" value={search} onChange={(e) => setSearch(e.target.value)} title={t('Show only Harbors whose name, platform or ID contains this text.')} placeholder={t('Search by name, platform, or ID...')} />
          <select aria-label={t('All statuses')} title={t('Show Harbors with any connection status, or only one.')} value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)}>
            <option value="all">{t('All statuses')}</option>
            <option value="Connected">{t('Connected')}</option>
            <option value="Degraded">{t('Degraded')}</option>
            <option value="Disconnected">{t('Disconnected')}</option>
            <option value="Unknown">{t('Unknown')}</option>
          </select>
        </div>
      </div>

      <TruncationNote shown={harbors.length} total={harborTotal} />
      <DataTable
        tableKey="harbors"
        columns={columns}
        rows={filtered}
        rowKey={(harbor) => harbor.id}
        onRowClick={(harbor) => setDetail({ open: true, harbor })}
        autoRefresh={{ seconds: refreshSeconds, onChange: setRefreshSeconds }}
        onRefresh={load}
        refreshTitle={t('Refresh harbors')}
        emptyMessage={emptyState}
        placeholder={harbors.length > 0 ? undefined : loading ? <p className="text-dim">{t('Loading...')}</p> : emptyState}
      />

      {chartHarbor && (
        <div className="harbor-metrics-page-section">
          <div className="harbor-metrics-picker">
            <label title={t('Choose which Harbor\'s activity charts to show below.')}>{t('Charts for')}
              <select aria-label={t('Harbor to chart')} title={t('Choose which Harbor\'s activity charts to show below.')} value={chartHarbor.id} onChange={(e) => setChartHarborId(e.target.value)}>
                {harbors.map((h) => <option key={h.id} value={h.id}>{h.name}</option>)}
              </select>
            </label>
          </div>
          {!detail.open && <HarborMetricsPanel key={chartHarbor.id} harborId={chartHarbor.id} />}
        </div>
      )}
    </div>
  );
}
