import { useMemo, useState } from 'react';
import { StyleSheet, View, type GestureResponderEvent, type LayoutChangeEvent } from 'react-native';
import { getMissionHistory } from '@dashboard/api/client';
import {
  computeYTicks,
  formatBucketLabel,
  formatTooltipTime,
  historyBuckets,
  MISSION_HISTORY_RANGES,
  missionHistoryQuery,
  type MissionHistoryRangeValue,
} from '@dashboard/lib/missionHistory';
import type { Fleet, MissionHistorySummaryResult, Vessel } from '@dashboard/types/models';
import { useQuery } from '../../data/useQuery';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing } from '../../theme/typography';
import { AppText, IconButton, SegmentedControl, SelectField } from '../ui';

const CHART_HEIGHT = 160;

/**
 * Mission History (the dashboard's MissionHistoryChart): completed, failed, and other missions per time bucket
 * over the last hour, day, week, or month, narrowed to a fleet or vessel. Touch and drag over the chart to read a
 * bucket; screen-reader users get the totals as its label and step through the buckets with swipe up and down (an
 * adjustable element), each read as text. `refreshToken` reloads it with the rest of Home.
 */
export function MissionHistoryChart({ vessels, fleets, refreshToken }: { vessels: Vessel[]; fleets: Fleet[]; refreshToken: number }) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const [range, setRange] = useState<MissionHistoryRangeValue>('week');
  const [fleetId, setFleetId] = useState('');
  const [vesselId, setVesselId] = useState('');
  const [hovered, setHovered] = useState<number | null>(null);
  const [width, setWidth] = useState(0);

  const filteredVessels = useMemo(() => (fleetId ? vessels.filter((v) => v.fleetId === fleetId) : vessels), [vessels, fleetId]);
  const effectiveVesselId = vesselId && filteredVessels.some((v) => v.id === vesselId) ? vesselId : '';
  const rangeDef = MISSION_HISTORY_RANGES.find((r) => r.value === range) ?? MISSION_HISTORY_RANGES[2];

  const query = useQuery<MissionHistorySummaryResult | null>(
    () => getMissionHistory(missionHistoryQuery(rangeDef, fleetId, effectiveVesselId, new Date())).catch(() => null),
    [range, fleetId, effectiveVesselId, refreshToken],
  );
  const history = query.data;
  const buckets = useMemo(() => historyBuckets(history), [history]);
  const maxCount = Math.max(1, ...buckets.map((b) => b.complete + b.failed + b.other));
  const ticks = computeYTicks(maxCount);
  const yMax = ticks[ticks.length - 1] || 1;
  const labelIndexes = buckets.length > 1 ? [0, Math.floor((buckets.length - 1) / 2), buckets.length - 1] : [0];
  const shown = hovered !== null ? buckets[hovered] : null;

  const pick = (event: GestureResponderEvent) => {
    if (!width || buckets.length === 0) return;
    const index = Math.floor((event.nativeEvent.locationX / width) * buckets.length);
    setHovered(Math.max(0, Math.min(buckets.length - 1, index)));
  };

  const bucketText = (b: { timestampMs: number; complete: number; failed: number; other: number }) =>
    `${formatTooltipTime(b.timestampMs)}: ${t('Complete')} ${b.complete}, ${t('Failed')} ${b.failed}${b.other > 0 ? `, ${t('Other')} ${b.other}` : ''}`;
  const step = (delta: number) => {
    if (buckets.length === 0) return;
    const from = hovered ?? (delta > 0 ? -1 : buckets.length);
    setHovered(Math.max(0, Math.min(buckets.length - 1, from + delta)));
  };

  const summary = t('{{total}} total, {{complete}} complete, {{failed}} failed', {
    total: history?.totalCount ?? 0, complete: history?.completeCount ?? 0, failed: history?.failedCount ?? 0,
  });

  return (
    <View style={[styles.card, { backgroundColor: colors.surface, borderColor: colors.border }]} testID="mission-history">
      <View style={styles.header}>
        <AppText variant="heading" accessibilityRole="header" style={styles.flex}>{t('Mission History')}</AppText>
        <IconButton icon="refresh" label={t('Refresh')} onPress={() => void query.reload()} testID="mission-history-refresh" />
      </View>
      <SegmentedControl
        label={t('Time range')}
        value={range}
        onChange={(value) => { setRange(value); setHovered(null); }}
        options={MISSION_HISTORY_RANGES.map((r) => ({ value: r.value, label: t(r.label), testID: `mission-history-range-${r.value}` }))}
      />
      <View style={styles.filters}>
        <View style={styles.flex}>
          <SelectField
            label={t('Fleet')}
            value={fleetId}
            onChange={(value) => { setFleetId(value); setVesselId(''); setHovered(null); }}
            allowEmpty
            placeholder={t('All Fleets')}
            closeLabel={t('Close')}
            options={fleets.map((f) => ({ value: f.id, label: f.name }))}
            testID="mission-history-fleet"
          />
        </View>
        <View style={styles.flex}>
          <SelectField
            label={t('Vessel')}
            value={effectiveVesselId}
            onChange={(value) => { setVesselId(value); setHovered(null); }}
            allowEmpty
            placeholder={t('All Vessels')}
            closeLabel={t('Close')}
            options={filteredVessels.map((v) => ({ value: v.id, label: v.name }))}
            testID="mission-history-vessel"
          />
        </View>
      </View>
      <View style={styles.stats} accessible accessibilityLabel={summary}>
        <Stat value={history?.totalCount ?? 0} label={t('Total')} color="text" />
        <Stat value={history?.completeCount ?? 0} label={t('Complete')} color="success" />
        <Stat value={history?.failedCount ?? 0} label={t('Failed')} color="danger" />
        {(history?.otherCount ?? 0) > 0 ? <Stat value={history?.otherCount ?? 0} label={t('Other')} color="textMuted" /> : null}
      </View>
      {query.loading ? (
        <AppText muted style={styles.empty}>{t('Loading mission history...')}</AppText>
      ) : buckets.length === 0 ? (
        <AppText muted style={styles.empty} testID="mission-history-empty">{t('No mission data for this time range')}</AppText>
      ) : (
        <View>
          <View
            testID="mission-history-chart"
            accessible
            accessibilityRole="adjustable"
            accessibilityLabel={`${t('Mission History')}: ${summary}`}
            accessibilityValue={shown ? { text: bucketText(shown) } : undefined}
            accessibilityHint={t('Swipe up or down to read each time period.')}
            accessibilityActions={[{ name: 'increment' }, { name: 'decrement' }]}
            onAccessibilityAction={(e) => step(e.nativeEvent.actionName === 'increment' ? 1 : -1)}
            onLayout={(e: LayoutChangeEvent) => setWidth(e.nativeEvent.layout.width)}
            onStartShouldSetResponder={() => true}
            onMoveShouldSetResponder={() => true}
            onResponderGrant={pick}
            onResponderMove={pick}
            style={[styles.chart, { borderBottomColor: colors.border }]}
          >
            {buckets.map((b, i) => {
              const h = (n: number) => (n / yMax) * CHART_HEIGHT;
              const active = hovered === i;
              return (
                <View key={b.timestampMs} style={styles.column}>
                  {b.other > 0 ? <View style={{ height: h(b.other), backgroundColor: colors.textMuted, opacity: active ? 0.8 : 0.5 }} /> : null}
                  {b.failed > 0 ? <View style={{ height: h(b.failed), backgroundColor: colors.danger, opacity: active ? 1 : 0.85 }} /> : null}
                  {b.complete > 0 ? <View style={{ height: h(b.complete), backgroundColor: colors.success, opacity: active ? 1 : 0.85 }} /> : null}
                </View>
              );
            })}
          </View>
          <View style={styles.axis}>
            {labelIndexes.map((i) => (
              <AppText key={i} variant="caption" muted>{formatBucketLabel(buckets[i].timestampMs, rangeDef.stepMinutes, rangeDef.hours)}</AppText>
            ))}
          </View>
          {shown ? (
            <View style={[styles.tooltip, { borderColor: colors.border, backgroundColor: colors.surfaceRaised }]} accessibilityLiveRegion="polite" testID="mission-history-tooltip">
              <AppText variant="label">{formatTooltipTime(shown.timestampMs)}</AppText>
              <AppText variant="caption">
                {`${t('Complete')}: ${shown.complete}   ${t('Failed')}: ${shown.failed}${shown.other > 0 ? `   ${t('Other')}: ${shown.other}` : ''}   ${t('Total')}: ${shown.complete + shown.failed + shown.other}`}
              </AppText>
            </View>
          ) : null}
        </View>
      )}
      <View style={styles.legend}>
        <Legend color={colors.success} label={t('Complete')} />
        <Legend color={colors.danger} label={t('Failed')} />
        <Legend color={colors.textMuted} label={t('Other')} />
      </View>
    </View>
  );
}

