import { Linking, StyleSheet, View } from 'react-native';
import { assignmentBlockerTitle, type MissionLandingState } from '@dashboard/lib/missionActions';
import type { GitHubPullRequestDetail, LandingPreviewResult, MissionAssignmentBlocker } from '@dashboard/types/models';
import { AppText, Button, KeyValueRow, ListRow, Section, StatusBadge } from '../../../components/ui';
import { useLocale } from '../../../i18n/LocaleContext';
import { spacing } from '../../../theme/typography';

const pad = { paddingHorizontal: spacing.lg, paddingVertical: spacing.sm };

/** Why a Pending mission is waiting (the dashboard's AssignmentBlockerCard), with links to what holds it. */
export function AssignmentBlockerCard({ blocker, onOpen }: { blocker: MissionAssignmentBlocker; onOpen: (path: string) => void }) {
  const { t, formatDateTime } = useLocale();
  return (
    <Section title={t('Why This Mission Is Waiting')}>
      <View style={[pad, styles.gap]} testID="mission-assignment-blocker">
        <View style={styles.row}>
          <AppText variant="label" style={styles.flex}>{t(assignmentBlockerTitle(blocker.reason))}</AppText>
          <StatusBadge label={t('Pending')} tone={blocker.reason === 'AwaitingDispatch' ? 'success' : 'warning'} />
        </View>
        <AppText selectable>{blocker.summary}</AppText>
        {blocker.untilUtc ? <AppText muted>{t('Expected to clear at {{time}}', { time: formatDateTime(blocker.untilUtc) })}</AppText> : null}
      </View>
      {blocker.dependsOnMissionId ? (
        <ListRow title={t('Depends on')} subtitle={blocker.dependsOnMissionId} onPress={() => onOpen(`/missions/${blocker.dependsOnMissionId}`)} />
      ) : null}
      {blocker.blockingMissionIds.map((missionId) => (
        <ListRow key={missionId} title={t('Held by')} subtitle={missionId} onPress={() => onOpen(`/missions/${missionId}`)} />
      ))}
      {blocker.captains.map((captain) => (
        <ListRow
          key={captain.captainId}
          icon="person-circle-outline"
          title={captain.captainName || captain.captainId}
          subtitle={captain.detail}
          onPress={() => onOpen(captain.objectiveId ? `/backlog/${captain.objectiveId}` : `/captains/${captain.captainId}`)}
        />
      ))}
    </Section>
  );
}

