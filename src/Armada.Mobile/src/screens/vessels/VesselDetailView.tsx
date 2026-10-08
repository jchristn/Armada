import { Stack, useRouter, type Href } from 'expo-router';
import { useEffect, useMemo, useRef } from 'react';
import { RefreshControl, ScrollView, StyleSheet, View } from 'react-native';
import {
  getVessel,
  getVesselLandingPreview,
  getVesselReadiness,
  listFleets,
  listMissionSummaries,
  listPipelines,
} from '@dashboard/api/client';
import type { Fleet, LandingPreviewResult, MissionSummary, Pipeline, Vessel, VesselReadinessResult } from '@dashboard/types/models';
import { ActionRow, InfoRow } from '../../build/fields';
import { useLiveResource } from '../../build/useLiveResource';
import { AppText, Button, ErrorState, IconButton, ListRow, LoadingState, Section, StatusBadge } from '../../components/ui';
import { statusTone } from '../../components/ask/statusTone';
import { Disclosure } from '../../components/ui/Disclosure';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { spacing, typography } from '../../theme/typography';
import { LandingPreviewCard, ReadinessCard } from './ReadinessCards';
import { useVesselActions } from './VesselActions';

/** Missions listed on the vessel page (the dashboard loads up to 1000). */
const MISSION_LIMIT = 1000;

interface VesselBundle {
  vessel: Vessel;
  fleets: Fleet[];
  pipelines: Pipeline[];
}

export interface VesselDetailViewProps {
  id: string;
  /** Inside the Vessels tab's split view (tablets): no header title, no own navigation. */
  embedded?: boolean;
  /** Open the edit form once loaded (`?edit=1`, used after Duplicate). */
  openEdit?: boolean;
  /** Called after the edit form was opened for `openEdit` (the route clears the parameter). */
  onEditOpened?: () => void;
  /** After the vessel was deleted (the route goes back; the split view clears its selection). */
  onDeleted?: () => void;
  /** After an edit or other change (the split view reloads its list). */
  onChanged?: () => void;
}

/**
 * The vessel page (the dashboard's VesselDetail): actions (Dispatch, Manage Objectives, Manage Fleet, View History,
 * Onboarding, Run Check, Open Workspace, Health, and the full menu), readiness, landing preview, every vessel field,
 * project / style / branch / dock-boundary / model context, and the vessel's missions. Live: missions and the landing
 * preview reload on mission events.
 */
