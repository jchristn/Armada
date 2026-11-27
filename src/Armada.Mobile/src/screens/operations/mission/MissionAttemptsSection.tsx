import { StyleSheet, View } from 'react-native';
import { getMissionAttempts, listMissionSubmissions } from '@dashboard/api/client';
import type { Mission, MissionAttempt, MissionAttemptHistory, StageSubmission } from '@dashboard/types/models';
import { AppText, Section, StatusBadge } from '../../../components/ui';
import { useLiveRefresh } from '../../../data/useLiveRefresh';
import { useQuery } from '../../../data/useQuery';
import { useLocale } from '../../../i18n/LocaleContext';
import { useTheme } from '../../../theme/ThemeContext';
import { spacing } from '../../../theme/typography';
import {
  attemptFacts,
  attemptsNewestFirst,
  decidingSubmissions,
  dispositionTone,
  findingLocation,
  formatAttemptDuration,
  severityTone,
} from './attemptsModel';

/** WebSocket events that change a mission's attempts (RS-12.3). */
export const ATTEMPT_EVENTS = ['mission.attempt_changed', 'mission.escalated', 'mission.repaired'] as const;

type MissionRef = Pick<Mission, 'id' | 'lastUpdateUtc'>;

/**
 * The mission's attempts timeline (GET /api/v1/missions/{id}/attempts), newest first: each attempt's captain, model,
 * disposition, failure kind, repair, escalation, Judge verdict, and duration, plus the escalation and recovery
 * counters. Hidden while the mission has no recorded attempt.
 */
export function MissionAttemptsSection({ mission }: { mission: MissionRef }) {
  const { t, formatRelativeTime } = useLocale();
  const { colors } = useTheme();
  const query = useQuery<MissionAttemptHistory>(() => getMissionAttempts(mission.id), [mission.id, mission.lastUpdateUtc], t('Failed to load attempts.'));
  useLiveRefresh(ATTEMPT_EVENTS, () => { void query.reload(); });
  const history = query.data;
  if (!history || history.attempts.length === 0) return null;
  const counters = [
    history.escalationCount > 0 ? t('Escalations: {{count}}', { count: history.escalationCount }) : null,
    history.redispatchAttempts > 0 ? t('Redispatches: {{count}}', { count: history.redispatchAttempts }) : null,
    history.outputRepairAttempts > 0 ? t('Output repairs: {{count}}', { count: history.outputRepairAttempts }) : null,
    history.stallRecoveryAttempts > 0 ? t('Stall recoveries: {{count}}', { count: history.stallRecoveryAttempts }) : null,
  ].filter((c): c is string => !!c);
  const escalatedFrom = history.escalatedFromModel
    ? t('Escalated from {{model}}', { model: history.escalatedFromModel })
    : null;

  const row = (a: MissionAttempt) => {
    const who = [a.captainName || a.captainId, a.model].filter(Boolean).join(' · ');
    const duration = formatAttemptDuration(a.durationMs);
    const when = [duration, formatRelativeTime(a.endedUtc)].filter(Boolean).join(' · ');
    const facts = attemptFacts(a);
    const title = a.reworkRound > 0
      ? t('Attempt {{attempt}}, rework {{round}}', { attempt: a.attempt, round: a.reworkRound })
      : t('Attempt {{attempt}}', { attempt: a.attempt });
    const spoken = [title, t(a.disposition), who, ...facts.map(f => `${t(f.label)}: ${f.value}`), a.escalated ? t('Escalated') : null, when]
      .filter(Boolean).join(', ');
    return (
      <View key={a.outcomeId || `${a.attempt}-${a.reworkRound}`} style={[styles.row, { borderBottomColor: colors.border }]}
        accessible accessibilityLabel={spoken} testID={`mission-attempt-${a.attempt}-${a.reworkRound}`}>
        <View style={styles.head}>
          <AppText variant="label" style={styles.flex}>{title}</AppText>
          {a.escalated ? <StatusBadge label={t('Escalated')} tone="warning" /> : null}
          <StatusBadge label={t(a.disposition)} tone={dispositionTone(a.disposition)} />
        </View>
        {who ? <AppText variant="caption" muted selectable>{who}</AppText> : null}
        {facts.map(f => <AppText key={f.label} variant="caption">{`${t(f.label)}: ${f.value}`}</AppText>)}
        {a.failureReason ? <AppText variant="caption" muted numberOfLines={3} selectable>{a.failureReason}</AppText> : null}
        {when ? <AppText variant="caption" muted>{when}</AppText> : null}
      </View>
    );
  };

  return (
    <Section title={t('Attempts ({{count}})', { count: history.attempts.length })} footer={[escalatedFrom, ...counters].filter(Boolean).join('  ·  ') || undefined}>
      <View testID="mission-attempts">{attemptsNewestFirst(history.attempts).map(row)}</View>
    </Section>
  );
}

