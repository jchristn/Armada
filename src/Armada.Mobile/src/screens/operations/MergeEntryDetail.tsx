import { Stack, useRouter, type Href } from 'expo-router';
import { useState } from 'react';
import { RefreshControl, StyleSheet, View } from 'react-native';
import {
  cancelMergeEntry, deleteMergeEntry, getMergeEntry, getVesselLandingPreview, processMergeEntry,
} from '@dashboard/api/client';
import type { LandingPreviewResult, MergeEntry } from '@dashboard/types/models';
import { EntityStatusBadge } from '../../components/app/EntityStatusBadge';
import { JsonSheet } from '../../components/app/JsonSheet';
import { useConfirm } from '../../components/app/useConfirm';
import { AppText, Button, CodeBlock, KeyValueRow, Screen, Section, StatusBadge } from '../../components/ui';
import { errorMessage } from '../../data/errors';
import { useInterval } from '../../data/useInterval';
import { useLiveRefresh } from '../../data/useLiveRefresh';
import { useNameLookups } from '../../data/useNameLookups';
import { useQuery } from '../../data/useQuery';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { useTheme } from '../../theme/ThemeContext';
import { spacing } from '../../theme/typography';
import type { OperationsDetailProps } from './listTypes';
import { MERGE_QUEUE_REFRESH_MS } from './MergeQueueList';
import { DetailActions, DetailHeading, DetailState, useWhen } from './w24/DetailParts';
import { MissionOutputSheet, type MissionOutputRequest } from '../../components/app/MissionOutputSheet';

interface MergeEntryData {
  entry: MergeEntry;
  preview: LandingPreviewResult | null;
}

/** Loads a merge entry and, when it has a vessel, the vessel's landing preview for its branch (best-effort). */
export async function loadMergeEntry(id: string): Promise<MergeEntryData> {
  const entry = await getMergeEntry(id);
  let preview: LandingPreviewResult | null = null;
  if (entry.vesselId) {
    try { preview = await getVesselLandingPreview(entry.vesselId, entry.branchName); } catch { preview = null; }
  }
  return { entry, preview };
}

/**
 * A merge queue entry (the dashboard's MergeQueueDetail): landing preview, fields, test command and output, and the
 * Process, Cancel, Mission Diff, Mission Log, View JSON, and Delete actions.
 */
