import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { enumerateVesselImportBatches } from '@dashboard/api/client';
import type { VesselImportBatch, VesselImportBatchStatus } from '@dashboard/types/models';
import { BATCH_STATUSES, BATCH_STATUS_META, batchStatusBadge, categorizationBadge } from '@dashboard/lib/vesselImportLabels';
import { usePagedList } from '../../../build/usePagedList';
import { AppText, Banner, Button, EmptyState, LoadingState, StatusBadge } from '../../../components/ui';
import { SelectField } from '../../../components/ui/SelectSheet';
import { useLocale } from '../../../i18n/LocaleContext';
import { useTheme } from '../../../theme/ThemeContext';
import { MIN_TOUCH, spacing } from '../../../theme/typography';

/** Import history (newest first) with a status filter; a batch opens on the right step. */
export function ImportHistory({ onOpen }: { onOpen: (batch: VesselImportBatch) => void }) {
  const { t, formatRelativeTime } = useLocale();
  const { colors } = useTheme();
  const [status, setStatus] = useState<VesselImportBatchStatus | ''>('');
  const list = usePagedList<VesselImportBatch>(
    (pageNumber, pageSize) => enumerateVesselImportBatches({ pageNumber, pageSize, status }),
    [status],
    { pageSize: 10 },
  );

  return (
    <View testID="import-history">
      <View style={styles.pad}>
        <SelectField
          label={t('Status')}
          value={status}
          options={[{ value: '', label: t('All statuses') }, ...BATCH_STATUSES.map((s) => ({ value: s, label: t(BATCH_STATUS_META[s].label) }))]}
          onChange={setStatus}
          closeLabel={t('Close')}
        />
      </View>
      {list.error ? <Banner tone="danger" title={list.error} /> : null}
      {list.loading ? <LoadingState label={t('Loading...')} /> : null}
      {!list.loading && !list.error && list.items.length === 0 ? (
        <EmptyState
          title={status ? t('No batches match this status') : t('No imports yet')}
          message={t('Each discovery is saved as a batch here, so you can come back to a review or check what an import created.')}
        />
      ) : null}
      {list.items.map((b) => {
        const badge = batchStatusBadge(t, b.status);
        const cat = b.categorizationStatus && b.categorizationStatus !== 'None' ? categorizationBadge(t, b.categorizationStatus) : null;
        const resumable = b.status === 'Discovered' || b.status === 'Discovering' || b.categorizationStatus === 'Completed';
        const counts = t('{{paths}} paths, {{candidates}} candidates, {{created}} created, {{skipped}} skipped, {{failed}} failed', {
          paths: b.requestedPathCount, candidates: b.candidateCount, created: b.createdCount, skipped: b.skippedCount, failed: b.failedCount,
        });
        return (
          <Pressable
            key={b.id}
            accessibilityRole="button"
            accessibilityLabel={`${resumable ? t('Continue') : t('View')}: ${b.id}, ${badge.label}, ${counts}`}
            onPress={() => onOpen(b)}
            style={({ pressed }) => [styles.row, { borderBottomColor: colors.border, backgroundColor: pressed ? colors.background : colors.surface }]}
            testID={`import-batch-${b.id}`}
          >
            <View style={styles.head}>
              <AppText variant="label" style={styles.flex}>{formatRelativeTime(b.createdUtc)}</AppText>
              <StatusBadge label={badge.label} tone={badge.tone} />
            </View>
            <AppText variant="caption" muted selectable>{b.id}</AppText>
            <AppText variant="caption">{counts}</AppText>
            {cat ? <StatusBadge label={cat.label} tone={cat.tone} /> : null}
            <AppText variant="caption" color="primary">{resumable ? t('Continue') : t('View')}</AppText>
          </Pressable>
        );
      })}
      {list.hasMore ? <Button label={list.loadingMore ? t('Loading...') : t('Show more')} variant="ghost" onPress={list.loadMore} disabled={list.loadingMore} /> : null}
      <Button label={t('Refresh')} variant="ghost" icon="refresh" onPress={() => void list.refresh()} />
    </View>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  pad: { paddingHorizontal: spacing.lg },
  row: { paddingHorizontal: spacing.lg, paddingVertical: spacing.sm, gap: 2, minHeight: MIN_TOUCH, borderBottomWidth: StyleSheet.hairlineWidth },
  head: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
});
