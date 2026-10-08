import { StyleSheet, View } from 'react-native';
import type { FleetActionKind, FleetActionRun, FleetActionRunStatus, FleetActionTargetStatus } from '@dashboard/types/models';
import { KIND_LABELS, runStatusBadge, targetStatusBadge, TEMPLATE_VARIABLES } from '@dashboard/lib/fleetActionLabels';
import { runProgress } from '@dashboard/lib/fleetActionForm';
import { CodeBlock } from '../../components/ask/CodeBlock';
import { AppText, BottomSheet, Button, StatusBadge } from '../../components/ui';
import { Disclosure } from '../../components/ui/Disclosure';
import { useLocale, type Translate } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing } from '../../theme/typography';

/** The kind of an action (Command runs a shell command, so it is shown as a warning, like the dashboard). */
export function KindBadge({ kind }: { kind: FleetActionKind }) {
  const { t } = useLocale();
  return <StatusBadge label={t(KIND_LABELS[kind])} tone={kind === 'Command' ? 'warning' : 'info'} />;
}

export function RunStatusBadge({ status }: { status: FleetActionRunStatus }) {
  const { t } = useLocale();
  const badge = runStatusBadge(t, status);
  return <StatusBadge label={badge.label} tone={badge.tone} />;
}

export function TargetStatusBadge({ status }: { status: FleetActionTargetStatus }) {
  const { t } = useLocale();
  const badge = targetStatusBadge(t, status);
  return <StatusBadge label={badge.label} tone={badge.tone} />;
}

type ProgressRun = Pick<FleetActionRun, 'targetCount' | 'succeededCount' | 'failedCount' | 'skippedCount' | 'cancelledCount'>;

/** "1 succeeded, 1 failed, 0 skipped of 4": the progress bar's text, also read with a run's row. */
export function runProgressSummary(t: Translate, run: ProgressRun): string {
  const { total } = runProgress(run);
  return t('{{succeeded}} succeeded, {{failed}} failed, {{skipped}} skipped of {{total}}', {
    succeeded: run.succeededCount.toLocaleString(),
    failed: run.failedCount.toLocaleString(),
    skipped: run.skippedCount.toLocaleString(),
    total: total.toLocaleString(),
  });
}

/**
 * Segmented progress (succeeded / failed / skipped / cancelled of total) with the same text summary as the
 * dashboard's RunProgress, so the numbers are readable without the colors.
 */
export function RunProgressBar({ run, compact = false }: { run: ProgressRun; compact?: boolean }) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const { total, done, percent } = runProgress(run);
  const flex = (n: number) => (total > 0 ? n / total : 0);
  const summary = runProgressSummary(t, run);
  const rest = Math.max(0, total - done);
  return (
    <View style={styles.progress}>
      <View
        style={[styles.bar, { backgroundColor: colors.border }]}
        accessible
        accessibilityRole="progressbar"
        accessibilityLabel={t('Targets finished')}
        accessibilityValue={{ min: 0, max: total, now: done, text: summary }}
      >
        <View style={{ flex: flex(run.succeededCount), backgroundColor: colors.success }} />
        <View style={{ flex: flex(run.failedCount), backgroundColor: colors.danger }} />
        <View style={{ flex: flex(run.skippedCount), backgroundColor: colors.warning }} />
        <View style={{ flex: flex(run.cancelledCount), backgroundColor: colors.textMuted }} />
        <View style={{ flex: total > 0 ? rest / total : 1 }} />
      </View>
      <AppText variant="caption" muted>
        {summary}
        {!compact ? ` (${t('{{percent}}% finished', { percent: percent.toLocaleString() })})` : ''}
        {run.cancelledCount > 0 ? `, ${t('{{count}} cancelled', { count: run.cancelledCount.toLocaleString() })}` : ''}
      </AppText>
    </View>
  );
}

/** "View JSON" (the dashboard's JsonViewer): the record as indented, selectable JSON in a sheet. */
export function JsonSheet({ open, title, data, onClose }: { open: boolean; title: string; data: unknown; onClose: () => void }) {
  const { t } = useLocale();
  return (
    <BottomSheet open={open} title={title} onClose={onClose} closeLabel={t('Close')} testID="json-sheet">
      <CodeBlock text={open ? JSON.stringify(data, null, 2) : ''} maxHeight={520} />
    </BottomSheet>
  );
}

/**
 * The template variables the server renders (the dashboard's TemplateVariableHelp). With `onInsert`, each
 * variable is a button that appends `{{name}}` to the body field.
 */
export function TemplateVariableHelp({ onInsert }: { onInsert?: (token: string) => void }) {
  const { t } = useLocale();
  const { colors } = useTheme();
  return (
    <View style={styles.help}>
      <Disclosure title={t('Template variables')} testID="template-variables">
        <AppText variant="caption" muted style={styles.helpText}>
          {t('Variables are substituted per vessel in a single pass, without shell escaping. Names are case-insensitive. Any other {{name}} is rejected.')}
        </AppText>
        {TEMPLATE_VARIABLES.map((v) => {
          const token = `{{${v.name}}}`;
          return (
            <View key={v.name} style={[styles.variable, { borderColor: colors.border }]}>
              {onInsert ? (
                <Button
                  label={token}
                  variant="secondary"
                  onPress={() => onInsert(token)}
                  accessibilityHint={t('Insert {{token}}', { token })}
                  testID={`template-insert-${v.name}`}
                  style={styles.insert}
                />
              ) : <AppText variant="mono" selectable>{token}</AppText>}
              <AppText variant="caption" muted>{t(v.description)}</AppText>
            </View>
          );
        })}
      </Disclosure>
    </View>
  );
}

const styles = StyleSheet.create({
  progress: { gap: spacing.xs },
  bar: { flexDirection: 'row', height: 8, borderRadius: radius.pill, overflow: 'hidden' },
  help: { marginBottom: spacing.lg },
  helpText: { marginBottom: spacing.sm },
  variable: { borderTopWidth: StyleSheet.hairlineWidth, paddingVertical: spacing.sm, gap: spacing.xs },
  insert: { alignSelf: 'flex-start', marginBottom: 0 },
});
