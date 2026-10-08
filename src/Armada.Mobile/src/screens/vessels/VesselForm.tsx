import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { createVessel, updateVessel } from '@dashboard/api/client';
import type { Fleet, Pipeline, Vessel } from '@dashboard/types/models';
import {
  buildVesselPayload,
  emptyVesselForm,
  findLandingMode,
  getLandingModes,
  vesselToForm,
  type VesselFormState,
} from '@dashboard/lib/vesselForm';
import { SwitchField } from '../../build/fields';
import { AppText, Button, TextField } from '../../components/ui';
import { SelectField, type SelectOption } from '../../components/ui/SelectSheet';
import { useLocale } from '../../i18n/LocaleContext';
import { spacing } from '../../theme/typography';

/**
 * The one Create / Edit vessel form (the dashboard's VesselFormModal), used by the Vessels tab and the vessel page:
 * identity, GitHub token override, landing (mode incl. unset = global default, cleanup, auto-approve, pipeline),
 * branch policy, dock boundary, auto-land, the Definition-of-Done gate, and context. The payload is built by the
 * shared lib/vesselForm, so both clients send exactly the same request. Shown in the vessel actions sheet.
 */
export interface VesselFormProps {
  /** The vessel being edited, or null to create one. */
  vessel: Vessel | null;
  fleets: Fleet[];
  pipelines: Pipeline[];
  onClose: () => void;
  /** After a successful save, with the saved name and whether it was a create. */
  onSaved: (name: string, created: boolean) => void;
  /** A failed save (the dashboard shows "Save failed."). */
  onError: (message: string) => void;
}

