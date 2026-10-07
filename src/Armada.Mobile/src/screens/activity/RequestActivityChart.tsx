import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import type { RequestHistorySummaryResult } from '@dashboard/types/models';
import { normalizeSummaryBuckets, type ActivityRangeId } from '@dashboard/lib/requestHistory';
import { AppText } from '../../components/ui/AppText';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing } from '../../theme/typography';

/**
 * Bucketed request volume with the success / failure split (the dashboard's request activity chart), drawn with
 * Views. Tapping a bar shows its numbers below the chart (the dashboard's hover tooltip).
 */
export function RequestActivityChart({ summary, rangeId }: { summary: RequestHistorySummaryResult | null; rangeId: ActivityRangeId }) {
  const { t, formatDateTime } = useLocale();
  const { colors } = useTheme();
  const [picked, setPicked] = useState<string | null>(null);
  const buckets = normalizeSummaryBuckets(summary, rangeId);
  const total = buckets.reduce((sum, b) => sum + b.totalCount, 0);
  const max = Math.max(...buckets.map((b) => b.totalCount), 1);
  if (total === 0) {
    return <AppText muted style={styles.empty} testID="request-chart-empty">{t('No requests in this time range. Widen the range above to see older traffic.')}</AppText>;
  }
  const bucket = buckets.find((b) => b.bucketStartUtc === picked) ?? null;
  return (
    <View testID="request-chart">
      <View style={styles.plot} accessible accessibilityLabel={`${t('Requests')}: ${total}`}>
        {buckets.map((b) => {
          const height = b.totalCount > 0 ? Math.max((b.totalCount / max) * 100, 6) : 2;
          const failure = b.totalCount > 0 ? (b.failureCount / b.totalCount) * 100 : 0;
          return (
            <Pressable key={b.bucketStartUtc} style={styles.column} onPress={() => setPicked(b.bucketStartUtc)} accessibilityRole="button" accessibilityLabel={`${formatDateTime(b.bucketStartUtc)}: ${b.totalCount}`}>
              <View style={[styles.bar, { height: `${height}%`, backgroundColor: b.totalCount > 0 ? colors.success : colors.border, opacity: picked === b.bucketStartUtc ? 1 : 0.85 }]}>
                {failure > 0 ? <View style={{ height: `${failure}%`, backgroundColor: colors.danger }} /> : null}
              </View>
            </Pressable>
          );
        })}
      </View>
      <View style={styles.axis}>
        <AppText variant="caption" muted>{formatDateTime(buckets[0].bucketStartUtc)}</AppText>
        <AppText variant="caption" muted>{formatDateTime(buckets[buckets.length - 1].bucketEndUtc)}</AppText>
      </View>
      {bucket ? (
        <View style={[styles.tip, { borderColor: colors.border, backgroundColor: colors.surface }]} testID="request-chart-tip">
          <AppText variant="label">{`${formatDateTime(bucket.bucketStartUtc)} - ${formatDateTime(bucket.bucketEndUtc)}`}</AppText>
          <AppText variant="caption">{t('{{count}} total', { count: bucket.totalCount })}</AppText>
          <AppText variant="caption">{t('{{count}} success', { count: bucket.successCount })}</AppText>
          <AppText variant="caption">{t('{{count}} failed', { count: bucket.failureCount })}</AppText>
          <AppText variant="caption">{t('{{ms}} ms avg', { ms: bucket.averageDurationMs.toFixed(2) })}</AppText>
        </View>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  empty: { padding: spacing.md },
  plot: { height: 120, flexDirection: 'row', alignItems: 'flex-end', gap: 1, paddingHorizontal: spacing.xs },
  column: { flex: 1, height: '100%', justifyContent: 'flex-end' },
  bar: { width: '100%', borderTopLeftRadius: 2, borderTopRightRadius: 2, overflow: 'hidden', justifyContent: 'flex-start' },
  axis: { flexDirection: 'row', justifyContent: 'space-between', marginTop: spacing.xs },
  tip: { marginTop: spacing.sm, padding: spacing.sm, borderWidth: StyleSheet.hairlineWidth, borderRadius: radius.sm, gap: 2 },
});
