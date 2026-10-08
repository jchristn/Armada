import { useRouter, type Href } from 'expo-router';
import { useMemo, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import type { VesselImportBatch, VesselImportItem, VesselImportOutcome } from '@dashboard/types/models';
import { OUTCOMES, OUTCOME_META, batchStatusBadge, outcomeBadge, outcomeReasonLabel } from '@dashboard/lib/vesselImportLabels';
import { AppText, Banner, Button, StatusBadge } from '../../../components/ui';
import { useLocale } from '../../../i18n/LocaleContext';
import { useTheme } from '../../../theme/ThemeContext';
import { radius, spacing, typography } from '../../../theme/typography';
import { ChipGroup } from '../health/ChipGroup';

const PAGE = 50;

export interface ImportResultsStepProps {
  batch: VesselImportBatch;
  items: VesselImportItem[];
  /** Paths the operator selected (background progress target). */
  selectedCount?: number;
  /** True while a background import is polled. */
  polling?: boolean;
  jobId?: string | null;
  pollError?: string;
}

/** Results of an import (also a batch from the history): status, progress while it runs, counts, and per-item outcomes. */
export function ImportResultsStep({ batch, items, selectedCount, polling = false, jobId, pollError }: ImportResultsStepProps) {
  const { t, formatDateTime } = useLocale();
  const { colors } = useTheme();
  const router = useRouter();
  const [outcome, setOutcome] = useState<VesselImportOutcome | ''>('');
  const [shown, setShown] = useState(PAGE);
  const counts = useMemo(() => {
    const c: Partial<Record<VesselImportOutcome, number>> = {};
    for (const item of items) c[item.outcome] = (c[item.outcome] ?? 0) + 1;
    return c;
  }, [items]);
  const filtered = useMemo(() => items.filter((i) => !outcome || i.outcome === outcome), [items, outcome]);
  const status = batchStatusBadge(t, batch.status);
  const processed = batch.createdCount + batch.skippedCount + batch.failedCount;
  const target = Math.max(selectedCount ?? 0, processed, 1);
  const percent = Math.min(100, Math.round((processed / target) * 100));
  const stats: [string, number][] = [[t('Created'), batch.createdCount], [t('Skipped'), batch.skippedCount], [t('Failed'), batch.failedCount], [t('Candidates'), batch.candidateCount]];

  return (
    <View testID="import-results">
      <View style={[styles.head, styles.pad]}>
        <StatusBadge label={status.label} tone={status.tone} />
        <AppText variant="caption" muted selectable>{batch.id}</AppText>
        <AppText variant="caption" muted>{formatDateTime(batch.createdUtc)}</AppText>
      </View>
      {polling ? (
        <View style={[styles.box, { borderColor: colors.info }]} accessibilityLiveRegion="polite" accessibilityRole="progressbar" accessibilityValue={{ min: 0, max: 100, now: percent }}>
          <AppText>{t('{count, plural, one {Importing # repository in the background.} other {Importing # repositories in the background.}}', { count: selectedCount ?? 0 })}</AppText>
          <View style={[styles.track, { backgroundColor: colors.track }]}><View style={[styles.bar, { width: `${percent}%`, backgroundColor: colors.primary }]} /></View>
          <AppText variant="caption" muted>{`${t('{{processed}} of {{total}} processed', { processed, total: target })}${jobId ? ` - ${jobId}` : ''}`}</AppText>
          {jobId ? <AppText variant="caption" color="primary" accessibilityRole="link" onPress={() => router.push('/jobs' as Href)}>{t('Open the Jobs page')}</AppText> : null}
          <AppText variant="caption" muted>{t('You can leave this screen; the import keeps running and appears in the import history.')}</AppText>
        </View>
      ) : null}
      {pollError ? <Banner tone="danger" title={pollError} /> : null}
      <View style={[styles.stats, styles.pad]}>
        {stats.map(([label, value]) => (
          <View key={label} style={[styles.stat, { borderColor: colors.border, backgroundColor: colors.surface }]} accessible accessibilityLabel={`${label}: ${value}`}>
            <AppText variant="caption" muted>{label}</AppText>
            <AppText variant="heading">{String(value)}</AppText>
          </View>
        ))}
      </View>
      {items.length > 0 ? (
        <>
          <ChipGroup
            scroll
            chips={[{ key: 'all', label: t('All'), count: items.length }, ...OUTCOMES.filter((o) => (counts[o] ?? 0) > 0).map((o) => ({ key: o, label: t(OUTCOME_META[o].label), count: counts[o] ?? 0 }))]}
            selected={[outcome || 'all']}
            onToggle={(key) => { setOutcome(key === 'all' ? '' : key as VesselImportOutcome); setShown(PAGE); }}
            label={t('Filter items by outcome')}
          />
          {filtered.slice(0, shown).map((item) => {
            const badge = outcomeBadge(t, item.outcome);
            const vesselId = item.vesselId ?? item.existingVesselId;
            return (
              <View key={item.id || item.path} style={[styles.item, { borderBottomColor: colors.border }]} testID={`import-result-${item.proposedName}`}>
                <View style={styles.head}>
                  <AppText variant="label" style={styles.flex}>{item.proposedName}</AppText>
                  <StatusBadge label={badge.label} tone={badge.tone} />
                </View>
                {item.outcomeReason ? <AppText variant="caption">{outcomeReasonLabel(t, item.outcomeReason)}</AppText> : null}
                {item.outcomeMessage ? <AppText variant="caption" muted>{item.outcomeMessage}</AppText> : null}
                <AppText variant="caption" muted style={typography.mono}>{item.path}</AppText>
                {vesselId ? (
                  <AppText variant="caption" color="primary" accessibilityRole="link" onPress={() => router.push(`/vessels/${encodeURIComponent(vesselId)}` as Href)}>{vesselId}</AppText>
                ) : null}
              </View>
            );
          })}
          {filtered.length > shown ? <Button label={t('Show more')} variant="ghost" onPress={() => setShown((n) => n + PAGE)} /> : null}
        </>
      ) : null}
      {items.length === 0 && !polling ? <AppText muted style={styles.pad}>{t('This batch has no items.')}</AppText> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  pad: { paddingHorizontal: spacing.lg },
  head: { flexDirection: 'row', flexWrap: 'wrap', alignItems: 'center', gap: spacing.sm, marginBottom: spacing.sm },
  box: { borderWidth: 1, borderRadius: radius.md, padding: spacing.md, marginHorizontal: spacing.md, marginBottom: spacing.md, gap: spacing.xs },
  track: { height: 6, borderRadius: 3, overflow: 'hidden' },
  bar: { height: 6 },
  stats: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm, marginBottom: spacing.md },
  stat: { flexGrow: 1, minWidth: 120, borderWidth: StyleSheet.hairlineWidth, borderRadius: radius.md, padding: spacing.md },
  item: { paddingHorizontal: spacing.lg, paddingVertical: spacing.sm, borderBottomWidth: StyleSheet.hairlineWidth, gap: 2 },
});
