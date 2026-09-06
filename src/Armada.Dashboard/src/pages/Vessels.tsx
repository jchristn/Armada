import { useEffect, useState, useMemo, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { listVessels, listFleets, listPipelines, createVessel, updateVessel, deleteVessel, getVesselGitStatus, getVesselBranches } from '../api/client';
import BranchesModal from '../components/vessels/BranchesModal';
import type { Fleet, Vessel, Pipeline } from '../types/models';
import Pagination from '../components/shared/Pagination';
import ActionMenu from '../components/shared/ActionMenu';
import BuildContextModal from '../components/vessels/BuildContextModal';
import StatusBadge from '../components/shared/StatusBadge';
import ConfirmDialog from '../components/shared/ConfirmDialog';
import JsonViewer from '../components/shared/JsonViewer';
import CopyButton from '../components/shared/CopyButton';
import RefreshButton from '../components/shared/RefreshButton';
import AutoRefreshSelect from '../components/shared/AutoRefreshSelect';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import PageHeader from '../components/shared/PageHeader';
import ErrorModal from '../components/shared/ErrorModal';
import { useLocale } from '../context/LocaleContext';
import { useNotifications } from '../context/NotificationContext';
import { buildVesselDuplicatePayload } from '../lib/duplicates';
import { useResourceTable } from '../lib/useResourceTable';

interface VesselForm {
  name: string;
  fleetId: string;
  repoUrl: string;
  defaultBranch: string;
  localPath: string;
  workingDirectory: string;
  projectContext: string;
  styleGuide: string;
  enableModelContext: boolean;
  modelContext: string;
  gitHubTokenOverride: string;
  clearGitHubTokenOverride: boolean;
  landingMode: string;
  branchCleanupPolicy: string;
  allowConcurrentMissions: boolean;
  defaultPipelineId: string;
  secretScanEnabled: boolean;
  protectedPathPatterns: string;
  privateIdentifierDenylist: string;
  autoLandEnabled: boolean;
  autoLandMaxFiles: string;
  autoLandMaxLines: string;
  autoLandPathAllowGlobs: string;
  autoLandPathDenyGlobs: string;
  definitionOfDoneEnabled: boolean;
  definitionOfDoneBuildCommand: string;
  definitionOfDoneTestCommand: string;
  definitionOfDoneTimeoutSeconds: string;
}

const emptyForm: VesselForm = {
  name: '', fleetId: '', repoUrl: '', defaultBranch: 'main', localPath: '', workingDirectory: '',
  projectContext: '', styleGuide: '', enableModelContext: true, modelContext: '', gitHubTokenOverride: '', clearGitHubTokenOverride: false, landingMode: 'LocalMerge', branchCleanupPolicy: 'LocalAndRemote', allowConcurrentMissions: false, defaultPipelineId: '',
  secretScanEnabled: false, protectedPathPatterns: '', privateIdentifierDenylist: '',
  autoLandEnabled: false, autoLandMaxFiles: '', autoLandMaxLines: '', autoLandPathAllowGlobs: '', autoLandPathDenyGlobs: '',
  definitionOfDoneEnabled: false, definitionOfDoneBuildCommand: '', definitionOfDoneTestCommand: '', definitionOfDoneTimeoutSeconds: '',
};

export default function Vessels() {
  const navigate = useNavigate();
  const { t } = useLocale();
  const { pushToast } = useNotifications();
  const [vessels, setVessels] = useState<Vessel[]>([]);
  const [fleets, setFleets] = useState<Fleet[]>([]);
  const [pipelines, setPipelines] = useState<Pipeline[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [gitStatus, setGitStatus] = useState<Record<string, { ahead: number | null; behind: number | null }>>({});
  const [branchCounts, setBranchCounts] = useState<Record<string, number | null>>({});
  const [branchesModal, setBranchesModal] = useState<{ vesselId: string; vesselName: string } | null>(null);

  // Modal
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState<Vessel | null>(null);
  const [form, setForm] = useState<VesselForm>({ ...emptyForm });

  // JSON viewer
  const [jsonData, setJsonData] = useState<{ open: boolean; title: string; data: unknown }>({ open: false, title: '', data: null });
  const [buildContextVessel, setBuildContextVessel] = useState<Vessel | null>(null);

  // Confirm
  const [confirm, setConfirm] = useState<{ open: boolean; title: string; message: string; onConfirm: () => void }>({ open: false, title: '', message: '', onConfirm: () => {} });

  // Select-based column filters (equality; applied before the shared table hook)
  const [fleetFilter, setFleetFilter] = useState('');
  const [landingModeFilter, setLandingModeFilter] = useState('');

  const fleetMap = useMemo(() => {
    const m = new Map<string, string>();
    for (const f of fleets) m.set(f.id, f.name);
    return m;
  }, [fleets]);

  function fleetName(id: string | null): string {
    if (!id) return '';
    return fleetMap.get(id) ?? id.substring(0, 8);
  }

  const baseRows = useMemo(() => {
    return vessels.filter(v =>
      (!fleetFilter || v.fleetId === fleetFilter) &&
      (!landingModeFilter || (v.landingMode ?? '') === landingModeFilter)
    );
  }, [vessels, fleetFilter, landingModeFilter]);

  const table = useResourceTable({
    rows: baseRows,
    getId: (v) => v.id,
    columnValues: {
      name: (v) => v.name.toLowerCase(),
      repoUrl: (v) => (v.repoUrl ?? '').toLowerCase(),
      fleetId: (v) => fleetName(v.fleetId).toLowerCase(),
      defaultBranch: (v) => (v.defaultBranch ?? 'main').toLowerCase(),
      createdUtc: (v) => v.createdUtc,
    },
    initialSortField: 'name',
    initialSortDir: 'asc',
    initialPageSize: 25,
  });

  const load = useCallback(async () => {
    try {
      setLoading(true);
      const [vResult, fResult, pResult] = await Promise.all([listVessels({ pageSize: 9999 }), listFleets({ pageSize: 9999 }), listPipelines({ pageSize: 9999 })]);
      setVessels(vResult.objects);
      setFleets(fResult.objects);
      setPipelines(pResult.objects);
      setError('');

      // Fetch git status and branch counts for each vessel in the background (non-blocking)
      const statusMap: Record<string, { ahead: number | null; behind: number | null }> = {};
      const countMap: Record<string, number | null> = {};
      await Promise.all(vResult.objects.map(async (v: Vessel) => {
        try {
          const gs = await getVesselGitStatus(v.id);
          statusMap[v.id] = { ahead: gs.commitsAhead, behind: gs.commitsBehind };
        } catch {
          statusMap[v.id] = { ahead: null, behind: null };
        }
        try {
          const br = await getVesselBranches(v.id);
          countMap[v.id] = br.branchCount;
        } catch {
          countMap[v.id] = null;
        }
      }));
      setGitStatus(statusMap);
      setBranchCounts(countMap);
    } catch {
      setError(t('Failed to load vessels.'));
    } finally {
      setLoading(false);
    }
  }, [t]);

  useEffect(() => { load(); }, [load]);

  const { seconds: refreshSeconds, setSeconds: setRefreshSeconds } = useAutoRefresh('vessels', load);

  // CRUD
  function openCreate() { setForm({ ...emptyForm }); setEditing(null); setShowForm(true); }
  function openEdit(v: Vessel) {
    setForm({
      name: v.name,
      fleetId: v.fleetId ?? '',
      repoUrl: v.repoUrl ?? '',
      defaultBranch: v.defaultBranch || 'main',
      localPath: v.localPath ?? '',
      workingDirectory: v.workingDirectory ?? '',
      projectContext: v.projectContext ?? '',
      styleGuide: v.styleGuide ?? '',
      landingMode: v.landingMode ?? '',
      branchCleanupPolicy: v.branchCleanupPolicy ?? '',
      allowConcurrentMissions: v.allowConcurrentMissions,
      enableModelContext: v.enableModelContext,
      modelContext: v.modelContext ?? '',
      gitHubTokenOverride: '',
      clearGitHubTokenOverride: false,
      defaultPipelineId: v.defaultPipelineId ?? '',
      secretScanEnabled: v.secretScanEnabled ?? false,
      protectedPathPatterns: (v.protectedPathPatterns || []).join('\n'),
      privateIdentifierDenylist: (v.privateIdentifierDenylist || []).join('\n'),
      autoLandEnabled: v.autoLandEnabled ?? false,
      autoLandMaxFiles: v.autoLandMaxFiles ? String(v.autoLandMaxFiles) : '',
      autoLandMaxLines: v.autoLandMaxLines ? String(v.autoLandMaxLines) : '',
      autoLandPathAllowGlobs: (v.autoLandPathAllowGlobs || []).join('\n'),
      autoLandPathDenyGlobs: (v.autoLandPathDenyGlobs || []).join('\n'),
      definitionOfDoneEnabled: v.definitionOfDoneEnabled ?? false,
      definitionOfDoneBuildCommand: v.definitionOfDoneBuildCommand || '',
      definitionOfDoneTestCommand: v.definitionOfDoneTestCommand || '',
      definitionOfDoneTimeoutSeconds: v.definitionOfDoneTimeoutSeconds ? String(v.definitionOfDoneTimeoutSeconds) : '',
    });
    setEditing(v);
    setShowForm(true);
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    try {
      const payload: Record<string, unknown> = { ...form };
      if (!payload.localPath) delete payload.localPath;
      if (!payload.workingDirectory) delete payload.workingDirectory;
      if (!payload.projectContext) delete payload.projectContext;
      if (!payload.styleGuide) delete payload.styleGuide;
      if (!payload.landingMode) delete payload.landingMode;
      if (!payload.branchCleanupPolicy) delete payload.branchCleanupPolicy;
      if (!payload.modelContext) delete payload.modelContext;
      if (!payload.defaultPipelineId) delete payload.defaultPipelineId;
      payload.protectedPathPatterns = form.protectedPathPatterns.split(/\r?\n/).map((s) => s.trim()).filter((s) => s.length > 0);
      payload.privateIdentifierDenylist = form.privateIdentifierDenylist.split(/\r?\n/).map((s) => s.trim()).filter((s) => s.length > 0);
      payload.autoLandMaxFiles = form.autoLandMaxFiles.trim() ? Math.max(0, parseInt(form.autoLandMaxFiles, 10) || 0) : 0;
      payload.autoLandMaxLines = form.autoLandMaxLines.trim() ? Math.max(0, parseInt(form.autoLandMaxLines, 10) || 0) : 0;
      payload.autoLandPathAllowGlobs = form.autoLandPathAllowGlobs.split(/\r?\n/).map((s) => s.trim()).filter((s) => s.length > 0);
      payload.autoLandPathDenyGlobs = form.autoLandPathDenyGlobs.split(/\r?\n/).map((s) => s.trim()).filter((s) => s.length > 0);
      payload.definitionOfDoneBuildCommand = form.definitionOfDoneBuildCommand.trim();
      payload.definitionOfDoneTestCommand = form.definitionOfDoneTestCommand.trim();
      payload.definitionOfDoneTimeoutSeconds = form.definitionOfDoneTimeoutSeconds.trim() ? Math.max(30, parseInt(form.definitionOfDoneTimeoutSeconds, 10) || 1800) : 1800;
      delete payload.clearGitHubTokenOverride;
      if (editing)
      {
        if (form.clearGitHubTokenOverride)
          payload.gitHubTokenOverride = '';
        else if (!form.gitHubTokenOverride.trim())
          delete payload.gitHubTokenOverride;
        else
          payload.gitHubTokenOverride = form.gitHubTokenOverride.trim();
      }
      else if (!form.gitHubTokenOverride.trim())
      {
        delete payload.gitHubTokenOverride;
      }
      else
      {
        payload.gitHubTokenOverride = form.gitHubTokenOverride.trim();
      }
      if (editing) await updateVessel(editing.id, payload);
      else await createVessel(payload);
      setShowForm(false);
      pushToast('success', editing
        ? t('Vessel "{{name}}" saved.', { name: form.name })
        : t('Vessel "{{name}}" created.', { name: form.name }));
      load();
    } catch { setError(t('Save failed.')); }
  }

  function handleDelete(id: string, name: string) {
    setConfirm({
      open: true,
      title: t('Delete Vessel'),
      message: t('Delete vessel "{{name}}"? This cannot be undone.', { name }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        try {
          await deleteVessel(id);
          pushToast('warning', t('Vessel "{{name}}" deleted.', { name }));
          load();
        } catch { setError(t('Delete failed.')); }
      },
    });
  }

  function handleBulkDelete() {
    setConfirm({
      open: true,
      title: t('Delete Selected Vessels'),
      message: t('Delete {{count}} selected vessel(s)? This cannot be undone.', { count: table.selected.length }),
      onConfirm: async () => {
        setConfirm(c => ({ ...c, open: false }));
        const ids = [...table.selected];
        table.setSelected([]);
        let failed = 0;
        for (const id of ids) {
          try { await deleteVessel(id); } catch { failed++; }
        }
        const success = ids.length - failed;
        if (success > 0) {
          pushToast(failed > 0 ? 'warning' : 'success', failed > 0
            ? t('Deleted {{success}} vessels. {{failed}} failed.', { success, failed })
            : t('Deleted {{success}} vessels.', { success }));
        }
        if (failed > 0) setError(t('Deleted {{success}} vessels, {{failed}} failed.', { success: ids.length - failed, failed }));
        load();
      },
    });
  }

  function manageObjectives(vessel: Vessel) {
    const params = new URLSearchParams({ vesselId: vessel.id });
    if (vessel.fleetId) {
      params.set('fleetId', vessel.fleetId);
    }

    navigate(`/backlog?${params.toString()}`);
  }

  async function handleDuplicate(vessel: Vessel) {
    try {
      const created = await createVessel(buildVesselDuplicatePayload(vessel));
      pushToast('success', t('Vessel "{{name}}" duplicated.', { name: created.name }));
      navigate(`/vessels/${created.id}?edit=1`);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('Duplicate failed.'));
    }
  }

  return (
    <div>
      <PageHeader
        title={t('Vessels')}
        subtitle={t('Git repositories registered with Armada')}
        actions={(
          <>
            <button className="btn btn-sm" onClick={() => navigate('/workspace')}>
              {t('Workspace')}
            </button>
            {table.selected.length > 0 && (
              <button className="btn btn-sm btn-danger" onClick={handleBulkDelete}>
                {t('Delete Selected')} ({table.selected.length})
              </button>
            )}
            <button className="btn btn-primary btn-sm" onClick={openCreate}>+ {t('Vessel')}</button>
            <AutoRefreshSelect seconds={refreshSeconds} onChange={setRefreshSeconds} />
            <RefreshButton onRefresh={load} title="Refresh vessel data" />
          </>
        )}
      />

      <ErrorModal error={error} onClose={() => setError('')} />

      {/* Create/Edit Modal */}
      {showForm && (
        <div className="modal-overlay" onClick={() => setShowForm(false)}>
          <form className="modal" style={{ width: 'min(1080px, 95vw)', maxWidth: 'min(1080px, 95vw)', maxHeight: '92vh', overflowY: 'auto' }} onClick={e => e.stopPropagation()} onSubmit={handleSubmit}>
            <h3>{editing ? t('Edit Vessel') : t('Create Vessel')}</h3>

            {/* Row 1: Name + Fleet + Repo URL (3 cols) */}
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 2fr', gap: '0 1.5rem' }}>
              <label>{t('Name')}<input value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} required /></label>
              <label>{t('Fleet')}
                <select value={form.fleetId} onChange={e => setForm({ ...form, fleetId: e.target.value })}>
                  <option value="">{t('Select a fleet...')}</option>
                  {fleets.map(f => <option key={f.id} value={f.id}>{f.name}</option>)}
                </select>
              </label>
              <label>{t('Repository URL')}<input value={form.repoUrl} onChange={e => setForm({ ...form, repoUrl: e.target.value })} required placeholder="https://github.com/org/repo.git" /></label>
            </div>

            {/* Row 2: Default Branch + Local Path + Working Directory (3 cols) */}
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: '0 1.5rem' }}>
              <label>{t('Default Branch')}<input value={form.defaultBranch} onChange={e => setForm({ ...form, defaultBranch: e.target.value })} /></label>
              <label>{t('Local Path')}<input value={form.localPath} onChange={e => setForm({ ...form, localPath: e.target.value })} /></label>
              <label>{t('Working Directory')}<input value={form.workingDirectory} onChange={e => setForm({ ...form, workingDirectory: e.target.value })} /></label>
            </div>

            <label>
              GitHub Token Override
              <input
                type="password"
                value={form.gitHubTokenOverride}
                onChange={e => setForm({ ...form, gitHubTokenOverride: e.target.value, clearGitHubTokenOverride: false })}
                placeholder={editing && editing.hasGitHubTokenOverride ? 'Leave blank to keep existing override' : 'Optional per-vessel GitHub token'}
                autoComplete="new-password"
              />
              <div className="text-dim" style={{ fontSize: '0.8em' }}>
                {editing
                  ? editing.hasGitHubTokenOverride
                    ? 'This vessel already has an override. Leave blank to keep it, enter a new token to replace it, or clear it below.'
                    : 'No vessel override is stored. Armada will use the global GitHub token if one is configured.'
                  : 'Optional. Leave blank to use the global GitHub token from Armada settings.'}
              </div>
            </label>
            {editing && editing.hasGitHubTokenOverride && (
              <label style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <input
                  type="checkbox"
                  checked={form.clearGitHubTokenOverride}
                  onChange={e => setForm({ ...form, clearGitHubTokenOverride: e.target.checked, gitHubTokenOverride: e.target.checked ? '' : form.gitHubTokenOverride })}
                  style={{ width: 'auto' }}
                />
                Clear existing GitHub token override
              </label>
            )}

            {/* Row 3: Landing Mode + Branch Cleanup + Pipeline (3 cols) */}
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: '0 1.5rem' }}>
              <label title={t('How completed mission work is integrated.')}>{t('Landing Mode')}
                <select value={form.landingMode} onChange={e => setForm({ ...form, landingMode: e.target.value })}>
                  <option value="">{t('Default')}</option>
                  <option value="LocalMerge">{t('Local Merge')}</option>
                  <option value="PullRequest">{t('Pull Request')}</option>
                  <option value="MergeQueue">Merge Queue</option>
                  <option value="None">{t('None')}</option>
                </select>
              </label>
              <label title={t('When and how mission branches are deleted after successful landing.')}>{t('Branch Cleanup')}
                <select value={form.branchCleanupPolicy} onChange={e => setForm({ ...form, branchCleanupPolicy: e.target.value })}>
                  <option value="">{t('Default')}</option>
                  <option value="LocalOnly">{t('Local Only')}</option>
                  <option value="LocalAndRemote">{t('Local and Remote')}</option>
                  <option value="None">{t('None')}</option>
                </select>
              </label>
              <label>{t('Default Pipeline')}
                <select value={form.defaultPipelineId} onChange={e => setForm({ ...form, defaultPipelineId: e.target.value })}>
                  <option value="">{t('None (WorkerOnly)')}</option>
                  {pipelines.map(p => (
                    <option key={p.id} value={p.id}>{p.name} ({p.stages.map(s => s.personaName).join(' -> ')})</option>
                  ))}
                </select>
              </label>
            </div>

            {/* Checkboxes */}
            <div style={{ display: 'flex', gap: '2rem', marginBottom: '0.5rem' }}>
              <label style={{ display: 'inline-flex', alignItems: 'center', gap: '0.4rem', marginBottom: 0, lineHeight: 1, cursor: 'pointer' }} title={t('When enabled, multiple missions can run on this vessel at the same time.')}>
                <input type="checkbox" checked={form.allowConcurrentMissions} onChange={e => setForm({ ...form, allowConcurrentMissions: e.target.checked })} style={{ width: 'auto', margin: 0, verticalAlign: 'middle' }} />
                <span style={{ verticalAlign: 'middle' }}>{t('Allow Concurrent Missions')}</span>
              </label>
              <label style={{ display: 'inline-flex', alignItems: 'center', gap: '0.4rem', marginBottom: 0, lineHeight: 1, cursor: 'pointer' }} title={t('When enabled, AI agents accumulate key knowledge about this repository during missions.')}>
                <input type="checkbox" checked={form.enableModelContext} onChange={e => setForm({ ...form, enableModelContext: e.target.checked })} style={{ width: 'auto', margin: 0, verticalAlign: 'middle' }} />
                <span style={{ verticalAlign: 'middle' }}>{t('Enable Model Context')}</span>
              </label>
              <label style={{ display: 'inline-flex', alignItems: 'center', gap: '0.4rem', marginBottom: 0, lineHeight: 1, cursor: 'pointer' }} title={t('Scan each mission diff for secrets before landing, and flag protected paths / private identifiers.')}>
                <input type="checkbox" checked={form.secretScanEnabled} onChange={e => setForm({ ...form, secretScanEnabled: e.target.checked })} style={{ width: 'auto', margin: 0, verticalAlign: 'middle' }} />
                <span style={{ verticalAlign: 'middle' }}>{t('Scan Mission Diffs for Secrets')}</span>
              </label>
            </div>

            {/* Dock boundary path/identifier rules */}
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '0 1.5rem', marginBottom: '0.5rem' }}>
              <label style={{ display: 'flex', flexDirection: 'column' }}>
                {t('Protected Path Patterns')}
                <textarea value={form.protectedPathPatterns} onChange={e => setForm({ ...form, protectedPathPatterns: e.target.value })} rows={2} placeholder={t('One glob per line, e.g. .env* or infra/**')} style={{ resize: 'vertical' }} />
              </label>
              <label style={{ display: 'flex', flexDirection: 'column' }}>
                {t('Private Identifier Denylist')}
                <textarea value={form.privateIdentifierDenylist} onChange={e => setForm({ ...form, privateIdentifierDenylist: e.target.value })} rows={2} placeholder={t('One value per line; do not list real secrets')} style={{ resize: 'vertical' }} />
              </label>
            </div>

            {/* Auto-land rules */}
            <div style={{ marginBottom: '0.5rem' }}>
              <label style={{ display: 'inline-flex', alignItems: 'center', gap: '0.4rem', marginBottom: '0.5rem', lineHeight: 1, cursor: 'pointer' }} title={t('When enabled, a passing mission must satisfy the rules below to land unattended; otherwise it holds for review.')}>
                <input type="checkbox" checked={form.autoLandEnabled} onChange={e => setForm({ ...form, autoLandEnabled: e.target.checked })} style={{ width: 'auto', margin: 0, verticalAlign: 'middle' }} />
                <span style={{ verticalAlign: 'middle' }}>{t('Auto-land small changes')}</span>
              </label>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '0 1.5rem' }}>
                <label style={{ display: 'flex', flexDirection: 'column' }}>
                  {t('Max Files (0 = no limit)')}
                  <input type="number" min={0} value={form.autoLandMaxFiles} onChange={e => setForm({ ...form, autoLandMaxFiles: e.target.value })} placeholder="0" />
                </label>
                <label style={{ display: 'flex', flexDirection: 'column' }}>
                  {t('Max Lines (0 = no limit)')}
                  <input type="number" min={0} value={form.autoLandMaxLines} onChange={e => setForm({ ...form, autoLandMaxLines: e.target.value })} placeholder="0" />
                </label>
                <label style={{ display: 'flex', flexDirection: 'column' }}>
                  {t('Auto-land Allowed Paths')}
                  <textarea value={form.autoLandPathAllowGlobs} onChange={e => setForm({ ...form, autoLandPathAllowGlobs: e.target.value })} rows={2} placeholder={t('One glob per line, e.g. src/**')} style={{ resize: 'vertical' }} />
                </label>
                <label style={{ display: 'flex', flexDirection: 'column' }}>
                  {t('Auto-land Denied Paths')}
                  <textarea value={form.autoLandPathDenyGlobs} onChange={e => setForm({ ...form, autoLandPathDenyGlobs: e.target.value })} rows={2} placeholder={t('One glob per line, e.g. infra/**')} style={{ resize: 'vertical' }} />
                </label>
              </div>
            </div>

            {/* In-dock Definition-of-Done gate */}
            <div style={{ marginBottom: '0.5rem' }}>
              <label style={{ display: 'inline-flex', alignItems: 'center', gap: '0.4rem', marginBottom: '0.5rem', lineHeight: 1, cursor: 'pointer' }} title={t('When enabled, the build and unit-test commands below run inside the mission checkout before landing; a failure blocks acceptance.')}>
                <input type="checkbox" checked={form.definitionOfDoneEnabled} onChange={e => setForm({ ...form, definitionOfDoneEnabled: e.target.checked })} style={{ width: 'auto', margin: 0, verticalAlign: 'middle' }} />
                <span style={{ verticalAlign: 'middle' }}>{t('Run in-dock build + tests before acceptance')}</span>
              </label>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '0 1.5rem' }}>
                <label style={{ display: 'flex', flexDirection: 'column' }}>
                  {t('Build Command')}
                  <input value={form.definitionOfDoneBuildCommand} onChange={e => setForm({ ...form, definitionOfDoneBuildCommand: e.target.value })} placeholder={t('e.g. dotnet build')} />
                </label>
                <label style={{ display: 'flex', flexDirection: 'column' }}>
                  {t('Test Command')}
                  <input value={form.definitionOfDoneTestCommand} onChange={e => setForm({ ...form, definitionOfDoneTestCommand: e.target.value })} placeholder={t('e.g. dotnet test')} />
                </label>
                <label style={{ display: 'flex', flexDirection: 'column' }}>
                  {t('Per-phase Timeout (seconds)')}
                  <input type="number" min={30} max={7200} value={form.definitionOfDoneTimeoutSeconds} onChange={e => setForm({ ...form, definitionOfDoneTimeoutSeconds: e.target.value })} placeholder="1800" />
                </label>
              </div>
            </div>

            {/* Context textareas always 3 cols -- fills remaining vertical space */}
            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: '0 1.5rem' }}>
              <label style={{ display: 'flex', flexDirection: 'column' }}>
                {t('Project Context')}
                <textarea value={form.projectContext} onChange={e => setForm({ ...form, projectContext: e.target.value })} style={{ minHeight: '150px', resize: 'vertical' }} />
              </label>
              <label style={{ display: 'flex', flexDirection: 'column' }}>
                {t('Style Guide')}
                <textarea value={form.styleGuide} onChange={e => setForm({ ...form, styleGuide: e.target.value })} style={{ minHeight: '150px', resize: 'vertical' }} />
              </label>
              <label style={{ display: 'flex', flexDirection: 'column' }}>
                {t('Model Context')}
                <textarea value={form.modelContext} onChange={e => setForm({ ...form, modelContext: e.target.value })} placeholder={form.enableModelContext ? t('Agent-accumulated context...') : t('Enable Model Context to use')} disabled={!form.enableModelContext} style={{ minHeight: '150px', resize: 'vertical', ...(form.enableModelContext ? {} : { opacity: 0.4 }) }} />
              </label>
            </div>

            <div className="modal-actions">
              <button type="submit" className="btn btn-primary">{t('Save')}</button>
              <button type="button" className="btn" onClick={() => setShowForm(false)}>{t('Cancel')}</button>
            </div>
          </form>
        </div>
      )}

      <JsonViewer open={jsonData.open} title={jsonData.title} data={jsonData.data} onClose={() => setJsonData({ open: false, title: '', data: null })} />
      {buildContextVessel && (
        <BuildContextModal
          vessel={buildContextVessel}
          onClose={() => setBuildContextVessel(null)}
          onBuilt={(updated) => {
            setVessels((prev) => prev.map((x) => (x.id === updated.id ? updated : x)));
            pushToast('success', t('Model Context updated for "{{name}}".', { name: updated.name }));
          }}
        />
      )}
      <ConfirmDialog open={confirm.open} title={confirm.title} message={confirm.message}
        onConfirm={confirm.onConfirm} onCancel={() => setConfirm(c => ({ ...c, open: false }))} />

      {loading && vessels.length === 0 && <p className="text-dim">{t('Loading...')}</p>}
      {!loading && vessels.length === 0 && <p className="text-dim">{t('No vessels configured.')}</p>}

      {vessels.length > 0 && (
        <>
          <Pagination pageNumber={table.currentPage} pageSize={table.pageSize} totalPages={table.totalPages}
            totalRecords={table.sorted.length}
            onPageChange={p => table.setPageNumber(p)} onPageSizeChange={s => { table.setPageSize(s); table.setPageNumber(1); }} />

          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th className="col-checkbox">
                    <input type="checkbox" checked={table.allSelected} onChange={e => e.target.checked ? table.selectAll() : table.clearSelection()} title={t('Select all vessels')} />
                  </th>
                  <th className="sortable" onClick={() => table.handleSort('name')} title={t('Vessel name -- click to sort')}>
                    {t('Name')}{table.sortIcon('name')}
                  </th>
                  <th>{t('ID')}</th>
                  <th className="sortable" onClick={() => table.handleSort('fleetId')} title={t('Fleet -- click to sort')}>
                    {t('Fleet')}{table.sortIcon('fleetId')}
                  </th>
                  <th title={t('Remote git repository URL')}>{t('Repo URL')}</th>
                  <th className="sortable" onClick={() => table.handleSort('defaultBranch')} title={t('Default branch -- click to sort')}>
                    {t('Branch')}{table.sortIcon('defaultBranch')}
                  </th>
                  <th title={t('How completed mission work is integrated (LocalMerge, PullRequest, MergeQueue, None)')}>{t('Landing Mode')}</th>
                  <th title={t('Commits ahead and behind the remote default branch')}>{t('Sync')}</th>
                  <th title={t('Number of branches in the vessel repository')}>{t('Branches')}</th>
                  <th className="text-right">{t('Actions')}</th>
                </tr>
                <tr className="column-filter-row">
                  <td></td>
                  <td><input type="text" className="col-filter" value={table.colFilters.name ?? ''} onChange={e => table.setColFilter('name', e.target.value)} placeholder={t('Search...')} /></td>
                  <td></td>
                  <td>
                    <select className="col-filter" title={t('Filter vessels by fleet')} value={fleetFilter} onChange={e => { setFleetFilter(e.target.value); table.setPageNumber(1); }}>
                      <option value="">{t('All Fleets')}</option>
                      {fleets.map(f => <option key={f.id} value={f.id}>{f.name}</option>)}
                    </select>
                  </td>
                  <td><input type="text" className="col-filter" value={table.colFilters.repoUrl ?? ''} onChange={e => table.setColFilter('repoUrl', e.target.value)} placeholder={t('Search...')} /></td>
                  <td></td>
                  <td>
                    <select className="col-filter" title={t('Filter vessels by landing mode')} value={landingModeFilter} onChange={e => { setLandingModeFilter(e.target.value); table.setPageNumber(1); }}>
                      <option value="">{t('All Modes')}</option>
                      <option value="LocalMerge">LocalMerge</option>
                      <option value="PullRequest">PullRequest</option>
                      <option value="MergeQueue">MergeQueue</option>
                      <option value="None">None</option>
                    </select>
                  </td>
                  <td></td>
                  <td></td>
                  <td></td>
                </tr>
              </thead>
              <tbody>
                {table.paginated.map(v => (
                  <tr key={v.id} className="clickable" onClick={() => openEdit(v)}>
                    <td className="col-checkbox" onClick={e => e.stopPropagation()}>
                      <input type="checkbox" checked={table.selected.includes(v.id)} onChange={() => table.toggleSelect(v.id)} title={t('Select this vessel')} />
                    </td>
                    <td><strong>{v.name}</strong></td>
                    <td className="mono text-dim table-id-cell">
                      <span className="id-display">
                        <span className="id-value" title={v.id}>{v.id}</span>
                        <CopyButton text={v.id} onClick={e => e.stopPropagation()} />
                      </span>
                    </td>
                    <td>
                      {v.fleetId ? (
                        <a href="#" onClick={e => { e.preventDefault(); e.stopPropagation(); navigate(`/fleets/${v.fleetId}`); }}>
                          {fleetName(v.fleetId)}
                        </a>
                      ) : '-'}
                    </td>
                    <td className="text-dim table-url-cell">
                      {v.repoUrl ? (
                        <span className="id-display">
                          <span className="url-value" title={v.repoUrl}>{v.repoUrl}</span>
                          <CopyButton text={v.repoUrl} onClick={e => e.stopPropagation()} title="Copy URL" />
                        </span>
                      ) : '-'}
                    </td>
                    <td className="text-dim table-url-cell">
                      <span className="id-display">
                        <span className="url-value" title={v.defaultBranch || 'main'}>{v.defaultBranch || 'main'}</span>
                        <CopyButton text={v.defaultBranch || 'main'} onClick={e => e.stopPropagation()} title="Copy branch" />
                      </span>
                    </td>
                    <td className="text-dim" title={v.landingMode === 'LocalMerge' ? t('Merge into local working directory') : v.landingMode === 'PullRequest' ? t('Push and create pull request') : v.landingMode === 'MergeQueue' ? t('Enqueue for validated merge') : v.landingMode === 'None' ? t('No automatic landing') : t('Uses global setting')}>{v.landingMode || '-'}</td>
                    <td>
                      {(() => {
                        const gs = gitStatus[v.id];
                        if (!gs || (gs.ahead === null && gs.behind === null)) return <span className="text-dim">-</span>;
                        const ahead = gs.ahead ?? 0;
                        const behind = gs.behind ?? 0;
                        if (ahead === 0 && behind === 0) return <span className="git-sync-badge git-sync-even" title={t('Up to date with remote')}>{t('in sync')}</span>;
                        return (
                          <span className="git-sync-badges">
                            {ahead > 0 && <span className="git-sync-badge git-sync-ahead" title={t('{{count}} commit(s) ahead of remote -- needs push', { count: ahead })}>{ahead} {t('ahead')}</span>}
                            {behind > 0 && <span className="git-sync-badge git-sync-behind" title={t('{{count}} commit(s) behind remote -- needs pull', { count: behind })}>{behind} {t('behind')}</span>}
                          </span>
                        );
                      })()}
                    </td>
                    <td onClick={e => e.stopPropagation()}>
                      {(() => {
                        const count = branchCounts[v.id];
                        return (
                          <button
                            className="btn btn-sm"
                            title={t('Manage branches')}
                            onClick={() => setBranchesModal({ vesselId: v.id, vesselName: v.name })}>
                            {count === null || count === undefined ? t('Branches') : t('{{count}} branches', { count })}
                          </button>
                        );
                      })()}
                    </td>
                    <td className="text-right" onClick={e => e.stopPropagation()}>
                      <ActionMenu id={`vessel-${v.id}`} items={[
                        { label: 'Manage Branches', onClick: () => setBranchesModal({ vesselId: v.id, vesselName: v.name }) },
                        { label: 'Manage Objectives', onClick: () => manageObjectives(v) },
                        { label: 'Manage Fleet', onClick: () => navigate(`/fleets/${v.fleetId}`), disabled: !v.fleetId },
                        { label: 'Open Workspace', onClick: () => navigate(`/workspace/${v.id}`) },
                        { label: 'View Detail', onClick: () => navigate(`/vessels/${v.id}`) },
                        { label: v.modelContext && v.modelContext.trim().length > 0 ? 'Refine Context' : 'Build Context', onClick: () => setBuildContextVessel(v) },
                        { label: 'Edit', onClick: () => openEdit(v) },
                        { label: 'Duplicate', onClick: () => void handleDuplicate(v) },
                        { label: 'View JSON', onClick: () => setJsonData({ open: true, title: `Vessel: ${v.name}`, data: v }) },
                        { label: 'Delete', danger: true, onClick: () => handleDelete(v.id, v.name) },
                      ]} />
                    </td>
                  </tr>
                ))}
                {table.paginated.length === 0 && (
                  <tr><td colSpan={10} className="text-dim">{t('No vessels match the current filters.')}</td></tr>
                )}
              </tbody>
            </table>
          </div>
        </>
      )}

      {branchesModal && (
        <BranchesModal
          vesselId={branchesModal.vesselId}
          vesselName={branchesModal.vesselName}
          open={true}
          onClose={() => { setBranchesModal(null); void load(); }}
        />
      )}
    </div>
  );
}
