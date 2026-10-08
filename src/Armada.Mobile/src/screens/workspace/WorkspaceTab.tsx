import { useFocusEffect, useRouter, type Href } from 'expo-router';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { getWorkspaceStatus, listVessels } from '@dashboard/api/client';
import type { Vessel, WorkspaceStatusResult } from '@dashboard/types/models';
import { ListPane } from '../../build/ListPane';
import { useLiveResource } from '../../build/useLiveResource';
import { AppText, StatusBadge } from '../../components/ui';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { MIN_TOUCH, spacing } from '../../theme/typography';
import { readRecentWorkspaceVessels } from './recentVessels';

/** Workspace status is fetched for the recent vessels plus the first ones listed, up to this many (the dashboard's 12). */
const STATUS_PREVIEW = 12;

interface PickerRow { vessel: Vessel; recent: boolean }

/**
 * The Vessels hub Workspace tab (the dashboard's WorkspaceVesselPicker): find a vessel by name, id, or working
 * directory and open it as a browsable, editable workspace. Recent vessels come first with their branch, active
 * missions, and sync state.
 */
export function WorkspaceTab() {
  const { t } = useLocale();
  const { colors } = useTheme();
  const router = useRouter();
  const [query, setQuery] = useState('');
  const [recent, setRecent] = useState<string[]>([]);
  const [statusById, setStatusById] = useState<Record<string, WorkspaceStatusResult | undefined>>({});
  const vessels = useLiveResource(async () => (await listVessels({ pageSize: 9999 }))?.objects ?? [], [], { live: ['vessel.'] });

  // Re-read on focus: opening a workspace moves its vessel to the front.
  useFocusEffect(useCallback(() => { void readRecentWorkspaceVessels().then(setRecent); }, []));

  const all = useMemo(() => vessels.data ?? [], [vessels.data]);
  useEffect(() => {
    const ids = [...recent, ...all.map((v) => v.id)].filter((id, i, arr) => arr.indexOf(id) === i).slice(0, STATUS_PREVIEW);
    if (ids.length === 0) return undefined;
    let cancelled = false;
    void Promise.all(ids.map(async (id) => {
      try { return [id, await getWorkspaceStatus(id)] as const; } catch { return [id, undefined] as const; }
    })).then((results) => {
      if (!cancelled) setStatusById((cur) => ({ ...cur, ...Object.fromEntries(results) }));
    });
    return () => { cancelled = true; };
  }, [recent, all]);

  const rows: PickerRow[] = useMemo(() => {
    const term = query.trim().toLowerCase();
    const matches = (v: Vessel) => !term || v.name.toLowerCase().includes(term) || v.id.toLowerCase().includes(term) || (v.workingDirectory || '').toLowerCase().includes(term);
    const recentRows = recent.map((id) => all.find((v) => v.id === id)).filter((v): v is Vessel => !!v && matches(v)).map((vessel) => ({ vessel, recent: true }));
    const others = all.filter((v) => !recent.includes(v.id) && matches(v)).map((vessel) => ({ vessel, recent: false }));
    return [...recentRows, ...others];
  }, [all, recent, query]);

  const renderItem = ({ item, index }: { item: PickerRow; index: number }) => {
    const v = item.vessel;
    const status = statusById[v.id];
    const ready = status?.hasWorkingDirectory !== false;
    const sync = status
      ? [status.branchName || t('No branch info'), t('{{count}} active mission(s)', { count: status.activeMissionCount }),
        typeof status.commitsAhead === 'number' ? `${t('{{count}} ahead', { count: status.commitsAhead })}${typeof status.commitsBehind === 'number' ? ` / ${t('{{count}} behind', { count: status.commitsBehind })}` : ''}` : t('No git sync data')].join(' - ')
      : null;
    const header = index === 0 || rows[index - 1].recent !== item.recent
      ? <AppText variant="subheading" muted accessibilityRole="header" style={styles.section}>{item.recent ? t('Recent Vessels') : t('All Vessels')}</AppText>
      : null;
    return (
      <View>
        {header}
        <Pressable
          accessibilityRole="button"
          accessibilityLabel={[v.name, ready ? t('Workspace ready') : t('No working directory'), sync].filter(Boolean).join(', ')}
          onPress={() => router.push(`/workspace/${encodeURIComponent(v.id)}` as Href)}
          style={({ pressed }) => [styles.row, { borderBottomColor: colors.border, backgroundColor: pressed ? colors.background : colors.surface }]}
          testID={`workspace-open-${v.name}`}
        >
          <View style={styles.head}>
            <AppText variant="label" style={styles.flex} numberOfLines={1}>{v.name}</AppText>
            <StatusBadge label={ready ? t('Workspace ready') : t('No working directory')} tone={ready ? 'success' : 'warning'} />
          </View>
          <AppText variant="caption" muted numberOfLines={1}>{v.workingDirectory || t('No working directory configured')}</AppText>
          {sync ? <AppText variant="caption" muted numberOfLines={2}>{sync}</AppText> : null}
        </Pressable>
      </View>
    );
  };

  return (
    <View style={styles.flex} testID="workspace-tab">
      <ListPane<PickerRow>
        testID="workspace-picker"
        items={rows}
        keyOf={(r) => `${r.recent ? 'r' : 'a'}-${r.vessel.id}`}
        renderItem={renderItem}
        loading={vessels.loading}
        refreshing={vessels.refreshing}
        error={vessels.error}
        onRefresh={() => void vessels.refresh()}
        search={{ value: query, onChange: setQuery, placeholder: t('Find a vessel by name, id, or working directory') }}
        summary={t('Open a vessel as a browsable, editable repository workspace inside Armada.')}
        emptyTitle={all.length === 0 ? t('No vessels yet') : t('No vessels match the current filter.')}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  section: { marginHorizontal: spacing.lg, marginTop: spacing.md, marginBottom: spacing.sm, textTransform: 'uppercase' },
  row: { paddingHorizontal: spacing.lg, paddingVertical: spacing.sm, gap: 2, minHeight: MIN_TOUCH + 8, borderBottomWidth: StyleSheet.hairlineWidth },
  head: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
});
