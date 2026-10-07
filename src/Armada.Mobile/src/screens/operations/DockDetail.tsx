import { Stack, useRouter, type Href } from 'expo-router';
import { useState } from 'react';
import { RefreshControl, StyleSheet, View } from 'react-native';
import { deleteDock, getDock } from '@dashboard/api/client';
import { parseDockGitAnchors } from '@dashboard/lib/dockAnchors';
import { JsonSheet } from '../../components/app/JsonSheet';
import { useConfirm } from '../../components/app/useConfirm';
import { AppText, Button, KeyValueRow, Screen, Section, StatusBadge } from '../../components/ui';
import { errorMessage } from '../../data/errors';
import { useNameLookups } from '../../data/useNameLookups';
import { useQuery } from '../../data/useQuery';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { useTheme } from '../../theme/ThemeContext';
import { spacing } from '../../theme/typography';
import type { OperationsDetailProps } from './listTypes';
import { DetailActions, DetailState, useWhen } from './w24/DetailParts';

/** A dock (the dashboard's DockDetail): fields, links to vessel and captain, starting point, JSON, delete. */
export function DockDetail({ id, embedded }: OperationsDetailProps) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const router = useRouter();
  const { pushToast } = useNotifications();
  const when = useWhen();
  const lookups = useNameLookups({ vessels: true, captains: true });
  const [confirmElement, ask] = useConfirm('dock-detail-confirm');
  const [json, setJson] = useState(false);
  const query = useQuery(() => getDock(id), [id], t('Failed to load dock.'));
  const dock = query.data;
  const title = t('Dock Details');

  if (!dock) {
    return (
      <View style={styles.fill} testID="dock-detail">
        {embedded ? null : <Stack.Screen options={{ title }} />}
        <DetailState loading={query.loading} error={query.error} missing={!query.loading} missingTitle={t('Dock not found.')}
          onRetry={() => void query.refresh()} onBack={embedded ? undefined : () => router.back()} backLabel={t('Back to Docks')} />
      </View>
    );
  }
  const anchors = parseDockGitAnchors(dock.gitAnchorsJson);
  return (
    <Screen testID="dock-detail" refreshControl={<RefreshControl refreshing={query.refreshing} onRefresh={() => void query.refresh()} tintColor={colors.primary} />}>
      {embedded ? null : <Stack.Screen options={{ title }} />}
      <View style={styles.head}>
        <AppText variant="heading" selectable>{dock.branchName || dock.id}</AppText>
        <StatusBadge label={dock.active ? t('Active') : t('Inactive')} tone={dock.active ? 'success' : 'cancelled'} />
      </View>
      <DetailActions>
        <Button label={t('View JSON')} variant="secondary" icon="code-outline" onPress={() => setJson(true)} testID="dock-json" />
        <Button label={t('Delete')} variant="danger" icon="trash-outline" testID="dock-delete" onPress={() => ask({
          title: t('Delete Dock'),
          message: t('Delete dock {{name}}? This will clean up the git worktree and cannot be undone.', { name: dock.id }),
          confirmLabel: t('Delete'),
          danger: true,
          onConfirm: async () => {
            try {
              await deleteDock(dock.id);
              pushToast('warning', t('Dock {{id}} deleted.', { id: dock.id }));
              if (embedded) void query.reload(); else router.back();
            } catch (e) {
              pushToast('error', errorMessage(e, t('Delete failed.')));
            }
          },
        })} />
      </DetailActions>
      <Section>
        <KeyValueRow label={t('ID')} value={dock.id} mono testID="dock-id" />
        <KeyValueRow label={t('Tenant ID')} value={dock.tenantId} mono />
        <KeyValueRow label={t('Vessel')} value={dock.vesselId ? lookups.vesselName(dock.vesselId) : null}
          onPress={dock.vesselId ? () => router.push(`/vessels/${dock.vesselId}` as Href) : undefined} testID="dock-vessel" />
        <KeyValueRow label={t('Captain')} value={dock.captainId ? lookups.captainName(dock.captainId) : null}
          onPress={dock.captainId ? () => router.push(`/captains/${dock.captainId}` as Href) : undefined} testID="dock-captain" />
        <KeyValueRow label={t('Branch Name')} value={dock.branchName} mono />
        <KeyValueRow label={t('Worktree Path')} value={dock.worktreePath} mono />
        <KeyValueRow label={t('Created')} value={when(dock.createdUtc)} />
        <KeyValueRow label={t('Last Updated')} value={when(dock.lastUpdateUtc)} />
      </Section>
      {anchors ? (
        <Section title={t('Starting Point')}>
          <KeyValueRow label={t('Start Commit')} value={anchors.startCommit ?? null} mono />
          <KeyValueRow label={t('Target Branch')} value={anchors.targetBranch ?? null} mono />
          <KeyValueRow label={t('Working Branch')} value={anchors.workingBranch ?? null} mono />
          {anchors.recentPathCommits && anchors.recentPathCommits.length > 0 ? (
            <KeyValueRow label={t('Recent Commits On Relevant Paths')}>
              {anchors.recentPathCommits.map((c, i) => <AppText key={i} variant="mono" selectable>{c}</AppText>)}
            </KeyValueRow>
          ) : null}
          {anchors.subjectTermsPresent && anchors.subjectTermsPresent.length > 0 ? (
            <KeyValueRow label={t('Subject Terms Already In Tree')} value={anchors.subjectTermsPresent.join(', ')} />
          ) : null}
        </Section>
      ) : null}
      {confirmElement}
      <JsonSheet open={json} title={t('Dock: {{id}}', { id: dock.id })} data={dock} onClose={() => setJson(false)} />
    </Screen>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  head: { paddingHorizontal: spacing.lg, gap: spacing.sm, marginBottom: spacing.md },
});
