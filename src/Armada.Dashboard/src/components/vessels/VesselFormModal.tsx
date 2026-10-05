import { useState } from 'react';
import { createVessel, updateVessel } from '../../api/client';
import type { Fleet, Pipeline, Vessel } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import {
  buildVesselPayload,
  emptyVesselForm,
  findLandingMode,
  getLandingModes,
  vesselToForm,
  type VesselFormState,
} from '../../lib/vesselForm';

interface VesselFormModalProps {
  /** The vessel being edited, or null to create a new one. */
  vessel: Vessel | null;
  fleets: Fleet[];
  pipelines: Pipeline[];
  onClose: () => void;
  /** Called after a successful save with the vessel name. */
  onSaved: (name: string, created: boolean) => void;
  onError: (message: string) => void;
}

const checkboxLabelStyle: React.CSSProperties = { display: 'inline-flex', alignItems: 'center', gap: '0.4rem', marginBottom: 0, lineHeight: 1, cursor: 'pointer' };
const checkboxStyle: React.CSSProperties = { width: 'auto', margin: 0, verticalAlign: 'middle' };
const columnLabelStyle: React.CSSProperties = { display: 'flex', flexDirection: 'column' };

/**
 * The one Create/Edit vessel form, used by the Vessels list and the vessel page so both expose the same settings
 * (landing, branch policy, auto-approve, concurrency, auto-land, definition of done, context).
 */
