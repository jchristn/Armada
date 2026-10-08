import { useMemo, useState } from 'react';
import { StyleSheet, View, type GestureResponderEvent, type LayoutChangeEvent } from 'react-native';
import { getHarborMetrics } from '@dashboard/api/client';
import { formatDurationMs } from '@dashboard/lib/fleetActionLabels';
import { formatBucketLabel, formatTooltipTime } from '@dashboard/lib/missionHistory';
import { formatTokens } from '@dashboard/lib/tokenUsage';
import type { HarborLaunchSpeed, HarborLinkSegmentState, HarborMetrics, HarborMetricsRange } from '@dashboard/types/models';
import { FieldCard } from '../../components/resource/DetailParts';
import { AppText } from '../../components/ui/AppText';
import { IconButton } from '../../components/ui/IconButton';
import { SegmentedControl } from '../../components/ui/SegmentedControl';
import type { Translate } from '../../i18n/LocaleContext';
import { normalizeUtc, useLocale } from '../../i18n/LocaleContext';
import { useLoad } from '../../resource/useLoad';
import { useTheme } from '../../theme/ThemeContext';
import type { Palette } from '../../theme/palette';
import { CHROME_MAX_FONT_SCALE, radius, spacing } from '../../theme/typography';

const CHART_HEIGHT = 120;
const RANGES: { value: HarborMetricsRange; label: string; hours: number }[] = [
  { value: '1h', label: '1h', hours: 1 },
  { value: '24h', label: '24h', hours: 24 },
  { value: '7d', label: '7d', hours: 168 },
];
const TOKEN_COLORS: (keyof Palette)[] = ['primary', 'success', 'warning', 'info', 'danger'];
const MAX_TOKEN_SERIES = 5;

/** One drawn series of a bucket chart. */
export interface ChartSeries {
  key: string;
  label: string;
  color: keyof Palette;
}

/** One bucket of a chart: its start time and one value per series. */
export interface ChartBucket {
  startUtc: string;
  values: number[];
}

function parseMs(utc: string): number {
  return new Date(normalizeUtc(utc) ?? utc).getTime();
}

/** Jobs over time as stacked series: missions finished / failed, then interactive and other launches finished / failed. */
export function jobSeries(t: Translate, metrics: HarborMetrics): { series: ChartSeries[]; buckets: ChartBucket[] } {
  return {
    series: [
      { key: 'missionsFinished', label: t('Missions finished'), color: 'success' },
      { key: 'missionsFailed', label: t('Missions failed'), color: 'danger' },
      { key: 'interactiveFinished', label: t('Interactive finished'), color: 'info' },
      { key: 'interactiveFailed', label: t('Interactive failed'), color: 'warning' },
    ],
    buckets: metrics.jobs.buckets.map((b) => ({
      startUtc: b.bucketStartUtc,
      values: [b.missionsFinished, b.missionsFailed, b.interactiveFinished, b.interactiveFailed],
    })),
  };
}

/** Token usage per bucket, stacked by runtime and model: the biggest few series by name, the rest as "Other". */
export function tokenSeries(t: Translate, metrics: HarborMetrics): { series: ChartSeries[]; buckets: ChartBucket[] } {
  const keyOf = (runtime: string, model: string) => `${runtime} / ${model}`;
  const top = metrics.tokens.series.slice(0, MAX_TOKEN_SERIES);
  const hasOther = metrics.tokens.series.length > MAX_TOKEN_SERIES;
  const series: ChartSeries[] = top.map((s, i) => ({ key: keyOf(s.runtime, s.model), label: keyOf(s.runtime, s.model), color: TOKEN_COLORS[i % TOKEN_COLORS.length] }));
  if (hasOther) series.push({ key: '__other', label: t('Other'), color: 'textMuted' });
  const buckets = metrics.tokens.buckets.map((b) => {
    const values = top.map((s) => b.series.find((x) => x.runtime === s.runtime && x.model === s.model)?.totalTokens ?? 0);
    if (hasOther) values.push(Math.max(0, b.totalTokens - values.reduce((a, v) => a + v, 0)));
    return { startUtc: b.bucketStartUtc, values };
  });
  return { series, buckets };
}

