import { useState } from 'react';
import { ScrollView, StyleSheet, View } from 'react-native';
import { getTokenUsage } from '@dashboard/api/client';
import type { TokenUsageSummaryResult } from '@dashboard/types/models';
import { formatTokens, TOKEN_USAGE_TIME_RANGES, type TokenUsageRangeValue } from '@dashboard/lib/tokenUsage';
import { DetailBody, FieldCard } from '../../components/resource/DetailParts';
import { AppText } from '../../components/ui/AppText';
import { SegmentedControl } from '../../components/ui/SegmentedControl';
import { useLocale } from '../../i18n/LocaleContext';
import { useLoad } from '../../resource/useLoad';
import { useTheme } from '../../theme/ThemeContext';
import type { Palette } from '../../theme/palette';
import { spacing } from '../../theme/typography';

type Metric = 'total' | 'byType';
interface Series { key: string; label: string; color: keyof Palette }

const MODEL_COLORS: (keyof Palette)[] = ['primary', 'success', 'danger', 'warning', 'info', 'textMuted'];
const TYPE_SERIES = (t: (s: string) => string): Series[] => [
  { key: 'input', label: t('Input'), color: 'primary' },
  { key: 'output', label: t('Output'), color: 'success' },
  { key: 'cached', label: t('Cached'), color: 'warning' },
];

/** Series and per-bucket values for the usage-over-time chart (by model, or by token type). */
export function usageSeries(data: TokenUsageSummaryResult | null, metric: Metric, t: (s: string) => string): { series: Series[]; values: number[][] } {
  const buckets = data?.buckets || [];
  if (metric === 'byType') return { series: TYPE_SERIES(t), values: buckets.map((b) => [b.inputTokens, b.outputTokens, b.cachedTokens]) };
  const series = (data?.byModel || []).map((m, i) => ({ key: m.model, label: m.model, color: MODEL_COLORS[i % MODEL_COLORS.length] }));
  return { series, values: buckets.map((b) => series.map((s) => b.models.find((m) => m.model === s.key)?.totalTokens ?? 0)) };
}

function Legend({ series }: { series: Series[] }) {
  const { colors } = useTheme();
  return (
    <View style={styles.legend}>
      {series.map((s) => (
        <View key={s.key} style={styles.legendItem}>
          <View style={[styles.swatch, { backgroundColor: colors[s.color] }]} />
          <AppText variant="caption">{s.label}</AppText>
        </View>
      ))}
    </View>
  );
}

/**
 * Activity > Token Usage: totals (total, input, output, cached, estimated records), usage over time as stacked bars
 * (by model or by token type), and usage by model, for the last hour, day, week, or month.
 */
