import { useEffect, useMemo, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { getVesselReadiness, previewWorkflowProfileForVessel, runCheck } from '@dashboard/api/client';
import type { CheckRun, CheckRunRequest, CheckRunType, Vessel, VesselReadinessResult, WorkflowProfile, WorkflowProfileResolutionPreviewResult } from '@dashboard/types/models';
import { getAvailableCheckTypes, requiresEnvironment } from '@dashboard/lib/deliveryForms';
import { FieldCard } from '../../components/resource/DetailParts';
import { AppText } from '../../components/ui/AppText';
import { BottomSheet } from '../../components/ui/BottomSheet';
import { Button } from '../../components/ui/Button';
import { FormActions } from '../../components/ui/StickyFooter';
import { SelectField } from '../../components/ui/SelectSheet';
import { TextField } from '../../components/ui/TextField';
import { useLocale } from '../../i18n/LocaleContext';
import { recordOptions, valueOptions } from '../../resource/lookups';
import { errorText } from '../../resource/useLoad';
import { spacing } from '../../theme/typography';

export interface RunCheckSheetProps {
  open: boolean;
  prefill: Partial<CheckRunRequest> | null;
  vessels: Vessel[];
  profiles: WorkflowProfile[];
  onClose: () => void;
  /** The run finished (passed, failed, or still running); the caller toasts and opens it. */
  onRan: (run: CheckRun) => void;
}

/**
 * The dashboard's Run Check modal as a sheet. Each opening remounts the form (a new key), so it starts from the
 * prefill; closing keeps it mounted so the sheet animates out.
 */
export function RunCheckSheet(props: RunCheckSheetProps) {
  const [session, setSession] = useState(0);
  const [shownOpen, setShownOpen] = useState(props.open);
  if (props.open !== shownOpen) {
    setShownOpen(props.open);
    if (props.open) setSession((n) => n + 1);
  }
  return <RunCheckForm key={session} {...props} />;
}

function RunCheckForm({ open, prefill, vessels, profiles, onClose, onRan }: RunCheckSheetProps) {
  const { t } = useLocale();
  const p = prefill ?? {};
  const [vesselId, setVesselId] = useState(p.vesselId || '');
  const [workflowProfileId, setWorkflowProfileId] = useState(p.workflowProfileId || '');
  const [type, setType] = useState<CheckRunType>(p.type || 'Build');
  const [environmentName, setEnvironmentName] = useState(p.environmentName || '');
  const [label, setLabel] = useState(p.label || '');
  const [missionId, setMissionId] = useState(p.missionId || '');
  const [voyageId, setVoyageId] = useState(p.voyageId || '');
  const [deploymentId, setDeploymentId] = useState(p.deploymentId || '');
  const [branchName, setBranchName] = useState(p.branchName || '');
  const [commitHash, setCommitHash] = useState(p.commitHash || '');
  const [commandOverride, setCommandOverride] = useState(p.commandOverride || '');
  // Each async result is stored with the inputs it was loaded for; while the inputs differ it is still loading.
  const [previewState, setPreviewState] = useState<{ key: string; value: WorkflowProfileResolutionPreviewResult | null }>({ key: '', value: null });
  const [readinessState, setReadinessState] = useState<{ key: string; value: VesselReadinessResult | null }>({ key: '', value: null });
  const [running, setRunning] = useState(false);
  const [error, setError] = useState('');

  const previewKey = `${vesselId}|${workflowProfileId}`;
  useEffect(() => {
    if (!open || !vesselId) return undefined;
    let cancelled = false;
    previewWorkflowProfileForVessel(vesselId, workflowProfileId || undefined)
      .then((value) => { if (!cancelled) setPreviewState({ key: previewKey, value }); })
      .catch(() => { if (!cancelled) setPreviewState({ key: previewKey, value: null }); });
    return () => { cancelled = true; };
  }, [open, previewKey, vesselId, workflowProfileId]);

  const readinessKey = `${vesselId}|${workflowProfileId}|${type}|${environmentName}|${commandOverride.trim().length === 0}`;
  useEffect(() => {
    if (!open || !vesselId) return undefined;
    let cancelled = false;
    getVesselReadiness(vesselId, {
      workflowProfileId: workflowProfileId || null,
      checkType: type,
      environmentName: environmentName || null,
      includeWorkflowRequirements: commandOverride.trim().length === 0,
    })
      .then((value) => { if (!cancelled) setReadinessState({ key: readinessKey, value }); })
      .catch(() => { if (!cancelled) setReadinessState({ key: readinessKey, value: null }); });
    return () => { cancelled = true; };
  }, [open, readinessKey, commandOverride, environmentName, type, vesselId, workflowProfileId]);

  const resolving = !!vesselId && previewState.key !== previewKey;
  const loadingReadiness = !!vesselId && readinessState.key !== readinessKey;
  const preview = previewState.key === previewKey ? previewState.value : null;
  const readiness = readinessState.key === readinessKey ? readinessState.value : null;
  const activePreview = vesselId ? preview : null;
  const activeReadiness = vesselId ? readiness : null;
  const resolvedProfile = activePreview?.resolvedProfile || null;
  const availableTypes = useMemo(
    () => (activePreview?.availableCheckTypes?.length ? activePreview.availableCheckTypes as CheckRunType[] : getAvailableCheckTypes(resolvedProfile)),
    [activePreview, resolvedProfile],
  );
  const environmentOptions = resolvedProfile?.environments || [];
  // The dashboard snaps the type to an available one unless a command override is given, and fills the only environment.
  const effectiveType: CheckRunType = !commandOverride.trim() && !availableTypes.includes(type) ? (availableTypes[0] || 'Build') : type;
  const needsEnvironment = requiresEnvironment(effectiveType);
  const effectiveEnvironment = !needsEnvironment ? '' : (environmentName || (environmentOptions.length === 1 ? environmentOptions[0].environmentName : ''));
  const runBlocked = (activeReadiness?.errorCount || 0) > 0;

  async function submit() {
    setError('');
    if (!vesselId) { setError(t('Select a vessel before running a check.')); return; }
    if (needsEnvironment && !effectiveEnvironment && !commandOverride.trim()) { setError(t('Select an environment for this check type.')); return; }
    setRunning(true);
    try {
      const run = await runCheck({
        vesselId,
        workflowProfileId: workflowProfileId || null,
        type: effectiveType,
        environmentName: effectiveEnvironment || null,
        label: label || null,
        missionId: missionId || null,
        voyageId: voyageId || null,
        deploymentId: deploymentId || null,
        branchName: branchName || null,
        commitHash: commitHash || null,
        commandOverride: commandOverride || null,
      });
      onRan(run);
    } catch (err: unknown) {
      setError(errorText(err, t('Check run failed.')));
    } finally {
      setRunning(false);
    }
  }

  const close = t('Close');
  const body = (
    <>
      <SelectField label={t('Vessel')} value={vesselId} options={recordOptions(vessels, t('Select a vessel...'))} onChange={setVesselId} closeLabel={close} testID="run-check-vessel" />
      <SelectField label={t('Workflow Profile')} value={workflowProfileId} options={recordOptions(profiles, t('Resolved default'))} onChange={setWorkflowProfileId} closeLabel={close} />
      <SelectField label={t('Check Type')} value={effectiveType} options={valueOptions(commandOverride.trim() ? Array.from(new Set([...availableTypes, type])) : availableTypes)} onChange={(v) => setType(v as CheckRunType)} closeLabel={close} testID="run-check-type" />
      {needsEnvironment ? (
        environmentOptions.length > 0 ? (
          <SelectField
            label={t('Environment')}
            value={effectiveEnvironment}
            options={[{ value: '', label: t('Select an environment...') }, ...environmentOptions.map((e) => ({ value: e.environmentName, label: e.environmentName }))]}
            onChange={setEnvironmentName}
            closeLabel={close}
          />
        ) : <TextField label={t('Environment')} value={environmentName} onChangeText={setEnvironmentName} autoCapitalize="none" />
      ) : null}
      <TextField label={t('Label')} value={label} onChangeText={setLabel} placeholder={t('Optional display label')} testID="run-check-label" />
      <TextField label={t('Mission ID')} value={missionId} onChangeText={setMissionId} autoCapitalize="none" />
      <TextField label={t('Voyage ID')} value={voyageId} onChangeText={setVoyageId} autoCapitalize="none" />
      <TextField label={t('Deployment ID')} value={deploymentId} onChangeText={setDeploymentId} autoCapitalize="none" />
      <TextField label={t('Branch')} value={branchName} onChangeText={setBranchName} autoCapitalize="none" />
      <TextField label={t('Commit')} value={commitHash} onChangeText={setCommitHash} autoCapitalize="none" />
      <TextField label={t('Command Override')} value={commandOverride} onChangeText={setCommandOverride} multiline autoCapitalize="none" placeholder={t('Optional ad-hoc command. Leave blank to use the selected or resolved workflow profile command.')} />

      <View style={styles.block}>
        <AppText variant="label">{t('Resolved Profile')}</AppText>
        <AppText variant="caption" muted>
          {resolving ? t('Resolving...') : resolvedProfile ? `${resolvedProfile.name} \u2022 ${t('Resolution mode')}: ${activePreview?.resolutionMode ?? '-'}` : t('No workflow profile resolved. Provide a command override or select a profile.')}
        </AppText>
        {!resolving && activePreview?.resolvedProfile ? (
          <>
            <AppText variant="caption" muted>{`${t('Available check types')}: ${activePreview.availableCheckTypes.length > 0 ? activePreview.availableCheckTypes.join(', ') : t('None')}`}</AppText>
            {activePreview.commandPreviews.length === 0 ? <AppText variant="caption" muted>{t('No resolved commands are available for this vessel/profile combination.')}</AppText> : activePreview.commandPreviews.map((c) => (
              <AppText key={`${c.checkType}:${c.environmentName ?? ''}`} variant="mono" selectable>{`${c.checkType}${c.environmentName ? ` (${c.environmentName})` : ''}: ${c.command}`}</AppText>
            ))}
          </>
        ) : null}
      </View>
      <FieldCard title={t('Preflight')} testID="run-check-preflight">
        <View style={styles.pad}>
          {!vesselId ? <AppText muted>{t('Select a vessel to evaluate readiness.')}</AppText> : loadingReadiness ? <AppText muted>{t('Loading...')}</AppText> : activeReadiness ? (
            <>
              <AppText>{`${activeReadiness.setupChecklistSatisfiedCount}/${activeReadiness.setupChecklistTotalCount} \u2022 ${t('Errors')}: ${activeReadiness.errorCount} \u2022 ${t('Warnings')}: ${activeReadiness.warningCount}`}</AppText>
              {activeReadiness.issues.map((issue, index) => (
                <AppText key={`${issue.code}:${index}`} variant="caption" color={issue.severity === 'Error' ? 'danger' : issue.severity === 'Warning' ? 'warning' : undefined}>{`${issue.title}: ${issue.message}`}</AppText>
              ))}
            </>
          ) : <AppText muted>-</AppText>}
        </View>
      </FieldCard>
      {error ? <AppText color="danger" accessibilityRole="alert" testID="run-check-error">{error}</AppText> : null}
    </>
  );
  return (
    <BottomSheet
      open={open}
      title={t('Run Check')}
      onClose={onClose}
      closeLabel={t('Close')}
      testID="run-check"
      // Run Check stays below the long form (preview, preflight), reachable without scrolling.
      footer={open ? (
        <FormActions>
          <Button label={t('Cancel')} variant="ghost" onPress={onClose} disabled={running} />
          <Button label={running ? t('Running...') : t('Run Check')} onPress={() => void submit()} busy={running} disabled={runBlocked} testID="run-check-submit" />
        </FormActions>
      ) : undefined}
    >
      {open ? body : null}
    </BottomSheet>
  );
}

const styles = StyleSheet.create({
  block: { gap: spacing.xs, marginBottom: spacing.lg },
  pad: { padding: spacing.md, gap: spacing.xs },
});