/** Share of each link state in the timeline, in milliseconds, for the strip and its spoken summary. */
export function linkStateDurations(metrics: HarborMetrics): Record<HarborLinkSegmentState, number> {
  const totals: Record<HarborLinkSegmentState, number> = { Unknown: 0, Connected: 0, Reconnecting: 0, Down: 0 };
  for (const s of metrics.link.segments) totals[s.state] += Math.max(0, parseMs(s.endUtc) - parseMs(s.startUtc));
  return totals;
}

const SEGMENT_COLORS: Record<HarborLinkSegmentState, keyof Palette> = {
  Connected: 'success',
  Reconnecting: 'warning',
  Down: 'danger',
  Unknown: 'track',
};

interface BucketChartProps {
  testID: string;
  title: string;
  summary: string;
  series: ChartSeries[];
  buckets: ChartBucket[];
  stepMinutes: number;
  hours: number;
  format: (value: number) => string;
  /** A horizontal reference line (the slot chart's capacity), drawn across the plot. */
  reference?: { value: number; label: string };
  /** When set, the first series is drawn as a bar and this series index as a marker on top of it (peak and average). */
  markerSeries?: number;
}

/**
 * A bucketed bar chart drawn with Views, like Mission History: stacked series, an optional reference line, touch and
 * drag to read a bucket, and an adjustable element for screen readers that steps through the buckets.
 */
function BucketChart({ testID, title, summary, series, buckets, stepMinutes, hours, format, reference, markerSeries }: BucketChartProps) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const [picked, setPicked] = useState<number | null>(null);
  const [width, setWidth] = useState(0);
  const stacked = markerSeries === undefined;
  const heights = buckets.map((b) => (stacked ? b.values.reduce((a, v) => a + v, 0) : Math.max(...b.values)));
  const yMax = Math.max(1, reference?.value ?? 0, ...heights);
  const shown = picked !== null ? buckets[picked] : null;
  const labelIndexes = Array.from(new Set(buckets.length > 1 ? [0, Math.floor((buckets.length - 1) / 2), buckets.length - 1] : [0]));

  const describe = (b: ChartBucket) =>
    `${formatTooltipTime(parseMs(b.startUtc))}: ${series.map((s, i) => `${s.label} ${format(b.values[i] ?? 0)}`).join(', ')}`;
  const pick = (event: GestureResponderEvent) => {
    if (!width || buckets.length === 0) return;
    const index = Math.floor((event.nativeEvent.locationX / width) * buckets.length);
    setPicked(Math.max(0, Math.min(buckets.length - 1, index)));
  };
  const step = (delta: number) => {
    if (buckets.length === 0) return;
    const from = picked ?? (delta > 0 ? -1 : buckets.length);
    setPicked(Math.max(0, Math.min(buckets.length - 1, from + delta)));
  };

  if (buckets.length === 0) return <AppText muted style={styles.empty}>{t('No data for this time range')}</AppText>;

  return (
    <View>
      <View
        testID={testID}
        accessible
        accessibilityRole="adjustable"
        accessibilityLabel={`${title}: ${summary}`}
        accessibilityValue={shown ? { text: describe(shown) } : undefined}
        accessibilityHint={t('Swipe up or down to read each time period.')}
        accessibilityActions={[{ name: 'increment' }, { name: 'decrement' }]}
        onAccessibilityAction={(e) => step(e.nativeEvent.actionName === 'increment' ? 1 : -1)}
        onLayout={(e: LayoutChangeEvent) => setWidth(e.nativeEvent.layout.width)}
        onStartShouldSetResponder={() => true}
        onMoveShouldSetResponder={() => true}
        onResponderGrant={pick}
        onResponderMove={pick}
        style={[styles.plot, { borderBottomColor: colors.border }]}
      >
        {buckets.map((b, i) => {
          const h = (n: number) => (n / yMax) * CHART_HEIGHT;
          const opacity = picked === i ? 1 : 0.85;
          if (!stacked) {
            const bar = b.values[0] ?? 0;
            const marker = b.values[markerSeries] ?? 0;
            return (
              <View key={b.startUtc} style={styles.column}>
                {marker > 0 ? <View style={[styles.marker, { bottom: h(marker), backgroundColor: colors[series[markerSeries].color] }]} /> : null}
                {bar > 0 ? <View style={{ height: h(bar), backgroundColor: colors[series[0].color], opacity: picked === i ? 0.7 : 0.45 }} /> : null}
              </View>
            );
          }
          return (
            <View key={b.startUtc} style={styles.column}>
              {b.values.map((v, si) => (v > 0 ? <View key={series[si].key} style={{ height: h(v), backgroundColor: colors[series[si].color], opacity }} /> : null)).reverse()}
            </View>
          );
        })}
        {reference && reference.value > 0 ? (
          <View pointerEvents="none" style={[styles.reference, { bottom: (reference.value / yMax) * CHART_HEIGHT, borderColor: colors.danger }]} />
        ) : null}
      </View>
      <View style={styles.axis}>
        {labelIndexes.map((i) => (
          <AppText key={i} variant="caption" muted maxFontSizeMultiplier={CHROME_MAX_FONT_SCALE} style={styles.axisLabel}>
            {formatBucketLabel(parseMs(buckets[i].startUtc), stepMinutes, hours)}
          </AppText>
        ))}
      </View>
      {shown ? (
        <View style={[styles.tooltip, { borderColor: colors.border, backgroundColor: colors.surfaceRaised }]} accessibilityLiveRegion="polite" testID={`${testID}-tooltip`}>
          <AppText variant="label">{formatTooltipTime(parseMs(shown.startUtc))}</AppText>
          {series.map((s, i) => <AppText key={s.key} variant="caption">{`${s.label}: ${format(shown.values[i] ?? 0)}`}</AppText>)}
          {reference ? <AppText variant="caption">{`${reference.label}: ${format(reference.value)}`}</AppText> : null}
        </View>
      ) : null}
      <Legend series={series} reference={reference?.label} />
    </View>
  );
}

