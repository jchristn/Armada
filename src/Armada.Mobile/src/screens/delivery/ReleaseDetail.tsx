import { Stack, useLocalSearchParams, useRouter, type Href } from 'expo-router';
import { useState } from 'react';
import {
  createRelease, deleteRelease, getRelease, getReleaseGitHubPullRequests, listCheckRuns, listDeployments, listObjectives, listVoyages,
  refreshRelease, updateRelease,
} from '@dashboard/api/client';
import type { CheckRun, Deployment, GitHubPullRequestDetail, Objective, Release, Voyage } from '@dashboard/types/models';
import { splitList } from '@dashboard/lib/deliveryForms';
import { useAuth } from '../../auth/AuthContext';
import { ActionBar, DetailBody, DetailHeader, DetailPending, Field, FieldCard, JsonSheet, TextBlock } from '../../components/resource/DetailParts';
import { FormSheet } from '../../components/resource/FormSheet';
import { ResourceRow, useConfirm } from '../../components/resource/ResourceRow';
import { resourceStyles } from '../../components/resource/styles';
import { AppText } from '../../components/ui/AppText';
import { Banner } from '../../components/ui/Banner';
import { Button } from '../../components/ui/Button';
import { StatusBadge } from '../../components/ui/StatusBadge';
import { confirmOpenExternalUrl } from '../../lib/externalLinks';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { param, prefillQuery } from '../../resource/links';
import { ALL, useNameMap, useReference, useVessels } from '../../resource/lookups';
import { statusBadge } from '../../resource/status';
import { errorText, useLoad, useReloadOnFocus } from '../../resource/useLoad';
import { useWorkflowProfiles } from './deliveryLookups';
import { newReleaseValues, releaseFields, releasePayload, releaseValues, type ReleasePrefill } from './releaseForm';

export interface ReleaseDetailViewProps {
  id: string;
  embedded?: boolean;
  onDeleted?: () => void;
  onChanged?: () => void;
}

interface ReleaseData {
  release: Release;
  objectives: Objective[];
  pullRequests: GitHubPullRequestDetail[];
}

/**
 * One release (the dashboard's /releases/:id): status, version, notes, linked voyages / missions / checks, backlog
 * items, deployment evidence, artifacts, and GitHub pull requests, with Deploy, Run Check, View JSON, Refresh Derived
 * Fields, Edit, and Delete (tenant admins edit).
 */