/**
 * Structured results and findings the mission's captains submitted (GET /api/v1/missions/{id}/submissions): the
 * submission that decided each attempt, with its verdict or status, summary, and findings by severity. Hidden when
 * nothing was submitted.
 */
export function MissionFindingsSection({ mission }: { mission: MissionRef }) {
  const { t, formatRelativeTime } = useLocale();
  const { colors } = useTheme();
  const query = useQuery<StageSubmission[]>(() => listMissionSubmissions(mission.id), [mission.id, mission.lastUpdateUtc], t('Failed to load submissions.'));
  useLiveRefresh(ATTEMPT_EVENTS, () => { void query.reload(); });
  const views = decidingSubmissions(query.data ?? []);
  if (views.length === 0) return null;
  return (
    <Section title={t('Submitted Results')}>
      <View testID="mission-submissions">
        {views.map((v, index) => (
          <View key={`${v.attemptId}-${index}`} style={[styles.row, { borderBottomColor: colors.border }]} testID={`mission-submission-${index}`}>
            <View style={styles.head} accessible accessibilityLabel={[`${v.persona} · ${t(v.kind)}`, v.outcome].filter(Boolean).join(', ')}>
              <AppText variant="label" style={styles.flex}>{`${v.persona} · ${t(v.kind)}`}</AppText>
              {v.outcome ? <StatusBadge label={v.outcome} tone={v.outcome === 'PASS' || v.outcome === 'Completed' ? 'success' : 'warning'} /> : null}
            </View>
            {v.summary ? <AppText selectable>{v.summary}</AppText> : null}
            {v.findings.length === 0 && v.kind !== 'Plan' ? <AppText variant="caption" muted>{t('No findings.')}</AppText> : null}
            {v.findings.map((f, i) => {
              const location = findingLocation(f);
              return (
                <View key={i} style={styles.finding} accessible
                  accessibilityLabel={[t('Severity {{severity}}', { severity: f.severity }), location, f.scenario, f.requiredFix ? t('Required fix: {{fix}}', { fix: f.requiredFix }) : null].filter(Boolean).join(', ')}
                  testID={`mission-finding-${index}-${i}`}>
                  <View style={styles.head}>
                    <StatusBadge label={f.severity} tone={severityTone(f.severity)} />
                    {location ? <AppText variant="mono" style={styles.flex} numberOfLines={1}>{location}</AppText> : null}
                  </View>
                  <AppText selectable>{f.scenario}</AppText>
                  {f.requiredFix ? <AppText variant="caption" muted selectable>{t('Required fix: {{fix}}', { fix: f.requiredFix })}</AppText> : null}
                </View>
              );
            })}
            <AppText variant="caption" muted>{[t(v.source), formatRelativeTime(v.createdUtc)].join(' · ')}</AppText>
          </View>
        ))}
      </View>
    </Section>
  );
}

const styles = StyleSheet.create({
  row: { paddingHorizontal: spacing.lg, paddingVertical: spacing.sm, gap: spacing.xs, borderBottomWidth: StyleSheet.hairlineWidth },
  head: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, flexWrap: 'wrap' },
  finding: { gap: spacing.xs, paddingVertical: spacing.xs },
  flex: { flex: 1 },
});