export function MergeEntryDetail({ id, embedded }: OperationsDetailProps) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const router = useRouter();
  const { pushToast } = useNotifications();
  const when = useWhen();
  const lookups = useNameLookups({ vessels: true });
  const [confirmElement, ask] = useConfirm('merge-entry-confirm');
  const [json, setJson] = useState(false);
  const [view, setView] = useState<MissionOutputRequest | null>(null);
  const query = useQuery(() => loadMergeEntry(id), [id], t('Failed to load merge entry.'));
  useLiveRefresh(['mission.'], () => { void query.reload(); });
  useInterval(() => { void query.reload(); }, MERGE_QUEUE_REFRESH_MS);
  const entry = query.data?.entry ?? null;
  const preview = query.data?.preview ?? null;
  const title = t('Merge Entry');

  const act = async (action: () => Promise<unknown>, success: string, severity: 'success' | 'warning', failure: string, after?: () => void) => {
    try {
      await action();
      pushToast(severity, success);
      if (after) after(); else await query.reload();
    } catch (e) {
      pushToast('error', errorMessage(e, failure));
    }
  };

  if (!entry) {
    return (
      <View style={styles.fill} testID="merge-entry-detail">
        {embedded ? null : <Stack.Screen options={{ title }} />}
        <DetailState
          loading={query.loading}
          error={query.error}
          missing={!query.loading}
          missingTitle={t('Merge entry not found.')}
          onRetry={() => void query.refresh()}
          onBack={embedded ? undefined : () => router.back()}
          backLabel={t('Back')}
        />
      </View>
    );
  }

  const ready = !!preview?.isReadyToLand;
  return (
    <Screen
      testID="merge-entry-detail"
      refreshControl={<RefreshControl refreshing={query.refreshing} onRefresh={() => void query.refresh()} tintColor={colors.primary} />}
    >
      {embedded ? null : <Stack.Screen options={{ title }} />}
      <View style={styles.head}>
        <AppText variant="heading" accessibilityRole="header" selectable testID="merge-entry-title">{`${entry.branchName} -> ${entry.targetBranch}`}</AppText>
        <EntityStatusBadge status={entry.status} testID="merge-entry-status" />
      </View>
      <DetailActions>
        <Button label={t('Process')} icon="play-outline" onPress={() => ask({
          title: t('Process Entry'),
          message: t('Process merge entry for branch "{{branchName}}"?', { branchName: entry.branchName }),
          confirmLabel: t('Process'),
          onConfirm: () => act(() => processMergeEntry(entry.id), t('Merge entry {{id}} processing started.', { id: entry.id }), 'success', t('Process failed.')),
        })} testID="merge-entry-process" />
        <Button label={t('Cancel')} variant="secondary" icon="stop-circle-outline" onPress={() => ask({
          title: t('Cancel Entry'),
          message: t('Cancel merge entry for branch "{{branchName}}"?', { branchName: entry.branchName }),
          confirmLabel: t('Cancel Entry'),
          danger: true,
          onConfirm: () => act(() => cancelMergeEntry(entry.id), t('Merge entry {{id}} cancelled.', { id: entry.id }), 'warning', t('Cancel failed.')),
        })} testID="merge-entry-cancel" />
        {entry.missionId ? (
          <>
            <Button label={t('Diff')} variant="secondary" icon="git-compare-outline" onPress={() => setView({ kind: 'diff', missionId: entry.missionId!, title: t('Mission {{id}}...', { id: entry.missionId!.substring(0, 8) }) })} testID="merge-entry-diff" />
            <Button label={t('Log')} variant="secondary" icon="document-text-outline" onPress={() => setView({ kind: 'log', missionId: entry.missionId!, title: t('Mission {{id}}...', { id: entry.missionId!.substring(0, 8) }) })} testID="merge-entry-log" />
          </>
        ) : null}
        <Button label={t('View JSON')} variant="ghost" icon="code-outline" onPress={() => setJson(true)} testID="merge-entry-json" />
        <Button label={t('Delete')} variant="danger" icon="trash-outline" onPress={() => ask({
          title: t('Delete Entry'),
          message: t('Delete merge entry {{id}}? This cannot be undone.', { id: entry.id }),
          confirmLabel: t('Delete'),
          danger: true,
          onConfirm: () => act(() => deleteMergeEntry(entry.id), t('Merge entry {{id}} deleted.', { id: entry.id }), 'warning', t('Delete failed.'),
            () => { if (embedded) void query.reload(); else router.back(); }),
        })} testID="merge-entry-delete" />
      </DetailActions>

      <Section title={t('Landing Preview')}>
        <View style={styles.pad} testID="merge-entry-landing">
          <View style={styles.rowBetween}>
            <AppText variant="mono" selectable style={styles.flex}>{`${entry.branchName} -> ${entry.targetBranch}`}</AppText>
            <StatusBadge label={ready ? t('Ready To Land') : t('Needs Review')} tone={ready ? 'success' : 'warning'} />
          </View>
          {!preview ? <AppText muted>{t('Landing preview is not available for this merge entry yet.')}</AppText> : (
            <>
              <AppText variant="caption" muted>{`${t('Branch category')}: ${preview.branchCategory}`}</AppText>
              <AppText variant="caption" muted>{`${t('Landing mode')}: ${preview.landingMode || t('Inherited')}`}</AppText>
              <AppText variant="caption" muted>{`${t('Cleanup')}: ${preview.branchCleanupPolicy || t('Inherited')}`}</AppText>
              {preview.expectedLandingAction ? <AppText variant="caption" muted>{`${t('Action')}: ${preview.expectedLandingAction}`}</AppText> : null}
              <AppText variant="caption" muted>{preview.targetBranchProtected ? t('Protected target branch') : t('Target branch not protected')}</AppText>
              {preview.protectedBranchMatch ? <AppText variant="caption" muted>{`${t('Policy')}: ${preview.protectedBranchMatch}`}</AppText> : null}
              {preview.requirePullRequestForProtectedBranches ? <AppText variant="caption" muted>{t('PR required for protected branches')}</AppText> : null}
              {preview.requireMergeQueueForReleaseBranches ? <AppText variant="caption" muted>{t('Merge queue required for release branches')}</AppText> : null}
              {preview.issues.length > 0 ? preview.issues.map((issue, index) => (
                <View key={`${issue.code}-${index}`} style={styles.issue}>
                  <View style={styles.rowBetween}>
                    <AppText variant="label" style={styles.flex}>{issue.title}</AppText>
                    <StatusBadge label={issue.severity} tone={issue.severity.toLowerCase() === 'error' ? 'failed' : 'warning'} />
                  </View>
                  <AppText variant="caption" muted>{issue.message}</AppText>
                </View>
              )) : <AppText color="success">{t('No landing blockers are currently predicted for this merge entry.')}</AppText>}
            </>
          )}
        </View>
      </Section>

      <Section>
        <KeyValueRow label={t('ID')} value={entry.id} mono testID="merge-entry-id" />
        <KeyValueRow label={t('Branch')} value={entry.branchName} mono />
        <KeyValueRow label={t('Target Branch')} value={entry.targetBranch} mono />
        <KeyValueRow label={t('Priority')} value={entry.priority} />
        <KeyValueRow label={t('Vessel')} value={entry.vesselId ? lookups.vesselName(entry.vesselId) : null}
          onPress={entry.vesselId ? () => router.push(`/vessels/${entry.vesselId}` as Href) : undefined} />
        <KeyValueRow label={t('Mission')} value={entry.missionId} mono
          onPress={entry.missionId ? () => router.push(`/missions/${entry.missionId}` as Href) : undefined} testID="merge-entry-mission" />
        <KeyValueRow label={t('Batch ID')} value={entry.batchId} mono />
        <KeyValueRow label={t('Test Exit Code')} value={entry.testExitCode ?? null} />
        <KeyValueRow label={t('Tenant ID')} value={entry.tenantId} mono />
        <KeyValueRow label={t('Created')} value={when(entry.createdUtc)} />
        <KeyValueRow label={t('Test Started')} value={when(entry.testStartedUtc)} />
        <KeyValueRow label={t('Completed')} value={when(entry.completedUtc)} />
        <KeyValueRow label={t('Last Updated')} value={when(entry.lastUpdateUtc)} />
      </Section>

      {entry.testCommand ? (
        <View style={styles.block}>
          <DetailHeading>{t('Test Command')}</DetailHeading>
          <View style={styles.pad}><CodeBlock text={entry.testCommand} /></View>
        </View>
      ) : null}
      {entry.testOutput ? (
        <View style={styles.block}>
          <DetailHeading>{t('Test Output')}</DetailHeading>
          <View style={styles.pad}><CodeBlock text={entry.testOutput} testID="merge-entry-test-output" /></View>
        </View>
      ) : null}

      {confirmElement}
      <JsonSheet open={json} title={t('Merge Entry: {{id}}', { id: entry.id })} data={entry} onClose={() => setJson(false)} />
      <MissionOutputSheet request={view} onClose={() => setView(null)} />
    </Screen>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  head: { paddingHorizontal: spacing.lg, gap: spacing.sm, marginBottom: spacing.md },
  pad: { paddingHorizontal: spacing.lg, paddingVertical: spacing.sm, gap: spacing.xs },
  rowBetween: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  flex: { flex: 1 },
  issue: { gap: 2, marginTop: spacing.sm },
  block: { marginBottom: spacing.xl },
});