export function ReleaseDetailView({ id, embedded, onDeleted, onChanged }: ReleaseDetailViewProps) {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { pushToast } = useNotifications();
  const router = useRouter();
  const canManage = isAdmin || isTenantAdmin;
  const vessels = useVessels();
  const vesselNames = useNameMap(vessels);
  const profiles = useWorkflowProfiles();
  const profileNames = useNameMap(profiles);
  const voyages = useReference<Voyage>(() => listVoyages(ALL));
  const checkRuns = useReference<CheckRun>(() => listCheckRuns(ALL));
  const deployments = useReference<Deployment>(() => listDeployments(ALL), ['deployment.']);
  const { confirm, dialog } = useConfirm('release-confirm');
  const [editing, setEditing] = useState(false);
  const [jsonOpen, setJsonOpen] = useState(false);
  const [refreshingDerived, setRefreshingDerived] = useState(false);

  const { data, loading, refreshing, error, reload, refresh, setData } = useLoad<ReleaseData>(async () => {
    const [release, objectives, pullRequests] = await Promise.all([
      getRelease(id),
      listObjectives({ ...ALL, releaseId: id }).then((r) => r.objects ?? []).catch(() => [] as Objective[]),
      getReleaseGitHubPullRequests(id).then((r) => r ?? []).catch(() => [] as GitHubPullRequestDetail[]),
    ]);
    return { release, objectives, pullRequests };
  }, [id], { fallbackError: t('Failed to load release.') });
  useReloadOnFocus(reload);

  if (!data) return <DetailPending loading={loading} error={error} onRetry={() => void reload()} />;
  const r = data.release;
  const setRelease = (next: Release) => setData({ ...data, release: next });
  const go = (href: string) => router.push(href as Href);
  const relatedDeployments = deployments.filter((d) => d.releaseId === r.id);
  const relatedObjectives = data.objectives.filter((o) => o.releaseIds.includes(r.id));
  const voyageName = (vid: string) => voyages.find((v) => v.id === vid)?.title || vid;
  const checkName = (cid: string) => {
    const run = checkRuns.find((c) => c.id === cid);
    return run ? (run.label || run.type) : cid;
  };

  async function refreshDerived() {
    setRefreshingDerived(true);
    try {
      const refreshed = await refreshRelease(r.id);
      setRelease(refreshed);
      pushToast('success', t('Release "{{title}}" refreshed from linked work.', { title: refreshed.title }));
      onChanged?.();
    } catch (err: unknown) {
      pushToast('error', errorText(err, t('Refresh failed.')));
    } finally {
      setRefreshingDerived(false);
    }
  }

  function remove() {
    confirm({
      title: t('Delete Release'),
      message: t('Delete "{{title}}"? This removes only the release record.', { title: r.title }),
      confirmLabel: t('Delete'),
      danger: true,
      onConfirm: async () => {
        try {
          await deleteRelease(r.id);
          pushToast('warning', t('Release "{{title}}" deleted.', { title: r.title }));
          if (onDeleted) onDeleted();
          else router.back();
        } catch (err: unknown) {
          pushToast('error', errorText(err, t('Delete failed.')));
        }
      },
    });
  }

  const deployLink = `/deployments/new${prefillQuery({
    vesselId: r.vesselId, workflowProfileId: r.workflowProfileId, releaseId: r.id, voyageId: r.voyageIds[0], missionId: r.missionIds[0],
    title: `${r.title} Deploy`, sourceRef: r.tagName || r.version, summary: r.summary,
  })}`;
  const checkLink = `/delivery${prefillQuery({
    tab: 'checks', run: '1', vesselId: r.vesselId, workflowProfileId: r.workflowProfileId, voyageId: r.voyageIds[0], missionId: r.missionIds[0], label: r.title,
  })}`;

  return (
    <DetailBody embedded={embedded} refreshing={refreshing} onRefresh={() => void refresh()} testID="release-detail">
      {!embedded ? <Stack.Screen options={{ title: r.title }} /> : null}
      <DetailHeader title={r.title} subtitle={r.id} testID="release-title" badges={<StatusBadge {...statusBadge(t, r.status)} />} />
      {!canManage ? <Banner tone="info" title={t('You can view releases, but only tenant administrators can create or change them.')} /> : null}
      <ActionBar>
        <Button label={t('Deploy')} icon="rocket-outline" style={resourceStyles.action} onPress={() => go(deployLink)} testID="release-deploy" />
        <Button label={t('Run Check')} variant="secondary" style={resourceStyles.action} onPress={() => go(checkLink)} />
        <Button label={t('View JSON')} variant="ghost" style={resourceStyles.action} onPress={() => setJsonOpen(true)} />
        {canManage ? <Button label={refreshingDerived ? t('Refreshing...') : t('Refresh Derived Fields')} variant="secondary" busy={refreshingDerived} style={resourceStyles.action} onPress={() => void refreshDerived()} testID="release-refresh" /> : null}
        {canManage ? <Button label={t('Edit')} variant="secondary" icon="create-outline" style={resourceStyles.action} onPress={() => setEditing(true)} testID="release-edit" /> : null}
        {canManage ? <Button label={t('Delete')} variant="danger" style={resourceStyles.action} onPress={remove} testID="release-delete" /> : null}
      </ActionBar>

      <FieldCard title={t('Overview')}>
        <Field label={t('Status')} value={t(r.status)} />
        <Field label={t('Version')} value={r.version || t('Unversioned')} mono />
        <Field label={t('Tag Name')} value={r.tagName} mono />
        <Field label={t('Vessel')} value={r.vesselId ? (vesselNames.get(r.vesselId) || r.vesselId) : null} onPress={r.vesselId ? () => go(`/vessels/${r.vesselId}`) : undefined} />
        <Field label={t('Workflow Profile')} value={r.workflowProfileId ? (profileNames.get(r.workflowProfileId) || r.workflowProfileId) : t('Resolved default')} />
        <Field label={t('Created')} value={formatDateTime(r.createdUtc)} />
        <Field label={t('Last Updated')} value={formatRelativeTime(r.lastUpdateUtc)} />
        <Field label={t('Published')} value={r.publishedUtc ? formatDateTime(r.publishedUtc) : null} />
      </FieldCard>
      <TextBlock title={t('Summary')} text={r.summary} />
      <TextBlock title={t('Notes')} text={r.notes} />

      <FieldCard title={t('Linked Voyages')}>
        {r.voyageIds.length === 0 ? <AppText muted style={resourceStyles.pad}>{t('None')}</AppText> : r.voyageIds.map((vid) => (
          <ResourceRow key={vid} title={voyageName(vid)} subtitle={vid} onPress={() => go(`/voyages/${vid}`)} />
        ))}
      </FieldCard>
      <FieldCard title={t('Linked Missions')}>
        {r.missionIds.length === 0 ? <AppText muted style={resourceStyles.pad}>{t('None')}</AppText> : r.missionIds.map((mid) => (
          <ResourceRow key={mid} title={mid} onPress={() => go(`/missions/${mid}`)} />
        ))}
      </FieldCard>
      <FieldCard title={t('Linked Checks')}>
        {r.checkRunIds.length === 0 ? <AppText muted style={resourceStyles.pad}>{t('None')}</AppText> : r.checkRunIds.map((cid) => (
          <ResourceRow key={cid} title={checkName(cid)} subtitle={cid} onPress={() => go(`/checks/${cid}`)} />
        ))}
      </FieldCard>
      <FieldCard title={`${t('Linked Backlog Items')} (${relatedObjectives.length})`}>
        {relatedObjectives.length === 0 ? <AppText muted style={resourceStyles.pad}>{t('No backlog items currently reference this release.')}</AppText> : relatedObjectives.map((o) => (
          <ResourceRow
            key={o.id}
            title={o.title}
            subtitle={o.description || o.id}
            badge={statusBadge(t, o.status)}
            onPress={() => go(`/backlog/${o.id}`)}
            actions={[{ key: 'history', label: t('View History'), icon: 'time-outline', onPress: () => go(`/history?objectiveId=${encodeURIComponent(o.id)}`) }]}
          />
        ))}
      </FieldCard>
      <FieldCard title={`${t('Deployment Evidence')} (${relatedDeployments.length})`}>
        {relatedDeployments.length === 0 ? <AppText muted style={resourceStyles.pad}>{t('No deployments are linked to this release yet.')}</AppText> : relatedDeployments.map((d) => (
          <ResourceRow
            key={d.id}
            title={d.title}
            subtitle={`${d.environmentName || t('No environment')} \u2022 ${d.checkRunIds.length} ${t('checks')} \u2022 ${d.requestHistorySummary?.totalCount || 0} ${t('requests')}`}
            badge={statusBadge(t, d.status)}
            meta={t(d.verificationStatus)}
            onPress={() => go(`/deployments/${d.id}`)}
          />
        ))}
      </FieldCard>
      <FieldCard title={t('Artifacts')}>
        {r.artifacts.length === 0 ? (
          <AppText muted style={resourceStyles.pad}>{t('No artifacts are currently linked to this release. Refresh the release after relevant check runs complete to rebuild derived artifact metadata.')}</AppText>
        ) : r.artifacts.map((a) => (
          <ResourceRow
            key={`${a.sourceId ?? ''}:${a.path}`}
            title={a.path}
            subtitle={`${a.sourceType}${a.sourceId ? ` ${a.sourceId}` : ''} \u2022 ${a.sizeBytes.toLocaleString()} ${t('bytes')}${a.lastWriteUtc ? ` \u2022 ${formatDateTime(a.lastWriteUtc)}` : ''}`}
            onPress={a.sourceId ? () => go(`/checks/${a.sourceId}`) : undefined}
          />
        ))}
      </FieldCard>
      <FieldCard title={`${t('GitHub Pull Requests')} (${data.pullRequests.length})`} testID="release-pull-requests">
        {data.pullRequests.length === 0 ? <AppText muted style={resourceStyles.pad}>{t('No linked GitHub pull requests were found for this release yet.')}</AppText> : data.pullRequests.map((pr) => (
          <ResourceRow
            key={`${pr.repository}-${pr.number}`}
            title={pr.title}
            subtitle={`${pr.repository} #${pr.number} \u2022 ${t('Checks')}: ${pr.checks.length} \u2022 ${t('Reviews')}: ${pr.reviews.length} \u2022 ${t('Reviewers')}: ${pr.requestedReviewers.length > 0 ? pr.requestedReviewers.join(', ') : '-'}`}
            badge={statusBadge(t, pr.state)}
            meta={`${t(pr.reviewStatus)}${pr.updatedUtc ? ` \u2022 ${formatRelativeTime(pr.updatedUtc)}` : ''}`}
            onPress={() => { confirmOpenExternalUrl(pr.url, t); }}
          />
        ))}
      </FieldCard>

      <FormSheet
        testID="release-form"
        open={editing}
        title={t('Edit Release')}
        initial={releaseValues(r)}
        fields={() => releaseFields(t, vessels, profiles)}
        submitLabel={t('Save Changes')}
        onClose={() => setEditing(false)}
        onSubmit={async (values) => {
          const updated = await updateRelease(r.id, releasePayload(values, []));
          setRelease(updated);
          setEditing(false);
          pushToast('success', t('Release "{{title}}" saved.', { title: updated.title }));
          onChanged?.();
        }}
      />
      <JsonSheet open={jsonOpen} title={r.title} data={r} onClose={() => setJsonOpen(false)} />
      {dialog}
    </DetailBody>
  );
}