function Legend({ series, reference }: { series: ChartSeries[]; reference?: string }) {
  const { colors } = useTheme();
  return (
    <View style={styles.legend}>
      {series.map((s) => (
        <View key={s.key} style={styles.legendItem}>
          <View style={[styles.swatch, { backgroundColor: colors[s.color] }]} />
          <AppText variant="caption" muted>{s.label}</AppText>
        </View>
      ))}
      {reference ? (
        <View style={styles.legendItem}>
          <View style={[styles.swatchLine, { borderColor: colors.danger }]} />
          <AppText variant="caption" muted>{reference}</AppText>
        </View>
      ) : null}
    </View>
  );
}

function Stats({ items, testID }: { items: { label: string; value: string }[]; testID?: string }) {
  return (
    <View style={styles.stats} testID={testID}>
      {items.map((s) => (
        <View key={s.label} style={styles.stat} accessible accessibilityLabel={`${s.label}: ${s.value}`}>
          <AppText variant="heading">{s.value}</AppText>
          <AppText variant="caption" muted>{s.label}</AppText>
        </View>
      ))}
    </View>
  );
}

/** A tiny bar sparkline of per-bucket medians; empty buckets show as a flat track. */
function Sparkline({ values }: { values: (number | null)[] }) {
  const { colors } = useTheme();
  const max = Math.max(1, ...values.map((v) => v ?? 0));
  return (
    <View style={styles.sparkline} importantForAccessibility="no-hide-descendants" accessibilityElementsHidden>
      {values.map((v, i) => (
        <View key={i} style={styles.sparkColumn}>
          <View style={{ height: v === null ? 1 : Math.max(2, (v / max) * 24), backgroundColor: v === null ? colors.track : colors.primary }} />
        </View>
      ))}
    </View>
  );
}