/** The landing preview card: readiness pill, policy summary, latest check, predicted issues. */
export function LandingPreviewCard({ preview, loading, landing, branchName }: {
  preview: LandingPreviewResult | null;
  loading: boolean;
  landing: MissionLandingState;
  branchName: string | null;
}) {
  const { t } = useLocale();
  const route = preview?.sourceBranch ? `${preview.sourceBranch} -> ${preview.targetBranch}` : branchName || t('No branch selected');
  return (
    <Section title={t('Landing Preview')}>
      <View style={[pad, styles.gap]} testID="mission-landing-preview">
        <View style={styles.row}>
          <AppText variant="mono" selectable style={styles.flex}>{route}</AppText>
          <StatusBadge label={t(landing.pill.label)} tone={landing.pill.tone === 'ready' ? 'success' : 'warning'} />
        </View>
        {loading ? <AppText muted>{t('Calculating landing preview...')}</AppText> : null}
        {!loading && !preview ? <AppText muted>{t('Landing preview is not available for this mission yet.')}</AppText> : null}
      </View>
      {!loading && preview ? (
        <>
          <KeyValueRow label={t('Branch category')} value={preview.branchCategory} />
          <KeyValueRow label={t('Landing mode')} value={preview.landingMode || t('Inherited')} />
          <KeyValueRow label={t('Cleanup')} value={preview.branchCleanupPolicy || t('Inherited')} />
          {preview.expectedLandingAction ? <KeyValueRow label={t('Action')} value={preview.expectedLandingAction} /> : null}
          <KeyValueRow label={t('Checks')} value={preview.requirePassingChecksToLand ? t('Passing checks required') : t('Passing checks optional')} />
          <KeyValueRow label={t('Target branch')} value={preview.targetBranchProtected ? t('Protected target branch') : t('Target branch not protected')} />
          {preview.protectedBranchMatch ? <KeyValueRow label={t('Policy')} value={preview.protectedBranchMatch} mono /> : null}
          {preview.requirePullRequestForProtectedBranches ? <ListRow title={t('PR required for protected branches')} /> : null}
          {preview.requireMergeQueueForReleaseBranches ? <ListRow title={t('Merge queue required for release branches')} /> : null}
          {preview.latestCheckSummary ? <ListRow title={t('Latest check')} subtitle={preview.latestCheckSummary} /> : null}
          {preview.issues.length > 0 ? preview.issues.map((issue, index) => (
            <View key={`${issue.code}-${index}`} style={[pad, styles.gap]}>
              <View style={styles.row}>
                <AppText variant="label" style={styles.flex}>{issue.title}</AppText>
                <StatusBadge label={issue.severity} tone={issue.severity.toLowerCase() === 'error' ? 'error' : 'warning'} />
              </View>
              <AppText muted>{issue.message}</AppText>
            </View>
          )) : (
            <View style={pad}><AppText color="success">{t('No landing blockers are currently predicted for this mission.')}</AppText></View>
          )}
        </>
      ) : null}
    </Section>
  );
}

/** GitHub pull-request evidence for a mission with a PR URL. */
export function PullRequestCard({ prUrl, detail, loading, onRefresh }: {
  prUrl: string;
  detail: GitHubPullRequestDetail | null;
  loading: boolean;
  onRefresh: () => void;
}) {
  const { t } = useLocale();
  return (
    <Section title={t('GitHub Pull Request')}>
      <View style={[pad, styles.buttons]}>
        <Button label={t('Open GitHub')} variant="secondary" icon="open-outline" onPress={() => void Linking.openURL(prUrl)} />
        <Button label={loading ? t('Refreshing...') : t('Refresh')} variant="ghost" icon="refresh" disabled={loading} onPress={onRefresh} />
      </View>
      {loading ? <View style={pad}><AppText muted>{t('Loading GitHub pull-request evidence...')}</AppText></View> : null}
      {!loading && !detail ? <View style={pad}><AppText muted>{t('GitHub pull-request evidence is unavailable for this mission.')}</AppText></View> : null}
      {!loading && detail ? (
        <>
          <View style={pad}><AppText selectable>{detail.title}</AppText></View>
          <KeyValueRow label={t('Repository')} value={detail.repository} />
          <KeyValueRow label={t('Review Status')} value={detail.reviewStatus} />
          <KeyValueRow label={t('State')} value={detail.state} />
          <KeyValueRow label={t('Mergeability')} value={detail.mergeableState || '-'} />
          <View style={pad}><AppText variant="label">{t('Reviews')}</AppText></View>
          {detail.reviews.length === 0 ? <View style={pad}><AppText muted>{t('No reviews')}</AppText></View> : detail.reviews.map((review, index) => (
            <ListRow key={`review-${index}`} title={review.reviewerLogin || t('Unknown')} subtitle={review.state} />
          ))}
          <View style={pad}><AppText variant="label">{t('Checks')}</AppText></View>
          {detail.checks.length === 0 ? <View style={pad}><AppText muted>{t('No provider checks')}</AppText></View> : detail.checks.map((check, index) => (
            <ListRow key={`check-${index}`} title={check.name} subtitle={`${check.status}${check.conclusion ? ` / ${check.conclusion}` : ''}`} />
          ))}
        </>
      ) : null}
    </Section>
  );
}

const styles = StyleSheet.create({
  gap: { gap: spacing.xs },
  row: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
  flex: { flex: 1 },
  buttons: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm },
});
