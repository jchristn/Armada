import { StyleSheet, View } from 'react-native';
import type { BlackBoxRecoveryPath, BlackBoxRecoveryRunResult } from '@dashboard/types/blackbox';
import { AppText, Banner, BottomSheet, Button, CodeBlock, StatusBadge, SwitchRow } from '../../components/ui';
import { useLocale } from '../../i18n/LocaleContext';
import { spacing } from '../../theme/typography';
import { pathLosesData } from './blackBoxModel';

/** Where a recovery run stands: the dry run's plan waiting for confirmation, or the confirmed run's result. */
export type RecoveryStage =
  | { stage: 'confirm'; path: BlackBoxRecoveryPath; dryRun: BlackBoxRecoveryRunResult; acknowledged: boolean; running: boolean }
  | { stage: 'done'; path: BlackBoxRecoveryPath; result: BlackBoxRecoveryRunResult };

/**
 * The recovery confirmation (the dashboard's recovery dialog): the plan the dry run returned, a data-loss
 * acknowledgement when the path can lose work, and Run; after the run, the per-step results.
 */
export function RecoverySheet({ state, onAcknowledge, onConfirm, onClose }: {
  state: RecoveryStage | null;
  onAcknowledge: (value: boolean) => void;
  onConfirm: () => void;
  onClose: () => void;
}) {
  const { t } = useLocale();
  const open = state !== null;
  const title = state ? state.path.title : '';
  const losesData = state ? pathLosesData(state.path) : false;
  const footer = state?.stage === 'confirm' ? (
    <View style={styles.actions}>
      <Button label={t('Cancel')} variant="ghost" onPress={onClose} testID="bbx-recovery-cancel" />
      <Button
        label={t('Run recovery')}
        variant={losesData ? 'danger' : 'primary'}
        busy={state.running}
        disabled={state.running || (losesData && !state.acknowledged)}
        onPress={onConfirm}
        testID="bbx-recovery-confirm"
      />
    </View>
  ) : state?.stage === 'done' ? (
    <View style={styles.actions}>
      <Button label={t('Close')} onPress={onClose} testID="bbx-recovery-close" />
    </View>
  ) : null;

  return (
    <BottomSheet open={open} title={title} onClose={onClose} closeLabel={t('Close')} footer={footer} testID="bbx-recovery-sheet">
      {state?.stage === 'confirm' ? (
        <View style={styles.body}>
          <AppText muted>{t('Dry run complete. Nothing has run yet. These steps will run in order:')}</AppText>
          {(state.dryRun.plan ?? state.path.steps ?? []).map((step) => (
            <View key={step.order} style={styles.step} testID={`bbx-recovery-step-${step.order}`}>
              <AppText variant="label">{`${step.order}. ${step.description}`}</AppText>
              {step.cliCommand || step.mcpCommand ? <CodeBlock wrap text={step.cliCommand || step.mcpCommand || ''} /> : null}
              {step.dataLossRisk ? <StatusBadge label={t('Can lose work')} tone="failed" /> : null}
            </View>
          ))}
          {state.path.expectedOutcome ? <AppText muted>{`${t('Expected outcome')}: ${state.path.expectedOutcome}`}</AppText> : null}
          {state.dryRun.message ? <AppText variant="caption" muted>{state.dryRun.message}</AppText> : null}
          {losesData ? (
            <>
              <Banner tone="danger" title={t('This recovery can lose work')} message={t('Uncommitted or unlanded changes in the dock may be discarded.')} testID="bbx-recovery-data-loss" />
              <SwitchRow
                label={t('I understand this can lose work')}
                value={state.acknowledged}
                onChange={onAcknowledge}
                testID="bbx-recovery-acknowledge"
              />
            </>
          ) : null}
        </View>
      ) : null}
      {state?.stage === 'done' ? (
        <View style={styles.body} testID="bbx-recovery-result">
          <StatusBadge label={state.result.succeeded ? t('Succeeded') : t('Failed')} tone={state.result.succeeded ? 'success' : 'failed'} />
          {state.result.message ? <AppText>{state.result.message}</AppText> : null}
          {(state.result.stepResults ?? []).map((step) => (
            <View key={step.order} style={styles.step}>
              <AppText variant="label">{`${step.order}. ${step.tool ?? ''}`.trim()}</AppText>
              <StatusBadge label={step.succeeded ? t('Succeeded') : t('Failed')} tone={step.succeeded ? 'success' : 'failed'} />
              {step.message ? <AppText variant="caption" muted selectable>{step.message}</AppText> : null}
            </View>
          ))}
        </View>
      ) : null}
    </BottomSheet>
  );
}

const styles = StyleSheet.create({
  body: { gap: spacing.md },
  step: { gap: spacing.xs },
  actions: { flexDirection: 'row', justifyContent: 'flex-end', gap: spacing.sm },
});
