import { cancelVoyage, createMission, getVoyage, listMissions, purgeVoyage } from '@dashboard/api/client';
import { findLandingMode, getVoyageLandingModes } from '@dashboard/lib/vesselForm';
import {
  formatDeliveryMode,
  isVoyageActive,
  parseCaptainOverrides,
  retryMissionPayload,
  splitVoyageResponse,
  voyageProgress,
} from '@dashboard/lib/voyageDetail';
import type { Mission, Voyage } from '@dashboard/types/models';
import { Stack, useRouter, type Href } from 'expo-router';
import { useState } from 'react';
import { RefreshControl, StyleSheet, View } from 'react-native';
import { EntityStatusBadge } from '../../components/app/EntityStatusBadge';
import { JsonSheet } from '../../components/app/JsonSheet';
import { useConfirm } from '../../components/app/useConfirm';
import { ActionSheet, AppText, Button, EmptyState, ErrorState, ListRow, LoadingState, Screen, Section } from '../../components/ui';
import { KeyValueRow } from '../../components/ui/KeyValueRow';
import { ProgressBar } from '../../components/ui/ProgressBar';
import { detailLoadError } from '../../data/errors';
import { useLiveRefresh } from '../../data/useLiveRefresh';
import { useNameLookups } from '../../data/useNameLookups';
import { useQuery } from '../../data/useQuery';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { useTheme } from '../../theme/ThemeContext';
import { spacing } from '../../theme/typography';
import type { OperationsDetailProps } from './listTypes';
import { MissionOutputSheet, type MissionOutputRequest } from '../../components/app/MissionOutputSheet';

interface VoyageData {
  voyage: Voyage;
  missions: Mission[];
}

/** Loads a voyage and its missions (getVoyage answers either shape; missions are listed separately when absent). */
export async function loadVoyageData(id: string): Promise<VoyageData> {
  const parts = splitVoyageResponse(await getVoyage(id));
  if (parts.missions) return { voyage: parts.voyage, missions: parts.missions };
  let missions: Mission[] = [];
  try {
    const result = await listMissions({ pageSize: 1000, filters: { voyageId: id } });
    missions = result?.objects ?? [];
  } catch {
    missions = [];
  }
  return { voyage: parts.voyage, missions };
}

/** The query string a prefilled form reads (Run Check, Draft Release): empty values are left out. */
export function prefillQuery(values: Record<string, string | null | undefined>): string {
  const pairs = Object.entries(values).filter(([, v]) => !!v).map(([k, v]) => `${encodeURIComponent(k)}=${encodeURIComponent(v as string)}`);
  return pairs.length ? `?${pairs.join('&')}` : '';
}

/**
 * Voyage detail (the dashboard's /voyages/:id): status, details, landing configuration, captain assignments,
 * timestamps, progress, playbooks, and the voyage's missions with their diff and log; cancel, retry failed,
 * delete, Run Check, Draft Release, and View JSON. Follows the voyage and its missions live.
 */