export function VesselDetailView({ id, embedded, openEdit, onEditOpened, onDeleted, onChanged }: VesselDetailViewProps) {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { colors } = useTheme();
  const router = useRouter();

  const bundle = useLiveResource<VesselBundle>(async () => {
    const [vessel, fleets, pipelines] = await Promise.all([
      getVessel(id),
      listFleets({ pageSize: 9999 }),
      listPipelines({ pageSize: 9999 }),
    ]);
    return { vessel, fleets: fleets.objects, pipelines: pipelines.objects };
  }, [id]);

  const missions = useLiveResource<MissionSummary[]>(
    async () => (await listMissionSummaries({ pageSize: MISSION_LIMIT, filters: { vesselId: id } })).objects || [],
    [id],
    { live: ['mission.'] },
  );

  const vessel = bundle.data?.vessel ?? null;
  const defaultBranch = vessel?.defaultBranch || null;

  // Readiness and the landing preview load after the vessel (they can be slow); mission events change both.
  const readiness = useLiveResource<VesselReadinessResult>(() => getVesselReadiness(id), [id, vessel?.lastUpdateUtc], { enabled: !!vessel, live: ['mission.'] });
  const preview = useLiveResource<LandingPreviewResult>(() => getVesselLandingPreview(id, defaultBranch), [id, defaultBranch, vessel?.lastUpdateUtc], { enabled: !!vessel, live: ['mission.'] });

  const reloadAll = async () => {
    await Promise.all([bundle.reload(), missions.reload(), readiness.reload(), preview.reload()]);
  };

  const actions = useVesselActions({
    fleets: bundle.data?.fleets ?? [],
    pipelines: bundle.data?.pipelines ?? [],
    onChanged: () => { void reloadAll(); onChanged?.(); },
    onDeleted: () => {
      onChanged?.();
      if (onDeleted) onDeleted();
      else if (router.canGoBack()) router.back();
      else router.replace('/vessels' as Href);
    },
    exclude: ['detail'],
  });

  // ?edit=1 (after Duplicate): open the form once the vessel has loaded.
  const editOpened = useRef(false);
  useEffect(() => {
    if (!openEdit || !vessel || editOpened.current) return;
    editOpened.current = true;
    actions.run('edit', vessel);
    onEditOpened?.();
  }, [openEdit, vessel, actions, onEditOpened]);

  const fleetName = useMemo(() => {
    const fleetId = vessel?.fleetId;
    if (!fleetId) return null;
    return bundle.data?.fleets.find((f) => f.id === fleetId)?.name ?? fleetId;
  }, [vessel, bundle.data]);

  if (bundle.loading && !vessel) return <LoadingState label={t('Loading...')} />;
  if (!vessel) {
    return (
      <View style={styles.fill}>
        {embedded ? null : <Stack.Screen options={{ title: t('Vessel') }} />}
        <ErrorState title={t('Vessel not found.')} message={bundle.error} retryLabel={t('Retry')} onRetry={() => void bundle.refresh()} />
      </View>
    );
  }

  const yesNo = (v: boolean | null | undefined) => (v ? t('Yes') : t('No'));
  const pipelineName = bundle.data?.pipelines.find((p) => p.id === vessel.defaultPipelineId)?.name || vessel.defaultPipelineId || t('None (WorkerOnly)');
  const contextBlocks: { key: string; title: string; text: string }[] = [];
  if (vessel.projectContext) contextBlocks.push({ key: 'project', title: t('Project Context'), text: vessel.projectContext });
  if (vessel.styleGuide) contextBlocks.push({ key: 'style', title: t('Style Guide'), text: vessel.styleGuide });
  if (vessel.protectedBranchPatterns && vessel.protectedBranchPatterns.length > 0) {
    contextBlocks.push({ key: 'branches', title: t('Protected Branch Patterns'), text: vessel.protectedBranchPatterns.join('\n') });
  }
  if (vessel.enableModelContext && vessel.modelContext) contextBlocks.push({ key: 'model', title: t('Model Context'), text: vessel.modelContext });
  const showBoundary = vessel.secretScanEnabled || (vessel.protectedPathPatterns?.length ?? 0) > 0 || (vessel.privateIdentifierDenylist?.length ?? 0) > 0;

  return (
    <View style={[styles.fill, { backgroundColor: colors.background }]} testID="vessel-detail">
      {embedded ? null : (
        <Stack.Screen
          options={{
            title: vessel.name,
            headerRight: () => <IconButton icon="ellipsis-horizontal-circle-outline" label={t('Actions')} onPress={() => actions.openMenu(vessel)} testID="vessel-detail-menu" />,
          }}
        />
      )}
      <ScrollView
        contentContainerStyle={styles.content}
        refreshControl={<RefreshControl refreshing={bundle.refreshing} onRefresh={() => void reloadAll()} tintColor={colors.primary} />}
      >
        <View style={styles.column}>
          {embedded ? (
            <View style={styles.embeddedHead}>
              <AppText variant="title" style={styles.flex} accessibilityRole="header">{vessel.name}</AppText>
              <IconButton icon="ellipsis-horizontal-circle-outline" label={t('Actions')} onPress={() => actions.openMenu(vessel)} testID="vessel-detail-menu" />
            </View>
          ) : null}
          <ActionRow>
            <Button label={t('Dispatch')} icon="paper-plane-outline" onPress={() => actions.run('dispatch', vessel)} testID="vessel-detail-dispatch" />
            <Button label={t('Edit')} variant="secondary" icon="create-outline" onPress={() => actions.run('edit', vessel)} testID="vessel-detail-edit" />
            <Button label={t('View History')} variant="secondary" icon="time-outline" onPress={() => actions.run('history', vessel)} testID="vessel-detail-history" />
            <Button label={t('Manage Branches')} variant="secondary" icon="git-branch-outline" onPress={() => actions.run('branches', vessel)} testID="vessel-detail-branches" />
            <Button label={t('Manage Objectives')} variant="secondary" onPress={() => actions.run('objectives', vessel)} testID="vessel-detail-objectives" />
            {vessel.fleetId ? <Button label={t('Manage Fleet')} variant="secondary" onPress={() => actions.run('fleet', vessel)} testID="vessel-detail-fleet" /> : null}
            <Button label={t('Onboarding')} variant="secondary" onPress={() => actions.run('onboarding', vessel)} testID="vessel-detail-onboarding" />
            <Button label={t('Run Check')} variant="secondary" onPress={() => actions.run('runCheck', vessel)} testID="vessel-detail-run-check" />
            <Button label={t('Open Workspace')} variant="secondary" onPress={() => actions.run('workspace', vessel)} testID="vessel-detail-workspace" />
            <Button label={t('Health')} variant="secondary" icon="pulse-outline" onPress={() => actions.run('health', vessel)} testID="vessel-detail-health" />
          </ActionRow>

          <ReadinessCard title={t('Readiness')} readiness={readiness.error ? null : readiness.data} loading={readiness.loading} emptyMessage={t('Readiness data is not available for this vessel yet.')} testID="vessel-readiness" />
          <LandingPreviewCard preview={preview.error ? null : preview.data} loading={preview.loading} />

          <Section title={t('Vessel')}>
            <InfoRow label={t('ID')} value={vessel.id} mono />
            <InfoRow label={t('Name')} value={vessel.name} />
            {vessel.fleetId ? (
              <ListRow title={fleetName ?? vessel.fleetId} subtitle={t('Fleet')} onPress={() => actions.run('fleet', vessel)} testID="vessel-detail-fleet-link" />
            ) : <InfoRow label={t('Fleet')} value={null} />}
            <InfoRow label={t('Repo URL')} value={vessel.repoUrl} mono />
            <InfoRow label={t('Default Branch')} value={vessel.defaultBranch || 'main'} />
            <InfoRow label={t('Local Path')} value={vessel.localPath} mono />
            <InfoRow label={t('Working Directory')} value={vessel.workingDirectory} mono />
            <InfoRow label={t('Landing Mode')} value={vessel.landingMode} testID="vessel-detail-landing-mode" />
            <InfoRow label={t('Branch Cleanup Policy')} value={vessel.branchCleanupPolicy} />
            <InfoRow label={t('Release Branch Prefix')} value={vessel.releaseBranchPrefix || 'release/'} mono />
            <InfoRow label={t('Hotfix Branch Prefix')} value={vessel.hotfixBranchPrefix || 'hotfix/'} mono />
            <InfoRow label={t('Require Passing Checks To Land')} value={yesNo(vessel.requirePassingChecksToLand)} />
            <InfoRow label={t('Require PR For Protected Branches')} value={yesNo(vessel.requirePullRequestForProtectedBranches)} />
            <InfoRow label={t('Require Merge Queue For Release Branches')} value={yesNo(vessel.requireMergeQueueForReleaseBranches)} />
            <InfoRow label={t('Allow Concurrent Missions')} value={yesNo(vessel.allowConcurrentMissions)} />
            <InfoRow
              label={t('Agent Auto-Approve')}
              value={vessel.autoApprove === true ? t('On for this vessel') : vessel.autoApprove === false ? t('Off for this vessel') : t('Use captain setting')}
            />
            <InfoRow label={t('Auto-Land Gate')} value={vessel.autoLandEnabled ? t('Enabled') : t('Disabled')} />
            {vessel.autoLandEnabled ? (
              <>
                <InfoRow label={t('Auto-Land Max Files')} value={vessel.autoLandMaxFiles ?? 0} />
                <InfoRow label={t('Auto-Land Max Lines')} value={vessel.autoLandMaxLines ?? 0} />
                <InfoRow label={t('Auto-Land Allowed Paths')} value={vessel.autoLandPathAllowGlobs?.length ? vessel.autoLandPathAllowGlobs.join(', ') : null} mono />
                <InfoRow label={t('Auto-Land Denied Paths')} value={vessel.autoLandPathDenyGlobs?.length ? vessel.autoLandPathDenyGlobs.join(', ') : null} mono />
              </>
            ) : null}
            <InfoRow label={t('Definition-of-Done Gate')} value={vessel.definitionOfDoneEnabled ? t('Enabled') : t('Disabled')} />
            {vessel.definitionOfDoneEnabled ? (
              <>
                <InfoRow label={t('DoD Build Command')} value={vessel.definitionOfDoneBuildCommand} mono />
                <InfoRow label={t('DoD Test Command')} value={vessel.definitionOfDoneTestCommand} mono />
                <InfoRow label={t('DoD Timeout (s)')} value={vessel.definitionOfDoneTimeoutSeconds ?? 1800} />
              </>
            ) : null}
            <InfoRow label={t('GitHub Token Override')} value={vessel.hasGitHubTokenOverride ? t('Configured') : t('Inherited / None')} />
            <InfoRow label={t('Default Pipeline')} value={pipelineName} />
            <InfoRow label={t('Active')} value={yesNo(vessel.active !== false)} />
            <InfoRow label={t('Created')} value={`${formatRelativeTime(vessel.createdUtc)} (${formatDateTime(vessel.createdUtc)})`} />
            <InfoRow label={t('Last Updated')} value={`${formatRelativeTime(vessel.lastUpdateUtc)} (${formatDateTime(vessel.lastUpdateUtc)})`} />
          </Section>

          {contextBlocks.length > 0 || showBoundary ? (
            <Section title={t('Context')}>
              <View style={styles.blocks}>
                {contextBlocks.map((block) => (
                  <Disclosure key={block.key} title={block.title} testID={`vessel-detail-context-${block.key}`}>
                    <AppText selectable style={[typography.mono, styles.pre]}>{block.text}</AppText>
                  </Disclosure>
                ))}
                {showBoundary ? (
                  <Disclosure title={t('Dock Boundary')} testID="vessel-detail-context-boundary">
                    <AppText>{`${t('Secret Scan')}: ${vessel.secretScanEnabled ? t('Enabled') : t('Disabled')}`}</AppText>
                    {vessel.protectedPathPatterns?.length ? (
                      <AppText selectable style={[typography.mono, styles.pre]}>{`${t('Protected paths')}:\n${vessel.protectedPathPatterns.join('\n')}`}</AppText>
                    ) : null}
                    {vessel.privateIdentifierDenylist?.length ? (
                      <AppText selectable style={[typography.mono, styles.pre]}>{`${t('Private identifiers')}:\n${vessel.privateIdentifierDenylist.join('\n')}`}</AppText>
                    ) : null}
                  </Disclosure>
                ) : null}
              </View>
            </Section>
          ) : null}

          <Section title={t('Missions')}>
            {missions.loading ? (
              <InfoRow label={t('Missions')} value={t('Loading...')} />
            ) : (missions.data ?? []).length === 0 ? (
              <AppText muted style={styles.empty}>{t('No missions yet')}</AppText>
            ) : (
              (missions.data ?? []).map((m) => (
                <ListRow
                  key={m.id}
                  title={m.title}
                  subtitle={[m.id, m.captainId, m.branchName].filter(Boolean).join(' \u00B7 ')}
                  accessory={<StatusBadge label={m.status} tone={statusTone(m.status)} />}
                  accessibilityValue={m.status}
                  onPress={() => router.push(`/missions/${encodeURIComponent(m.id)}` as Href)}
                  testID={`vessel-mission-${m.id}`}
                />
              ))
            )}
          </Section>
        </View>
      </ScrollView>
      {actions.elements}
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  flex: { flex: 1 },
  content: { paddingVertical: spacing.lg, flexGrow: 1 },
  column: { width: '100%', maxWidth: 820, alignSelf: 'center' },
  embeddedHead: { flexDirection: 'row', alignItems: 'center', paddingHorizontal: spacing.lg, marginBottom: spacing.md },
  blocks: { padding: spacing.md, gap: spacing.sm },
  pre: { fontSize: 12, lineHeight: 17 },
  empty: { padding: spacing.lg },
});
