import type { BlackBoxEntry } from '@dashboard/types/blackbox';
import { StyleSheet, View } from 'react-native';
import { AppText, Button, KeyValueRow, Section, StatusBadge } from '../../components/ui';
import { useLocale } from '../../i18n/LocaleContext';
import { spacing } from '../../theme/typography';
import { spaced } from './blackBoxModel';
import { buildLabel, rankedChanges, resilienceOf, selfHealableTone, severityTone, type ResilienceChange } from './resilienceModel';

export interface ResilienceSectionProps {
  entry: BlackBoxEntry;
  busy: boolean;
  /** Open Ask Armada with the "Draft Armada fix mission" prompt. */
  onDraft: () => void;
  /** Copy a proposed change's mission brief. */
  onCopyBrief: (change: ResilienceChange) => void;
}

/**
 * The "Armada resilience" section of a black box entry, right after the summary: in Armada's handoff and landing
 * scope, whether Armada could have healed the failure itself, the detection gap, severity and recurrence, the
 * recoveries Armada already attempted, code pointers at the build commit, the proposed Armada changes with their
 * acceptance tests and the regression test; outside the scope, the short reason no Armada change is suggested.
 */
export function ResilienceSection({ entry, busy, onDraft, onCopyBrief }: ResilienceSectionProps) {
  const { t } = useLocale();
  const res = resilienceOf(entry);
  if (!res) return null;
  const changes = rankedChanges(res);
  const build = buildLabel(res, entry);
  const pointers = res.codePointers ?? [];
  const attempted = res.attemptedRecoveries ?? [];

  if (!res.inScope && changes.length === 0) {
    return (
      <Section title={t('Armada resilience')}>
        <View style={styles.pad} testID="bbx-resilience">
          <AppText muted testID="bbx-resilience-none">{res.noChangeReason || t('No Armada change suggested: the failure is outside Armada\'s handoff and landing.')}</AppText>
        </View>
      </Section>
    );
  }

  const occurrences = res.occurrences30Days ?? 1;
  const recurrence = (res.armadaVersionCount ?? 0) > 1
    ? t('{{count}} in 30 days, {{versions}} Armada versions', { count: occurrences, versions: res.armadaVersionCount ?? 0 })
    : t('{{count}} in 30 days', { count: occurrences });

  return (
    <Section title={t('Armada resilience')} footer={res.inScope ? t('Armada\'s handoff and landing could have been more resilient here: look for the Armada fix first.') : undefined}>
      <View style={[styles.pad, styles.gap]} testID="bbx-resilience">
        <View style={styles.badges}>
          <StatusBadge label={t('Self-healable: {{value}}', { value: t(res.selfHealable ?? 'Unknown') })} tone={selfHealableTone(res.selfHealable)} />
          {res.severity ? <StatusBadge label={t('{{severity}} severity', { severity: t(res.severity) })} tone={severityTone(res.severity)} /> : null}
          {res.inScope ? <StatusBadge label={t('Handoff and landing')} tone="info" /> : null}
        </View>
        {res.selfHealingProposal ? (
          <AppText selectable testID="bbx-resilience-self-healing">{t('Armada could have {{proposal}}.', { proposal: res.selfHealingProposal.replace(/\.$/, '') })}</AppText>
        ) : null}
      </View>
      {res.scopeReason ? <KeyValueRow label={t('Scope')} value={res.scopeReason} /> : null}
      {res.detectionGap ? <KeyValueRow label={t('Detection gap ({{kind}})', { kind: t(spaced(res.detectionGapKind ?? 'Unknown')) })} value={res.detectionGap} testID="bbx-resilience-gap" /> : null}
      {res.invariant ? <KeyValueRow label={t('Invariant')} value={res.invariant} /> : null}
      <KeyValueRow label={t('Recurrence')} value={recurrence} testID="bbx-resilience-recurrence" />
      {attempted.length > 0 ? <KeyValueRow label={t('Armada already attempted')} value={attempted.join('; ')} testID="bbx-resilience-attempted" /> : null}
      {pointers.length > 0 ? (
        <View style={[styles.pad, styles.gap]} testID="bbx-resilience-pointers">
          <AppText variant="label">{build ? t('Code pointers (Armada {{build}})', { build }) : t('Code pointers')}</AppText>
          {pointers.map((p) => <AppText key={p} variant="caption" selectable>{p}</AppText>)}
        </View>
      ) : null}
      {changes.map((change) => (
        <View key={`${change.rank}-${change.title}`} style={[styles.pad, styles.gap]} testID={`bbx-resilience-change-${change.rank}`}>
          <AppText variant="label">{`${change.rank}. ${change.title}`}</AppText>
          {change.severity ? <StatusBadge label={t('{{severity}} severity', { severity: t(change.severity) })} tone={severityTone(change.severity)} /> : null}
          <AppText variant="caption" selectable>{change.change}</AppText>
          {change.acceptanceTest ? <AppText variant="caption" muted selectable>{t('Acceptance test: {{test}}', { test: change.acceptanceTest })}</AppText> : null}
          <Button label={t('Copy fix brief')} icon="copy-outline" variant="ghost" disabled={busy} onPress={() => onCopyBrief(change)} testID={`bbx-resilience-copy-${change.rank}`} />
        </View>
      ))}
      {res.regressionTest ? (
        <KeyValueRow label={t('Regression test')} value={`${res.regressionTest.suite}: ${res.regressionTest.description}`} testID="bbx-resilience-regression" />
      ) : null}
      <View style={styles.pad}>
        <Button label={t('Draft Armada fix mission')} icon="construct-outline" variant="secondary" disabled={busy} accessibilityHint={t('Opens Ask Armada with the top proposed Armada change filled in; nothing is sent until you press Send')} onPress={onDraft} testID="bbx-draft-fix" />
      </View>
    </Section>
  );
}

const styles = StyleSheet.create({
  pad: { paddingHorizontal: spacing.lg, paddingVertical: spacing.sm },
  gap: { gap: spacing.xs },
  badges: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.xs },
});