export function VoyageDetail({ id, embedded }: OperationsDetailProps) {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { colors } = useTheme();
  const router = useRouter();
  const { pushToast } = useNotifications();
  const lookups = useNameLookups({ vessels: true, captains: true });
  const query = useQuery(() => loadVoyageData(id), [id], t('Failed to load voyage.'));
  const [confirmElement, confirm] = useConfirm('voyage-confirm');
  const [error, setError] = useState('');
  const [jsonOpen, setJsonOpen] = useState(false);
  const [deleted, setDeleted] = useState(false);
  const [output, setOutput] = useState<MissionOutputRequest | null>(null);
  const [missionMenu, setMissionMenu] = useState<Mission | null>(null);
  const landingModes = getVoyageLandingModes(t);

  useLiveRefresh(['mission.', 'voyage.'], () => { void query.reload(); }, !deleted);

  const title = query.data?.voyage.title || query.data?.voyage.id || t('Voyage');
  const header = embedded ? null : <Stack.Screen options={{ title }} />;

  if (deleted) {
    return <>{header}<EmptyState icon="trash-outline" title={t('Voyage "{{title}}" deleted.', { title })} /></>;
  }
  if (query.loading) return <>{header}<LoadingState label={t('Loading...')} /></>;
  if (!query.data) {
    const failed = detailLoadError(t, query.failure, query.error, t('Voyage not found.'), t('Failed to load voyage.'));
    return (
      <>
        {header}
        <ErrorState title={failed.title} message={failed.message} retryLabel={t('Retry')} onRetry={() => void query.refresh()} />
      </>
    );
  }

  const { voyage, missions } = query.data;
  const progress = voyageProgress(missions);
  const landing = findLandingMode(landingModes, voyage.landingMode);
  const overrides = parseCaptainOverrides(voyage.captainOverridesJson);
  const snapshots = missions[0]?.playbookSnapshots ?? [];
  const selections = voyage.selectedPlaybooks ?? [];
  const failed = missions.filter((m) => m.status === 'Failed');
  const first = missions[0];
  const message = (e: unknown) => (e instanceof Error ? e.message : String(e));

  const handleCancel = () => confirm({
    title: t('Cancel Voyage'),
    message: t('Cancel this voyage? All pending missions will be cancelled.'),
    confirmLabel: t('Cancel Voyage'),
    danger: true,
    onConfirm: async () => {
      try {
        await cancelVoyage(voyage.id);
        pushToast('warning', t('Voyage "{{title}}" cancelled.', { title: voyage.title || voyage.id }));
        void query.reload();
      } catch (e) {
        setError(t('Cancel failed: {{message}}', { message: message(e) }));
      }
    },
  });

  const handleDelete = () => confirm({
    title: t('Delete Voyage'),
    message: t('Permanently delete this voyage and all its missions? This cannot be undone.'),
    confirmLabel: t('Delete'),
    danger: true,
    onConfirm: async () => {
      try {
        await purgeVoyage(voyage.id);
        pushToast('warning', t('Voyage "{{title}}" deleted.', { title: voyage.title || voyage.id }));
        if (embedded) setDeleted(true);
        else if (router.canGoBack()) router.back();
        else router.replace('/missions?tab=voyages' as Href);
      } catch (e) {
        setError(t('Delete failed: {{message}}', { message: message(e) }));
      }
    },
  });

  const handleRetryFailed = () => confirm({
    title: t('Retry Failed Missions'),
    message: t('Retry {{count}} failed mission(s)? New missions will be created with the same parameters.', { count: failed.length }),
    confirmLabel: t('Retry Failed'),
    onConfirm: async () => {
      try {
        for (const m of failed) await createMission(retryMissionPayload(m));
        pushToast('success', t('Retried {{count}} failed mission(s).', { count: failed.length }));
        void query.reload();
      } catch (e) {
        setError(t('Retry failed: {{message}}', { message: message(e) }));
      }
    },
  });

  return (
    <Screen
      testID="voyage-detail"
      refreshControl={<RefreshControl refreshing={query.refreshing} onRefresh={() => void query.refresh()} tintColor={colors.primary} />}
    >
      {header}
      {confirmElement}
      <View style={styles.head}>
        <AppText variant="heading" accessibilityRole="header" testID="voyage-detail-title" selectable>{title}</AppText>
        <EntityStatusBadge status={voyage.status} testID="voyage-detail-status" />
      </View>
      {error || query.error ? (
        <View style={styles.pad}>
          <AppText color="danger" accessibilityRole="alert" testID="voyage-detail-error">{error || query.error}</AppText>
        </View>
      ) : null}

      <View style={styles.actions}>
        <Button
          testID="voyage-run-check"
          variant="secondary"
          label={t('Run Check')}
          onPress={() => router.push(`/checks${prefillQuery({ vesselId: first?.vesselId, voyageId: voyage.id, branchName: first?.branchName, label: voyage.title })}` as Href)}
        />
        <Button
          testID="voyage-draft-release"
          variant="secondary"
          label={t('Draft Release')}
          onPress={() => router.push(`/releases/new${prefillQuery({
            vesselId: first?.vesselId,
            voyageIds: voyage.id,
            missionIds: missions.map((m) => m.id).join(','),
            title: voyage.title ? `${voyage.title} Release` : 'Voyage Release',
          })}` as Href)}
        />
        {isVoyageActive(voyage.status) ? <Button testID="voyage-cancel" variant="danger" label={t('Cancel Voyage')} onPress={handleCancel} /> : null}
        {failed.length > 0 ? <Button testID="voyage-retry-failed" variant="secondary" label={`${t('Retry Failed')} (${failed.length})`} onPress={handleRetryFailed} /> : null}
        <Button testID="voyage-json" variant="ghost" label={t('View JSON')} onPress={() => setJsonOpen(true)} />
        {!isVoyageActive(voyage.status) ? <Button testID="voyage-delete" variant="danger" label={t('Delete')} onPress={handleDelete} /> : null}
      </View>

      {missions.length > 0 ? (
        <View style={styles.progress}>
          <ProgressBar percent={progress.percent} color="primary" label={t('Progress')} testID="voyage-progress" />
          <AppText variant="caption" muted testID="voyage-progress-text">
            {t('{{completed}}/{{total}} complete, {{failed}} failed', { completed: progress.completed, total: progress.total, failed: progress.failed })}
          </AppText>
        </View>
      ) : null}

      <Section title={t('Details')}>
        <KeyValueRow label={t('ID')} value={voyage.id} mono />
        <KeyValueRow label={t('Description')} value={voyage.description} />
      </Section>

      <Section title={t('Configuration')}>
        <KeyValueRow
          label={t('Auto-Merge PRs')}
          value={voyage.autoMergePullRequests != null ? (voyage.autoMergePullRequests ? t('Yes') : t('No')) : '-'}
        />
        <KeyValueRow label={t('Landing Mode')} testID="voyage-landing-mode">
          <AppText>{`${voyage.landingMode || t('Default')} (${landing.short})`}</AppText>
          <AppText variant="caption" muted>{landing.description}</AppText>
        </KeyValueRow>
      </Section>

      {overrides.length > 0 ? (
        <Section title={t('Captain Assignments')}>
          {overrides.map((o, i) => (
            <KeyValueRow
              key={`${o.persona}-${i}`}
              label={o.persona}
              value={`${o.captainId ? lookups.captainName(o.captainId) : t('Any')}${o.fallbackTier ? ` - ${t('fallback: {{tier}}', { tier: t(o.fallbackTier) })}` : ''}`}
              onPress={o.captainId ? () => router.push(`/captains/${o.captainId}` as Href) : undefined}
            />
          ))}
        </Section>
      ) : null}

      <Section title={t('Timestamps')}>
        <KeyValueRow label={t('Created')} value={`${formatDateTime(voyage.createdUtc)} (${formatRelativeTime(voyage.createdUtc)})`} />
        {voyage.completedUtc ? (
          <KeyValueRow label={t('Completed')} value={`${formatDateTime(voyage.completedUtc)} (${formatRelativeTime(voyage.completedUtc)})`} />
        ) : null}
      </Section>

      {snapshots.length > 0 || selections.length > 0 ? (
        <Section
          title={t('Playbooks')}
          footer={snapshots.length > 0
            ? t('These snapshots show the actual playbook content and delivery mode that were applied to the voyage missions.')
            : t('This voyage has playbook selections recorded, but mission snapshots are not available yet.')}
        >
          {snapshots.length > 0
            ? snapshots.map((s, i) => (
              <ListRow
                key={`${s.fileName}-${i}`}
                title={s.fileName}
                subtitle={`${t(formatDeliveryMode(s.deliveryMode))} - ${s.worktreeRelativePath || s.resolvedPath || '-'}\n${s.description || t('No description')}`}
              />
            ))
            : selections.map((s, i) => (
              <ListRow
                key={`${s.playbookId}-${i}`}
                title={s.playbookId}
                subtitle={`${t(formatDeliveryMode(s.deliveryMode))} - ${t('Playbook details are available after mission snapshots are created.')}`}
              />
            ))}
        </Section>
      ) : null}

      <Section title={t('Missions')}>
        {missions.length === 0 ? (
          <View style={styles.pad}><AppText muted>{t('No missions in this voyage.')}</AppText></View>
        ) : missions.map((m) => (
          <ListRow
            key={m.id}
            testID={`voyage-mission-row-${m.id}`}
            title={m.title}
            subtitle={[m.id, `${t('Vessel')}: ${lookups.vesselName(m.vesselId)}`, `${t('Captain')}: ${lookups.captainName(m.captainId)}`, m.branchName ? `${t('Branch')}: ${m.branchName}` : null].filter(Boolean).join('\n')}
            accessory={<EntityStatusBadge status={m.status} />}
            accessibilityValue={t(m.status)}
            menu={{ label: t('Actions'), onPress: () => setMissionMenu(m), testID: `voyage-mission-menu-${m.id}` }}
            onPress={() => router.push(`/missions/${m.id}` as Href)}
            onLongPress={() => setMissionMenu(m)}
          />
        ))}
      </Section>

      <ActionSheet
        open={missionMenu !== null}
        title={missionMenu?.title ?? ''}
        closeLabel={t('Close')}
        onClose={() => setMissionMenu(null)}
        testID="voyage-mission-actions"
        actions={missionMenu ? [
          { key: 'diff', label: t('View Diff'), icon: 'git-compare-outline', onPress: () => setOutput({ kind: 'diff', missionId: missionMenu.id, title: missionMenu.title }) },
          { key: 'log', label: t('View Log'), icon: 'document-text-outline', onPress: () => setOutput({ kind: 'log', missionId: missionMenu.id, title: missionMenu.title }) },
          { key: 'detail', label: t('View Detail'), icon: 'open-outline', onPress: () => router.push(`/missions/${missionMenu.id}` as Href) },
        ] : []}
      />
      <MissionOutputSheet request={output} onClose={() => setOutput(null)} />
      <JsonSheet open={jsonOpen} title={t('Voyage: {{title}}', { title })} data={voyage} onClose={() => setJsonOpen(false)} />
    </Screen>
  );
}

const styles = StyleSheet.create({
  head: { paddingHorizontal: spacing.lg, gap: spacing.sm, marginBottom: spacing.md },
  pad: { paddingHorizontal: spacing.lg, paddingVertical: spacing.sm },
  actions: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm, paddingHorizontal: spacing.md, marginBottom: spacing.md },
  progress: { paddingHorizontal: spacing.lg, gap: spacing.xs, marginBottom: spacing.lg },
});