export function VesselForm({ vessel, fleets, pipelines, onClose, onSaved, onError }: VesselFormProps) {
  const { t } = useLocale();
  const [form, setForm] = useState<VesselFormState>(() => (vessel ? vesselToForm(vessel) : { ...emptyVesselForm }));
  const [saving, setSaving] = useState(false);
  const landingModes = getLandingModes(t);
  const set = <K extends keyof VesselFormState>(key: K, value: VesselFormState[K]) => setForm((f) => ({ ...f, [key]: value }));
  const canSave = form.name.trim().length > 0 && form.repoUrl.trim().length > 0 && !saving;

  async function save() {
    if (!canSave) return;
    setSaving(true);
    try {
      const payload = buildVesselPayload(form, vessel);
      if (vessel) await updateVessel(vessel.id, payload as Partial<Vessel>);
      else await createVessel(payload as Partial<Vessel>);
      onSaved(form.name, !vessel);
    } catch {
      onError(t('Save failed.'));
    } finally {
      setSaving(false);
    }
  }

  const fleetOptions: SelectOption<string>[] = [{ value: '', label: t('Select a fleet...') }, ...fleets.map((f) => ({ value: f.id, label: f.name }))];
  const landingOptions: SelectOption<string>[] = landingModes.map((m) => ({ value: m.value, label: m.label, description: m.description }));
  const cleanupOptions: SelectOption<string>[] = [
    { value: '', label: t('Default') },
    { value: 'LocalOnly', label: t('Local Only') },
    { value: 'LocalAndRemote', label: t('Local and Remote') },
    { value: 'None', label: t('None') },
  ];
  const autoApproveOptions: SelectOption<string>[] = [
    { value: 'inherit', label: t('Use captain setting') },
    { value: 'off', label: t('Off for this vessel') },
    { value: 'on', label: t('On for this vessel') },
  ];
  const pipelineOptions: SelectOption<string>[] = [
    { value: '', label: t('None (WorkerOnly)') },
    ...pipelines.map((p) => ({ value: p.id, label: `${p.name} (${(p.stages || []).map((s) => s.personaName).join(' -> ')})` })),
  ];

  const tokenHint = vessel
    ? vessel.hasGitHubTokenOverride
      ? t('This vessel already has an override. Leave blank to keep it, enter a new token to replace it, or clear it below.')
      : t('No vessel override is stored. Armada will use the global GitHub token if one is configured.')
    : t('Optional. Leave blank to use the global GitHub token from Armada settings.');

  return (
    <View>
      <TextField testID="vessel-form-name" label={t('Name')} value={form.name} onChangeText={(v) => set('name', v)} autoCapitalize="none" autoCorrect={false} />
      <SelectField testID="vessel-form-fleet" label={t('Fleet')} value={form.fleetId} options={fleetOptions} onChange={(v) => set('fleetId', v)} closeLabel={t('Close')} />
      <TextField testID="vessel-form-repo-url" label={t('Repository URL')} value={form.repoUrl} onChangeText={(v) => set('repoUrl', v)} placeholder="https://github.com/org/repo.git" autoCapitalize="none" autoCorrect={false} keyboardType="url" />
      <TextField testID="vessel-form-default-branch" label={t('Default Branch')} value={form.defaultBranch} onChangeText={(v) => set('defaultBranch', v)} autoCapitalize="none" autoCorrect={false} />
      <TextField testID="vessel-form-local-path" label={t('Local Path')} value={form.localPath} onChangeText={(v) => set('localPath', v)} autoCapitalize="none" autoCorrect={false} />
      <TextField testID="vessel-form-working-directory" label={t('Working Directory')} value={form.workingDirectory} onChangeText={(v) => set('workingDirectory', v)} autoCapitalize="none" autoCorrect={false} />
      <TextField
        testID="vessel-form-github-token"
        label={t('GitHub Token Override')}
        value={form.gitHubTokenOverride}
        onChangeText={(v) => setForm((f) => ({ ...f, gitHubTokenOverride: v, clearGitHubTokenOverride: false }))}
        placeholder={vessel && vessel.hasGitHubTokenOverride ? t('Leave blank to keep existing override') : t('Optional per-vessel GitHub token')}
        secret
        revealLabel={t('Show')}
        hideLabel={t('Hide')}
        hint={tokenHint}
      />
      {vessel && vessel.hasGitHubTokenOverride ? (
        <SwitchField
          testID="vessel-form-clear-token"
          label={t('Clear existing GitHub token override')}
          value={form.clearGitHubTokenOverride}
          onChange={(v) => setForm((f) => ({ ...f, clearGitHubTokenOverride: v, gitHubTokenOverride: v ? '' : f.gitHubTokenOverride }))}
        />
      ) : null}

      <Heading text={t('Landing')} />
      <SelectField
        testID="vessel-form-landing-mode"
        label={t('Landing Mode')}
        value={form.landingMode}
        options={landingOptions}
        onChange={(v) => set('landingMode', v)}
        closeLabel={t('Close')}
        hint={findLandingMode(landingModes, form.landingMode).description}
      />
      <SelectField testID="vessel-form-branch-cleanup" label={t('Branch Cleanup')} value={form.branchCleanupPolicy} options={cleanupOptions} onChange={(v) => set('branchCleanupPolicy', v)} closeLabel={t('Close')} hint={t('When and how mission branches are deleted after successful landing.')} />
      <SelectField testID="vessel-form-auto-approve" label={t('Agent Auto-Approve')} value={form.autoApproveMode} options={autoApproveOptions} onChange={(v) => set('autoApproveMode', v)} closeLabel={t('Close')} hint={t('Whether CLI captains run missions on this vessel with their auto-approve (permission bypass) flags. Overrides the captain setting when set.')} />
      <SelectField testID="vessel-form-pipeline" label={t('Default Pipeline')} value={form.defaultPipelineId} options={pipelineOptions} onChange={(v) => set('defaultPipelineId', v)} closeLabel={t('Close')} />
      <SwitchField testID="vessel-form-concurrent" label={t('Allow Concurrent Missions')} value={form.allowConcurrentMissions} onChange={(v) => set('allowConcurrentMissions', v)} hint={t('When enabled, multiple missions can run on this vessel at the same time.')} />
      <SwitchField testID="vessel-form-model-context-enabled" label={t('Enable Model Context')} value={form.enableModelContext} onChange={(v) => set('enableModelContext', v)} hint={t('When enabled, AI agents accumulate key knowledge about this repository during missions.')} />
      <SwitchField testID="vessel-form-secret-scan" label={t('Scan Mission Diffs for Secrets')} value={form.secretScanEnabled} onChange={(v) => set('secretScanEnabled', v)} hint={t('Scan each mission diff for secrets before landing, and flag protected paths / private identifiers.')} />

      <Heading text={t('Branch policy')} />
      <TextField testID="vessel-form-release-prefix" label={t('Release Branch Prefix')} value={form.releaseBranchPrefix} onChangeText={(v) => set('releaseBranchPrefix', v)} autoCapitalize="none" autoCorrect={false} />
      <TextField testID="vessel-form-hotfix-prefix" label={t('Hotfix Branch Prefix')} value={form.hotfixBranchPrefix} onChangeText={(v) => set('hotfixBranchPrefix', v)} autoCapitalize="none" autoCorrect={false} />
      <Multiline testID="vessel-form-protected-branches" label={t('Protected Branch Patterns')} value={form.protectedBranchPatterns} onChange={(v) => set('protectedBranchPatterns', v)} placeholder={t('One pattern per line, e.g. main or release/*')} />
      <SwitchField testID="vessel-form-require-checks" label={t('Require Passing Checks To Land')} value={form.requirePassingChecksToLand} onChange={(v) => set('requirePassingChecksToLand', v)} />
      <SwitchField testID="vessel-form-require-pr" label={t('Require PR For Protected Branches')} value={form.requirePullRequestForProtectedBranches} onChange={(v) => set('requirePullRequestForProtectedBranches', v)} />
      <SwitchField testID="vessel-form-require-merge-queue" label={t('Require Merge Queue For Release Branches')} value={form.requireMergeQueueForReleaseBranches} onChange={(v) => set('requireMergeQueueForReleaseBranches', v)} />

      <Heading text={t('Dock Boundary')} />
      <Multiline testID="vessel-form-protected-paths" label={t('Protected Path Patterns')} value={form.protectedPathPatterns} onChange={(v) => set('protectedPathPatterns', v)} placeholder={t('One glob per line, e.g. .env* or infra/**')} />
      <Multiline testID="vessel-form-private-identifiers" label={t('Private Identifier Denylist')} value={form.privateIdentifierDenylist} onChange={(v) => set('privateIdentifierDenylist', v)} placeholder={t('One value per line; do not list real secrets')} />

      <Heading text={t('Auto-land')} />
      <SwitchField testID="vessel-form-auto-land" label={t('Auto-land small changes')} value={form.autoLandEnabled} onChange={(v) => set('autoLandEnabled', v)} hint={t('When enabled, a passing mission must satisfy the rules below to land unattended; otherwise it holds for review.')} />
      <TextField testID="vessel-form-auto-land-max-files" label={t('Max Files (0 = no limit)')} value={form.autoLandMaxFiles} onChangeText={(v) => set('autoLandMaxFiles', v)} keyboardType="number-pad" placeholder="0" />
      <TextField testID="vessel-form-auto-land-max-lines" label={t('Max Lines (0 = no limit)')} value={form.autoLandMaxLines} onChangeText={(v) => set('autoLandMaxLines', v)} keyboardType="number-pad" placeholder="0" />
      <Multiline testID="vessel-form-auto-land-allow" label={t('Auto-land Allowed Paths')} value={form.autoLandPathAllowGlobs} onChange={(v) => set('autoLandPathAllowGlobs', v)} placeholder={t('One glob per line, e.g. src/**')} />
      <Multiline testID="vessel-form-auto-land-deny" label={t('Auto-land Denied Paths')} value={form.autoLandPathDenyGlobs} onChange={(v) => set('autoLandPathDenyGlobs', v)} placeholder={t('One glob per line, e.g. infra/**')} />

      <Heading text={t('Definition of Done')} />
      <SwitchField testID="vessel-form-dod" label={t('Run in-dock build + tests before acceptance')} value={form.definitionOfDoneEnabled} onChange={(v) => set('definitionOfDoneEnabled', v)} hint={t('When enabled, the build and unit-test commands below run inside the mission checkout before landing; a failure blocks acceptance.')} />
      <TextField testID="vessel-form-dod-build" label={t('Build Command')} value={form.definitionOfDoneBuildCommand} onChangeText={(v) => set('definitionOfDoneBuildCommand', v)} placeholder={t('e.g. dotnet build')} autoCapitalize="none" autoCorrect={false} />
      <TextField testID="vessel-form-dod-test" label={t('Test Command')} value={form.definitionOfDoneTestCommand} onChangeText={(v) => set('definitionOfDoneTestCommand', v)} placeholder={t('e.g. dotnet test')} autoCapitalize="none" autoCorrect={false} />
      <TextField testID="vessel-form-dod-timeout" label={t('Per-phase Timeout (seconds)')} value={form.definitionOfDoneTimeoutSeconds} onChangeText={(v) => set('definitionOfDoneTimeoutSeconds', v)} keyboardType="number-pad" placeholder="1800" />

      <Heading text={t('Context')} />
      <Multiline testID="vessel-form-project-context" label={t('Project Context')} value={form.projectContext} onChange={(v) => set('projectContext', v)} tall />
      <Multiline testID="vessel-form-style-guide" label={t('Style Guide')} value={form.styleGuide} onChange={(v) => set('styleGuide', v)} tall />
      <Multiline
        testID="vessel-form-model-context"
        label={t('Model Context')}
        value={form.modelContext}
        onChange={(v) => set('modelContext', v)}
        placeholder={form.enableModelContext ? t('Agent-accumulated context...') : t('Enable Model Context to use')}
        disabled={!form.enableModelContext}
        tall
      />

      <View style={styles.actions}>
        <Button label={t('Cancel')} variant="ghost" onPress={onClose} testID="vessel-form-cancel" />
        <Button label={t('Save')} onPress={() => void save()} busy={saving} disabled={!canSave} testID="vessel-form-save" />
      </View>
    </View>
  );
}

function Heading({ text }: { text: string }) {
  return <AppText variant="subheading" muted accessibilityRole="header" style={styles.heading}>{text}</AppText>;
}

function Multiline({ label, value, onChange, placeholder, disabled, tall, testID }: {
  label: string; value: string; onChange: (v: string) => void; placeholder?: string; disabled?: boolean; tall?: boolean; testID?: string;
}) {
  return (
    <TextField
      testID={testID}
      label={label}
      value={value}
      onChangeText={onChange}
      placeholder={placeholder}
      multiline
      editable={!disabled}
      autoCapitalize="none"
      autoCorrect={false}
      textAlignVertical="top"
      numberOfLines={tall ? 6 : 3}
    />
  );
}

const styles = StyleSheet.create({
  heading: { textTransform: 'uppercase', marginTop: spacing.sm, marginBottom: spacing.md },
  actions: { flexDirection: 'row', justifyContent: 'flex-end', gap: spacing.sm, marginTop: spacing.sm },
});
