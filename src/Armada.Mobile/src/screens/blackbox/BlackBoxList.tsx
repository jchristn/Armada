import { useMemo, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { listBlackBox } from '@dashboard/api/client';
import type { BlackBoxEntrySummary } from '@dashboard/types/blackbox';
import { PagedList } from '../../components/app/PagedList';
import { ListRow, SelectField, StatusBadge } from '../../components/ui';
import { useLiveRefresh } from '../../data/useLiveRefresh';
import { useNameLookups } from '../../data/useNameLookups';
import { usePagedList } from '../../data/usePagedList';
import { useLocale } from '../../i18n/LocaleContext';
import { spacing } from '../../theme/typography';
import { BLACKBOX_FAILURE_KINDS, BLACKBOX_PAGE_SIZE, entryRowSubtitle, resolutionTone, spaced } from './blackBoxModel';

/**
 * The Missions hub's Black Box tab (the dashboard's Missions > Black Box Entries section, simplified for the app):
 * recent entries newest first, filtered by vessel and failure kind on the server, paged as you scroll. Choosing a row
 * calls `onSelect` (the hub shows the entry beside the list or pushes /blackbox/:id). Live: blackbox.* events reload
 * what is shown.
 */
export function BlackBoxList({ onSelect, selectedId }: { onSelect: (id: string) => void; selectedId?: string | null }) {
  const { t, formatRelativeTime } = useLocale();
  const names = useNameLookups({ vessels: true });
  const [vesselId, setVesselId] = useState('');
  const [kind, setKind] = useState('');

  const list = usePagedList(
    (pageNumber, pageSize) => listBlackBox({ pageNumber, pageSize, vesselId: vesselId || undefined, kind: kind || undefined }),
    (e: BlackBoxEntrySummary) => e.id,
    [vesselId, kind],
    BLACKBOX_PAGE_SIZE,
    t('Failed to load the black box.'),
  );
  useLiveRefresh(['blackbox.'], () => { void list.reload(); });

  const vesselOptions = useMemo(() => names.vessels.map((v) => ({ value: v.id, label: v.name || v.id })), [names.vessels]);
  const kindOptions = useMemo(() => BLACKBOX_FAILURE_KINDS.map((k) => ({ value: k, label: t(spaced(k)) })), [t]);
  const filtered = !!vesselId || !!kind;

  const header = (
    <View style={styles.header}>
      <SelectField
        label={t('Vessel')}
        value={vesselId}
        onChange={setVesselId}
        options={vesselOptions}
        allowEmpty
        placeholder={t('All vessels')}
        closeLabel={t('Close')}
        testID="bbx-filter-vessel"
      />
      <SelectField
        label={t('Failure kind')}
        value={kind}
        onChange={setKind}
        options={kindOptions}
        allowEmpty
        placeholder={t('All kinds')}
        closeLabel={t('Close')}
        testID="bbx-filter-kind"
      />
    </View>
  );

  return (
    <View style={styles.fill} testID="bbx-list">
      <PagedList
        state={list}
        header={header}
        keyExtractor={(e) => e.id}
        emptyTitle={filtered ? t('No black box entries match the current filters.') : t('No black box entries yet.')}
        emptyMessage={filtered ? undefined : t('Armada records an entry here whenever a mission or voyage fails.')}
        loadingLabel={t('Loading...')}
        testID="bbx-entries"
        renderItem={({ item }) => (
          <ListRow
            testID={`bbx-row-${item.id}`}
            title={item.missionTitle || item.reasonExcerpt || item.id}
            subtitle={`${entryRowSubtitle(item)}\n${t(spaced(item.failureKind || item.step))} - ${formatRelativeTime(item.createdUtc)}`}
            accessory={<StatusBadge label={t(spaced(item.resolution))} tone={resolutionTone(item.resolution)} />}
            accessibilityValue={t(spaced(item.resolution))}
            selected={item.id === selectedId}
            onPress={() => onSelect(item.id)}
          />
        )}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  header: { paddingTop: spacing.sm, paddingHorizontal: spacing.md },
});
