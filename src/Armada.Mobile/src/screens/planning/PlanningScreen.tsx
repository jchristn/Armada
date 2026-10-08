import { Stack, useLocalSearchParams, useRouter, type Href } from 'expo-router';
import { useMemo, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { deletePlanningSession, listPlanningSessions, stopPlanningSession } from '@dashboard/api/client';
import type { PlanningSession } from '@dashboard/types/models';
import { removeSession, upsertSession } from '@dashboard/lib/planningSessions';
import { useActionRunner } from '../../build/fields';
import { ListPane } from '../../build/ListPane';
import { useLiveResource } from '../../build/useLiveResource';
import { statusTone } from '../../components/ask/statusTone';
import { AppText, Banner, Button, ConfirmDialog, EmptyState, ListRow, SplitView, StatusBadge, SwipeRow, type SwipeAction } from '../../components/ui';
import { useLocale } from '../../i18n/LocaleContext';
import { useLayout } from '../../navigation/useLayout';
import { useNotifications } from '../../notifications/NotificationContext';
import { useTheme } from '../../theme/ThemeContext';
import { spacing } from '../../theme/typography';
import { PlanningSessionView } from './PlanningSessionView';
import { PlanningStartSheet, type PlanningPrefill } from './PlanningStartSheet';
import { usePlanningCatalog } from './usePlanningCatalog';

/** Socket events that change the session list (messages stream into the open session, not the list). */
export const PLANNING_LIST_EVENTS = ['planning-session.changed', 'planning-session.deleted', 'planning-session.dispatch.created'];

/** Where a prefilled start came from (the dashboard's fromObjective / fromIncident / fromWorkspace / fromSetupWizard). */
export type PlanningPrefillSource = 'objective' | 'incident' | 'workspace' | 'setup';

function param(value: string | string[] | undefined): string | undefined {
  const v = Array.isArray(value) ? value[0] : value;
  return v ? v : undefined;
}

/** The start-form prefill carried by /planning route params (`new=1` opens the form). */
export function prefillFromParams(params: Record<string, string | string[] | undefined>): { open: boolean; source: PlanningPrefillSource | null; prefill: PlanningPrefill | null } {
  const prefill: PlanningPrefill = {
    title: param(params.title),
    captainId: param(params.captainId),
    fleetId: param(params.fleetId),
    vesselId: param(params.vesselId),
    pipelineId: param(params.pipelineId),
    objectiveId: param(params.objectiveId),
    initialPrompt: param(params.prompt),
  };
  const from = param(params.from);
  const source = from === 'objective' || from === 'incident' || from === 'workspace' || from === 'setup' ? from : null;
  const any = Object.values(prefill).some(Boolean);
  return { open: param(params.new) === '1' || any, source, prefill: any ? prefill : null };
}

/**
 * Planning (the dashboard's Planning page): the sessions (live: started, ended, dispatched, and deleted ones update
 * in place), Start Session in a sheet, End Session and Delete as swipe actions, Delete All with the typed
 * confirmation. Phones open a session on its own route; tablets show it beside the list.
 */
export function PlanningScreen({ initialSessionId = null }: { initialSessionId?: string | null }) {
  const { t, formatRelativeTime } = useLocale();
  const { colors } = useTheme();
  const router = useRouter();
  const { isTablet } = useLayout();
  const { pushToast } = useNotifications();
  const params = useLocalSearchParams<Record<string, string>>();
  const fromRoute = useMemo(() => prefillFromParams(params), [params]);
  const catalog = usePlanningCatalog();
  const { busy, run } = useActionRunner();
  const sessions = useLiveResource(async () => upsertAll(await listPlanningSessions()), [], { live: PLANNING_LIST_EVENTS });
  const [search, setSearch] = useState('');
  const [startOpen, setStartOpen] = useState(fromRoute.open && !initialSessionId);
  const [selectedId, setSelectedId] = useState<string | null>(initialSessionId);
  const [drafts, setDrafts] = useState<Record<string, string>>({});
  const [confirmEnd, setConfirmEnd] = useState<PlanningSession | null>(null);
  const [confirmDeleteAll, setConfirmDeleteAll] = useState(false);

  const all = useMemo(() => sessions.data ?? [], [sessions.data]);
  const shown = useMemo(() => {
    const term = search.trim().toLowerCase();
    if (!term) return all;
    return all.filter((s) => [s.title, s.id, catalog.captainName(s.captainId), catalog.vesselName(s.vesselId), s.status].some((v) => v.toLowerCase().includes(term)));
  }, [all, search, catalog]);

  const open = (id: string, draft: string | null = null) => {
    if (draft) setDrafts((d) => ({ ...d, [id]: draft }));
    if (isTablet) setSelectedId(id);
    else router.push((draft ? { pathname: '/planning/[id]', params: { id, prompt: draft } } : `/planning/${id}`) as Href);
  };

  const closeSelected = () => {
    setSelectedId(null);
    if (initialSessionId) router.replace('/planning' as Href);
    void sessions.reload();
  };

  const endSession = async (target: PlanningSession) => {
    const result = await run(`end:${target.id}`, () => stopPlanningSession(target.id), t('Planning session is ending.'));
    if (result) sessions.setData((cur) => upsertSession(cur ?? [], result.session));
  };

  const deleteOne = async (target: PlanningSession) => {
    const done = await run(`delete:${target.id}`, async () => { await deletePlanningSession(target.id); return true; });
    if (!done) return;
    sessions.setData((cur) => removeSession(cur ?? [], target.id));
    if (selectedId === target.id) setSelectedId(null);
    pushToast('warning', t('Planning session deleted.'));
  };

  // Each session is deleted on its own so one that cannot be deleted (still running) does not stop the rest.
  const deleteAll = async () => {
    const targets = all.slice();
    if (targets.length === 0) return;
    await run('deleteAll', async () => {
      let failures = 0;
      for (const target of targets) {
        try {
          await deletePlanningSession(target.id);
          sessions.setData((cur) => removeSession(cur ?? [], target.id));
        } catch {
          failures += 1;
        }
      }
      setSelectedId(null);
      if (failures > 0) pushToast('warning', t('{{count}} planning session(s) could not be deleted (they may still be running).', { count: failures }));
      else pushToast('warning', t('All planning sessions deleted.'));
    });
  };

  const prefillNotice = fromRoute.source === 'objective'
    ? t('Prefilled from a backlog item. Review the vessel and prompt, then start a planning session to turn that scoped work into an execution plan.')
    : fromRoute.source === 'incident'
      ? t('Prefilled from an incident. Review the vessel and prompt, then start a planning session for the hotfix or recovery path.')
      : fromRoute.source === 'setup'
        ? t('Prefilled from the setup wizard. Review the vessel and prompt, then start a planning session to turn onboarding follow-up into an execution plan.')
        : fromRoute.source === 'workspace'
          ? t('Prefilled from Workspace. Review the vessel and prompt, then start a planning session.')
          : null;

  const list = (
    <ListPane
      testID="planning-list"
      items={shown}
      keyOf={(s) => s.id}
      loading={sessions.loading}
      refreshing={sessions.refreshing}
      error={sessions.error}
      onRefresh={() => void sessions.refresh()}
      search={{ value: search, onChange: setSearch, placeholder: t('Search planning sessions') }}
      actions={all.length > 0 ? [{ key: 'delete-all', icon: 'trash-outline', label: t('Delete all planning sessions'), onPress: () => setConfirmDeleteAll(true) }] : []}
      summary={t('{{count}} total', { count: all.length })}
      header={(
        <View style={styles.header}>
          <AppText muted style={styles.gap}>{t('Chat with a captain against a specific vessel, preserve the transcript, and dispatch directly from the planning output.')}</AppText>
          {prefillNotice ? <Banner tone="info" title={prefillNotice} /> : null}
          {prefillNotice && fromRoute.prefill?.objectiveId ? (
            <Button label={t('Open Backlog Item')} variant="ghost" onPress={() => router.push(`/backlog/${fromRoute.prefill?.objectiveId}` as Href)} />
          ) : null}
          <Button label={t('Start Session')} icon="add" onPress={() => setStartOpen(true)} testID="planning-new" />
        </View>
      )}
      emptyTitle={t('No planning sessions yet.')}
      emptyMessage={t('Choose an existing planning session from the table above, or start a new one to begin chatting with a captain.')}
      renderItem={({ item }) => {
        const canEnd = item.status === 'Active' || item.status === 'Responding';
        const ending = busy === `end:${item.id}` || item.status === 'Stopping';
        const actions: SwipeAction[] = [];
        if (canEnd && !ending) actions.push({ key: 'end', label: t('End Session'), icon: 'stop-circle-outline', onPress: () => setConfirmEnd(item) });
        actions.push({ key: 'delete', label: t('Delete'), icon: 'trash-outline', tone: 'danger', onPress: () => void deleteOne(item) });
        return (
          <SwipeRow actions={actions} testID={`planning-row-${item.id}`}>
            <ListRow
              title={item.title}
              subtitle={[catalog.captainName(item.captainId), catalog.vesselName(item.vesselId), catalog.pipelineName(item.pipelineId), ending ? t('Ending...') : formatRelativeTime(item.lastUpdateUtc)].join(' \u00b7 ')}
              accessory={<StatusBadge label={t(item.status)} tone={statusTone(item.status)} />}
              selected={isTablet && selectedId === item.id}
              onPress={() => open(item.id)}
              testID={`planning-open-${item.id}`}
            />
          </SwipeRow>
        );
      }}
    />
  );

  const detail = selectedId
    ? <PlanningSessionView key={selectedId} sessionId={selectedId} catalog={catalog} embedded initialComposer={drafts[selectedId] ?? null} onClosed={closeSelected} />
    : <EmptyState icon="chatbubbles-outline" title={t('Current Session')} message={t('Choose an existing planning session from the table above, or start a new one to begin chatting with a captain.')} />;

  return (
    <View style={[styles.fill, { backgroundColor: colors.background }]}>
      <Stack.Screen options={{ title: t('Planning') }} />
      {isTablet ? <SplitView master={list} detail={detail} masterWidth={420} /> : list}
      <PlanningStartSheet
        open={startOpen}
        onClose={() => setStartOpen(false)}
        catalog={catalog}
        prefill={fromRoute.prefill}
        onStarted={(id, draft) => {
          setStartOpen(false);
          void sessions.reload();
          open(id, draft);
        }}
        onTimedOut={() => void sessions.reload()}
      />
      <ConfirmDialog
        open={!!confirmEnd}
        title={t('End Planning Session')}
        message={t('End this planning session and release the reserved captain and dock? The transcript will be kept until you delete it or it expires under server retention settings.')}
        confirmLabel={t('End Session')}
        cancelLabel={t('Cancel')}
        onConfirm={() => { const target = confirmEnd; setConfirmEnd(null); if (target) void endSession(target); }}
        onCancel={() => setConfirmEnd(null)}
        testID="planning-list-end-confirm"
      />
      <ConfirmDialog
        open={confirmDeleteAll}
        title={t('Delete All Planning Sessions')}
        message={t('Delete all {{count}} planning session(s) and their transcripts? This cannot be undone.', { count: all.length })}
        confirmLabel={t('Delete All')}
        cancelLabel={t('Cancel')}
        danger
        typedConfirmation="delete"
        typedLabel={t('Type delete to confirm')}
        onConfirm={() => { setConfirmDeleteAll(false); void deleteAll(); }}
        onCancel={() => setConfirmDeleteAll(false)}
        testID="planning-delete-all-confirm"
      />
    </View>
  );
}

/** The server's list, newest update first (the dashboard keeps this order with upsertSession). */
function upsertAll(items: PlanningSession[]): PlanningSession[] {
  return items.reduce<PlanningSession[]>((acc, s) => upsertSession(acc, s), []);
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  header: { paddingHorizontal: spacing.lg },
  gap: { marginBottom: spacing.md },
});
