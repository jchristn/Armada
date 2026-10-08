import { useLocalSearchParams, useRouter } from 'expo-router';
import { useCallback, useEffect, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { deleteFleetAction, enumerateFleetActions, getSettings } from '@dashboard/api/client';
import type { FleetAction, FleetActionKind } from '@dashboard/types/models';
import { useActionRunner } from '../../build/fields';
import { ListPane } from '../../build/ListPane';
import { usePagedList } from '../../build/usePagedList';
import { useAuth } from '../../auth/AuthContext';
import { AppText, Button, ConfirmDialog, ListRow, SwipeRow, type SwipeAction } from '../../components/ui';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { spacing } from '../../theme/typography';
import { JsonSheet, KindBadge } from './common';
import { FleetActionFormSheet } from './FleetActionFormSheet';
import { NO_RUN_FLOW, RunFlowSheet, type RunFlow } from './RunFlowSheet';

type Order = 'CreatedDescending' | 'CreatedAscending';

/** The run kind from `?kind=` (Command or Mission), or undefined. */
export function parseRunKind(raw: string | string[] | undefined): FleetActionKind | undefined {
  const text = Array.isArray(raw) ? raw[0] : raw;
  return text === 'Command' || text === 'Mission' ? text : undefined;
}

/** Vessel ids from `?vessels=a,b` (the vessel screens' "Run action" shortcut). */
export function parseVesselIds(raw: string | string[] | undefined): string[] {
  const text = Array.isArray(raw) ? raw.join(',') : raw ?? '';
  return Array.from(new Set(text.split(',').map((s) => s.trim()).filter(Boolean)));
}

/**
 * The Actions tab of Fleet Actions (the dashboard's FleetActionsTable): saved actions with endless scroll and a
 * created-date order toggle; swipe for Run, Edit, Duplicate, View JSON, and Delete (built-ins are hidden instead).
 * Tenant admins can create and run actions; `?run=new` starts the run flow (vessel picker, then the run sheet) and
 * `?run=new&vessels=<ids>` skips the picker with those vessels.
 */
export function FleetActionsTab() {
  const { t, formatRelativeTime } = useLocale();
  const { isTenantAdmin } = useAuth();
  const { pushToast } = useNotifications();
  const router = useRouter();
  const params = useLocalSearchParams<{ run?: string; vessels?: string; kind?: string }>();
  const { run } = useActionRunner();
  const [order, setOrder] = useState<Order>('CreatedDescending');
  const [defaultTimeout, setDefaultTimeout] = useState(300);
  const [form, setForm] = useState<{ open: boolean; mode: 'create' | 'edit'; source: FleetAction | null }>({ open: false, mode: 'create', source: null });
  const [json, setJson] = useState<FleetAction | null>(null);
  const [confirmDelete, setConfirmDelete] = useState<FleetAction | null>(null);
  const [runFlow, setRunFlow] = useState<RunFlow>(NO_RUN_FLOW);

  const list = usePagedList(
    useCallback((pageNumber: number, pageSize: number) => enumerateFleetActions({ pageNumber, pageSize, order }), [order]),
    [order],
  );

  useEffect(() => {
    let cancelled = false;
    getSettings()
      .then((s) => {
        const fa = (s as { fleetActions?: { defaultTimeoutSeconds?: number } } | undefined)?.fleetActions;
        if (!cancelled && fa?.defaultTimeoutSeconds) setDefaultTimeout(fa.defaultTimeoutSeconds);
      })
      .catch(() => undefined);
    return () => { cancelled = true; };
  }, []);

  // `?run=new` (and optional `vessels=`) opens the run flow once, then clears the parameters.
  const startRun = params.run === 'new';
  const presetVessels = parseVesselIds(params.vessels).join(',');
  const presetKind = parseRunKind(params.kind);
  useEffect(() => {
    if (!startRun) return;
    const ids = presetVessels ? presetVessels.split(',') : [];
    // eslint-disable-next-line react-hooks/set-state-in-effect -- a deep link opens the run flow once
    setRunFlow({ stage: ids.length > 0 ? 'run' : 'pick', actionId: null, vesselIds: ids, kind: presetKind });
    router.setParams({ run: undefined, vessels: undefined, kind: undefined });
  }, [startRun, presetVessels, presetKind, router]);

  async function remove(a: FleetAction) {
    setConfirmDelete(null);
    const ok = await run('delete', async () => { await deleteFleetAction(a.id); return true; });
    if (!ok) return;
    pushToast('warning', a.isBuiltIn ? t('Built-in action "{{name}}" hidden.', { name: a.name }) : t('Fleet action "{{name}}" deleted.', { name: a.name }));
    void list.reload();
  }

  const openCreate = () => setForm({ open: true, mode: 'create', source: null });

  return (
    <View style={styles.fill}>
      <ListPane
        testID="fleet-actions-list"
        items={list.items}
        keyOf={(a) => a.id}
        loading={list.loading}
        refreshing={list.refreshing}
        error={list.error}
        onRefresh={() => void list.refresh()}
        onEndReached={list.loadMore}
        loadingMore={list.loadingMore}
        actions={[
          { key: 'order', icon: order === 'CreatedDescending' ? 'arrow-down' : 'arrow-up', label: order === 'CreatedDescending' ? t('Newest first') : t('Oldest first'), onPress: () => setOrder((o) => (o === 'CreatedDescending' ? 'CreatedAscending' : 'CreatedDescending')) },
          ...(isTenantAdmin ? [{ key: 'new', icon: 'add' as const, label: t('Action'), onPress: openCreate }] : []),
        ]}
        header={(
          <View style={styles.header}>
            <AppText variant="caption" muted>{t('Reusable commands and mission prompts you can run across many vessels.')}</AppText>
            {isTenantAdmin ? (
              <Button label={t('Run action...')} icon="play-outline" variant="secondary" onPress={() => setRunFlow({ stage: 'pick', actionId: null, vesselIds: [] })} testID="fleet-actions-run" />
            ) : null}
            {list.totalRecords > 0 ? <AppText variant="caption" muted>{t('{{count}} actions', { count: list.totalRecords })}</AppText> : null}
          </View>
        )}
        emptyTitle={t('No fleet actions yet')}
        emptyMessage={`${t('Armada seeds five built-in actions into each tenant the first time actions are listed: Fast-forward default branch, Prune merged branches, Build, Update outdated dependencies, and Add a test project.')} ${t('Deleted built-ins are hidden for good and never re-seeded. Create your own action to get started.')}`}
        emptyAction={isTenantAdmin ? { label: t('Action'), onPress: openCreate } : undefined}
        renderItem={({ item }) => {
          const swipe: SwipeAction[] = [];
          if (isTenantAdmin) {
            swipe.push({ key: 'run', label: t('Run'), icon: 'play-outline', onPress: () => setRunFlow({ stage: 'pick', actionId: item.id, vesselIds: [] }) });
            swipe.push({ key: 'duplicate', label: t('Duplicate'), icon: 'copy-outline', onPress: () => setForm({ open: true, mode: 'create', source: item }) });
          }
          swipe.push({ key: 'json', label: t('View JSON'), icon: 'code-slash-outline', onPress: () => setJson(item) });
          if (isTenantAdmin) swipe.push({ key: 'delete', label: t('Delete'), icon: 'trash-outline', tone: 'danger', onPress: () => setConfirmDelete(item) });
          const facts = [
            item.isBuiltIn ? t('Built-in') : t('Custom'),
            !item.active ? t('Hidden') : null,
            item.kind === 'Command' ? t('{{value}} s', { value: item.timeoutSeconds.toLocaleString() }) : null,
            `${t('Concurrency')} ${item.defaultConcurrency.toLocaleString()}`,
            t('Updated {{when}}', { when: formatRelativeTime(item.lastUpdateUtc) }),
          ].filter(Boolean).join(' \u00b7 ');
          return (
            <SwipeRow actions={swipe} testID={`fleet-action-swipe-${item.name}`}>
              <ListRow
                testID={`fleet-action-row-${item.name}`}
                title={item.name}
                subtitle={[item.description, facts].filter(Boolean).join('\n')}
                accessory={<KindBadge kind={item.kind} />}
                onPress={() => (isTenantAdmin ? setForm({ open: true, mode: 'edit', source: item }) : setJson(item))}
                accessibilityHint={isTenantAdmin ? t('Edit') : t('View JSON')}
              />
            </SwipeRow>
          );
        }}
      />
      <FleetActionFormSheet
        open={form.open}
        mode={form.mode}
        source={form.source}
        defaultTimeoutSeconds={defaultTimeout}
        onClose={() => setForm((f) => ({ ...f, open: false }))}
        onSaved={(saved) => {
          setForm((f) => ({ ...f, open: false }));
          pushToast('success', t('Fleet action "{{name}}" saved.', { name: saved.name }));
          void list.reload();
        }}
      />
      <JsonSheet open={json !== null} title={json ? t('Fleet action: {{name}}', { name: json.name }) : ''} data={json} onClose={() => setJson(null)} />
      <ConfirmDialog
        open={confirmDelete !== null}
        title={confirmDelete?.isBuiltIn ? t('Hide built-in action') : t('Delete fleet action')}
        message={confirmDelete
          ? (confirmDelete.isBuiltIn
            ? t('"{{name}}" is a built-in action. Deleting it hides it permanently (a soft delete) and Armada will not seed it again. Past runs keep their snapshot.', { name: confirmDelete.name })
            : t('Delete "{{name}}"? Past runs keep their snapshot of the definition. This cannot be undone.', { name: confirmDelete.name }))
          : ''}
        confirmLabel={confirmDelete?.isBuiltIn ? t('Hide') : t('Delete')}
        cancelLabel={t('Cancel')}
        danger
        onConfirm={() => { if (confirmDelete) void remove(confirmDelete); }}
        onCancel={() => setConfirmDelete(null)}
        testID="fleet-action-delete-confirm"
      />
      <RunFlowSheet flow={runFlow} onChange={setRunFlow} />
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  header: { marginHorizontal: spacing.lg, marginBottom: spacing.sm, gap: spacing.sm },
});
