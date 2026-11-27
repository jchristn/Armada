import { useEffect, useMemo, useState } from 'react';
import { RefreshControl, ScrollView, StyleSheet, View } from 'react-native';
import { getConcurrencyMetrics, listVessels } from '@dashboard/api/client';
import { listAll } from '@dashboard/api/paging';
import { bucketBars, CONCURRENCY_RANGES, WAIT_GROUPS, type WaitGroupKey } from '@dashboard/lib/concurrencyCharts';
import { formatWaitDuration, waitGateLabel } from '@dashboard/lib/missionWaits';
import type { ConcurrencyMetrics, Vessel } from '@dashboard/types/models';
import { AppText, ErrorState, LoadingState, ProgressBar, Section, SegmentedControl, SelectField } from '../../components/ui';
import { KeyValueRow } from '../../components/ui/KeyValueRow';
import { KpiCard, KpiGrid } from '../../components/ui/KpiCard';
import { useQuery } from '../../data/useQuery';
import { useLocale } from '../../i18n/LocaleContext';
import { spacing } from '../../theme/typography';

type RangeKey = typeof CONCURRENCY_RANGES[number]['key'];

/** Pure: minutes waited per gate group over the whole window, in WAIT_GROUPS order. */
export function groupTotals(metrics: ConcurrencyMetrics | null): Record<WaitGroupKey, number> {
  const totals: Record<WaitGroupKey, number> = { captain: 0, vessel: 0, capacity: 0, readiness: 0 };
  for (const bar of bucketBars(metrics)) for (const g of WAIT_GROUPS) totals[g.key] += bar.minutes[g.key];
  return totals;
}

/** Pure: the window start and end for a range key, ending now. */
export function concurrencyWindow(key: RangeKey, now: Date = new Date()): { from: string; to: string } {
  const range = CONCURRENCY_RANGES.find(r => r.key === key) ?? CONCURRENCY_RANGES[1];
  return { from: new Date(now.getTime() - range.hours * 3600 * 1000).toISOString(), to: now.toISOString() };
}

/**
 * Activity > Concurrency (the dashboard's ConcurrencyPanel): how long missions waited and on what, from recorded wait
 * intervals (GET /api/v1/metrics/concurrency). Slot wait tiles, wait time by cause, the busiest buckets, and the
 * per-gate totals, for a window and optionally one vessel.
 */
export function ConcurrencyTab() {
  const { t, formatDateTime } = useLocale();
  const [rangeKey, setRangeKey] = useState<RangeKey>('24h');
  const [vesselId, setVesselId] = useState('');
  const [vessels, setVessels] = useState<Vessel[]>([]);
  useEffect(() => { listAll(listVessels).then(r => setVessels(r.objects || [])).catch(() => {}); }, []);
  const query = useQuery<ConcurrencyMetrics>(() => {
    const w = concurrencyWindow(rangeKey);
    return getConcurrencyMetrics({ from: w.from, to: w.to, vesselId: vesselId || undefined });
  }, [rangeKey, vesselId], t('Concurrency metrics unavailable.'));
  const metrics = query.data;
  const totals = useMemo(() => groupTotals(metrics ?? null), [metrics]);
  const totalMinutes = Object.values(totals).reduce((a, b) => a + b, 0);
  const busiest = useMemo(() => bucketBars(metrics ?? null).filter(b => b.totalMinutes > 0).sort((a, b) => b.totalMinutes - a.totalMinutes).slice(0, 5), [metrics]);
  const dur = (s: number | null) => (s === null ? '-' : formatWaitDuration(s));

  return (
    <ScrollView contentContainerStyle={styles.content} testID="concurrency"
      refreshControl={<RefreshControl refreshing={query.loading && !!metrics} onRefresh={() => { void query.reload(); }} />}>
      <SegmentedControl
        label={t('Window')}
        value={rangeKey}
        onChange={setRangeKey}
        options={CONCURRENCY_RANGES.map(r => ({ value: r.key, label: t(r.label), testID: `concurrency-range-${r.key}` }))}
      />
      <SelectField
        label={t('Vessel')}
        value={vesselId}
        onChange={setVesselId}
        options={vessels.map(v => ({ value: v.id, label: v.name }))}
        placeholder={t('All vessels')}
        allowEmpty
        closeLabel={t('Close')}
        searchLabel={t('Search')}
        testID="concurrency-vessel"
      />
      {query.error && !metrics ? <ErrorState title={t('Concurrency metrics unavailable.')} message={query.error} retryLabel={t('Retry')} onRetry={() => { void query.reload(); }} /> : null}
      {!metrics && !query.error ? <LoadingState label={t('Loading...')} /> : null}
      {metrics ? (
        <View style={styles.body}>
          <KpiGrid>
            <KpiCard label={t('Slot wait p50')} value={dur(metrics.slotWait.p50Seconds)} testID="concurrency-p50" />
            <KpiCard label={t('Slot wait p95')} value={dur(metrics.slotWait.p95Seconds)} />
            <KpiCard label={t('Longest slot wait')} value={dur(metrics.slotWait.maxSeconds)} />
            <KpiCard label={t('Missions that waited')} value={metrics.slotWait.missions} />
          </KpiGrid>
          {metrics.byGate.length === 0 ? (
            <AppText muted testID="concurrency-empty">{t('No waits recorded in this window.')}</AppText>
          ) : (
            <>
              <Section title={t('Wait time by cause')}>
                {WAIT_GROUPS.map(g => {
                  const minutes = totals[g.key];
                  const label = `${t(g.label)}: ${formatWaitDuration(minutes * 60)}`;
                  return (
                    <View key={g.key} style={styles.bar}>
                      <AppText variant="caption">{label}</AppText>
                      <ProgressBar percent={totalMinutes > 0 ? (minutes / totalMinutes) * 100 : 0} label={label} color="info" testID={`concurrency-group-${g.key}`} />
                    </View>
                  );
                })}
              </Section>
              {busiest.length > 0 ? (
                <Section title={t('Missions waiting')}>
                  {busiest.map(b => (
                    <KeyValueRow key={b.startUtc} label={formatDateTime(b.startUtc)}
                      value={`${formatWaitDuration(b.totalMinutes * 60)} · ${t('{{count}} missions waiting', { count: b.missionsWaiting })}`} />
                  ))}
                </Section>
              ) : null}
              <Section title={t('Wait by gate')} footer={metrics.truncated ? t('More than 50000 waits matched; totals are a lower bound. Narrow the window or pick a vessel.') : undefined}>
                {metrics.byGate.map(g => (
                  <KeyValueRow key={g.gate} label={t(waitGateLabel(g.gate))} value={`${formatWaitDuration(g.seconds)} · ${t('{{count}} missions', { count: g.missions })}`} testID={`concurrency-gate-${g.gate}`} />
                ))}
              </Section>
            </>
          )}
        </View>
      ) : null}
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  content: { padding: spacing.lg, gap: spacing.md },
  body: { gap: spacing.md },
  bar: { paddingHorizontal: spacing.lg, paddingVertical: spacing.xs, gap: spacing.xs },
});