export default function VesselFormModal({ vessel, fleets, pipelines, onClose, onSaved, onError }: VesselFormModalProps) {
  const { t } = useLocale();
  const [form, setForm] = useState<VesselFormState>(() => (vessel ? vesselToForm(vessel) : { ...emptyVesselForm }));
  const [saving, setSaving] = useState(false);
  const landingModes = getLandingModes(t);
  const editing = vessel !== null;

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setSaving(true);
    try {
      const payload = buildVesselPayload(form, vessel);
      if (vessel) await updateVessel(vessel.id, payload as Partial<Vessel>);
      else await createVessel(payload as Partial<Vessel>);
      onSaved(form.name, !editing);
    } catch {
      onError(t('Save failed.'));
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <form className="modal" aria-label={editing ? t('Edit Vessel') : t('Create Vessel')} style={{ width: 'min(1080px, 95vw)', maxWidth: 'min(1080px, 95vw)', maxHeight: '92vh', overflowY: 'auto' }} onClick={e => e.stopPropagation()} onSubmit={handleSubmit}>
        <h3>{editing ? t('Edit Vessel') : t('Create Vessel')}</h3>

        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: '0 1.5rem' }}>
          <label>{t('Name')}<input value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} required /></label>
          <label>{t('Fleet')}
            <select value={form.fleetId} onChange={e => setForm({ ...form, fleetId: e.target.value })}>
              <option value="">{t('Select a fleet...')}</option>
              {fleets.map(f => <option key={f.id} value={f.id}>{f.name}</option>)}
            </select>
          </label>
          <label>{t('Repository URL')}<input value={form.repoUrl} onChange={e => setForm({ ...form, repoUrl: e.target.value })} required placeholder="https://github.com/org/repo.git" /></label>
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: '0 1.5rem' }}>
          <label>{t('Default Branch')}<input value={form.defaultBranch} onChange={e => setForm({ ...form, defaultBranch: e.target.value })} /></label>
          <label>{t('Local Path')}<input value={form.localPath} onChange={e => setForm({ ...form, localPath: e.target.value })} /></label>
          <label>{t('Working Directory')}<input value={form.workingDirectory} onChange={e => setForm({ ...form, workingDirectory: e.target.value })} /></label>
        </div>

        <label>
          {t('GitHub Token Override')}
          <input
            type="password"
            value={form.gitHubTokenOverride}
            onChange={e => setForm({ ...form, gitHubTokenOverride: e.target.value, clearGitHubTokenOverride: false })}
            placeholder={vessel && vessel.hasGitHubTokenOverride ? t('Leave blank to keep existing override') : t('Optional per-vessel GitHub token')}
            autoComplete="new-password"
          />
          <div className="text-dim" style={{ fontSize: '0.8em' }}>
            {vessel
              ? vessel.hasGitHubTokenOverride
                ? t('This vessel already has an override. Leave blank to keep it, enter a new token to replace it, or clear it below.')
                : t('No vessel override is stored. Armada will use the global GitHub token if one is configured.')
              : t('Optional. Leave blank to use the global GitHub token from Armada settings.')}
          </div>
        </label>
        {vessel && vessel.hasGitHubTokenOverride && (
          <label style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <input
              type="checkbox"
              checked={form.clearGitHubTokenOverride}
              onChange={e => setForm({ ...form, clearGitHubTokenOverride: e.target.checked, gitHubTokenOverride: e.target.checked ? '' : form.gitHubTokenOverride })}
              style={{ width: 'auto' }}
            />
            {t('Clear existing GitHub token override')}
          </label>
        )}

        {/* Landing */}
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: '0 1.5rem' }}>
          <label title={t('How completed mission work is integrated.')}>{t('Landing Mode')}
            <select value={form.landingMode} onChange={e => setForm({ ...form, landingMode: e.target.value })}>
              {landingModes.map(m => (
                <option key={m.value || 'default'} value={m.value}>{m.label}</option>
              ))}
            </select>
            <small className="text-dim" style={{ display: 'block', marginTop: '0.25rem', fontWeight: 'normal' }}>
              {findLandingMode(landingModes, form.landingMode).description}
            </small>
          </label>
          <label title={t('When and how mission branches are deleted after successful landing.')}>{t('Branch Cleanup')}
            <select value={form.branchCleanupPolicy} onChange={e => setForm({ ...form, branchCleanupPolicy: e.target.value })}>
              <option value="">{t('Default')}</option>
              <option value="LocalOnly">{t('Local Only')}</option>
              <option value="LocalAndRemote">{t('Local and Remote')}</option>
              <option value="None">{t('None')}</option>
            </select>
          </label>
          <label title={t('Whether CLI captains run missions on this vessel with their auto-approve (permission bypass) flags. Overrides the captain setting when set.')}>{t('Agent Auto-Approve')}
            <select value={form.autoApproveMode} onChange={e => setForm({ ...form, autoApproveMode: e.target.value })}>
              <option value="inherit">{t('Use captain setting')}</option>
              <option value="off">{t('Off for this vessel')}</option>
              <option value="on">{t('On for this vessel')}</option>
            </select>
          </label>
          <label>{t('Default Pipeline')}
            <select value={form.defaultPipelineId} onChange={e => setForm({ ...form, defaultPipelineId: e.target.value })}>
              <option value="">{t('None (WorkerOnly)')}</option>
              {pipelines.map(p => (
                <option key={p.id} value={p.id}>{p.name} ({(p.stages || []).map(s => s.personaName).join(' -> ')})</option>
              ))}
            </select>
          </label>
        </div>

        <div style={{ display: 'flex', flexWrap: 'wrap', gap: '0.75rem 2rem', marginBottom: '0.5rem' }}>
          <label style={checkboxLabelStyle} title={t('When enabled, multiple missions can run on this vessel at the same time.')}>
            <input type="checkbox" checked={form.allowConcurrentMissions} onChange={e => setForm({ ...form, allowConcurrentMissions: e.target.checked })} style={checkboxStyle} />
            <span style={{ verticalAlign: 'middle' }}>{t('Allow Concurrent Missions')}</span>
          </label>
          <label style={checkboxLabelStyle} title={t('When enabled, AI agents accumulate key knowledge about this repository during missions.')}>
            <input type="checkbox" checked={form.enableModelContext} onChange={e => setForm({ ...form, enableModelContext: e.target.checked })} style={checkboxStyle} />
            <span style={{ verticalAlign: 'middle' }}>{t('Enable Model Context')}</span>
          </label>
          <label style={checkboxLabelStyle} title={t('Scan each mission diff for secrets before landing, and flag protected paths / private identifiers.')}>
            <input type="checkbox" checked={form.secretScanEnabled} onChange={e => setForm({ ...form, secretScanEnabled: e.target.checked })} style={checkboxStyle} />
            <span style={{ verticalAlign: 'middle' }}>{t('Scan Mission Diffs for Secrets')}</span>
          </label>
        </div>

        {/* Branch policy */}
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: '0 1.5rem' }}>
          <label>{t('Release Branch Prefix')}<input value={form.releaseBranchPrefix} onChange={e => setForm({ ...form, releaseBranchPrefix: e.target.value })} /></label>
          <label>{t('Hotfix Branch Prefix')}<input value={form.hotfixBranchPrefix} onChange={e => setForm({ ...form, hotfixBranchPrefix: e.target.value })} /></label>
          <label style={columnLabelStyle}>
            {t('Protected Branch Patterns')}
            <textarea value={form.protectedBranchPatterns} onChange={e => setForm({ ...form, protectedBranchPatterns: e.target.value })} rows={2} placeholder={t('One pattern per line, e.g. main or release/*')} style={{ resize: 'vertical' }} />
          </label>
        </div>
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: '0.75rem 2rem', marginBottom: '0.5rem' }}>
          <label style={checkboxLabelStyle}>
            <input type="checkbox" checked={form.requirePassingChecksToLand} onChange={e => setForm({ ...form, requirePassingChecksToLand: e.target.checked })} style={checkboxStyle} />
            <span style={{ verticalAlign: 'middle' }}>{t('Require Passing Checks To Land')}</span>
          </label>
          <label style={checkboxLabelStyle}>
            <input type="checkbox" checked={form.requirePullRequestForProtectedBranches} onChange={e => setForm({ ...form, requirePullRequestForProtectedBranches: e.target.checked })} style={checkboxStyle} />
            <span style={{ verticalAlign: 'middle' }}>{t('Require PR For Protected Branches')}</span>
          </label>
          <label style={checkboxLabelStyle}>
            <input type="checkbox" checked={form.requireMergeQueueForReleaseBranches} onChange={e => setForm({ ...form, requireMergeQueueForReleaseBranches: e.target.checked })} style={checkboxStyle} />
            <span style={{ verticalAlign: 'middle' }}>{t('Require Merge Queue For Release Branches')}</span>
          </label>
        </div>

        {/* Dock boundary path/identifier rules */}
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(260px, 1fr))', gap: '0 1.5rem', marginBottom: '0.5rem' }}>
          <label style={columnLabelStyle}>
            {t('Protected Path Patterns')}
            <textarea value={form.protectedPathPatterns} onChange={e => setForm({ ...form, protectedPathPatterns: e.target.value })} rows={2} placeholder={t('One glob per line, e.g. .env* or infra/**')} style={{ resize: 'vertical' }} />
          </label>
          <label style={columnLabelStyle}>
            {t('Private Identifier Denylist')}
            <textarea value={form.privateIdentifierDenylist} onChange={e => setForm({ ...form, privateIdentifierDenylist: e.target.value })} rows={2} placeholder={t('One value per line; do not list real secrets')} style={{ resize: 'vertical' }} />
          </label>
        </div>

        {/* Auto-land rules */}
        <div style={{ marginBottom: '0.5rem' }}>
          <label style={{ ...checkboxLabelStyle, marginBottom: '0.5rem' }} title={t('When enabled, a passing mission must satisfy the rules below to land unattended; otherwise it holds for review.')}>
            <input type="checkbox" checked={form.autoLandEnabled} onChange={e => setForm({ ...form, autoLandEnabled: e.target.checked })} style={checkboxStyle} />
            <span style={{ verticalAlign: 'middle' }}>{t('Auto-land small changes')}</span>
          </label>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(260px, 1fr))', gap: '0 1.5rem' }}>
            <label style={columnLabelStyle}>
              {t('Max Files (0 = no limit)')}
              <input type="number" min={0} value={form.autoLandMaxFiles} onChange={e => setForm({ ...form, autoLandMaxFiles: e.target.value })} placeholder="0" />
            </label>
            <label style={columnLabelStyle}>
              {t('Max Lines (0 = no limit)')}
              <input type="number" min={0} value={form.autoLandMaxLines} onChange={e => setForm({ ...form, autoLandMaxLines: e.target.value })} placeholder="0" />
            </label>
            <label style={columnLabelStyle}>
              {t('Auto-land Allowed Paths')}
              <textarea value={form.autoLandPathAllowGlobs} onChange={e => setForm({ ...form, autoLandPathAllowGlobs: e.target.value })} rows={2} placeholder={t('One glob per line, e.g. src/**')} style={{ resize: 'vertical' }} />
            </label>
            <label style={columnLabelStyle}>
              {t('Auto-land Denied Paths')}
              <textarea value={form.autoLandPathDenyGlobs} onChange={e => setForm({ ...form, autoLandPathDenyGlobs: e.target.value })} rows={2} placeholder={t('One glob per line, e.g. infra/**')} style={{ resize: 'vertical' }} />
            </label>
          </div>
        </div>

        {/* In-dock Definition-of-Done gate */}
        <div style={{ marginBottom: '0.5rem' }}>
          <label style={{ ...checkboxLabelStyle, marginBottom: '0.5rem' }} title={t('When enabled, the build and unit-test commands below run inside the mission checkout before landing; a failure blocks acceptance.')}>
            <input type="checkbox" checked={form.definitionOfDoneEnabled} onChange={e => setForm({ ...form, definitionOfDoneEnabled: e.target.checked })} style={checkboxStyle} />
            <span style={{ verticalAlign: 'middle' }}>{t('Run in-dock build + tests before acceptance')}</span>
          </label>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: '0 1.5rem' }}>
            <label style={columnLabelStyle}>
              {t('Build Command')}
              <input value={form.definitionOfDoneBuildCommand} onChange={e => setForm({ ...form, definitionOfDoneBuildCommand: e.target.value })} placeholder={t('e.g. dotnet build')} />
            </label>
            <label style={columnLabelStyle}>
              {t('Test Command')}
              <input value={form.definitionOfDoneTestCommand} onChange={e => setForm({ ...form, definitionOfDoneTestCommand: e.target.value })} placeholder={t('e.g. dotnet test')} />
            </label>
            <label style={columnLabelStyle}>
              {t('Per-phase Timeout (seconds)')}
              <input type="number" min={30} max={7200} value={form.definitionOfDoneTimeoutSeconds} onChange={e => setForm({ ...form, definitionOfDoneTimeoutSeconds: e.target.value })} placeholder="1800" />
            </label>
          </div>
        </div>

        {/* Context */}
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(260px, 1fr))', gap: '0 1.5rem' }}>
          <label style={columnLabelStyle}>
            {t('Project Context')}
            <textarea value={form.projectContext} onChange={e => setForm({ ...form, projectContext: e.target.value })} style={{ minHeight: '150px', resize: 'vertical' }} />
          </label>
          <label style={columnLabelStyle}>
            {t('Style Guide')}
            <textarea value={form.styleGuide} onChange={e => setForm({ ...form, styleGuide: e.target.value })} style={{ minHeight: '150px', resize: 'vertical' }} />
          </label>
          <label style={columnLabelStyle}>
            {t('Model Context')}
            <textarea value={form.modelContext} onChange={e => setForm({ ...form, modelContext: e.target.value })} placeholder={form.enableModelContext ? t('Agent-accumulated context...') : t('Enable Model Context to use')} disabled={!form.enableModelContext} style={{ minHeight: '150px', resize: 'vertical', ...(form.enableModelContext ? {} : { opacity: 0.4 }) }} />
          </label>
        </div>

        <div className="modal-actions">
          <button type="submit" className="btn btn-primary" disabled={saving}>{t('Save')}</button>
          <button type="button" className="btn" onClick={onClose}>{t('Cancel')}</button>
        </div>
      </form>
    </div>
  );
}
