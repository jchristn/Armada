import { useRouter, type Href } from 'expo-router';
import { Pressable, StyleSheet, View } from 'react-native';
import { getVesselConcurrency } from '@dashboard/api/client';
import { formatWaitDuration, waitGateLabel } from '@dashboard/lib/missionWaits';
import type { SlotWaitStatistics, VesselConcurrency } from '@dashboard/types/models';
import { AppText, Section, StatusBadge } from '../../components/ui';
import { KeyValueRow } from '../../components/ui/KeyValueRow';
import { useQuery } from '../../data/useQuery';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { MIN_TOUCH, spacing } from '../../theme/typography';

/**
 * The vessel's capacity in use (GET /api/v1/vessels/{id}/concurrency): voyage slots against the limit, slot wait over
 * 24 hours and 7 days, and the missions waiting. Pull to refresh reloads it with the vessel.
 */
export function VesselConcurrencySection({ vesselId, revision }: { vesselId: string; revision?: unknown }) {
  const { t } = useLocale();
  const router = useRouter();
  const { colors } = useTheme();
  const query = useQuery<VesselConcurrency>(() => getVesselConcurrency(vesselId), [vesselId, revision], t('Failed to load concurrency.'));
  const r = query.data;
  if (!r) return null;
  const full = r.maxParallelVoyages > 0 && r.voyageSlotsInUse >= r.maxParallelVoyages;
  const stats = (s: SlotWaitStatistics) => s.missions === 0 || s.p50Seconds === null ? t('No waits recorded')
    : t(s.missions === 1 ? 'p50 {{p50}}, p95 {{p95}}, max {{max}} (1 mission)' : 'p50 {{p50}}, p95 {{p95}}, max {{max}} ({{count}} missions)',
      { p50: formatWaitDuration(s.p50Seconds), p95: formatWaitDuration(s.p95Seconds), max: formatWaitDuration(s.maxSeconds), count: s.missions });
  return (
    <Section title={t('Concurrency')} footer={t('Each running mission also needs a captain slot.')}>
      <View testID="vessel-concurrency">
        <KeyValueRow label={t('Voyage slots')}>
          <View style={styles.inline}>
            <AppText>{r.maxParallelVoyages === 0 ? t('{{used}} in use (no limit)', { used: r.voyageSlotsInUse }) : t('{{used}} / {{limit}} in use', { used: r.voyageSlotsInUse, limit: r.maxParallelVoyages })}</AppText>
            {full ? <StatusBadge label={t('Full')} tone="warning" /> : null}
          </View>
        </KeyValueRow>
        <KeyValueRow label={t('Running missions')} value={r.runningMissions} />
        <KeyValueRow label={t('Slot wait, 24 hours')} value={stats(r.slotWait24h)} />
        <KeyValueRow label={t('Slot wait, 7 days')} value={stats(r.slotWait7d)} />
        {r.waiting.slice(0, 10).map(w => {
          const line = `${t(waitGateLabel(w.gate))} · ${formatWaitDuration(w.waitedSeconds)}`;
          return (
            <Pressable key={w.missionId} accessibilityRole="link" accessibilityLabel={`${w.title}, ${line}`}
              accessibilityHint={t('Opens the mission')} onPress={() => router.push(`/missions/${w.missionId}` as Href)}
              style={({ pressed }) => [styles.row, { borderTopColor: colors.border, opacity: pressed ? 0.6 : 1 }]} testID={`vessel-waiting-${w.missionId}`}>
              <AppText variant="label" style={styles.flex} numberOfLines={2}>{w.title}</AppText>
              <AppText variant="caption" muted>{line}</AppText>
            </Pressable>
          );
        })}
        {r.waitingCount > Math.min(10, r.waiting.length) ? (
          <AppText variant="caption" muted style={styles.pad}>{t('Showing {{shown}} of {{count}} waiting missions.', { shown: Math.min(10, r.waiting.length), count: r.waitingCount })}</AppText>
        ) : null}
      </View>
    </Section>
  );
}

const styles = StyleSheet.create({
  inline: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, flexWrap: 'wrap' },
  row: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, paddingHorizontal: spacing.lg, paddingVertical: spacing.sm, minHeight: MIN_TOUCH, borderTopWidth: StyleSheet.hairlineWidth },
  flex: { flex: 1 },
  pad: { paddingHorizontal: spacing.lg, paddingVertical: spacing.sm },
});
