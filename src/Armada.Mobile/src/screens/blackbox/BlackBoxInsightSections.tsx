import { useRouter, type Href } from 'expo-router';
import { useCallback } from 'react';
import { StyleSheet, View } from 'react-native';
import { getBlackBoxTimeline, listBlackBoxNearbySuccesses } from '@dashboard/api/client';
import type { BlackBoxNearbySuccess, BlackBoxTimelineItem } from '@dashboard/types/blackbox';
import { AppText, Button, ListRow, Section } from '../../components/ui';
import { usePagedList } from '../../data/usePagedList';
import { useLocale } from '../../i18n/LocaleContext';
import { spacing } from '../../theme/typography';
import { formatHoursFromFailure, timelineEntityRoute, timelineItemKey } from './insightModel';
import { spaced } from './blackBoxModel';

/** Rows per page for the entry screen's insight sections. */
export const INSIGHT_PAGE_SIZE = 10;

/** The paged footer of an insight section: an error with Retry, Load more, and the shown and total counts. */
function PageFooter({ shown, total, hasMore, loadingMore, error, onMore, onRetry, testID }: {
  shown: number;
  total: number;
  hasMore: boolean;
  loadingMore: boolean;
  error: string | null;
  onMore: () => void;
  onRetry: () => void;
  testID: string;
}) {
  const { t } = useLocale();
  return (
    <View style={styles.footer}>
      {error ? (
        <View style={styles.gap}>
          <AppText color="danger" accessibilityRole="alert" testID={`${testID}-error`}>{error}</AppText>
          <Button label={t('Retry')} variant="secondary" onPress={onRetry} testID={`${testID}-retry`} />
        </View>
      ) : null}
      {shown > 0 ? <AppText variant="caption" muted testID={`${testID}-count`}>{t('Showing {{shown}} of {{total}}', { shown, total })}</AppText> : null}
      {hasMore ? (
        <Button
          label={loadingMore ? t('Loading...') : t('Load more')}
          variant="secondary"
          disabled={loadingMore}
          onPress={onMore}
          testID={`${testID}-more`}
        />
      ) : null}
    </View>
  );
}

/**
 * The entry's combined timeline (GET /api/v1/blackbox/{id}/timeline): the entry and its notes, the mission and its
 * events, the dock, the merge queue entry, the captain and the Harbor link, oldest first, ten at a time.
 */
export function BlackBoxTimelineSection({ entryId }: { entryId: string }) {
  const { t, formatDateTime } = useLocale();
  const router = useRouter();
  const fetchPage = useCallback((pageNumber: number, pageSize: number) => getBlackBoxTimeline(entryId, { pageNumber, pageSize }), [entryId]);
  const list = usePagedList<BlackBoxTimelineItem>(fetchPage, timelineItemKey, [entryId], INSIGHT_PAGE_SIZE, t('Failed to load the timeline.'));
  return (
    <Section title={t('Timeline')} footer={t('Everything that happened around this failure, oldest first.')}>
      <View testID="bbx-timeline">
        {list.loading ? <AppText muted style={styles.pad} testID="bbx-timeline-loading">{t('Loading...')}</AppText> : null}
        {!list.loading && !list.error && list.items.length === 0 ? <AppText muted style={styles.pad} testID="bbx-timeline-empty">{t('No timeline events.')}</AppText> : null}
        {list.items.map((item, index) => {
          const route = timelineEntityRoute(item.entityId);
          return (
            <ListRow
              key={timelineItemKey(item)}
              testID={`bbx-timeline-${index}`}
              title={item.message || spaced(item.kind)}
              subtitle={[formatDateTime(item.timestampUtc), t(spaced(item.source)), spaced(item.kind)].filter(Boolean).join(' - ')}
              onPress={route ? () => router.push(route as Href) : undefined}
            />
          );
        })}
        <PageFooter
          shown={list.items.length}
          total={list.totalRecords}
          hasMore={list.hasMore}
          loadingMore={list.loadingMore}
          error={list.error}
          onMore={() => void list.loadMore()}
          onRetry={() => void list.refresh()}
          testID="bbx-timeline"
        />
      </View>
    </Section>
  );
}

/**
 * Successful landings on the same vessel near the failure that changed the same files
 * (GET /api/v1/blackbox/{id}/nearby-successes), most overlap first, each linked to its mission.
 */
export function BlackBoxNearbySuccessesSection({ entryId }: { entryId: string }) {
  const { t, formatDateTime } = useLocale();
  const router = useRouter();
  const fetchPage = useCallback((pageNumber: number, pageSize: number) => listBlackBoxNearbySuccesses(entryId, { pageNumber, pageSize }), [entryId]);
  const list = usePagedList<BlackBoxNearbySuccess>(fetchPage, (s) => s.successId, [entryId], INSIGHT_PAGE_SIZE, t('Failed to load nearby successes.'));
  return (
    <Section title={t('Nearby successes')} footer={t('Missions that landed on this vessel around the failure and changed the same files.')}>
      <View testID="bbx-nearby">
        {list.loading ? <AppText muted style={styles.pad} testID="bbx-nearby-loading">{t('Loading...')}</AppText> : null}
        {!list.loading && !list.error && list.items.length === 0 ? <AppText muted style={styles.pad} testID="bbx-nearby-empty">{t('No nearby successes.')}</AppText> : null}
        {list.items.map((success, index) => (
          <ListRow
            key={success.successId}
            testID={`bbx-nearby-${index}`}
            icon="checkmark-circle-outline"
            title={success.missionTitle || success.missionId}
            subtitle={[
              t('Landed {{when}} ({{offset}})', { when: formatDateTime(success.landedUtc), offset: formatHoursFromFailure(success.hoursFromFailure) }),
              t('{{count}} overlapping file(s)', { count: success.overlappingFileCount }),
              success.overlappingFiles.slice(0, 3).join(', '),
            ].filter(Boolean).join(' - ')}
            onPress={() => router.push(`/missions/${success.missionId}` as Href)}
          />
        ))}
        <PageFooter
          shown={list.items.length}
          total={list.totalRecords}
          hasMore={list.hasMore}
          loadingMore={list.loadingMore}
          error={list.error}
          onMore={() => void list.loadMore()}
          onRetry={() => void list.refresh()}
          testID="bbx-nearby"
        />
      </View>
    </Section>
  );
}

const styles = StyleSheet.create({
  pad: { paddingHorizontal: spacing.lg, paddingVertical: spacing.sm },
  footer: { paddingHorizontal: spacing.lg, paddingVertical: spacing.sm, gap: spacing.xs },
  gap: { gap: spacing.xs },
});