function LaunchSpeedRow({ row }: { row: HarborLaunchSpeed }) {
  const { t, locale } = useLocale();
  const { colors } = useTheme();
  const ms = (v: number | null) => formatDurationMs(t, locale, v);
  const jobs = t('{{count}} jobs', { count: row.jobCount });
  const firstOutput = `${t('First output')}: ${ms(row.firstOutputMedianMs)} / ${ms(row.firstOutputP95Ms)}`;
  const runtime = `${t('Runtime')}: ${ms(row.durationMedianMs)} / ${ms(row.durationP95Ms)}`;
  // The spoken label carries every visible line, then what the "a / b" pairs mean.
  const label = `${row.runtime}, ${jobs}. ${firstOutput}. ${runtime}. ${t('Median / p95')}.`;
  return (
    <View style={[styles.speedRow, { borderColor: colors.border }]} accessible accessibilityLabel={label} testID={`harbor-speed-${row.runtime}`}>
      <View style={styles.speedHead}>
        <AppText variant="label" style={styles.flex}>{row.runtime}</AppText>
        <AppText variant="caption" muted>{jobs}</AppText>
      </View>
      <AppText variant="caption">{firstOutput}</AppText>
      <AppText variant="caption">{runtime}</AppText>
      <Sparkline values={row.firstOutputMedianMsByBucket} />
    </View>
  );
}

/**
 * Harbor detail charts (the dashboard's Harbor metrics): jobs over time, slot usage against capacity, the link health
 * strip with heartbeat round trips, launch speed per runtime, and token usage, for the last hour, day, or week. The
 * Admiral computes every number (GET /api/v1/harbors/{id}/metrics); this only draws them.
 */