function Stat({ value, label, color }: { value: number; label: string; color: 'text' | 'success' | 'danger' | 'textMuted' }) {
  return (
    <View style={styles.stat}>
      <AppText variant="heading" color={color}>{String(value)}</AppText>
      <AppText variant="caption" muted>{label}</AppText>
    </View>
  );
}

function Legend({ color, label }: { color: string; label: string }) {
  return (
    <View style={styles.legendItem}>
      <View style={[styles.swatch, { backgroundColor: color }]} />
      <AppText variant="caption" muted>{label}</AppText>
    </View>
  );
}

const styles = StyleSheet.create({
  card: { borderWidth: StyleSheet.hairlineWidth, borderRadius: radius.md, padding: spacing.md, marginHorizontal: spacing.md, marginBottom: spacing.xl },
  header: { flexDirection: 'row', alignItems: 'center' },
  flex: { flex: 1 },
  filters: { flexDirection: 'row', gap: spacing.sm },
  stats: { flexDirection: 'row', gap: spacing.lg, marginBottom: spacing.md },
  stat: { alignItems: 'flex-start' },
  empty: { paddingVertical: spacing.xl, textAlign: 'center' },
  chart: { height: CHART_HEIGHT, flexDirection: 'row', alignItems: 'flex-end', gap: 1, borderBottomWidth: 1 },
  column: { flex: 1, justifyContent: 'flex-end', minWidth: 1 },
  axis: { flexDirection: 'row', justifyContent: 'space-between', marginTop: spacing.xs },
  tooltip: { borderWidth: 1, borderRadius: radius.sm, padding: spacing.sm, marginTop: spacing.sm, gap: 2 },
  legend: { flexDirection: 'row', gap: spacing.lg, marginTop: spacing.md },
  legendItem: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs },
  swatch: { width: 12, height: 12, borderRadius: 2 },
});
