import { useRouter, type Href } from 'expo-router';
import { useEffect, useMemo, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { enumerateFleetActions, getVessel, listPipelines, runAdHocFleetAction, runFleetAction } from '@dashboard/api/client';
import type { FleetAction, FleetActionKind, FleetActionRunStartResult, FleetActionUpsertRequest, Pipeline, Vessel } from '@dashboard/types/models';
import { KIND_DESCRIPTIONS, KIND_LABELS } from '@dashboard/lib/fleetActionLabels';
import { findUnknownTemplateVariables, renderTemplatePreview } from '@dashboard/lib/fleetActionTemplate';
import {
  adHocFromDefinition,
  buildAdHocDefinition,
  buildSavedRunRequest,
  emptyAdHoc,
  validateRunInputs,
  type AdHocRunForm,
  type RunMode,
} from '@dashboard/lib/fleetActionForm';
import { CodeBlock } from '../../components/ask/CodeBlock';
import { InfoRow, SwitchField } from '../../build/fields';
import { errorMessage } from '../../build/useLiveResource';
import { AppText, Banner, BottomSheet, Button, SegmentedControl, TextField } from '../../components/ui';
import { SelectField, type SelectOption } from '../../components/ui/SelectSheet';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { spacing } from '../../theme/typography';
import { KindBadge, TemplateVariableHelp } from './common';

export interface RunActionSheetProps {
  open: boolean;
  /** Vessels to run against (1-500); the first one is used for the preview. */
  vesselIds: string[];
  onClose: () => void;
  /** Preselect a saved action. */
  initialActionId?: string | null;
  /** Preferred kind: preselects the first saved action of that kind and the ad hoc kind. */
  initialKind?: FleetActionKind;
  /** Open in ad hoc mode prefilled with this definition (re-running an ad hoc run). */
  initialDefinition?: FleetActionUpsertRequest | null;
  /** Called after the server accepted the run; without it the sheet opens the run detail. */
  onStarted?: (result: FleetActionRunStartResult) => void;
}

/**
 * Run a fleet action on chosen vessels (the dashboard's RunActionModal): a saved action or an ad hoc definition,
 * the rendered preview for the first vessel, concurrency, then a confirmation step that spells out what runs where
 * before the run starts.
 */
export function RunActionSheet(props: RunActionSheetProps) {
  const { t } = useLocale();
  return (
    <BottomSheet open={props.open} title={t('Run fleet action')} onClose={props.onClose} closeLabel={t('Close')} testID="run-action">
      {props.open ? <RunActionContent {...props} /> : null}
    </BottomSheet>
  );
}

/** The run form itself (inside RunActionSheet or RunFlowSheet). */
export function RunActionContent({ vesselIds, onClose, initialActionId, initialKind, initialDefinition, onStarted }: RunActionSheetProps) {
  const { t } = useLocale();
  const { pushToast } = useNotifications();
  const router = useRouter();
  const [mode, setMode] = useState<RunMode>(initialDefinition ? 'adhoc' : 'saved');
  const [step, setStep] = useState<'configure' | 'confirm'>('configure');
  const [actions, setActions] = useState<FleetAction[]>([]);
  const [actionsLoading, setActionsLoading] = useState(true);
  const [actionsError, setActionsError] = useState<string | null>(null);
  const [selectedActionId, setSelectedActionId] = useState(initialActionId ?? '');
  const [pipelines, setPipelines] = useState<Pipeline[]>([]);
  const [previewVessel, setPreviewVessel] = useState<Vessel | null>(null);
  const [concurrency, setConcurrency] = useState('4');
  const [cleanTreeOverride, setCleanTreeOverride] = useState<boolean | null>(null);
  const [adHoc, setAdHoc] = useState<AdHocRunForm>(() => (initialDefinition ? adHocFromDefinition(initialDefinition) : emptyAdHoc(initialKind ?? 'Command')));
  const [submitted, setSubmitted] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    enumerateFleetActions({ pageNumber: 1, pageSize: 500 })
      .then((result) => {
        if (cancelled) return;
        const list = result?.objects ?? [];
        setActions(list);
        setSelectedActionId((current) => {
          if (current && list.some((a) => a.id === current)) return current;
          const preferred = initialKind ? list.find((a) => a.kind === initialKind) : undefined;
          return (preferred ?? list[0])?.id ?? '';
        });
      })
      .catch((e: unknown) => { if (!cancelled) setActionsError(errorMessage(e) || t('Failed to load fleet actions.')); })
      .finally(() => { if (!cancelled) setActionsLoading(false); });
    listPipelines({ pageSize: 9999 }).then((r) => { if (!cancelled) setPipelines(r?.objects ?? []); }).catch(() => undefined);
    return () => { cancelled = true; };
  }, [initialKind, t]);

  useEffect(() => {
    if (vesselIds.length === 0) return undefined;
    let cancelled = false;
    getVessel(vesselIds[0]).then((v) => { if (!cancelled) setPreviewVessel(v ?? null); }).catch(() => { if (!cancelled) setPreviewVessel(null); });
    return () => { cancelled = true; };
  }, [vesselIds]);

  const selectedAction = useMemo(() => actions.find((a) => a.id === selectedActionId) ?? null, [actions, selectedActionId]);
  // Default concurrency follows the chosen action; the clean-tree override resets with it.
  const [concurrencyFor, setConcurrencyFor] = useState<string | null>(null);
  if (mode === 'saved' && selectedAction && concurrencyFor !== selectedAction.id) {
    setConcurrencyFor(selectedAction.id);
    setConcurrency(String(Math.min(32, Math.max(1, selectedAction.defaultConcurrency || 4))));
    setCleanTreeOverride(null);
  }

  const effectiveKind: FleetActionKind = mode === 'saved' ? (selectedAction?.kind ?? 'Command') : adHoc.kind;
  const bodyText = mode === 'saved'
    ? (selectedAction ? (selectedAction.kind === 'Command' ? selectedAction.commandText : selectedAction.promptTemplate) ?? '' : '')
    : (adHoc.kind === 'Command' ? adHoc.commandText : adHoc.promptTemplate);
  const effectiveCleanTree = mode === 'saved' ? (cleanTreeOverride ?? selectedAction?.requiresCleanWorkingTree ?? true) : adHoc.requiresCleanWorkingTree;
  const errors = validateRunInputs({ vesselCount: vesselIds.length, mode, action: selectedAction, concurrency, adHoc });
  const unknownVars = findUnknownTemplateVariables(bodyText);
  const showError = (field: string) => (submitted && errors[field] && errors[field] !== 'unknown-variables' ? t(errors[field]) : null);
  const preview = useMemo(() => (previewVessel && bodyText
    ? renderTemplatePreview(bodyText, previewVessel, t('[health summary is rendered on the server for each vessel]'))
    : null), [previewVessel, bodyText, t]);
  const count = vesselIds.length;
  const concurrencyNumber = parseInt(concurrency, 10) || 1;

  function review() {
    setSubmitted(true);
    if (Object.keys(errors).length > 0) return;
    setServerError(null);
    setStep('confirm');
  }

  async function start() {
    setSubmitting(true);
    setServerError(null);
    const c = parseInt(concurrency, 10);
    try {
      const result = mode === 'saved' && selectedAction
        ? await runFleetAction(selectedAction.id, buildSavedRunRequest(vesselIds, c, selectedAction, cleanTreeOverride))
        : await runAdHocFleetAction({ VesselIds: vesselIds, Concurrency: c, Definition: buildAdHocDefinition(adHoc) });
      pushToast('success', t('{count, plural, one {Fleet action started on # vessel.} other {Fleet action started on # vessels.}}', { count: result.targetCount }));
      onClose();
      if (onStarted) onStarted(result);
      else router.push(`/fleet-actions/runs/${result.runId}` as Href);
    } catch (e) {
      setServerError(errorMessage(e) || t('Failed to start the run.'));
      setStep('configure');
    } finally {
      setSubmitting(false);
    }
  }

  const actionOptions: SelectOption<string>[] = actionsLoading
    ? [{ value: '', label: t('Loading...') }]
    : actions.length === 0 ? [{ value: '', label: t('No saved actions') }]
      : actions.map((a) => ({ value: a.id, label: `${a.name} (${t(KIND_LABELS[a.kind])})`, description: a.description ?? undefined }));
  const pipelineOptions: SelectOption<string>[] = [{ value: '', label: t('Vessel default') }, ...pipelines.map((p) => ({ value: p.id, label: p.name }))];

  if (step === 'confirm') {
    return (
      <View testID="run-action-confirm">
        <AppText variant="heading">{mode === 'saved' ? selectedAction?.name : adHoc.name}</AppText>
        {effectiveKind === 'Command' ? (
          <Banner
            tone="warning"
            title={t('{count, plural, one {This runs the command below in the working directory of # vessel, on the Admiral host or the vessel\'s preferred Harbor.} other {This runs the command below in the working directory of each of # vessels, on the Admiral host or each vessel\'s preferred Harbor.}}', { count })}
            message={effectiveCleanTree ? t('Vessels with uncommitted changes are skipped.') : t('The clean-tree check is off: the command also runs in vessels with uncommitted changes.')}
          />
        ) : (
          <Banner
            tone="info"
            title={t('{count, plural, one {This dispatches # voyage, one per vessel.} other {This dispatches # voyages, one per vessel.}}', { count })}
            message={t('{count, plural, one {At most # voyage from this run is active at a time.} other {At most # voyages from this run are active at a time.}}', { count: concurrencyNumber })}
          />
        )}
        <CodeBlock text={bodyText} />
        <View style={styles.facts}>
          <InfoRow label={t('Vessels')} value={count.toLocaleString()} />
          <InfoRow label={t('Concurrency')} value={concurrencyNumber.toLocaleString()} />
        </View>
        <Button
          label={submitting ? t('Starting...') : t('{count, plural, one {Run on # vessel} other {Run on # vessels}}', { count })}
          variant={effectiveKind === 'Command' ? 'danger' : 'primary'}
          busy={submitting}
          onPress={() => void start()}
          testID="run-action-start"
        />
        <Button label={t('Back')} variant="ghost" disabled={submitting} onPress={() => setStep('configure')} testID="run-action-back" />
      </View>
    );
  }

  return (
    <View testID="run-action-configure">
      <AppText muted style={styles.gap}>{t('{count, plural, one {# vessel selected} other {# vessels selected}}', { count })}</AppText>
      {serverError ? <Banner tone="danger" title={serverError} testID="run-action-error" /> : null}
      {showError('vessels') ? <Banner tone="danger" title={showError('vessels') ?? ''} /> : null}
      <SegmentedControl
        label={t('Action source')}
        value={mode}
        onChange={setMode}
        options={[
          { value: 'saved', label: t('Saved action'), testID: 'run-action-mode-saved' },
          { value: 'adhoc', label: t('Ad hoc'), testID: 'run-action-mode-adhoc' },
        ]}
      />
      {mode === 'saved' ? (
        <View>
          {actionsError ? <Banner tone="danger" title={actionsError} /> : null}
          <SelectField label={t('Action')} value={selectedActionId} options={actionOptions} onChange={setSelectedActionId} closeLabel={t('Close')} disabled={actionsLoading} error={showError('action')} testID="run-action-action" />
          {selectedAction ? (
            <View style={styles.gap}>
              <View style={styles.row}>
                <KindBadge kind={selectedAction.kind} />
                {selectedAction.description ? <AppText variant="caption" muted style={styles.flex}>{selectedAction.description}</AppText> : null}
              </View>
              <CodeBlock text={bodyText} />
              {selectedAction.kind === 'Command' ? (
                <SwitchField label={t('Skip vessels with uncommitted changes (clean-tree check)')} value={effectiveCleanTree} onChange={setCleanTreeOverride} testID="run-action-clean" />
              ) : null}
            </View>
          ) : null}
        </View>
      ) : (
        <View>
          <TextField label={t('Name')} value={adHoc.name} maxLength={200} placeholder={t('e.g. Show git status')} onChangeText={(name) => setAdHoc({ ...adHoc, name })} error={showError('name')} testID="run-action-name" />
          <SegmentedControl
            label={t('Kind')}
            value={adHoc.kind}
            onChange={(k) => setAdHoc({ ...adHoc, kind: k, requiresCleanWorkingTree: k === 'Command' })}
            options={(['Command', 'Mission'] as FleetActionKind[]).map((k) => ({ value: k, label: t(KIND_LABELS[k]), testID: `run-action-kind-${k}` }))}
          />
          <AppText variant="caption" muted style={styles.gap}>{t(KIND_DESCRIPTIONS[adHoc.kind])}</AppText>
          <TextField
            label={adHoc.kind === 'Command' ? t('Command text') : t('Prompt template')}
            value={adHoc.kind === 'Command' ? adHoc.commandText : adHoc.promptTemplate}
            onChangeText={(text) => setAdHoc(adHoc.kind === 'Command' ? { ...adHoc, commandText: text } : { ...adHoc, promptTemplate: text })}
            placeholder={adHoc.kind === 'Command' ? 'git status -sb' : undefined}
            multiline
            autoCapitalize="none"
            autoCorrect={false}
            error={unknownVars.length > 0 ? t('Unknown template variable: {{names}}', { names: unknownVars.join(', ') }) : showError('body')}
            testID="run-action-body"
          />
          {adHoc.kind === 'Mission' ? (
            <SelectField label={t('Pipeline')} value={adHoc.pipelineId} options={pipelineOptions} onChange={(pipelineId) => setAdHoc({ ...adHoc, pipelineId })} closeLabel={t('Close')} testID="run-action-pipeline" />
          ) : null}
          <TemplateVariableHelp />
          {adHoc.kind === 'Command' ? (
            <>
              <TextField label={t('Timeout (seconds)')} value={adHoc.timeoutSeconds} keyboardType="number-pad" onChangeText={(timeoutSeconds) => setAdHoc({ ...adHoc, timeoutSeconds })} error={showError('timeout')} testID="run-action-timeout" />
              <SwitchField label={t('Skip vessels with uncommitted changes (clean-tree check)')} value={adHoc.requiresCleanWorkingTree} onChange={(v) => setAdHoc({ ...adHoc, requiresCleanWorkingTree: v })} testID="run-action-adhoc-clean" />
            </>
          ) : null}
        </View>
      )}
      <TextField
        label={t('Concurrency')}
        value={concurrency}
        keyboardType="number-pad"
        onChangeText={setConcurrency}
        error={showError('concurrency')}
        hint={effectiveKind === 'Command'
          ? t('How many vessels run the command at the same time (1-32). The Admiral-wide limit also applies.')
          : t('How many voyages from this run may be active at once (1-32).')}
        testID="run-action-concurrency"
      />
      <View style={styles.gap}>
        <AppText variant="label">
          {previewVessel ? t('Preview for {{name}}', { name: previewVessel.name }) : t('Preview')}
          {count > 1 ? ` ${t('{count, plural, one {(and # more vessel)} other {(and # more vessels)}}', { count: count - 1 })}` : ''}
        </AppText>
        {!bodyText ? <AppText muted>{t('Choose or define an action to see the rendered text.')}</AppText> : null}
        {bodyText && !previewVessel ? <AppText muted>{t('Loading preview...')}</AppText> : null}
        {preview ? (
          <>
            <CodeBlock text={preview.text} testID="run-action-preview" />
            {preview.usesHealthSummary ? <AppText variant="caption" muted>{t('{{health.summary}} is rendered on the server from each vessel\'s latest health evaluation.')}</AppText> : null}
            {preview.missingBuildCommand ? <AppText variant="caption" color="warning">{t('This vessel has no build command, so it will be skipped.')}</AppText> : null}
          </>
        ) : null}
      </View>
      <Button label={t('Review and run')} onPress={review} disabled={actionsLoading && mode === 'saved'} testID="run-action-review" />
      <Button label={t('Cancel')} variant="ghost" onPress={onClose} />
    </View>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  gap: { marginBottom: spacing.md, gap: spacing.sm },
  row: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  facts: { marginVertical: spacing.md },
});
