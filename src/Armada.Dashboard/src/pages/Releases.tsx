import { duplicateDisplayName } from '../lib/duplicates';
import { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { createRelease, deleteRelease, listReleases, listVessels, listWorkflowProfiles, updateRelease } from '../api/client';
import { listAll } from '../api/paging';
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
import { useDeleteProgress } from '../lib/useDeleteProgress';

export default function Releases() {
  const navigate = useNavigate();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const deletion = useDeleteProgress();
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

  const tips = {
    title: t('Release name shown in lists and on deployments. Blank uses the vessel name and version, such as api 1.4.0.'),
    status: t('Lifecycle stage: Draft, Candidate, Shipped, Failed, or RolledBack. Setting Shipped records the published time once.'),
    vessel: t('Repository the release belongs to. Blank infers it from the linked missions or check runs; all linked work must share one vessel.'),
    workflowProfile: t('Profile used for this release and the default for its deployments. Blank uses the vessel\'s profile, then its fleet\'s, then the global default.'),
    version: t('Release version, such as 1.4.0. Blank uses a version found in a linked release-versioning check, else the vessel\'s highest release version with the patch bumped.'),
    tagName: t('Git tag for the release, such as v1.4.0. Blank uses v plus the version. Deployments of this release default to this ref.'),
    summary: t('One-paragraph description of what ships. Blank generates one from the linked voyages, missions, and checks.'),
    notes: t('Release notes as plain text. Blank generates notes from the linked work and artifacts; edit them before marking the release Shipped.'),
    voyageIds: t('Voyages included in this release, one ID per line or comma-separated, such as vyg_abc123. Their missions are linked automatically.'),
    missionIds: t('Missions included in this release, one ID per line or comma-separated, such as msn_abc123. Each must belong to the release\'s vessel.'),
    checkRunIds: t('Check runs that prove readiness, one ID per line or comma-separated, such as chk_abc123. Their artifacts become the release artifacts.'),
  };

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

  function openDuplicate(release: Release) {
    openEdit(release);
    setEditing(null);
    setCreateForm(current => ({ ...current, title: duplicateDisplayName(release.title), version: '', tagName: '', status: 'Draft' }));
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
        listAll(listReleases),
        listAll(listVessels),
        listAll(listWorkflowProfiles),
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
        if (!(await deletion.run({ kind: 'release', action: () => deleteRelease(release.id), errorMessage: (err) => err instanceof Error ? err.message : t('Delete failed.') }))) return;
        pushToast('warning', t('Release "{{title}}" deleted.', { title: release.title }));
        await load();
      },
    });
  }

  function linkedWork(release: Release): string {
    return `${release.voyageIds.length} ${t('voyages')}, ${release.missionIds.length} ${t('missions')}, ${release.checkRunIds.length} ${t('checks')}, ${release.artifacts.length} ${t('artifacts')}`;
  }

  const columns: DataTableColumn<Release>[] = [
    {
      key: 'id', label: t('ID'), required: true, headerTitle: t('Release ID (rel_ prefix), used by the REST API, MCP tools, and links.'), cellClassName: 'mono text-dim table-id-cell',
      render: (release) => (
        <span className="id-display">
          <span className="id-value" title={release.id}>{release.id}</span>
          <CopyButton text={release.id} onClick={e => e.stopPropagation()} />
        </span>
      ),
    },
    {
      key: 'title', label: t('Release'), required: true, headerTitle: t('Release title. Hover a row for its summary.'),
      filter: <input type="text" className="col-filter" aria-label={t('Filter by title')} title={t('Show only releases whose title contains this text.')} value={colFilters.title} onChange={e => setColFilters(f => ({ ...f, title: e.target.value }))} placeholder={t('Filter...')} />,
      // One line: version, tag and summary have their own columns; the summary is also in the tooltip.
      cellTitle: (release) => [release.title, release.summary].filter(Boolean).join('\n'),
      render: (release) => <strong className="cell-one-line">{release.title}</strong>,
    },
    {
      key: 'version', label: t('Version'), headerTitle: t('Release version. Hover a cell for its git tag.'), cellClassName: 'mono text-dim',
      cellTitle: (release) => release.tagName || undefined,
      render: (release) => <span className="cell-one-line">{release.version || t('Unversioned')}</span>,
    },
    {
      key: 'tag', label: t('Tag'), headerTitle: t('Git tag for the release, such as v1.4.0.'), defaultHidden: true, cellClassName: 'mono text-dim',
      render: (release) => <span className="cell-one-line" title={release.tagName || undefined}>{release.tagName || '-'}</span>,
    },
    { key: 'status', label: t('Status'), headerTitle: t('Lifecycle stage: Draft, Candidate, Shipped, Failed, or RolledBack.'), cellClassName: 'cell-nowrap', render: (release) => <StatusBadge status={release.status} /> },
    { key: 'vessel', label: t('Vessel'), headerTitle: t('Repository the release belongs to.'), cellClassName: 'text-dim', render: (release) => (release.vesselId ? (vesselMap.get(release.vesselId) || release.vesselId) : '-') },
    { key: 'workflow', label: t('Workflow'), headerTitle: t('Workflow profile set on the release. Resolved default means the vessel, fleet, or global default profile.'), cellClassName: 'text-dim', render: (release) => (release.workflowProfileId ? (profileMap.get(release.workflowProfileId) || release.workflowProfileId) : t('Resolved default')) },
    {
      key: 'linkedWork', label: t('Linked Work'), headerTitle: t('Counts of voyages, missions, check runs, and artifacts linked to the release.'), cellClassName: 'text-dim',
      render: (release) => <span className="cell-one-line" title={linkedWork(release)}>{linkedWork(release)}</span>,
    },
    {
      key: 'summary', label: t('Summary'), headerTitle: t('Short description of what ships.'), defaultHidden: true, cellClassName: 'text-dim',
      render: (release) => <span className="cell-one-line" title={release.summary || undefined}>{release.summary || '-'}</span>,
    },
    {
      key: 'published', label: t('Published'), headerTitle: t('When the release was first marked Shipped. Hover a cell for the exact time.'), cellClassName: 'text-dim cell-nowrap',
      cellTitle: (release) => (release.publishedUtc ? formatDateTime(release.publishedUtc) : ''),
      render: (release) => (release.publishedUtc ? formatRelativeTime(release.publishedUtc) : '-'),
    },
    {
      key: 'lastUpdated', label: t('Last Updated'), headerTitle: t('When the release record last changed. Hover a cell for the exact time.'), cellClassName: 'text-dim cell-nowrap',
      cellTitle: (release) => formatDateTime(release.lastUpdateUtc),
      render: (release) => formatRelativeTime(release.lastUpdateUtc),
    },
    {
      key: 'actions', label: t('Actions'), fixed: true, interactive: true, className: 'text-right',
      render: (release) => (
        <ActionMenu
          id={`release-${release.id}`}
          items={[
            { label: 'Open', title: 'Open the release detail page with its notes, linked work, artifacts, and deployments.', onClick: () => navigate(`/releases/${release.id}`) },
            ...(canManage ? [{ label: 'Duplicate', title: 'Edit a new copy before saving.', onClick: () => openDuplicate(release) }] : []),
              ...(canManage ? [{ label: 'Edit', title: 'Change the release\'s version, status, notes, and linked work.', onClick: () => openEdit(release) }] : []),
            { label: 'View JSON', title: 'Show the raw release record as returned by the API.', onClick: () => setJsonData({ open: true, title: release.title, data: release }) },
            ...(canManage ? [{ label: 'Delete', title: 'Delete this release record. Linked work, checks, and artifacts on disk are kept. Cannot be undone.', danger: true as const, onClick: () => handleDelete(release) }] : []),
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
              <button className="btn btn-primary" onClick={openCreate} title={t('Draft a release from voyages, missions, and check runs. Blank fields are generated from the linked work.')}>
                + {t('Release')}
              </button>
            )}
          </>
        )}
      />

      <ErrorModal error={error} onClose={() => setError('')} />
      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      {deletion.dialog}
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
            <label title={tips.title}>{t('Title')}
              <input title={tips.title} value={createForm.title} onChange={(event) => setCreateForm((current) => ({ ...current, title: event.target.value }))} />
            </label>
            <label title={tips.status}>{t('Status')}
              <select title={tips.status} value={createForm.status} onChange={(event) => setCreateForm((current) => ({ ...current, status: event.target.value as ReleaseStatus }))}>
                {RELEASE_STATUSES.map((value) => (
                  <option key={value} value={value}>{value}</option>
                ))}
              </select>
            </label>
            <label title={tips.vessel}>{t('Vessel')}
              <select title={tips.vessel} value={createForm.vesselId} onChange={(event) => setCreateForm((current) => ({ ...current, vesselId: event.target.value }))}>
                <option value="">{t('Resolve from linked work or select a vessel...')}</option>
                {vessels.map((vessel) => (
                  <option key={vessel.id} value={vessel.id}>{vessel.name}</option>
                ))}
              </select>
            </label>
            <label title={tips.workflowProfile}>{t('Workflow Profile')}
              <select title={tips.workflowProfile} value={createForm.workflowProfileId} onChange={(event) => setCreateForm((current) => ({ ...current, workflowProfileId: event.target.value }))}>
                <option value="">{t('Resolved default')}</option>
                {profiles.map((profile) => (
                  <option key={profile.id} value={profile.id}>{profile.name}</option>
                ))}
              </select>
            </label>
            <label title={tips.version}>{t('Version')}
              <input title={tips.version} value={createForm.version} onChange={(event) => setCreateForm((current) => ({ ...current, version: event.target.value }))} placeholder="1.2.3" />
            </label>
            <label title={tips.tagName}>{t('Tag Name')}
              <input title={tips.tagName} value={createForm.tagName} onChange={(event) => setCreateForm((current) => ({ ...current, tagName: event.target.value }))} placeholder="v1.2.3" />
            </label>
            <label title={tips.summary}>{t('Summary')}
              <textarea title={tips.summary} rows={3} value={createForm.summary} onChange={(event) => setCreateForm((current) => ({ ...current, summary: event.target.value }))} />
            </label>
            <label title={tips.notes}>{t('Notes')}
              <textarea title={tips.notes} rows={6} value={createForm.notes} onChange={(event) => setCreateForm((current) => ({ ...current, notes: event.target.value }))} />
            </label>
            <label title={tips.voyageIds}>{t('Voyage IDs')}
              <textarea title={tips.voyageIds} rows={3} value={createForm.voyageIds} onChange={(event) => setCreateForm((current) => ({ ...current, voyageIds: event.target.value }))} placeholder="vyg_..." />
            </label>
            <label title={tips.missionIds}>{t('Mission IDs')}
              <textarea title={tips.missionIds} rows={3} value={createForm.missionIds} onChange={(event) => setCreateForm((current) => ({ ...current, missionIds: event.target.value }))} placeholder="msn_..." />
            </label>
            <label title={tips.checkRunIds}>{t('Check Run IDs')}
              <textarea title={tips.checkRunIds} rows={3} value={createForm.checkRunIds} onChange={(event) => setCreateForm((current) => ({ ...current, checkRunIds: event.target.value }))} placeholder="chk_..." />
            </label>
            <div className="modal-actions">
              <button type="submit" className="btn btn-primary" disabled={saving} title={editing ? t('Save changes to this release. Artifacts are rebuilt from the linked check runs.') : t('Create the release. Blank fields are generated from the linked work.')}>{saving ? t('Saving...') : editing ? t('Save Changes') : t('Create Release')}</button>
              <button type="button" className="btn" onClick={() => setShowCreate(false)} disabled={saving} title={t('Close without saving.')}>{t('Cancel')}</button>
            </div>
          </form>
        </div>
      )}

      <div className="playbook-overview-grid">
        <div className="card playbook-overview-card" title={t('All release records you can see, before filters.')}>
          <span>{t('Total Releases')}</span>
          <strong>{releases.length}</strong>
        </div>
        <div className="card playbook-overview-card" title={t('Releases marked Shipped.')}>
          <span>{t('Shipped')}</span>
          <strong>{shippedCount}</strong>
        </div>
        <div className="card playbook-overview-card" title={t('Releases marked Candidate: ready for testing or deployment but not yet shipped.')}>
          <span>{t('Candidates')}</span>
          <strong>{candidateCount}</strong>
        </div>
        <div className="card playbook-overview-card" title={t('Releases marked Failed or RolledBack.')}>
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
            title={t('Filters the list to releases whose title, version, tag, summary, or ID contains this text.')}
          />
          <select aria-label={t('All statuses')} title={t('Show only releases in this lifecycle stage.')} value={statusFilter} onChange={(event) => setStatusFilter(event.target.value as typeof statusFilter)}>
            <option value="all">{t('All statuses')}</option>
            {RELEASE_STATUSES.map((status) => (
              <option key={status} value={status}>{status}</option>
            ))}
          </select>
          <select aria-label={t('All vessels')} title={t('Show only releases for this vessel.')} value={vesselFilter} onChange={(event) => setVesselFilter(event.target.value)}>
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
