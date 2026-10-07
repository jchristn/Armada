import { Stack, useRouter, type Href } from 'expo-router';
import { useCallback, useMemo, useState } from 'react';
import { Linking, RefreshControl, ScrollView, StyleSheet, View } from 'react-native';
import {
  approveMissionReview,
  deleteMission,
  denyMissionReview,
  getMission,
  getMissionGitHubPullRequest,
  getMissionLandingPreview,
  listCheckRuns,
  listDeployments,
  purgeMission,
  restartMission,
  retryMissionLanding,
  transitionMission,
  updateMission,
} from '@dashboard/api/client';
import {
  MISSION_TRANSITION_STATUSES,
  formatMissionDuration,
  isMissionLogCompleted,
  missionLandingState,
  type ReviewVerdict,
} from '@dashboard/lib/missionActions';
import type { Mission } from '@dashboard/types/models';
import { EntityStatusBadge } from '../../components/app/EntityStatusBadge';
import { JsonSheet } from '../../components/app/JsonSheet';
import { useConfirm } from '../../components/app/useConfirm';
import {
  ActionSheet,
  AppText,
  Button,
  CodeBlock,
  ErrorState,
  KeyValueRow,
  ListRow,
  LoadingState,
  Section,
  SegmentedControl,
  type SheetAction,
} from '../../components/ui';
import { errorMessage } from '../../data/errors';
import { useLiveRefresh } from '../../data/useLiveRefresh';
import { useNameLookups } from '../../data/useNameLookups';
import { useQuery } from '../../data/useQuery';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { useTheme } from '../../theme/ThemeContext';
import { spacing } from '../../theme/typography';
import type { OperationsDetailProps } from './listTypes';
import { AssignmentBlockerCard, LandingPreviewCard, PullRequestCard } from './mission/MissionCards';
import { MissionFormSheet, type MissionFormValues } from './mission/MissionFormSheet';
import { MissionDiffTab, MissionInstructionsTab, MissionLogTab } from './mission/MissionTabs';
import { ReviewSheet } from './mission/ReviewSheet';
import { TransitionSheet } from './mission/TransitionSheet';

export const MISSION_DETAIL_TABS = ['overview', 'diff', 'log', 'instructions'] as const;
export type MissionDetailTab = (typeof MISSION_DETAIL_TABS)[number];

/** Pure: a tab key from a route parameter (unknown values open the overview). */
export function missionDetailTab(value: string | string[] | undefined | null): MissionDetailTab {
  const raw = Array.isArray(value) ? value[0] : value;
  return (MISSION_DETAIL_TABS as readonly string[]).includes(raw ?? '') ? (raw as MissionDetailTab) : 'overview';
}

export interface MissionDetailProps extends OperationsDetailProps {
  /** Tab to open first (row actions View Diff / View Log open the detail on that tab). */
  initialTab?: MissionDetailTab;
  /** Called after the mission is deleted (route screens go back; split views clear the selection). */
  onDeleted?: () => void;
}

/**
 * Mission detail, at parity with the dashboard's MissionDetail page: status and review gate, assignment blocker,
 * landing preview, GitHub pull request evidence, every field and link, linked checks and deployments, description,
 * playbooks, and the diff, log, and instructions (as tabs, each with its own scroll). Actions: resolve review,
 * mark complete, land / retry landing, manual merge (Landing Mode None), run check, transition, edit, restart,
 * purge, delete, and View JSON. Follows the mission live over the socket.
 */