export function TokenUsageTab() {
  const { t, formatDateTime } = useLocale();
  const { colors } = useTheme();
  const [range, setRange] = useState<TokenUsageRangeValue>('day');
  const [metric, setMetric] = useState<Metric>('total');
  const r = TOKEN_USAGE_TIME_RANGES.find((x) => x.value === range) ?? TOKEN_USAGE_TIME_RANGES[1];
  const { data, loading, refreshing, refresh } = useLoad(async () => {
    const end = new Date();
    const start = new Date(end.getTime() - r.hours * 3600000);
    try {
      return await getTokenUsage({ fromUtc: start.toISOString(), toUtc: end.toISOString(), bucketMinutes: r.stepMinutes });
    } catch {
      return null;
    }
  }, [range]);
  const { series, values } = usageSeries(data, metric, t);
  const totals = values.map((row) => row.reduce((a, b) => a + b, 0));
  const max = Math.max(1, ...totals);
  const byModel = data?.byModel || [];
  const maxModel = Math.max(1, ...byModel.map((m) => m.totalTokens));
  const buckets = data?.buckets || [];

  return (
    <DetailBody embedded refreshing={refreshing} onRefresh={() => void refresh()} testID="activity-tokens">
      <View style={styles.pad}>
        <SegmentedControl label={t('Time range')} value={range} onChange={setRange} options={TOKEN_USAGE_TIME_RANGES.map((x) => ({ value: x.value, label: t(x.label), testID: `tokens-range-${x.value}` }))} />
        <SegmentedControl label={t('Metric')} value={metric} onChange={setMetric} options={[{ value: 'total', label: t('Total') }, { value: 'byType', label: t('By token type') }]} />
      </View>
      <View style={styles.stats} testID="tokens-stats">
        {[
          { label: t('Total'), value: data?.totalTokens ?? 0, color: 'text' as const },
          { label: t('Input'), value: data?.inputTokens ?? 0, color: 'primary' as const },
          { label: t('Output'), value: data?.outputTokens ?? 0, color: 'success' as const },
          { label: t('Cached'), value: data?.cachedTokens ?? 0, color: 'warning' as const },
        ].map((s) => (
          <View key={s.label} style={[styles.stat, { borderColor: colors.border, backgroundColor: colors.surface }]} accessible accessibilityLabel={`${s.label}: ${formatTokens(s.value)}`}>
            <AppText variant="heading" color={s.color}>{formatTokens(s.value)}</AppText>
            <AppText variant="caption" muted>{s.label}</AppText>
          </View>
        ))}
      </View>
      {(data?.estimatedCount ?? 0) > 0 ? (
        <AppText variant="caption" muted style={styles.pad}>{t('{{estimated}} of {{total}} records estimated', { estimated: data?.estimatedCount ?? 0, total: data?.recordCount ?? 0 })}</AppText>
      ) : null}

      <FieldCard title={t('Usage over time')}>
        {loading && !data ? <AppText muted style={styles.inner}>{t('Loading token usage...')}</AppText> : buckets.length === 0 || series.length === 0 ? (
          <AppText muted style={styles.inner}>{t('No token usage for this time range')}</AppText>
        ) : (
          <View style={styles.inner}>
            <AppText variant="caption" muted>{`${t('Tokens')}: 0 - ${formatTokens(max)}`}</AppText>
            <ScrollView horizontal showsHorizontalScrollIndicator={false}>
              <View style={styles.plot} testID="tokens-time-chart">
                {values.map((row, i) => (
                  <View key={buckets[i].bucketStartUtc} style={styles.column} accessible accessibilityLabel={`${formatDateTime(buckets[i].bucketStartUtc)}: ${formatTokens(totals[i])}`}>
                    <View style={[styles.stack, { height: `${(totals[i] / max) * 100}%` }]}>
                      {row.map((v, si) => (v > 0 ? <View key={series[si].key} style={{ flex: v, backgroundColor: colors[series[si].color] }} /> : null)).reverse()}
                    </View>
                  </View>
                ))}
              </View>
            </ScrollView>
            <View style={styles.axis}>
              <AppText variant="caption" muted>{formatDateTime(buckets[0].bucketStartUtc)}</AppText>
              <AppText variant="caption" muted>{formatDateTime(buckets[buckets.length - 1].bucketEndUtc)}</AppText>
            </View>
            <Legend series={series} />
          </View>
        )}
      </FieldCard>

      <FieldCard title={t('Usage by model')} testID="tokens-by-model">
        {loading && !data ? <AppText muted style={styles.inner}>{t('Loading token usage...')}</AppText> : byModel.length === 0 ? (
          <AppText muted style={styles.inner}>{t('No token usage for this time range')}</AppText>
        ) : (
          <View style={styles.inner}>
            {byModel.map((m) => {
              const segs = metric === 'byType'
                ? [{ v: m.inputTokens, c: 'primary' as const }, { v: m.outputTokens, c: 'success' as const }, { v: m.cachedTokens, c: 'warning' as const }]
                : [{ v: m.totalTokens, c: 'primary' as const }];
              return (
                <View key={m.model} style={styles.modelRow} accessible accessibilityLabel={`${m.model}: ${formatTokens(m.totalTokens)}, ${t('Input')} ${formatTokens(m.inputTokens)}, ${t('Output')} ${formatTokens(m.outputTokens)}, ${t('Cached')} ${formatTokens(m.cachedTokens)}`}>
                  <View style={styles.modelHead}>
                    <AppText variant="label" numberOfLines={1} style={styles.flex}>{m.model}</AppText>
                    <AppText variant="caption" muted>{formatTokens(m.totalTokens)}</AppText>
                  </View>
                  <View style={[styles.track, { backgroundColor: colors.background }]}>
                    <View style={[styles.fill, { width: `${(m.totalTokens / maxModel) * 100}%` }]}>
                      {segs.map((seg, i) => (seg.v > 0 ? <View key={i} style={{ flex: seg.v, backgroundColor: colors[seg.c] }} /> : null))}
                    </View>
                  </View>
                </View>
              );
            })}
            {metric === 'byType' ? <Legend series={TYPE_SERIES(t)} /> : null}
          </View>
        )}
      </FieldCard>
    </DetailBody>
  );
}

const styles = StyleSheet.create({
  pad: { marginHorizontal: spacing.md, marginBottom: spacing.sm },
  inner: { padding: spacing.md, gap: spacing.sm },
  stats: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm, marginHorizontal: spacing.md, marginBottom: spacing.md },
  stat: { flexGrow: 1, minWidth: 120, borderWidth: StyleSheet.hairlineWidth, borderRadius: 10, padding: spacing.md },
  plot: { height: 140, flexDirection: 'row', alignItems: 'flex-end', gap: 2 },
  column: { width: 6, height: '100%', justifyContent: 'flex-end' },
  stack: { width: '100%', overflow: 'hidden', borderTopLeftRadius: 1, borderTopRightRadius: 1 },
  axis: { flexDirection: 'row', justifyContent: 'space-between' },
  legend: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.md },
  legendItem: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs },
  swatch: { width: 10, height: 10, borderRadius: 2 },
  modelRow: { gap: spacing.xs },
  modelHead: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  flex: { flex: 1 },
  track: { height: 12, borderRadius: 3, overflow: 'hidden' },
  fill: { height: '100%', flexDirection: 'row' },
});