/** The prefill /releases/new reads from its query string (id lists comma separated, plus objectiveIds). */
export function releasePrefillFrom(params: Record<string, string | string[] | undefined>): ReleasePrefill {
  const keys: (keyof ReleasePrefill)[] = ['vesselId', 'workflowProfileId', 'title', 'version', 'tagName', 'summary', 'notes', 'status', 'voyageIds', 'missionIds', 'checkRunIds'];
  const out: ReleasePrefill = {};
  for (const key of keys) {
    const value = param(params[key]);
    if (value) out[key] = value;
  }
  return out;
}

/** The release create form: /releases/new (and /releases/new as an id, like the dashboard). */
export function ReleaseCreateScreen() {
  const params = useLocalSearchParams<Record<string, string>>();
  const { t } = useLocale();
  const router = useRouter();
  const { pushToast } = useNotifications();
  const { isAdmin, isTenantAdmin } = useAuth();
  const vessels = useVessels();
  const profiles = useWorkflowProfiles();
  const objectiveIds = splitList(param(params.objectiveIds));
  return (
    <DetailBody testID="release-create">
      <Stack.Screen options={{ title: t('Create Release') }} />
      {!(isAdmin || isTenantAdmin) ? <Banner tone="info" title={t('You can view releases, but only tenant administrators can create or change them.')} /> : null}
      {objectiveIds.length > 0 ? (
        <Banner tone="info" title={t('Prefilled from {{count}} backlog item(s). Armada will link this release back to those scoped work records when you create it.', { count: objectiveIds.length })} />
      ) : null}
      <FormSheet
        testID="release-form"
        open
        title={t('Create Release')}
        initial={newReleaseValues(releasePrefillFrom(params))}
        fields={() => releaseFields(t, vessels, profiles)}
        submitLabel={t('Create Release')}
        onClose={() => router.back()}
        onSubmit={async (values) => {
          const created = await createRelease(releasePayload(values, objectiveIds));
          pushToast('success', t('Release "{{title}}" created.', { title: created.title }));
          router.replace(`/releases/${created.id}` as Href);
        }}
      />
    </DetailBody>
  );
}

/** The /releases/:id route (`new` is the create form). */
export function ReleaseDetailRoute() {
  const { id } = useLocalSearchParams<{ id: string }>();
  if (!id || id === 'new') return <ReleaseCreateScreen />;
  return <ReleaseDetailView id={id} />;
}
