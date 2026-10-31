import { useState } from 'react';
import { createVessel, updateVessel, apiErrorMessage } from '../../api/client';
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
  const tip = {
    name: t('Display name for this repository in Armada. It also names the bare clone and the dock folders on disk, so keep it short and path-safe, e.g. my-service.'),
    fleet: t('Fleet this vessel belongs to. Fleets group repositories for dispatch, fleet actions and a shared default pipeline. Leave unselected to keep the vessel outside any fleet.'),
    repoUrl: t('Git remote Armada clones, fetches and pushes to, e.g. https://github.com/org/repo.git. Required. Captains work on branches made from this remote.'),
    defaultBranch: t('Branch missions start from and land into, e.g. main or develop. Defaults to main. Armada creates it in the bare clone if it is missing.'),
    localPath: t('Folder on the Admiral host where Armada keeps its bare clone of the repo; mission worktrees are made from it. Leave blank and Armada clones into its repos directory on first dispatch.'),
    workingDirectory: t('Your own checkout of this repo on the Admiral host, e.g. /home/me/code/my-service. Local Merge lands here, and Workspace, checks, health and fleet actions run in it. Armada never deletes it.'),
    token: t('GitHub token used only for this vessel (pushes, pull requests, checks), e.g. a fine-grained token scoped to this repo. Blank uses the global token from Settings. Stored write-only; never shown again.'),
    clearToken: t('Remove the stored per-vessel token when you save. The vessel then uses the global GitHub token from Settings, or none if that is not set.'),
    landingMode: t('How finished mission work reaches the repo: local merge, merge and push, pull request, merge queue, or manual. Default uses the voyage or global setting.'),
    branchCleanup: t('What happens to a mission branch after it lands. Local Only deletes it from the bare clone; Local and Remote also deletes it on the remote; None keeps it. Default is the global setting (Local Only).'),
    autoApprove: t('Whether CLI captains run here with their auto-approve (permission bypass) flags. Use captain setting keeps each captain as configured; On or Off overrides every captain on this vessel.'),
    pipeline: t('Persona pipeline used when a dispatch to this vessel does not name one, e.g. Architect then Worker then Judge. None falls back to the fleet default, then a single Worker stage.'),
    serialize: t('When a new mission overlaps an active mission on this vessel (shared files or a reserved hotspot directory), dispatch chains it after that mission so it starts once that one lands. Off (the default) dispatches it right away with an overlap warning. A dependency you set yourself is never replaced.'),
    concurrent: t('Let several missions be active on this vessel at once, each in its own worktree. Off (the default) runs one mission at a time, which avoids merge conflicts between missions.'),
    modelContext: t('Ask captains to record what they learn about this repo (build quirks, test patterns, architecture) into Model Context, which is fed to future missions. On by default.'),
    secretScan: t('Before landing, scan each mission diff for secrets such as API keys, and block changes to Protected Path Patterns or text from the Private Identifier Denylist. Off by default.'),
    releasePrefix: t('Branch name prefix that marks a release branch, e.g. release/ matches release/1.2. Used by the release merge queue rule below. Defaults to release/.'),
    hotfixPrefix: t('Branch name prefix that marks a hotfix branch, e.g. hotfix/ matches hotfix/login-fix. Landing readiness warns when a protected hotfix lands by local merge. Defaults to hotfix/.'),
    protectedBranches: t('Branches treated as protected, one exact name or glob per line, e.g. main or release/*. Used by the Require PR rule below and shown in landing readiness. Blank protects nothing.'),
    requireChecks: t('Block landing until at least one structured check (build, test, lint) has passed for the mission branch. Off by default.'),
    requirePr: t('When the target branch matches a Protected Branch Pattern, only Pull Request or Merge Queue landing is allowed; local merges are blocked. Off by default.'),
    requireMq: t('Branches starting with the Release Branch Prefix may only land through the merge queue. Off by default.'),
    protectedPaths: t('Files a mission may not change, one glob per line, e.g. .env* or infra/**. A diff touching a match is blocked before landing. Blank allows all paths.'),
    denylist: t('Company names, internal hostnames or other private strings that must not appear in added lines, one per line, e.g. corp.internal. Matches block landing. Do not list real secrets here.'),
    autoLand: t('When on, a passing mission lands unattended only if it meets the file, line and path limits below; anything larger holds for your review. Off uses the normal landing and review rules.'),
    maxFiles: t('Largest number of changed files a mission may have and still auto-land, e.g. 10. 0 or blank means no file limit.'),
    maxLines: t('Largest number of changed lines (added plus removed) a mission may have and still auto-land, e.g. 200. 0 or blank means no line limit.'),
    allowPaths: t('When set, a mission auto-lands only if every changed file matches one of these globs, one per line, e.g. src/** or docs/**. Blank allows any path.'),
    denyPaths: t('A mission that changes any file matching these globs never auto-lands and always holds for review, one per line, e.g. infra/** or *.csproj.'),
    dod: t('Run the build and test commands below inside the mission worktree before the mission is accepted. A failure, or a timeout, blocks acceptance and records the reason. Off by default.'),
    buildCmd: t('Shell command that builds the project in the mission worktree, e.g. dotnet build or npm run build. A non-zero exit blocks acceptance as a compile failure. Blank skips the build step.'),
    testCmd: t('Shell command that runs the unit tests in the mission worktree, e.g. dotnet test or npm test. A non-zero exit blocks acceptance as a test failure. Blank skips the test step.'),
    testOverride: t('Command fleet actions run for the Test intent on this vessel. Overrides the repo script and the detected ecosystem default. Blank uses detection.'),
    lintOverride: t('Command fleet actions run for the Lint intent on this vessel. Overrides the repo script and the detected ecosystem default. Blank uses detection.'),
    auditOverride: t('Command fleet actions run for the Audit intent on this vessel. Overrides the repo script and the detected ecosystem default. Blank uses detection.'),
    workerEnvelope: t('Optional Worker eligibility envelope. Leave every field blank for no envelope. When set, only captains whose model profile meets it run Worker missions on this vessel. The Architect may tighten it per mission but never loosen it.'),
    timeout: t('Time limit in seconds for each of the build and test commands, from 30 to 7200. Blank uses 1800 (30 minutes). Going over counts as a timeout failure.'),
    projectContext: t('What this project is: purpose, architecture, key files and dependencies. Added to every captain prompt for this vessel, so keep it accurate and concise.'),
    styleGuide: t('Coding rules captains must follow here: naming, patterns, language restrictions, preferred libraries. Added to every captain prompt for this vessel.'),
    modelContextText: t('Notes captains have accumulated about this repo during missions, fed to future missions. You can edit or trim it. Turn on Enable Model Context to use it.'),
    save: editing ? t('Save your changes to this vessel.') : t('Create the vessel with these settings. Armada clones the repository the first time a mission is dispatched to it.'),
    cancel: t('Close this form without saving any changes.'),
  };

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setSaving(true);
    try {
      const payload = buildVesselPayload(form, vessel);
      if (vessel) await updateVessel(vessel.id, payload as Partial<Vessel>);
      else await createVessel(payload as Partial<Vessel>);
      onSaved(form.name, !editing);
    } catch (err: unknown) {
      onError(apiErrorMessage(err, t('Save failed.')));
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="modal-overlay" onClick={onClose}>
      <form className="modal" aria-label={editing ? t('Edit Vessel') : t('Create Vessel')} style={{ width: '95vw', maxWidth: '95vw', height: '95vh', maxHeight: '95vh', overflowY: 'auto', boxSizing: 'border-box' }} onClick={e => e.stopPropagation()} onSubmit={handleSubmit}>
        <h3>{editing ? t('Edit Vessel') : t('Create Vessel')}</h3>

        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: '0 1.5rem' }}>
          <label title={tip.name}>{t('Name')}<input title={tip.name} value={form.name} onChange={e => setForm({ ...form, name: e.target.value })} required /></label>
          <label title={tip.fleet}>{t('Fleet')}
            <select title={tip.fleet} value={form.fleetId} onChange={e => setForm({ ...form, fleetId: e.target.value })}>
              <option value="">{t('Select a fleet...')}</option>
              {fleets.map(f => <option key={f.id} value={f.id}>{f.name}</option>)}
            </select>
          </label>
          <label title={tip.repoUrl}>{t('Repository URL')}<input title={tip.repoUrl} value={form.repoUrl} onChange={e => setForm({ ...form, repoUrl: e.target.value })} required placeholder="https://github.com/org/repo.git" /></label>
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: '0 1.5rem' }}>
          <label title={tip.defaultBranch}>{t('Default Branch')}<input title={tip.defaultBranch} value={form.defaultBranch} onChange={e => setForm({ ...form, defaultBranch: e.target.value })} /></label>
          <label title={tip.localPath}>{t('Local Path')}<input title={tip.localPath} value={form.localPath} onChange={e => setForm({ ...form, localPath: e.target.value })} /></label>
          <label title={tip.workingDirectory}>{t('Working Directory')}<input title={tip.workingDirectory} value={form.workingDirectory} onChange={e => setForm({ ...form, workingDirectory: e.target.value })} /></label>
        </div>

        <label title={tip.token}>
          {t('GitHub Token Override')}
          <input
            title={tip.token}
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
          <label style={{ display: 'flex', alignItems: 'center', gap: 8 }} title={tip.clearToken}>
            <input
              title={tip.clearToken}
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
          <label title={tip.landingMode}>{t('Landing Mode')}
            <select title={tip.landingMode} value={form.landingMode} onChange={e => setForm({ ...form, landingMode: e.target.value })}>
              {landingModes.map(m => (
                <option key={m.value || 'default'} value={m.value}>{m.label}</option>
              ))}
            </select>
            <small className="text-dim" style={{ display: 'block', marginTop: '0.25rem', fontWeight: 'normal' }}>
              {findLandingMode(landingModes, form.landingMode).description}
            </small>
          </label>
          <label title={tip.branchCleanup}>{t('Branch Cleanup')}
            <select title={tip.branchCleanup} value={form.branchCleanupPolicy} onChange={e => setForm({ ...form, branchCleanupPolicy: e.target.value })}>
              <option value="">{t('Default')}</option>
              <option value="LocalOnly">{t('Local Only')}</option>
              <option value="LocalAndRemote">{t('Local and Remote')}</option>
              <option value="None">{t('None')}</option>
            </select>
          </label>
          <label title={tip.autoApprove}>{t('Agent Auto-Approve')}
            <select title={tip.autoApprove} value={form.autoApproveMode} onChange={e => setForm({ ...form, autoApproveMode: e.target.value })}>
              <option value="inherit">{t('Use captain setting')}</option>
              <option value="off">{t('Off for this vessel')}</option>
              <option value="on">{t('On for this vessel')}</option>
            </select>
          </label>
          <label title={tip.pipeline}>{t('Default Pipeline')}
            <select title={tip.pipeline} value={form.defaultPipelineId} onChange={e => setForm({ ...form, defaultPipelineId: e.target.value })}>
              <option value="">{t('None (WorkerOnly)')}</option>
              {pipelines.map(p => (
                <option key={p.id} value={p.id}>{p.name} ({(p.stages || []).map(s => s.personaName).join(' -> ')})</option>
              ))}
            </select>
          </label>
        </div>

        <div style={{ display: 'flex', flexWrap: 'wrap', gap: '0.75rem 2rem', marginBottom: '0.5rem' }}>
          <label style={checkboxLabelStyle} title={tip.concurrent}>
            <input title={tip.concurrent} type="checkbox" checked={form.allowConcurrentMissions} onChange={e => setForm({ ...form, allowConcurrentMissions: e.target.checked })} style={checkboxStyle} />
            <span style={{ verticalAlign: 'middle' }}>{t('Allow Concurrent Missions')}</span>
          </label>
          <label style={checkboxLabelStyle} title={tip.serialize}>
            <input title={tip.serialize} type="checkbox" checked={form.serializeOverlappingMissions} onChange={e => setForm({ ...form, serializeOverlappingMissions: e.target.checked })} style={checkboxStyle} aria-label={t('Serialize Overlapping Missions')} />
            <span style={{ verticalAlign: 'middle' }}>{t('Serialize Overlapping Missions')}</span>
          </label>
          <label style={checkboxLabelStyle} title={tip.modelContext}>
            <input title={tip.modelContext} type="checkbox" checked={form.enableModelContext} onChange={e => setForm({ ...form, enableModelContext: e.target.checked })} style={checkboxStyle} />
            <span style={{ verticalAlign: 'middle' }}>{t('Enable Model Context')}</span>
          </label>
          <label style={checkboxLabelStyle} title={tip.secretScan}>
            <input title={tip.secretScan} type="checkbox" checked={form.secretScanEnabled} onChange={e => setForm({ ...form, secretScanEnabled: e.target.checked })} style={checkboxStyle} />
            <span style={{ verticalAlign: 'middle' }}>{t('Scan Mission Diffs for Secrets')}</span>
          </label>
        </div>

        {/* Branch policy */}
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: '0 1.5rem' }}>
          <label title={tip.releasePrefix}>{t('Release Branch Prefix')}<input title={tip.releasePrefix} value={form.releaseBranchPrefix} onChange={e => setForm({ ...form, releaseBranchPrefix: e.target.value })} /></label>
          <label title={tip.hotfixPrefix}>{t('Hotfix Branch Prefix')}<input title={tip.hotfixPrefix} value={form.hotfixBranchPrefix} onChange={e => setForm({ ...form, hotfixBranchPrefix: e.target.value })} /></label>
          <label style={columnLabelStyle} title={tip.protectedBranches}>
            {t('Protected Branch Patterns')}
            <textarea title={tip.protectedBranches} value={form.protectedBranchPatterns} onChange={e => setForm({ ...form, protectedBranchPatterns: e.target.value })} rows={2} placeholder={t('One pattern per line, e.g. main or release/*')} style={{ resize: 'vertical' }} />
          </label>
        </div>
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: '0.75rem 2rem', marginBottom: '0.5rem' }}>
          <label style={checkboxLabelStyle} title={tip.requireChecks}>
            <input title={tip.requireChecks} type="checkbox" checked={form.requirePassingChecksToLand} onChange={e => setForm({ ...form, requirePassingChecksToLand: e.target.checked })} style={checkboxStyle} />
            <span style={{ verticalAlign: 'middle' }}>{t('Require Passing Checks To Land')}</span>
          </label>
          <label style={checkboxLabelStyle} title={tip.requirePr}>
            <input title={tip.requirePr} type="checkbox" checked={form.requirePullRequestForProtectedBranches} onChange={e => setForm({ ...form, requirePullRequestForProtectedBranches: e.target.checked })} style={checkboxStyle} />
            <span style={{ verticalAlign: 'middle' }}>{t('Require PR For Protected Branches')}</span>
          </label>
          <label style={checkboxLabelStyle} title={tip.requireMq}>
            <input title={tip.requireMq} type="checkbox" checked={form.requireMergeQueueForReleaseBranches} onChange={e => setForm({ ...form, requireMergeQueueForReleaseBranches: e.target.checked })} style={checkboxStyle} />
            <span style={{ verticalAlign: 'middle' }}>{t('Require Merge Queue For Release Branches')}</span>
          </label>
        </div>

        {/* Dock boundary path/identifier rules */}
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(260px, 1fr))', gap: '0 1.5rem', marginBottom: '0.5rem' }}>
          <label style={columnLabelStyle} title={tip.protectedPaths}>
            {t('Protected Path Patterns')}
            <textarea title={tip.protectedPaths} value={form.protectedPathPatterns} onChange={e => setForm({ ...form, protectedPathPatterns: e.target.value })} rows={2} placeholder={t('One glob per line, e.g. .env* or infra/**')} style={{ resize: 'vertical' }} />
          </label>
          <label style={columnLabelStyle} title={tip.denylist}>
            {t('Private Identifier Denylist')}
            <textarea title={tip.denylist} value={form.privateIdentifierDenylist} onChange={e => setForm({ ...form, privateIdentifierDenylist: e.target.value })} rows={2} placeholder={t('One value per line; do not list real secrets')} style={{ resize: 'vertical' }} />
          </label>
        </div>

        {/* Auto-land rules */}
        <div style={{ marginBottom: '0.5rem' }}>
          <label style={{ ...checkboxLabelStyle, marginBottom: '0.5rem' }} title={tip.autoLand}>
            <input title={tip.autoLand} type="checkbox" checked={form.autoLandEnabled} onChange={e => setForm({ ...form, autoLandEnabled: e.target.checked })} style={checkboxStyle} />
            <span style={{ verticalAlign: 'middle' }}>{t('Auto-land small changes')}</span>
          </label>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(260px, 1fr))', gap: '0 1.5rem' }}>
            <label style={columnLabelStyle} title={tip.maxFiles}>
              {t('Max Files (0 = no limit)')}
              <input title={tip.maxFiles} type="number" min={0} value={form.autoLandMaxFiles} onChange={e => setForm({ ...form, autoLandMaxFiles: e.target.value })} placeholder="0" />
            </label>
            <label style={columnLabelStyle} title={tip.maxLines}>
              {t('Max Lines (0 = no limit)')}
              <input title={tip.maxLines} type="number" min={0} value={form.autoLandMaxLines} onChange={e => setForm({ ...form, autoLandMaxLines: e.target.value })} placeholder="0" />
            </label>
            <label style={columnLabelStyle} title={tip.allowPaths}>
              {t('Auto-land Allowed Paths')}
              <textarea title={tip.allowPaths} value={form.autoLandPathAllowGlobs} onChange={e => setForm({ ...form, autoLandPathAllowGlobs: e.target.value })} rows={2} placeholder={t('One glob per line, e.g. src/**')} style={{ resize: 'vertical' }} />
            </label>
            <label style={columnLabelStyle} title={tip.denyPaths}>
              {t('Auto-land Denied Paths')}
              <textarea title={tip.denyPaths} value={form.autoLandPathDenyGlobs} onChange={e => setForm({ ...form, autoLandPathDenyGlobs: e.target.value })} rows={2} placeholder={t('One glob per line, e.g. infra/**')} style={{ resize: 'vertical' }} />
            </label>
          </div>
        </div>

        {/* In-dock Definition-of-Done gate */}
        <div style={{ marginBottom: '0.5rem' }}>
          <label style={{ ...checkboxLabelStyle, marginBottom: '0.5rem' }} title={tip.dod}>
            <input title={tip.dod} type="checkbox" checked={form.definitionOfDoneEnabled} onChange={e => setForm({ ...form, definitionOfDoneEnabled: e.target.checked })} style={checkboxStyle} />
            <span style={{ verticalAlign: 'middle' }}>{t('Run in-dock build + tests before acceptance')}</span>
          </label>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: '0 1.5rem' }}>
            <label style={columnLabelStyle} title={tip.buildCmd}>
              {t('Build Command')}
              <input title={tip.buildCmd} value={form.definitionOfDoneBuildCommand} onChange={e => setForm({ ...form, definitionOfDoneBuildCommand: e.target.value })} placeholder={t('e.g. dotnet build')} />
            </label>
            <label style={columnLabelStyle} title={tip.testCmd}>
              {t('Test Command')}
              <input title={tip.testCmd} value={form.definitionOfDoneTestCommand} onChange={e => setForm({ ...form, definitionOfDoneTestCommand: e.target.value })} placeholder={t('e.g. dotnet test')} />
            </label>
            <label style={columnLabelStyle} title={tip.timeout}>
              {t('Per-phase Timeout (seconds)')}
              <input title={tip.timeout} type="number" min={30} max={7200} value={form.definitionOfDoneTimeoutSeconds} onChange={e => setForm({ ...form, definitionOfDoneTimeoutSeconds: e.target.value })} placeholder="1800" />
            </label>
          </div>
        </div>

        {/* Fleet action command overrides (Test, Lint, Audit intents) */}
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: '0 1.5rem', marginBottom: '0.5rem' }}>
          <label style={columnLabelStyle} title={tip.testOverride}>
            {t('Test Command Override')}
            <input title={tip.testOverride} value={form.testCommand} onChange={e => setForm({ ...form, testCommand: e.target.value })} placeholder={t('e.g. make test')} />
          </label>
          <label style={columnLabelStyle} title={tip.lintOverride}>
            {t('Lint Command Override')}
            <input title={tip.lintOverride} value={form.lintCommand} onChange={e => setForm({ ...form, lintCommand: e.target.value })} placeholder={t('e.g. npm run lint')} />
          </label>
          <label style={columnLabelStyle} title={tip.auditOverride}>
            {t('Audit Command Override')}
            <input title={tip.auditOverride} value={form.auditCommand} onChange={e => setForm({ ...form, auditCommand: e.target.value })} placeholder={t('e.g. osv-scanner scan source -r .')} />
          </label>
        </div>

        {/* Worker eligibility envelope (vessel default; missions may only tighten it) */}
        <fieldset title={tip.workerEnvelope} style={{ border: 'none', padding: 0, margin: '0 0 0.5rem 0' }}>
          <legend title={tip.workerEnvelope} style={{ fontWeight: 600, padding: 0, marginBottom: '0.25rem' }}>{t('Worker eligibility envelope')}</legend>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))', gap: '0 1.5rem' }}>
            <label style={columnLabelStyle} title={tip.workerEnvelope}>
              {t('Max files')}
              <input title={tip.workerEnvelope} type="number" min={1} max={1000} value={form.envelopeMaxFiles} onChange={e => setForm({ ...form, envelopeMaxFiles: e.target.value })} />
            </label>
            <label style={columnLabelStyle} title={tip.workerEnvelope}>
              {t('Max steps')}
              <input title={tip.workerEnvelope} type="number" min={1} max={1000} value={form.envelopeMaxSteps} onChange={e => setForm({ ...form, envelopeMaxSteps: e.target.value })} />
            </label>
            <label style={columnLabelStyle} title={tip.workerEnvelope}>
              {t('Min served context (tokens)')}
              <input title={tip.workerEnvelope} type="number" min={1} max={100000000} value={form.envelopeMinServedContextTokens} onChange={e => setForm({ ...form, envelopeMinServedContextTokens: e.target.value })} />
            </label>
          </div>
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: '0.75rem 2rem' }}>
            <span style={{ verticalAlign: 'middle' }}>{t('Required tools')}</span>
            <label style={checkboxLabelStyle} title={tip.workerEnvelope}>
              <input title={tip.workerEnvelope} type="checkbox" checked={form.envelopeRequireTools} onChange={e => setForm({ ...form, envelopeRequireTools: e.target.checked })} style={checkboxStyle} />
              <span style={{ verticalAlign: 'middle' }}>{t('Tool calls')}</span>
            </label>
            <label style={checkboxLabelStyle} title={tip.workerEnvelope}>
              <input title={tip.workerEnvelope} type="checkbox" checked={form.envelopeRequireMcp} onChange={e => setForm({ ...form, envelopeRequireMcp: e.target.checked })} style={checkboxStyle} />
              <span style={{ verticalAlign: 'middle' }}>{t('MCP tools')}</span>
            </label>
          </div>
        </fieldset>

        {/* Context */}
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(260px, 1fr))', gap: '0 1.5rem' }}>
          <label style={columnLabelStyle} title={tip.projectContext}>
            {t('Project Context')}
            <textarea title={tip.projectContext} value={form.projectContext} onChange={e => setForm({ ...form, projectContext: e.target.value })} style={{ minHeight: '150px', resize: 'vertical' }} />
          </label>
          <label style={columnLabelStyle} title={tip.styleGuide}>
            {t('Style Guide')}
            <textarea title={tip.styleGuide} value={form.styleGuide} onChange={e => setForm({ ...form, styleGuide: e.target.value })} style={{ minHeight: '150px', resize: 'vertical' }} />
          </label>
          <label style={columnLabelStyle} title={tip.modelContextText}>
            {t('Model Context')}
            <textarea title={tip.modelContextText} value={form.modelContext} onChange={e => setForm({ ...form, modelContext: e.target.value })} placeholder={form.enableModelContext ? t('Agent-accumulated context...') : t('Enable Model Context to use')} disabled={!form.enableModelContext} style={{ minHeight: '150px', resize: 'vertical', ...(form.enableModelContext ? {} : { opacity: 0.4 }) }} />
          </label>
        </div>

        <div className="modal-actions">
          <button type="submit" className="btn btn-primary" disabled={saving} title={tip.save}>{t('Save')}</button>
          <button type="button" className="btn" onClick={onClose} title={tip.cancel}>{t('Cancel')}</button>
        </div>
      </form>
    </div>
  );
}
