import { useCallback, useEffect, useMemo, useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import {
  createMission,
  deleteMission,
  listMissionSummaries,
  listUsers,
  purgeMission,
  restartMission,
  retryMissionLanding,
  transitionMission,
} from '@dashboard/api/client';
import { MISSION_LIST_STATUSES, canRetryLanding, matchesMissionColumnFilters } from '@dashboard/lib/missionActions';
import type { MissionSummary, UserMaster } from '@dashboard/types/models';
import { useAuth } from '../../auth/AuthContext';
import { EntityStatusBadge } from '../../components/app/EntityStatusBadge';
import { FilterButton, activeFilterCount } from '../../components/app/FilterSheet';
import { JsonSheet } from '../../components/app/JsonSheet';
import { PagedList } from '../../components/app/PagedList';
import { useConfirm } from '../../components/app/useConfirm';
import {
  ActionSheet,
  AppText,
  Button,
  Icon,
  IconButton,
  SearchField,
  SelectField,
  type SheetAction,
} from '../../components/ui';
import { errorMessage } from '../../data/errors';
import { useInterval } from '../../data/useInterval';
import { useLiveRefresh } from '../../data/useLiveRefresh';
import { useNameLookups } from '../../data/useNameLookups';
import { usePagedList } from '../../data/usePagedList';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { useTheme } from '../../theme/ThemeContext';
import { MIN_TOUCH, spacing } from '../../theme/typography';
import type { OperationsListProps } from './listTypes';
import type { MissionDetailTab } from './MissionDetail';
import { MissionFormSheet, type MissionFormValues } from './mission/MissionFormSheet';
import { TransitionSheet } from './mission/TransitionSheet';
import { useHardwareBack } from '../../navigation/useHardwareBack';

/** The dashboard Missions page's auto-refresh fallback (the socket is the primary source of updates). */
export const MISSIONS_POLL_MS = 30000;

/** Pure: the server filters for the list (status and the admin "view as user" scope). */
export function missionServerFilters(status: string, userId: string): Record<string, string> {
  const filters: Record<string, string> = {};
  if (status) filters.status = status;
  if (userId) filters.userId = userId;
  return filters;
}

/** Pure: a user's label in the scope picker (the dashboard's UserScopeFilter). */
export function userScopeLabel(user: Pick<UserMaster, 'firstName' | 'lastName' | 'email'>): string {
  const name = [user.firstName, user.lastName].filter(Boolean).join(' ').trim();
  return name ? `${name} (${user.email})` : user.email;
}

export interface MissionsListProps extends OperationsListProps {
  /** Opens a mission on a detail tab (row actions View Diff / View Log). */
  onOpenTab: (id: string, tab: MissionDetailTab) => void;
}

/**
 * The Missions list, at parity with the dashboard's Missions page: server-side status and user scope filters,
 * text search (the dashboard's title / status / branch column filters), endless scroll, live updates, row actions
 * (detail, restart, retry landing, diff, log, transition, JSON, cancel, purge), multi-select purge (long press),
 * and Create Mission.
 */
export function MissionsList({ onSelect, selectedId, onOpenTab }: MissionsListProps) {
  const { t, formatRelativeTime, formatDateTime } = useLocale();
  const { colors } = useTheme();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { pushToast } = useNotifications();
  const names = useNameLookups({ vessels: true, captains: true });
  const [status, setStatus] = useState('');
  const [userScope, setUserScope] = useState('');
  const [search, setSearch] = useState('');
  const [users, setUsers] = useState<UserMaster[]>([]);
  const [menuFor, setMenuFor] = useState<MissionSummary | null>(null);
  const [transitionFor, setTransitionFor] = useState<MissionSummary | null>(null);
  const [jsonFor, setJsonFor] = useState<MissionSummary | null>(null);
  const [selection, setSelection] = useState<string[] | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [creating, setCreating] = useState(false);
  const [confirmElement, confirm] = useConfirm('missions-confirm');
  const canScope = isAdmin || isTenantAdmin;

  useEffect(() => {
    if (!canScope) return undefined;
    let mounted = true;
    listUsers().then((result) => { if (mounted) setUsers(result?.objects ?? []); }).catch(() => undefined);
    return () => { mounted = false; };
  }, [canScope]);

  const list = usePagedList<MissionSummary>(
    (pageNumber, pageSize) => listMissionSummaries({ pageNumber, pageSize, filters: missionServerFilters(status, userScope) }),
    (m) => m.id,
    [status, userScope],
    25,
    t('Failed to load missions.'),
  );
  const reload = list.reload;
  useLiveRefresh(['mission.'], () => { void reload(); });
  useInterval(() => { void reload(); }, MISSIONS_POLL_MS);

  const shown = useMemo(() => {
    const term = search.trim();
    if (!term) return list.items;
    // One search box on a phone stands for the dashboard's title, status, and branch column filters.
    return list.items.filter((m) =>
      matchesMissionColumnFilters(m, { title: term, status: '', branch: '' })
      || matchesMissionColumnFilters(m, { title: '', status: term, branch: '' })
      || matchesMissionColumnFilters(m, { title: '', status: '', branch: term }));
  }, [list.items, search]);
  const view = useMemo(() => ({ ...list, items: shown }), [list, shown]);

  const act = useCallback(async (action: () => Promise<unknown>, success: string, failure: string) => {
    try {
      await action();
      pushToast('success', success);
      void reload();
    } catch (e) {
      pushToast('error', errorMessage(e, failure));
    }
  }, [pushToast, reload]);

  function rowActions(m: MissionSummary): SheetAction[] {
    return [
      { key: 'detail', label: t('View Detail'), icon: 'open-outline', onPress: () => onSelect(m.id) },
      { key: 'edit', label: t('Edit'), icon: 'create-outline', onPress: () => onSelect(m.id) },
      { key: 'restart', label: t('Restart'), icon: 'refresh-outline', onPress: () => void act(() => restartMission(m.id), t('Mission "{{title}}" restarted.', { title: m.title }), t('Restart failed.')) },
      ...(canRetryLanding(m.status) ? [{
        key: 'retry-landing',
        label: t('Retry Landing'),
        icon: 'git-merge-outline' as const,
        onPress: () => void act(() => retryMissionLanding(m.id), t('Landing succeeded for "{{title}}"', { title: m.title }), t('Retry landing failed.')),
      }] : []),
      { key: 'diff', label: t('View Diff'), icon: 'git-compare-outline', onPress: () => onOpenTab(m.id, 'diff') },
      { key: 'log', label: t('View Log'), icon: 'document-text-outline', onPress: () => onOpenTab(m.id, 'log') },
      { key: 'transition', label: t('Transition Status'), icon: 'swap-horizontal-outline', onPress: () => setTransitionFor(m) },
      { key: 'json', label: t('View JSON'), icon: 'code-slash-outline', onPress: () => setJsonFor(m) },
      {
        key: 'cancel',
        label: t('Cancel'),
        icon: 'close-circle-outline',
        danger: true,
        onPress: () => confirm({
          title: t('Cancel Mission'),
          message: t('Cancel mission "{{title}}"? The mission will be set to Cancelled status but remains in the database. Use Purge to permanently remove it.', { title: m.title }),
          confirmLabel: t('Cancel Mission'),
          danger: true,
          onConfirm: async () => {
            try {
              await deleteMission(m.id);
              pushToast('warning', t('Mission "{{title}}" cancelled.', { title: m.title }));
              void reload();
            } catch (e) {
              pushToast('error', errorMessage(e, t('Cancel failed.')));
            }
          },
        }),
      },
      {
        key: 'purge',
        label: t('Purge (permanent)'),
        icon: 'trash-outline',
        danger: true,
        onPress: () => confirm({
          title: t('Purge Mission'),
          message: t('Purge mission "{{title}}"? This will permanently remove it and clean up all associated resources. This cannot be undone.', { title: m.title }),
          confirmLabel: t('Purge'),
          danger: true,
          onConfirm: async () => {
            try {
              await purgeMission(m.id);
              pushToast('warning', t('Mission "{{title}}" purged.', { title: m.title }));
              void reload();
            } catch (e) {
              pushToast('error', errorMessage(e, t('Purge failed.')));
            }
          },
        }),
      },
    ];
  }

  function toggle(id: string) {
    setSelection((current) => {
      const now = current ?? [];
      return now.includes(id) ? now.filter((x) => x !== id) : [...now, id];
    });
  }

  function bulkDelete() {
    const ids = [...(selection ?? [])];
    confirm({
      title: t('Delete Selected Missions'),
      message: t('Delete {{count}} selected mission(s)? This cannot be undone.', { count: ids.length }),
      confirmLabel: t('Delete'),
      danger: true,
      onConfirm: async () => {
        setSelection(null);
        let failed = 0;
        for (const id of ids) {
          try { await purgeMission(id); } catch { failed++; }
        }
        const success = ids.length - failed;
        if (success > 0) {
          pushToast(failed > 0 ? 'warning' : 'success', failed > 0
            ? t('Purged {{success}} missions. {{failed}} failed.', { success, failed })
            : t('Purged {{success}} missions.', { success }));
        }
        if (failed > 0) pushToast('error', t('Deleted {{success}} missions, {{failed}} failed.', { success, failed }));
        void reload();
      },
    });
  }

  async function submitCreate(values: MissionFormValues) {
    setCreating(true);
    try {
      await createMission({ title: values.title, description: values.description, vesselId: values.vesselId, priority: values.priority, mode: values.mode });
      setCreateOpen(false);
      pushToast('success', t('Mission "{{title}}" created.', { title: values.title }));
      void reload();
    } catch (e) {
      pushToast('error', errorMessage(e, t('Create failed.')));
    } finally {
      setCreating(false);
    }
  }

  async function submitTransition(target: string) {
    const m = transitionFor;
    setTransitionFor(null);
    if (!m) return;
    await act(
      () => transitionMission(m.id, { status: target }),
      t('Mission "{{title}}" moved to {{status}}.', { title: m.title, status: target }),
      t('Transition failed.'),
    );
  }

  const selecting = selection !== null;
  useHardwareBack(selecting, () => setSelection(null));
  const header = (
    <View style={styles.header}>
      <SearchField value={search} onChangeText={setSearch} placeholder={t('Search missions')} clearLabel={t('Clear')} testID="missions-search" />
      <View style={styles.toolbar}>
        <FilterButton count={activeFilterCount({ status, userScope })} onClear={() => { setStatus(''); setUserScope(''); }} testID="missions-filters">
          <SelectField
            label={t('Status')}
            value={status}
            onChange={setStatus}
            allowEmpty
            placeholder={t('All Statuses')}
            closeLabel={t('Close')}
            options={MISSION_LIST_STATUSES.map((s) => ({ value: s, label: t(s) }))}
            testID="missions-filter-status"
          />
          {canScope ? (
            <SelectField
              label={t('View records for a specific user')}
              value={userScope}
              onChange={setUserScope}
              allowEmpty
              placeholder={t('All users')}
              closeLabel={t('Close')}
              searchLabel={t('Search...')}
              options={users.map((u) => ({ value: u.id, label: userScopeLabel(u) }))}
              testID="missions-filter-user"
            />
          ) : null}
        </FilterButton>
        {selecting ? (
          <Button label={t('Cancel')} variant="ghost" onPress={() => setSelection(null)} testID="missions-select-cancel" style={styles.flex} />
        ) : (
          <Button label={`+ ${t('Mission')}`} onPress={() => setCreateOpen(true)} testID="missions-create" style={styles.flex} />
        )}
      </View>
      {selecting ? (
        <View style={styles.toolbar}>
          <Button
            label={t('Select All')}
            variant="ghost"
            onPress={() => setSelection(shown.map((m) => m.id))}
            testID="missions-select-all"
            style={styles.flex}
          />
          <Button
            label={`${t('Delete Selected')} (${selection.length})`}
            variant="danger"
            disabled={selection.length === 0}
            onPress={bulkDelete}
            testID="missions-delete-selected"
            style={styles.flex}
          />
        </View>
      ) : null}
    </View>
  );

  return (
    <View style={styles.fill}>
      <PagedList
        testID="missions-list"
        state={view}
        header={header}
        keyExtractor={(m) => m.id}
        loadingLabel={t('Loading missions...')}
        emptyTitle={list.items.length > 0 ? t('No missions match the current filters.') : t('No missions found.')}
        renderItem={({ item: m }) => {
          const checked = selection?.includes(m.id) ?? false;
          const subtitle = [
            names.vesselName(m.vesselId),
            m.captainId ? names.captainName(m.captainId) : null,
            m.branchName,
            `${t('Priority')} ${m.priority}`,
          ].filter(Boolean).join(' - ');
          return (
            <View
              style={[styles.row, { borderBottomColor: colors.border, backgroundColor: m.id === selectedId || checked ? colors.surfaceRaised : colors.surface }, m.id === selectedId ? { borderLeftWidth: 3, borderLeftColor: colors.primary } : null]}
            >
              <Pressable
                style={({ pressed }) => [styles.rowMain, { opacity: pressed ? 0.7 : 1 }]}
                testID={`mission-row-${m.id}`}
                accessibilityRole={selecting ? 'checkbox' : 'button'}
                accessibilityState={selecting ? { checked } : { selected: m.id === selectedId }}
                accessibilityLabel={`${m.title}, ${t(m.status)}, ${subtitle}`}
                accessibilityHint={selecting ? undefined : t('Long press to select missions')}
                onPress={() => (selecting ? toggle(m.id) : onSelect(m.id))}
                onLongPress={() => (selecting ? toggle(m.id) : setSelection([m.id]))}
              >
                {selecting ? <Icon name={checked ? 'checkbox' : 'square-outline'} color={checked ? 'primary' : 'textMuted'} /> : null}
                <View style={styles.flex}>
                  <AppText variant="label" numberOfLines={2}>{m.title}</AppText>
                  <AppText variant="caption" muted numberOfLines={2}>{subtitle}</AppText>
                  <View style={styles.meta}>
                    <EntityStatusBadge status={m.status} />
                    <AppText variant="caption" muted accessibilityLabel={formatDateTime(m.createdUtc)}>{formatRelativeTime(m.createdUtc)}</AppText>
                  </View>
                </View>
              </Pressable>
              {!selecting ? (
                <>
                  <IconButton icon="ellipsis-horizontal" label={t('Actions')} color="textMuted" onPress={() => setMenuFor(m)} testID={`mission-row-menu-${m.id}`} />
                  <IconButton icon="checkbox-outline" label={t('Select')} color="textMuted" onPress={() => setSelection([m.id])} testID={`mission-row-select-${m.id}`} />
                </>
              ) : null}
            </View>
          );
        }}
      />
      <ActionSheet
        open={menuFor !== null}
        title={menuFor?.title ?? ''}
        actions={menuFor ? rowActions(menuFor) : []}
        onClose={() => setMenuFor(null)}
        closeLabel={t('Close')}
        testID="mission-row-actions"
      />
      <TransitionSheet
        open={transitionFor !== null}
        currentStatus={transitionFor?.status ?? ''}
        statuses={MISSION_LIST_STATUSES.filter((s) => s !== transitionFor?.status)}
        onClose={() => setTransitionFor(null)}
        onSubmit={(target) => void submitTransition(target)}
      />
      <JsonSheet open={jsonFor !== null} title={t('Mission: {{title}}', { title: jsonFor?.title ?? '' })} data={jsonFor} onClose={() => setJsonFor(null)} />
      <MissionFormSheet
        open={createOpen}
        kind="create"
        initial={{ title: '', description: '', vesselId: '', priority: 100, mode: 'Implementation' }}
        vessels={names.vessels}
        busy={creating}
        onClose={() => setCreateOpen(false)}
        onSubmit={(values) => void submitCreate(values)}
      />
      {confirmElement}
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  flex: { flex: 1 },
  header: { paddingTop: spacing.sm },
  toolbar: { flexDirection: 'row', gap: spacing.sm, paddingHorizontal: spacing.md },
  row: { flexDirection: 'row', alignItems: 'center', minHeight: MIN_TOUCH + 16, paddingLeft: spacing.lg, paddingRight: spacing.xs, borderBottomWidth: StyleSheet.hairlineWidth },
  rowMain: { flex: 1, flexDirection: 'row', alignItems: 'center', gap: spacing.md, paddingVertical: spacing.sm },
  meta: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, marginTop: 2 },
});
