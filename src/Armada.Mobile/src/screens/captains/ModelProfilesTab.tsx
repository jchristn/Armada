import { useMemo, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { listModelProfiles } from '@dashboard/api/client';
import { servedContextLabel } from '@dashboard/lib/modelProfiles';
import type { ModelProfile } from '@dashboard/types/models';
import { useSplitSelection } from '../../components/app/useSplitSelection';
import { PagedList } from '../../components/app/PagedList';
import { EmptyState, ListRow, SearchField, SplitView, StatusBadge } from '../../components/ui';
import { usePagedList } from '../../data/usePagedList';
import { useLocale } from '../../i18n/LocaleContext';
import { spacing } from '../../theme/typography';
import { ModelProfileDetailScreen } from './ModelProfileDetailScreen';

const profileRoute = (id: string) => `/model-profiles/${id}`;

/** Pure: a profile's display name ("model on provider (runtime)"). */
export function modelProfileName(p: Pick<ModelProfile, 'id' | 'identity'>): string {
  const model = p.identity.model || p.id;
  const where = [p.identity.provider, p.identity.runtime].filter(Boolean).join(', ');
  return where ? `${model} (${where})` : model;
}

/** Pure: profiles whose model, provider, or runtime contains the search text. */
export function filterModelProfiles(profiles: ModelProfile[], search: string): ModelProfile[] {
  const term = search.trim().toLowerCase();
  if (!term) return profiles;
  return profiles.filter(p => [p.identity.model, p.identity.provider, p.identity.runtime].some(v => (v ?? '').toLowerCase().includes(term)));
}

/**
 * The Model Profiles tab of the Captains hub (the dashboard's CaptainsHub `?tab=profiles`): every model as served,
 * read-only on mobile, with the profile detail beside the list on tablets; phones push /model-profiles/:id.
 */
export function ModelProfilesTab() {
  const { t } = useLocale();
  const selection = useSplitSelection(profileRoute);
  const [search, setSearch] = useState('');
  const list = usePagedList(
    (pageNumber, pageSize) => listModelProfiles({ pageNumber, pageSize }),
    (p: ModelProfile) => p.id,
    [],
    25,
    t('Failed to load model profiles.'),
  );
  const shown = useMemo(() => filterModelProfiles(list.items, search), [list.items, search]);
  const state = useMemo(() => ({ ...list, items: shown }), [list, shown]);
  const master = (
    <View style={styles.fill} testID="model-profiles-list">
      <PagedList
        state={state}
        header={<View style={styles.header}><SearchField value={search} onChangeText={setSearch} placeholder={t('Search models')} clearLabel={t('Clear')} testID="model-profile-search" /></View>}
        keyExtractor={(p) => p.id}
        emptyTitle={list.items.length > 0 ? t('No model profiles match the current filters.') : t('No model profiles found.')}
        loadingLabel={t('Loading...')}
        testID="model-profiles"
        renderItem={({ item }) => {
          const tier = item.tiering?.tier;
          return (
            <ListRow
              title={modelProfileName(item)}
              subtitle={servedContextLabel(item.identity, t)}
              accessory={tier ? <StatusBadge label={t(tier)} tone="info" /> : undefined}
              accessibilityValue={[tier ? t(tier) : null, servedContextLabel(item.identity, t)]}
              selected={selection.selectedId === item.id}
              onPress={() => selection.select(item.id)}
              testID={`model-profile-row-${item.id}`}
            />
          );
        }}
      />
    </View>
  );
  return (
    <SplitView
      master={master}
      detail={selection.selectedId
        ? <ModelProfileDetailScreen key={selection.selectedId} id={selection.selectedId} embedded />
        : selection.isTablet ? <EmptyState icon="hardware-chip-outline" title={t('Select an item to see its details.')} /> : null}
      onBack={selection.clear}
      backLabel={t('Back')}
    />
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  header: { paddingTop: spacing.sm },
});
