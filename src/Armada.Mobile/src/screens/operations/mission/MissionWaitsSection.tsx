import { useRouter, type Href } from 'expo-router';
import { Pressable, StyleSheet, View } from 'react-native';
import { getMissionWaits, listMissionSummaries } from '@dashboard/api/client';
import { baseTitle, branchLabel } from '@dashboard/lib/missionTree';
import { formatWaitDuration, slotWaitSeconds, summarizeWaits, waitBreakdown, waitDetailText, waitGateLabel } from '@dashboard/lib/missionWaits';
import type { EnumerationResult, Mission, MissionSummary, MissionWaitInterval } from '@dashboard/types/models';
import { EntityStatusBadge } from '../../../components/app/EntityStatusBadge';
import { AppText, Section } from '../../../components/ui';
import { useQuery } from '../../../data/useQuery';
import { useLocale } from '../../../i18n/LocaleContext';
import { useTheme } from '../../../theme/ThemeContext';
import { MIN_TOUCH, spacing } from '../../../theme/typography';

/**
 * How long the mission waited and on what (GET /api/v1/missions/{id}/waits): the dashboard's summary line, the slot
 * wait and the last waits. Hidden when nothing was recorded.
 */
export function MissionWaitsSection({ mission }: { mission: Pick<Mission, 'id' | 'readyUtc' | 'assignedUtc' | 'lastUpdateUtc'> }) {
  const { t, formatDateTime } = useLocale();
  const query = useQuery<EnumerationResult<MissionWaitInterval>>(() => getMissionWaits(mission.id, 1, 100), [mission.id, mission.lastUpdateUtc], t('Failed to load waits.'));
  const waits = query.data?.objects ?? [];
  if (waits.length === 0) return null;
  const summary = summarizeWaits(waits);
  const breakdown = waitBreakdown(summary, t);
  const slot = slotWaitSeconds(mission.readyUtc, mission.assignedUtc);
  return (
    <Section title={t('Wait Time')}>
      <View style={styles.pad} testID="mission-waits">
        <AppText>{summary.current
          ? t('Waiting {{duration}} so far, now on {{gate}} ({{breakdown}})', { duration: formatWaitDuration(summary.totalSeconds), gate: t(waitGateLabel(summary.current)), breakdown })
          : t('Waited {{duration}} before starting ({{breakdown}})', { duration: formatWaitDuration(summary.totalSeconds), breakdown })}</AppText>
        {slot !== null ? <AppText variant="caption" muted>{t('Slot wait {{duration}}', { duration: formatWaitDuration(slot) })}</AppText> : null}
        {waits.slice(-5).map(w => (
          <AppText key={w.id} variant="caption" muted>
            {[t(waitGateLabel(w.gate)), formatDateTime(w.startedUtc), w.endedUtc ? formatWaitDuration((Date.parse(w.endedUtc) - Date.parse(w.startedUtc)) / 1000) : t('ongoing'), w.detail ? t(waitDetailText(w.detail)!) : null]
              .filter(Boolean).join('  ·  ')}
          </AppText>
        ))}
      </View>
    </Section>
  );
}

/** Child missions that build on this one, with their branch marker; hidden without children. */
export function MissionChildrenSection({ missionId }: { missionId: string }) {
  const { t } = useLocale();
  const router = useRouter();
  const { colors } = useTheme();
  const query = useQuery<EnumerationResult<MissionSummary>>(() => listMissionSummaries({ pageNumber: 1, pageSize: 50, filters: { parentMissionId: missionId, order: 'CreatedAscending' } }), [missionId], t('Failed to load child missions.'));
  const children = query.data?.objects ?? [];
  if (children.length === 0) return null;
  const total = query.data?.totalRecords ?? children.length;
  return (
    <Section title={t('Children ({{count}})', { count: total })} footer={total > children.length ? t('Showing the first {{shown}} of {{count}} children.', { shown: children.length, count: total }) : undefined}>
      {children.map(child => {
        const branch = branchLabel(child);
        return (
          <Pressable key={child.id} accessibilityRole="link" accessibilityLabel={[baseTitle(child.title), t(child.status), branch ? t(branch) : null].filter(Boolean).join(', ')}
            accessibilityHint={t('Opens the mission')} onPress={() => router.push(`/missions/${child.id}` as Href)}
            style={({ pressed }) => [styles.row, { borderBottomColor: colors.border, opacity: pressed ? 0.6 : 1 }]} testID={`mission-child-${child.id}`}>
            <AppText variant="label" style={styles.flex} numberOfLines={2}>{baseTitle(child.title)}</AppText>
            {branch ? <AppText variant="caption" muted>{t(branch)}</AppText> : null}
            <EntityStatusBadge status={child.status} />
          </Pressable>
        );
      })}
    </Section>
  );
}

const styles = StyleSheet.create({
  pad: { paddingHorizontal: spacing.lg, paddingVertical: spacing.sm, gap: spacing.xs },
  row: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, paddingHorizontal: spacing.lg, paddingVertical: spacing.sm, minHeight: MIN_TOUCH, borderBottomWidth: StyleSheet.hairlineWidth },
  flex: { flex: 1 },
});
