import { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { createRelease, deleteRelease, listReleases, listVessels, listWorkflowProfiles, updateRelease } from '../api/client';
import type { Release, ReleaseStatus, ReleaseUpsertRequest, Vessel, WorkflowProfile } from '../types/models';
import { useAuth } from '../context/AuthContext';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import ActionMenu from '../components/shared/ActionMenu';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import ErrorModal from '../components/shared/ErrorModal';
import JsonViewer from '../components/shared/JsonViewer';
import CopyButton from '../components/shared/CopyButton';
import DataTable, { type DataTableColumn } from '../components/shared/DataTable';
import PageHeader from '../components/shared/PageHeader';
import StatusBadge from '../components/shared/StatusBadge';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import { RELEASE_STATUSES, splitList } from '../lib/deliveryForms';

export default function Releases() {
  const navigate = useNavigate();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();

  const [releases, setReleases] = useState<Release[]>([]);
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [profiles, setProfiles] = useState<WorkflowProfile[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [search, setSearch] = useState('');
  const [statusFilter, setStatusFilter] = useState<'all' | ReleaseStatus>('all');
  const [vesselFilter, setVesselFilter] = useState('all');
  const [colFilters, setColFilters] = useState({ title: '' });
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({
    open: false,
    title: '',
    message: '',
    onConfirm: () => {},
  });

  const canManage = isAdmin || isTenantAdmin;

  const EMPTY_CREATE_FORM = {
    vesselId: '',
    workflowProfileId: '',
    title: 'Draft Release',
    version: '',
    tagName: '',
    summary: '',
    notes: '',
    status: 'Draft' as ReleaseStatus,
    voyageIds: '',
    missionIds: '',
    checkRunIds: '',
  };

  const [showCreate, setShowCreate] = useState(false);
  const [editing, setEditing] = useState<Release | null>(null);
  const [saving, setSaving] = useState(false);
  const [createForm, setCreateForm] = useState(EMPTY_CREATE_FORM);

  function openCreate() {
    setEditing(null);
    setCreateForm(EMPTY_CREATE_FORM);
    setShowCreate(true);
  }

  function openEdit(release: Release) {
    setEditing(release);
    setCreateForm({
      vesselId: release.vesselId || '',
      workflowProfileId: release.workflowProfileId || '',
      title: release.title,
      version: release.version || '',
      tagName: release.tagName || '',
      summary: release.summary || '',
      notes: release.notes || '',
      status: release.status,
      voyageIds: (release.voyageIds || []).join('\n'),
      missionIds: (release.missionIds || []).join('\n'),
      checkRunIds: (release.checkRunIds || []).join('\n'),
    });
    setShowCreate(true);
  }

  async function handleCreate(event: React.FormEvent) {
    event.preventDefault();
    if (saving) return;
    try {
      setSaving(true);
      const payload: ReleaseUpsertRequest = {
        vesselId: createForm.vesselId || null,
        workflowProfileId: createForm.workflowProfileId || null,
        title: createForm.title.trim() || null,
        version: createForm.version.trim() || null,
        tagName: createForm.tagName.trim() || null,
        summary: createForm.summary.trim() || null,
        notes: createForm.notes.trim() || null,
        status: createForm.status,
        voyageIds: splitList(createForm.voyageIds),
        missionIds: splitList(createForm.missionIds),
        checkRunIds: splitList(createForm.checkRunIds),
        objectiveIds: [],
      };
      if (editing) {
        const updated = await updateRelease(editing.id, payload);
        setShowCreate(false);
        pushToast('success', t('Release "{{title}}" saved.', { title: updated.title }));
      } else {
        const created = await createRelease(payload);
        setShowCreate(false);
        pushToast('success', t('Release "{{title}}" created.', { title: created.title }));
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
      const [releaseResult, vesselResult, profileResult] = await Promise.all([
        listReleases({ pageSize: 9999 }),
        listVessels({ pageSize: 9999 }),
        listWorkflowProfiles({ pageSize: 9999 }),
      ]);
      setReleases(releaseResult.objects || []);
      setVessels(vesselResult.objects || []);
      setProfiles(profileResult.objects || []);
      setError('');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Failed to load releases.'));
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    load();
  }, []);

  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('releases', load);

  const vesselMap = useMemo(() => new Map(vessels.map((vessel) => [vessel.id, vessel.name])), [vessels]);
  const profileMap = useMemo(() => new Map(profiles.map((profile) => [profile.id, profile.name])), [profiles]);

  const filtered = useMemo(() => releases.filter((release) => {
    const normalizedSearch = search.trim().toLowerCase();
    const matchesSearch = normalizedSearch.length === 0
      || release.title.toLowerCase().includes(normalizedSearch)
      || (release.version || '').toLowerCase().includes(normalizedSearch)
      || (release.tagName || '').toLowerCase().includes(normalizedSearch)
      || (release.summary || '').toLowerCase().includes(normalizedSearch)
      || release.id.toLowerCase().includes(normalizedSearch);

    const matchesStatus = statusFilter === 'all' || release.status === statusFilter;
    const matchesVessel = vesselFilter === 'all' || release.vesselId === vesselFilter;
    const matchesColFilters = (!colFilters.title || release.title.toLowerCase().includes(colFilters.title.toLowerCase()));
    return matchesSearch && matchesStatus && matchesVessel && matchesColFilters;
  }), [colFilters, releases, search, statusFilter, vesselFilter]);

  const shippedCount = releases.filter((release) => release.status === 'Shipped').length;
  const candidateCount = releases.filter((release) => release.status === 'Candidate').length;
  const failedCount = releases.filter((release) => release.status === 'Failed' || release.status === 'RolledBack').length;

  function handleDelete(release: Release) {
    setConfirm({
      open: true,
      title: t('Delete Release'),
      message: t('Delete "{{title}}"? This removes the release record but does not delete linked work or artifacts on disk.', { title: release.title }),
      onConfirm: async () => {
        setConfirm((current) => ({ ...current, open: false }));
        try {
          await deleteRelease(release.id);
          pushToast('warning', t('Release "{{title}}" deleted.', { title: release.title }));
          await load();
        } catch (err: unknown) {
          setError(err instanceof Error ? err.message : t('Delete failed.'));
        }
      },
    });
  }

  function linkedWork(release: Release): string {
    return `${release.voyageIds.length} ${t('voyages')}, ${release.missionIds.length} ${t('missions')}, ${release.checkRunIds.length} ${t('checks')}, ${release.artifacts.length} ${t('artifacts')}`;
  }

  const columns: DataTableColumn<Release>[] = [
    {
      key: 'title', label: t('Release'), required: true,
      filter: <input type="text" className="col-filter" aria-label={t('Filter by title')} value={colFilters.title} onChange={e => setColFilters(f => ({ ...f, title: e.target.value }))} placeholder={t('Filter...')} />,
      // One line: version, tag and summary have their own columns; the summary is also in the tooltip.
      cellTitle: (release) => [release.title, release.summary].filter(Boolean).join('\n'),
      render: (release) => <strong className="cell-one-line">{release.title}</strong>,
    },
    {
      key: 'id', label: t('ID'), required: true, cellClassName: 'mono text-dim table-id-cell',
      render: (release) => (
        <span className="id-display">
          <span className="id-value" title={release.id}>{release.id}</span>
          <CopyButton text={release.id} onClick={e => e.stopPropagation()} />
        </span>
      ),
    },
    {
      key: 'version', label: t('Version'), cellClassName: 'mono text-dim',
      cellTitle: (release) => release.tagName || undefined,
      render: (release) => <span className="cell-one-line">{release.version || t('Unversioned')}</span>,
    },
    {
      key: 'tag', label: t('Tag'), defaultHidden: true, cellClassName: 'mono text-dim',
      render: (release) => <span className="cell-one-line" title={release.tagName || undefined}>{release.tagName || '-'}</span>,
    },
    { key: 'status', label: t('Status'), cellClassName: 'cell-nowrap', render: (release) => <StatusBadge status={release.status} /> },
    { key: 'vessel', label: t('Vessel'), cellClassName: 'text-dim', render: (release) => (release.vesselId ? (vesselMap.get(release.vesselId) || release.vesselId) : '-') },
    { key: 'workflow', label: t('Workflow'), cellClassName: 'text-dim', render: (release) => (release.workflowProfileId ? (profileMap.get(release.workflowProfileId) || release.workflowProfileId) : t('Resolved default')) },
    {
      key: 'linkedWork', label: t('Linked Work'), cellClassName: 'text-dim',
      render: (release) => <span className="cell-one-line" title={linkedWork(release)}>{linkedWork(release)}</span>,
    },
    {
      key: 'summary', label: t('Summary'), defaultHidden: true, cellClassName: 'text-dim',
      render: (release) => <span className="cell-one-line" title={release.summary || undefined}>{release.summary || '-'}</span>,
    },
    {
      key: 'published', label: t('Published'), cellClassName: 'text-dim cell-nowrap',
      cellTitle: (release) => (release.publishedUtc ? formatDateTime(release.publishedUtc) : ''),
      render: (release) => (release.publishedUtc ? formatRelativeTime(release.publishedUtc) : '-'),
    },
    {
      key: 'lastUpdated', label: t('Last Updated'), cellClassName: 'text-dim cell-nowrap',
      cellTitle: (release) => formatDateTime(release.lastUpdateUtc),
      render: (release) => formatRelativeTime(release.lastUpdateUtc),
    },
    {
      key: 'actions', label: t('Actions'), fixed: true, interactive: true, className: 'text-right',
      render: (release) => (
        <ActionMenu
          id={`release-${release.id}`}
          items={[
            { label: 'Open', onClick: () => navigate(`/releases/${release.id}`) },
            ...(canManage ? [{ label: 'Edit', onClick: () => openEdit(release) }] : []),
            { label: 'View JSON', onClick: () => setJsonData({ open: true, title: release.title, data: release }) },
            ...(canManage ? [{ label: 'Delete', danger: true as const, onClick: () => handleDelete(release) }] : []),
          ]}
        />
      ),
    },
  ];

  return (
    <div>
      <PageHeader
        title={t('Releases')}
        subtitle={t('First-class release records that bundle versions, notes, linked voyages and missions, structured checks, and derived artifacts.')}
        actions={(
          <>
            {canManage && (
              <button className="btn btn-primary" onClick={openCreate}>
                + {t('Release')}
              </button>
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

      {showCreate && (
        <div className="modal-overlay" onClick={() => setShowCreate(false)}>
          <form className="modal modal-large" onClick={(event) => event.stopPropagation()} onSubmit={handleCreate}>
            <h3>{editing ? t('Edit Release') : t('Create Release')}</h3>
            <label>{t('Title')}
              <input value={createForm.title} onChange={(event) => setCreateForm((current) => ({ ...current, title: event.target.value }))} />
            </label>
            <label>{t('Status')}
              <select value={createForm.status} onChange={(event) => setCreateForm((current) => ({ ...current, status: event.target.value as ReleaseStatus }))}>
                {RELEASE_STATUSES.map((value) => (
                  <option key={value} value={value}>{value}</option>
                ))}
              </select>
            </label>
            <label>{t('Vessel')}
              <select value={createForm.vesselId} onChange={(event) => setCreateForm((current) => ({ ...current, vesselId: event.target.value }))}>
                <option value="">{t('Resolve from linked work or select a vessel...')}</option>
                {vessels.map((vessel) => (
                  <option key={vessel.id} value={vessel.id}>{vessel.name}</option>
                ))}
              </select>
            </label>
            <label>{t('Workflow Profile')}
              <select value={createForm.workflowProfileId} onChange={(event) => setCreateForm((current) => ({ ...current, workflowProfileId: event.target.value }))}>
                <option value="">{t('Resolved default')}</option>
                {profiles.map((profile) => (
                  <option key={profile.id} value={profile.id}>{profile.name}</option>
                ))}
              </select>
            </label>
            <label>{t('Version')}
              <input value={createForm.version} onChange={(event) => setCreateForm((current) => ({ ...current, version: event.target.value }))} placeholder="1.2.3" />
            </label>
            <label>{t('Tag Name')}
              <input value={createForm.tagName} onChange={(event) => setCreateForm((current) => ({ ...current, tagName: event.target.value }))} placeholder="v1.2.3" />
            </label>
            <label>{t('Summary')}
              <textarea rows={3} value={createForm.summary} onChange={(event) => setCreateForm((current) => ({ ...current, summary: event.target.value }))} />
            </label>
            <label>{t('Notes')}
              <textarea rows={6} value={createForm.notes} onChange={(event) => setCreateForm((current) => ({ ...current, notes: event.target.value }))} />
            </label>
            <label>{t('Voyage IDs')}
              <textarea rows={3} value={createForm.voyageIds} onChange={(event) => setCreateForm((current) => ({ ...current, voyageIds: event.target.value }))} placeholder="voy_..." />
            </label>
            <label>{t('Mission IDs')}
              <textarea rows={3} value={createForm.missionIds} onChange={(event) => setCreateForm((current) => ({ ...current, missionIds: event.target.value }))} placeholder="mis_..." />
            </label>
            <label>{t('Check Run IDs')}
              <textarea rows={3} value={createForm.checkRunIds} onChange={(event) => setCreateForm((current) => ({ ...current, checkRunIds: event.target.value }))} placeholder="chk_..." />
            </label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary" disabled={saving}>{saving ? t('Saving...') : editing ? t('Save Changes') : t('Create Release')}</button>
              <button type="button" className="btn" onClick={() => setShowCreate(false)} disabled={saving}>{t('Cancel')}</button>
            </div>
          </form>
        </div>
      )}

      <div className="playbook-overview-grid">
        <div className="card playbook-overview-card">
          <span>{t('Total Releases')}</span>
          <strong>{releases.length}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Shipped')}</span>
          <strong>{shippedCount}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Candidates')}</span>
          <strong>{candidateCount}</strong>
        </div>
        <div className="card playbook-overview-card">
          <span>{t('Failed / Rolled Back')}</span>
          <strong>{failedCount}</strong>
        </div>
      </div>

      <div className="card" style={{ padding: '1rem', marginBottom: '1rem' }}>
        <div className="playbook-filter-row">
          <input
            type="text"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder={t('Search by title, version, tag, summary, or ID...')}
          />
          <select aria-label={t('All statuses')} value={statusFilter} onChange={(event) => setStatusFilter(event.target.value as typeof statusFilter)}>
            <option value="all">{t('All statuses')}</option>
            {RELEASE_STATUSES.map((status) => (
              <option key={status} value={status}>{status}</option>
            ))}
          </select>
          <select aria-label={t('All vessels')} value={vesselFilter} onChange={(event) => setVesselFilter(event.target.value)}>
            <option value="all">{t('All vessels')}</option>
            {vessels.map((vessel) => (
              <option key={vessel.id} value={vessel.id}>{vessel.name}</option>
            ))}
          </select>
        </div>
      </div>

      <DataTable
        tableKey="releases"
        columns={columns}
        rows={filtered}
        rowKey={(release) => release.id}
        onRowClick={(release) => (canManage ? openEdit(release) : navigate(`/releases/${release.id}`))}
        autoRefresh={{ seconds: refreshSeconds, onChange: setRefreshSeconds }}
        onRefresh={load}
        refreshTitle={t('Refresh releases')}
        placeholder={loading && releases.length === 0 ? <p className="text-dim">{t('Loading...')}</p> : filtered.length === 0 ? (
          <div className="playbook-empty-state">
            <strong>{t('No releases match the current filters.')}</strong>
            <span>{canManage ? t('Create a draft release from voyages, missions, or checks to begin tracking what is shipping.') : t('Ask a tenant administrator to create and manage release records.')}</span>
          </div>
        ) : undefined}
      />
    </div>
  );
}