export function MissionDetail({ id, embedded, initialTab = 'overview', onDeleted }: MissionDetailProps) {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { colors } = useTheme();
  const router = useRouter();
  const { pushToast } = useNotifications();
  const names = useNameLookups({ vessels: true, captains: true });
  const [tab, setTab] = useState<MissionDetailTab>(initialTab);
  const [menuOpen, setMenuOpen] = useState(false);
  const [editOpen, setEditOpen] = useState(false);
  const [editSaving, setEditSaving] = useState(false);
  const [transitionOpen, setTransitionOpen] = useState(false);
  const [reviewOpen, setReviewOpen] = useState(false);
  const [jsonOpen, setJsonOpen] = useState(false);
  const [confirmElement, confirm] = useConfirm('mission-confirm');

  const missionQuery = useQuery<Mission>(() => getMission(id), [id], t('Failed to load mission.'));
  const mission = missionQuery.data && missionQuery.data.id === id ? missionQuery.data : null;
  const reloadMission = missionQuery.reload;
  useLiveRefresh(['mission.'], () => { void reloadMission(); });

  // The landing preview follows every change to the mission (the dashboard reloads it with the mission).
  const previewQuery = useQuery(
    () => getMissionLandingPreview(id).catch(() => null),
    [id, mission?.lastUpdateUtc ?? ''],
  );
  const linkedQuery = useQuery(async () => {
    const [checks, deployments] = await Promise.all([
      listCheckRuns({ pageSize: 1000, filters: { missionId: id } }).catch(() => null),
      listDeployments({ pageSize: 1000, missionId: id }).catch(() => null),
    ]);
    return { checks: checks?.objects ?? [], deployments: deployments?.objects ?? [] };
  }, [id]);
  const prUrl = mission?.prUrl ?? null;
  const prQuery = useQuery(
    () => (prUrl ? getMissionGitHubPullRequest(id).catch(() => null) : Promise.resolve(null)),
    [id, prUrl ?? ''],
  );

  const open = useCallback((path: string) => router.push(path as Href), [router]);

  const run = useCallback(async (action: () => Promise<unknown>, success: { severity: 'success' | 'warning'; message: string } | null, failure: string) => {
    try {
      await action();
      if (success) pushToast(success.severity, success.message);
      void reloadMission();
    } catch (e) {
      pushToast('error', t(failure, { message: errorMessage(e, '') }));
    }
  }, [pushToast, reloadMission, t]);

  const landing = useMemo(
    () => (mission ? missionLandingState(mission, previewQuery.data) : null),
    [mission, previewQuery.data],
  );

  if (!mission) {
    if (missionQuery.loading) return <LoadingState label={t('Loading...')} />;
    return (
      <ErrorState
        title={t('Mission not found.')}
        message={missionQuery.error}
        retryLabel={t('Retry')}
        onRetry={() => void missionQuery.refresh()}
      />
    );
  }
  const state = landing ?? missionLandingState(mission, null);

  const land = () => run(
    () => retryMissionLanding(mission.id),
    { severity: 'success', message: t('Landing succeeded! Mission status updated.') },
    'Landing failed: {{message}}',
  );
  const runCheck = () => open('/checks');
  const manualMerge = () => { if (mission.vesselId) open(`/vessels/${mission.vesselId}`); };
  const markComplete = () => confirm({
    title: t('Mark Complete'),
    message: t('Mark mission "{{title}}" as Complete? Use this when the work has already landed and the mission just needs to graduate out of Review.', { title: mission.title }),
    confirmLabel: t('Mark Complete'),
    onConfirm: () => run(
      () => transitionMission(mission.id, { status: 'Complete' }),
      { severity: 'success', message: t('Mission "{{title}}" marked Complete.', { title: mission.title }) },
      'Mark Complete failed: {{message}}',
    ),
  });
  const restart = () => confirm({
    title: t('Restart Mission'),
    message: t('Restart mission "{{title}}"? This will reset the mission to Pending status.', { title: mission.title }),
    confirmLabel: t('Restart'),
    onConfirm: () => run(
      () => restartMission(mission.id),
      { severity: 'success', message: t('Mission "{{title}}" restarted.', { title: mission.title }) },
      'Restart failed: {{message}}',
    ),
  });
  const purge = () => confirm({
    title: t('Purge Mission'),
    message: t('Purge mission "{{title}}"? This will clean up all associated resources (branches, worktrees, etc.) and cannot be undone.', { title: mission.title }),
    confirmLabel: t('Purge'),
    danger: true,
    onConfirm: () => run(
      () => purgeMission(mission.id),
      { severity: 'warning', message: t('Mission "{{title}}" purged.', { title: mission.title }) },
      'Purge failed: {{message}}',
    ),
  });
  const remove = () => confirm({
    title: t('Delete Mission'),
    message: t('Permanently delete mission "{{title}}"? This cannot be undone.', { title: mission.title }),
    confirmLabel: t('Delete'),
    danger: true,
    onConfirm: async () => {
      try {
        await deleteMission(mission.id);
        pushToast('warning', t('Mission "{{title}}" deleted.', { title: mission.title }));
        if (onDeleted) onDeleted();
        else if (!embedded && router.canGoBack()) router.back();
        else open('/missions');
      } catch (e) {
        pushToast('error', t('Delete failed: {{message}}', { message: errorMessage(e, '') }));
      }
    },
  });

  async function saveEdit(values: MissionFormValues) {
    if (!mission) return;
    setEditSaving(true);
    try {
      await updateMission(mission.id, { title: values.title, description: values.description, priority: values.priority });
      setEditOpen(false);
      pushToast('success', t('Mission "{{title}}" saved.', { title: values.title }));
      void reloadMission();
    } catch (e) {
      pushToast('error', t('Save failed: {{message}}', { message: errorMessage(e, '') }));
    } finally {
      setEditSaving(false);
    }
  }

  async function submitTransition(status: string) {
    if (!mission) return;
    setTransitionOpen(false);
    await run(
      () => transitionMission(mission.id, { status }),
      { severity: 'success', message: t('Mission "{{title}}" moved to {{status}}.', { title: mission.title, status }) },
      'Transition failed: {{message}}',
    );
  }

  async function submitReview(verdict: ReviewVerdict, comment: string) {
    if (!mission) return;
    try {
      if (verdict === 'approve') {
        await approveMissionReview(mission.id, { comment: comment || undefined });
        pushToast('success', t('Review approved for "{{title}}".', { title: mission.title }));
      } else if (verdict === 'conditional') {
        await approveMissionReview(mission.id, { comment, conditional: true });
        pushToast('success', t('Conditionally approved "{{title}}". The next step will consider your feedback.', { title: mission.title }));
      } else if (verdict === 'morework') {
        await denyMissionReview(mission.id, { comment, action: 'RetryStage' });
        pushToast('warning', t('Sent "{{title}}" back for more work with your feedback.', { title: mission.title }));
      } else {
        await denyMissionReview(mission.id, { comment: comment || undefined, action: 'FailPipeline' });
        pushToast('warning', t('Review denied for "{{title}}".', { title: mission.title }));
      }
      setReviewOpen(false);
      void reloadMission();
    } catch (e) {
      pushToast('error', t('Review decision failed: {{message}}', { message: errorMessage(e, '') }));
    }
  }

  const landLabel = state.isRetry ? t('Retry Landing') : t('Land');
  const actions: SheetAction[] = [
    { key: 'edit', label: t('Edit'), icon: 'create-outline', onPress: () => setEditOpen(true) },
    ...(state.canResolveReview ? [{ key: 'review', label: t('Resolve Review'), icon: 'checkmark-done-outline' as const, onPress: () => setReviewOpen(true) }] : []),
    ...(state.canMarkComplete ? [{ key: 'complete', label: t('Mark Complete'), icon: 'checkmark-circle-outline' as const, onPress: markComplete }] : []),
    { key: 'diff', label: t('View Diff'), icon: 'git-compare-outline', onPress: () => setTab('diff') },
    { key: 'log', label: t('View Log'), icon: 'document-text-outline', onPress: () => setTab('log') },
    { key: 'instructions', label: t('View Instructions'), icon: 'reader-outline', onPress: () => setTab('instructions') },
    ...(mission.vesselId ? [{ key: 'check', label: t('Run Check'), icon: 'shield-checkmark-outline' as const, onPress: runCheck }] : []),
    { key: 'transition', label: t('Transition Status'), icon: 'swap-horizontal-outline', onPress: () => setTransitionOpen(true) },
    { key: 'json', label: t('View JSON'), icon: 'code-slash-outline', onPress: () => setJsonOpen(true) },
    { key: 'restart', label: t('Restart'), icon: 'refresh-outline', onPress: restart },
    ...(state.showLand ? [{ key: 'land', label: landLabel, icon: 'git-merge-outline' as const, onPress: () => void land() }] : []),
    ...(state.showManualMerge ? [{ key: 'merge', label: t('Merge in Manage Branches'), icon: 'git-branch-outline' as const, onPress: manualMerge }] : []),
    { key: 'purge', label: t('Purge'), icon: 'trash-bin-outline', danger: true, onPress: purge },
    { key: 'delete', label: t('Delete'), icon: 'trash-outline', danger: true, onPress: remove },
  ];

  const linked = linkedQuery.data ?? { checks: [], deployments: [] };
  const dateRow = (label: string, utc: string | null | undefined) => (
    <KeyValueRow label={label} value={utc ? `${formatRelativeTime(utc)} (${formatDateTime(utc)})` : '-'} />
  );
  const preferredFellBack = !!mission.requestedCaptainId && !!mission.captainId && mission.captainId !== mission.requestedCaptainId;

  const overview = (
    <ScrollView
      testID="mission-overview"
      contentContainerStyle={styles.scroll}
      refreshControl={<RefreshControl refreshing={missionQuery.refreshing} onRefresh={() => void missionQuery.refresh()} tintColor={colors.primary} />}
    >
      <View style={styles.primaryActions}>
        {state.canResolveReview ? <Button label={t('Resolve Review')} onPress={() => setReviewOpen(true)} testID="mission-action-review" /> : null}
        {state.canMarkComplete ? <Button label={t('Mark Complete')} onPress={markComplete} testID="mission-action-complete" /> : null}
        {state.showLand ? (
          <Button
            label={landLabel}
            icon="git-merge-outline"
            accessibilityHint={t('Rebase the mission branch and merge it into the target branch, then complete the mission')}
            onPress={() => void land()}
            testID="mission-action-land"
          />
        ) : null}
        {state.showManualMerge ? (
          <Button
            label={t('Merge in Manage Branches')}
            accessibilityHint={t('This vessel lands by hand (Landing Mode None). Merge the mission branch in Manage Branches; the mission completes once its branch is merged.')}
            onPress={manualMerge}
            testID="mission-action-merge"
          />
        ) : null}
      </View>

      {mission.status === 'Pending' && mission.assignmentBlocker ? <AssignmentBlockerCard blocker={mission.assignmentBlocker} onOpen={open} /> : null}

      <LandingPreviewCard preview={previewQuery.data ?? null} loading={previewQuery.loading} landing={state} branchName={mission.branchName} />

      {mission.prUrl ? (
        <PullRequestCard prUrl={mission.prUrl} detail={prQuery.data ?? null} loading={prQuery.loading || prQuery.refreshing} onRefresh={() => void prQuery.refresh()} />
      ) : null}

      <Section title={t('Details')}>
        <KeyValueRow label={t('ID')} value={mission.id} mono testID="mission-detail-id" />
        <KeyValueRow label={t('Tenant ID')} value={mission.tenantId} mono />
        <KeyValueRow label={t('Status')}><EntityStatusBadge status={mission.status} testID="mission-detail-status" /></KeyValueRow>
        <KeyValueRow label={t('Mode')} value={t(mission.mode || 'Implementation')} />
        <KeyValueRow label={t('Review Gate')}>
          {mission.requiresReview
            ? <EntityStatusBadge status={mission.status === 'Review' ? 'Waiting Review' : 'Required'} />
            : <AppText muted>{t('None')}</AppText>}
        </KeyValueRow>
        <KeyValueRow label={t('On Deny')} value={mission.requiresReview ? (mission.reviewDenyAction === 'FailPipeline' ? t('Fail pipeline') : t('Retry stage')) : '-'} />
        <KeyValueRow label={t('Review Requested')} value={mission.reviewRequestedUtc ? formatDateTime(mission.reviewRequestedUtc) : '-'} />
        <KeyValueRow label={t('Reviewed')} value={mission.reviewedUtc ? formatDateTime(mission.reviewedUtc) : '-'} />
        <KeyValueRow label={t('Reviewed By')} value={mission.reviewedByUserId} mono />
        <KeyValueRow label={t('Priority')} value={mission.priority} />
        <KeyValueRow label={t('Voyage')} value={mission.voyageId} mono onPress={mission.voyageId ? () => open(`/voyages/${mission.voyageId}`) : undefined} />
        <KeyValueRow label={t('Vessel')} value={mission.vesselId ? names.vesselName(mission.vesselId) : null} onPress={mission.vesselId ? () => open(`/vessels/${mission.vesselId}`) : undefined} />
        <KeyValueRow
          label={t('Preferred Captain')}
          value={mission.requestedCaptainId
            ? `${names.captainName(mission.requestedCaptainId)}${preferredFellBack ? ` ${t('(fell back to tier)')}` : ''}`
            : t('Auto (default routing)')}
          onPress={mission.requestedCaptainId ? () => open(`/captains/${mission.requestedCaptainId}`) : undefined}
        />
        <KeyValueRow label={t('Actual Captain')} value={mission.captainId ? names.captainName(mission.captainId) : null} onPress={mission.captainId ? () => open(`/captains/${mission.captainId}`) : undefined} />
        <KeyValueRow label={t('Parent Mission')} value={mission.parentMissionId} mono onPress={mission.parentMissionId ? () => open(`/missions/${mission.parentMissionId}`) : undefined} />
        <KeyValueRow label={t('Persona')} value={mission.persona || t('Worker')} />
        {mission.dependsOnMissionId ? (
          <KeyValueRow label={t('Depends On')} value={mission.dependsOnMissionId} mono onPress={() => open(`/missions/${mission.dependsOnMissionId}`)} />
        ) : null}
        {mission.status === 'WorkProduced' && mission.dependsOnMissionId === null && mission.persona && mission.persona !== 'Worker' ? (
          <KeyValueRow label={t('Pipeline Status')} value={t('Work complete -- handed off to the next pipeline stage')} />
        ) : null}
        <KeyValueRow label={t('Branch Name')} value={mission.branchName} mono />
        <KeyValueRow label={t('Dock')} value={mission.dockId} mono onPress={mission.dockId ? () => open(`/docks/${mission.dockId}`) : undefined} />
        <KeyValueRow label={t('Process ID')} value={mission.processId} />
        <KeyValueRow label={t('PR URL')} value={mission.prUrl} onPress={mission.prUrl ? () => void Linking.openURL(mission.prUrl ?? '') : undefined} />
        <KeyValueRow label={t('Commit Hash')} value={mission.commitHash} mono />
        {dateRow(t('Created'), mission.createdUtc)}
        {dateRow(t('Started'), mission.startedUtc)}
        {dateRow(t('Completed'), mission.completedUtc)}
        <KeyValueRow label={t('Total Runtime')} value={formatMissionDuration(mission.totalRuntimeMs, t)} />
        {dateRow(t('Last Updated'), mission.lastUpdateUtc)}
        <KeyValueRow label={t('Linked Checks')} value={linked.checks.length} />
        <KeyValueRow label={t('Linked Deployments')} value={linked.deployments.length} />
      </Section>

      {mission.failureReason ? (
        <Section title={t('Failure Reason')}>
          <View style={styles.pad}><CodeBlock wrap text={mission.failureReason} testID="mission-failure-reason" /></View>
        </Section>
      ) : null}
      {mission.reviewComment ? (
        <Section title={t('Review Comment')}>
          <View style={styles.pad}><CodeBlock wrap text={mission.reviewComment} /></View>
        </Section>
      ) : null}

      {linked.checks.length > 0 || linked.deployments.length > 0 ? (
        <>
          <Section title={t('Linked Checks')}>
            {linked.checks.length === 0 ? <ListRow title={t('No checks are linked to this mission yet.')} /> : linked.checks.map((check) => (
              <ListRow key={check.id} title={check.label || check.type} subtitle={check.id} onPress={() => open(`/checks/${check.id}`)} />
            ))}
          </Section>
          <Section title={t('Linked Deployments')}>
            {linked.deployments.length === 0 ? <ListRow title={t('No deployments are linked to this mission yet.')} /> : linked.deployments.map((deployment) => (
              <ListRow
                key={deployment.id}
                title={deployment.title}
                subtitle={`${deployment.environmentName || t('No environment')} - ${deployment.status} - ${deployment.verificationStatus}`}
                onPress={() => open(`/deployments/${deployment.id}`)}
              />
            ))}
          </Section>
        </>
      ) : null}

      {mission.description ? (
        <Section title={t('Description')}>
          <View style={styles.pad}><AppText selectable testID="mission-description">{mission.description}</AppText></View>
        </Section>
      ) : null}

      {mission.playbookSnapshots && mission.playbookSnapshots.length > 0 ? (
        <Section title={t('Playbooks')}>
          {mission.playbookSnapshots.map((snapshot, index) => (
            <View key={`${snapshot.fileName}-${index}`} style={[styles.pad, styles.playbook]}>
              <View style={styles.row}>
                <AppText variant="label" style={styles.flex}>{snapshot.fileName}</AppText>
                <EntityStatusBadge status={snapshot.deliveryMode.replace(/([a-z])([A-Z])/g, '$1 $2')} />
              </View>
              <AppText muted>{snapshot.description || t('No description')}</AppText>
              <AppText variant="caption" muted selectable>{t('Resolved Path')}: {snapshot.resolvedPath || '-'}</AppText>
              <AppText variant="caption" muted selectable>{t('Worktree Path')}: {snapshot.worktreeRelativePath || '-'}</AppText>
              <AppText variant="caption" muted>{t('Source Updated')}: {snapshot.sourceLastUpdateUtc ? formatDateTime(snapshot.sourceLastUpdateUtc) : '-'}</AppText>
              <CodeBlock text={snapshot.content} />
            </View>
          ))}
        </Section>
      ) : null}
    </ScrollView>
  );

  return (
    <View style={styles.fill} testID="mission-detail">
      {!embedded ? <Stack.Screen options={{ title: mission.title }} /> : null}
      <View style={styles.header}>
        <View style={styles.row}>
          <AppText variant="heading" accessibilityRole="header" style={styles.flex} numberOfLines={3} testID="mission-detail-title">{mission.title}</AppText>
          <Button label={t('Actions')} variant="secondary" icon="ellipsis-horizontal" onPress={() => setMenuOpen(true)} testID="mission-action-menu" style={styles.menuButton} />
        </View>
        <SegmentedControl<MissionDetailTab>
          label={t('Mission sections')}
          value={tab}
          onChange={setTab}
          options={[
            { value: 'overview', label: t('Overview'), testID: 'mission-tab-overview' },
            { value: 'diff', label: t('Diff'), testID: 'mission-tab-diff' },
            { value: 'log', label: t('Log'), testID: 'mission-tab-log' },
            { value: 'instructions', label: t('Instructions'), testID: 'mission-tab-instructions' },
          ]}
        />
      </View>
      <View style={styles.fill}>
        {tab === 'overview' ? overview : null}
        {tab === 'diff' ? <MissionDiffTab id={mission.id} /> : null}
        {tab === 'log' ? (
          <MissionLogTab id={mission.id} completed={isMissionLogCompleted(mission.status)} onMissionRefresh={() => void reloadMission()} />
        ) : null}
        {tab === 'instructions' ? <MissionInstructionsTab id={mission.id} /> : null}
      </View>

      <ActionSheet open={menuOpen} title={mission.title} actions={actions} onClose={() => setMenuOpen(false)} closeLabel={t('Close')} testID="mission-actions" />
      <MissionFormSheet
        open={editOpen}
        kind="edit"
        initial={{ title: mission.title, description: mission.description || '', vesselId: mission.vesselId || '', priority: mission.priority, mode: mission.mode }}
        vessels={names.vessels}
        busy={editSaving}
        onClose={() => setEditOpen(false)}
        onSubmit={(values) => void saveEdit(values)}
      />
      <TransitionSheet
        open={transitionOpen}
        currentStatus={mission.status}
        statuses={MISSION_TRANSITION_STATUSES}
        onClose={() => setTransitionOpen(false)}
        onSubmit={(status) => void submitTransition(status)}
      />
      <ReviewSheet open={reviewOpen} initialComment={mission.reviewComment || ''} onClose={() => setReviewOpen(false)} onSubmit={(v, c) => void submitReview(v, c)} />
      <JsonSheet open={jsonOpen} title={t('Mission: {{title}}', { title: mission.title })} data={mission} onClose={() => setJsonOpen(false)} />
      {confirmElement}
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  header: { paddingHorizontal: spacing.md, paddingTop: spacing.md, gap: spacing.sm },
  row: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  flex: { flex: 1 },
  menuButton: { marginBottom: 0 },
  scroll: { paddingBottom: spacing.xxl },
  primaryActions: { paddingHorizontal: spacing.md, paddingBottom: spacing.sm },
  pad: { paddingHorizontal: spacing.lg, paddingVertical: spacing.sm },
  playbook: { gap: spacing.xs },
});