export function HarborMetricsSection({ harborId }: { harborId: string }) {
  const { t, locale, formatDateTime } = useLocale();
  const { colors } = useTheme();
  const [range, setRange] = useState<HarborMetricsRange>('24h');
  const rangeDef = RANGES.find((r) => r.value === range) ?? RANGES[1];
  const { data, loading, error, reload } = useLoad(() => getHarborMetrics(harborId, range), [harborId, range], { fallbackError: t('Failed to load Harbor metrics.') });
  const metrics = data;
  const jobs = useMemo(() => (metrics ? jobSeries(t, metrics) : null), [metrics, t]);
  const tokens = useMemo(() => (metrics ? tokenSeries(t, metrics) : null), [metrics, t]);
  const durations = useMemo(() => (metrics ? linkStateDurations(metrics) : null), [metrics]);
  const ms = (v: number | null | undefined) => formatDurationMs(t, locale, v ?? null);
  const count = (v: number) => String(Math.round(v * 100) / 100);

  return (
    <View testID="harbor-metrics">
      <View style={styles.toolbar}>
        <AppText variant="heading" accessibilityRole="header" style={styles.flex}>{t('Metrics')}</AppText>
        <IconButton icon="refresh" label={t('Refresh')} onPress={() => void reload()} testID="harbor-metrics-refresh" />
      </View>
      <View style={styles.pad}>
        <SegmentedControl
          label={t('Time range')}
          value={range}
          onChange={setRange}
          options={RANGES.map((r) => ({ value: r.value, label: t(r.label), testID: `harbor-metrics-range-${r.value}` }))}
        />
      </View>
      {!metrics ? (
        <AppText muted={!error} color={error ? 'dangerText' : undefined} style={styles.empty} testID="harbor-metrics-pending">
          {error || (loading ? t('Loading Harbor metrics...') : t('No data for this time range'))}
        </AppText>
      ) : (
        <>
          <FieldCard title={t('Jobs over time')} testID="harbor-metrics-jobs">
            <View style={styles.inner}>
              <Stats items={[
                { label: t('Missions finished'), value: String(metrics.jobs.missionsFinished) },
                { label: t('Missions failed'), value: String(metrics.jobs.missionsFailed) },
                { label: t('Interactive finished'), value: String(metrics.jobs.interactiveFinished) },
                { label: t('Interactive failed'), value: String(metrics.jobs.interactiveFailed) },
                { label: t('Running'), value: String(metrics.jobs.running) },
              ]} />
              {jobs ? (
                <BucketChart
                  testID="harbor-jobs-chart"
                  title={t('Jobs over time')}
                  summary={t('{{finished}} finished, {{failed}} failed', {
                    finished: metrics.jobs.missionsFinished + metrics.jobs.interactiveFinished,
                    failed: metrics.jobs.missionsFailed + metrics.jobs.interactiveFailed,
                  })}
                  series={jobs.series}
                  buckets={jobs.buckets}
                  stepMinutes={metrics.bucketMinutes}
                  hours={rangeDef.hours}
                  format={(v) => String(v)}
                />
              ) : null}
            </View>
          </FieldCard>

          <FieldCard title={t('Slot usage')} testID="harbor-metrics-slots">
            <View style={styles.inner}>
              <Stats items={[
                { label: t('Peak'), value: String(metrics.slots.peak) },
                { label: t('Average'), value: count(metrics.slots.average) },
                { label: t('Max slots'), value: String(metrics.slots.maxConcurrentJobs) },
              ]} />
              <BucketChart
                testID="harbor-slots-chart"
                title={t('Slot usage')}
                summary={t('peak {{peak}}, average {{average}} of {{max}} slots', { peak: metrics.slots.peak, average: count(metrics.slots.average), max: metrics.slots.maxConcurrentJobs })}
                series={[
                  { key: 'peak', label: t('Peak'), color: 'info' },
                  { key: 'average', label: t('Average'), color: 'primary' },
                ]}
                buckets={metrics.slots.buckets.map((b) => ({ startUtc: b.bucketStartUtc, values: [b.peak, b.average] }))}
                markerSeries={1}
                reference={{ value: metrics.slots.maxConcurrentJobs, label: t('Max slots') }}
                stepMinutes={metrics.bucketMinutes}
                hours={rangeDef.hours}
                format={count}
              />
            </View>
          </FieldCard>

          <FieldCard title={t('Link health')} testID="harbor-metrics-link">
            <View style={styles.inner}>
              <View
                testID="harbor-link-strip"
                accessible
                accessibilityLabel={t('Link health: connected {{percent}}, {{disconnects}} disconnects, median round trip {{rtt}}', {
                  percent: metrics.link.connectedPercent === null ? '-' : `${Math.round(metrics.link.connectedPercent)}%`,
                  disconnects: metrics.link.disconnects,
                  rtt: ms(metrics.link.roundTripMedianMs),
                })}
                style={[styles.strip, { backgroundColor: colors.track }]}
              >
                {metrics.link.segments.map((s) => {
                  const span = parseMs(s.endUtc) - parseMs(s.startUtc);
                  return span > 0 ? <View key={`${s.startUtc}-${s.state}`} style={{ flex: span, backgroundColor: colors[SEGMENT_COLORS[s.state]] }} /> : null;
                })}
              </View>
              <View style={styles.legend}>
                {(['Connected', 'Reconnecting', 'Down', 'Unknown'] as HarborLinkSegmentState[]).filter((s) => (durations?.[s] ?? 0) > 0 || s !== 'Unknown').map((s) => (
                  <View key={s} style={styles.legendItem}>
                    <View style={[styles.swatch, { backgroundColor: colors[SEGMENT_COLORS[s]] }]} />
                    <AppText variant="caption" muted>{t(s)}</AppText>
                  </View>
                ))}
              </View>
              <Stats testID="harbor-link-stats" items={[
                { label: t('Connected'), value: metrics.link.connectedPercent === null ? '-' : `${Math.round(metrics.link.connectedPercent)}%` },
                { label: t('Disconnects'), value: String(metrics.link.disconnects) },
                { label: t('Reconnects'), value: metrics.link.reconnectCount === null ? '-' : String(metrics.link.reconnectCount) },
                { label: t('Median round trip'), value: ms(metrics.link.roundTripMedianMs) },
              ]} />
              {metrics.link.lastReconnectUtc ? (
                <AppText variant="caption" muted>{`${t('Last reconnect')}: ${formatDateTime(metrics.link.lastReconnectUtc)}`}</AppText>
              ) : null}
              <BucketChart
                testID="harbor-rtt-chart"
                title={t('Round trip')}
                summary={t('median {{rtt}}', { rtt: ms(metrics.link.roundTripMedianMs) })}
                series={[{ key: 'average', label: t('Average round trip'), color: 'primary' }]}
                buckets={metrics.link.roundTrip.map((b) => ({ startUtc: b.bucketStartUtc, values: [b.averageMs ?? 0] }))}
                stepMinutes={metrics.bucketMinutes}
                hours={rangeDef.hours}
                format={(v) => ms(Math.round(v))}
              />
            </View>
          </FieldCard>

          <FieldCard title={t('Launch speed')} testID="harbor-metrics-speed">
            <View style={styles.inner}>
              <AppText variant="caption" muted>{t('Median / p95 per runtime, for jobs that ended in this range')}</AppText>
              {metrics.launchSpeed.length === 0 ? (
                <AppText muted>{t('No jobs ended in this time range')}</AppText>
              ) : metrics.launchSpeed.map((row) => <LaunchSpeedRow key={row.runtime} row={row} />)}
            </View>
          </FieldCard>

          <FieldCard title={t('Token usage')} testID="harbor-metrics-tokens">
            <View style={styles.inner}>
              <Stats items={[
                { label: t('Total'), value: formatTokens(metrics.tokens.totalTokens) },
                { label: t('Input'), value: formatTokens(metrics.tokens.inputTokens) },
                { label: t('Output'), value: formatTokens(metrics.tokens.outputTokens) },
                { label: t('Cached'), value: formatTokens(metrics.tokens.cachedTokens) },
              ]} />
              {metrics.tokens.estimatedCount > 0 ? (
                <AppText variant="caption" muted>{t('{{estimated}} of {{total}} records estimated', { estimated: metrics.tokens.estimatedCount, total: metrics.tokens.recordCount })}</AppText>
              ) : null}
              {tokens && tokens.series.length > 0 ? (
                <BucketChart
                  testID="harbor-tokens-chart"
                  title={t('Token usage')}
                  summary={t('{{total}} tokens', { total: formatTokens(metrics.tokens.totalTokens) })}
                  series={tokens.series}
                  buckets={tokens.buckets}
                  stepMinutes={metrics.bucketMinutes}
                  hours={rangeDef.hours}
                  format={formatTokens}
                />
              ) : (
                <AppText muted>{t('No token usage for this time range')}</AppText>
              )}
            </View>
          </FieldCard>
        </>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  toolbar: { flexDirection: 'row', alignItems: 'center', marginHorizontal: spacing.md, marginTop: spacing.md },
  pad: { marginHorizontal: spacing.md, marginBottom: spacing.sm },
  inner: { padding: spacing.md, gap: spacing.sm },
  flex: { flex: 1 },
  empty: { padding: spacing.md, textAlign: 'center' },
  // Wraps at large text sizes instead of pushing numbers off the card.
  stats: { flexDirection: 'row', flexWrap: 'wrap', columnGap: spacing.lg, rowGap: spacing.sm },
  stat: { alignItems: 'flex-start' },
  plot: { height: CHART_HEIGHT, flexDirection: 'row', alignItems: 'flex-end', gap: 1, borderBottomWidth: 1 },
  column: { flex: 1, height: '100%', justifyContent: 'flex-end', minWidth: 1 },
  marker: { position: 'absolute', left: 0, right: 0, height: 3 },
  reference: { position: 'absolute', left: 0, right: 0, borderTopWidth: 2, borderStyle: 'dashed' },
  // Axis labels are chart chrome: capped, and each may wrap in its third of the width instead of running together.
  axis: { flexDirection: 'row', justifyContent: 'space-between', gap: spacing.sm },
  axisLabel: { flexShrink: 1 },
  tooltip: { borderWidth: 1, borderRadius: radius.sm, padding: spacing.sm, gap: 2 },
  legend: { flexDirection: 'row', flexWrap: 'wrap', columnGap: spacing.lg, rowGap: spacing.xs },
  legendItem: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs },
  swatch: { width: 12, height: 12, borderRadius: 2 },
  swatchLine: { width: 14, borderTopWidth: 2, borderStyle: 'dashed' },
  strip: { height: 18, flexDirection: 'row', borderRadius: radius.sm, overflow: 'hidden' },
  speedRow: { borderTopWidth: StyleSheet.hairlineWidth, paddingTop: spacing.sm, gap: 2 },
  speedHead: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  sparkline: { height: 26, flexDirection: 'row', alignItems: 'flex-end', gap: 1, marginTop: spacing.xs },
  sparkColumn: { flex: 1, justifyContent: 'flex-end' },
});
