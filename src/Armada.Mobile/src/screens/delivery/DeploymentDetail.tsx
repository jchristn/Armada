import { Stack, useLocalSearchParams, useRouter, type Href } from 'expo-router';
import { useState } from 'react';
import {
  approveDeployment, createDeployment, deleteDeployment, denyDeployment, getDeployment, listRunbookExecutions, rollbackDeployment,
  syncGitHubActions, updateDeployment, verifyDeployment,
} from '@dashboard/api/client';
import type { Deployment, RunbookExecution } from '@dashboard/types/models';
import { deploymentApprovalLabel } from '@dashboard/lib/deploymentApprovalLabel';
import { useAuth } from '../../auth/AuthContext';
import { ActionBar, DetailBody, DetailHeader, DetailPending, Field, FieldCard, JsonSheet, TextBlock } from '../../components/resource/DetailParts';
import { FormSheet } from '../../components/resource/FormSheet';
import { ResourceRow, useConfirm } from '../../components/resource/ResourceRow';
import { resourceStyles } from '../../components/resource/styles';
import { AppText } from '../../components/ui/AppText';
import { Button } from '../../components/ui/Button';
import { StatusBadge } from '../../components/ui/StatusBadge';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { param, prefillQuery } from '../../resource/links';
import { ALL, useNameMap, useVessels } from '../../resource/lookups';
import { statusBadge } from '../../resource/status';
import { errorText, useLoad, useReloadOnFocus } from '../../resource/useLoad';
import { useEnvironments, useReleases, useWorkflowProfiles } from './deliveryLookups';
import { deploymentFields, deploymentPayload, deploymentValues, linkDeploymentValues, newDeploymentValues, type DeploymentPrefill } from './deploymentForm';

export type DeploymentAction = 'approve' | 'deny' | 'verify' | 'rollback';

export interface DeploymentDetailViewProps {
  id: string;
  embedded?: boolean;
  onDeleted?: () => void;
  onChanged?: () => void;
}

interface DeploymentData {
  deployment: Deployment;
  executions: RunbookExecution[];
}

/**
 * One deployment (the dashboard's /deployments/:id): status and verification, approve / deny while it waits for
 * approval, verify and rollback otherwise (tenant admins, with the dashboard's confirmations), Run Check, Sync GitHub
 * Actions, Runbook, Create Incident, Open Release, Open Workspace, View JSON, Edit, Delete; monitoring, linked checks,
 * runbook executions, request summary, and identifiers. Live: reloads on deployment.* and runbook-execution.* events.
 */
export function DeploymentDetailView({ id, embedded, onDeleted, onChanged }: DeploymentDetailViewProps) {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { pushToast } = useNotifications();
  const router = useRouter();
  const canManage = isAdmin || isTenantAdmin;
  const vessels = useVessels();
  const vesselNames = useNameMap(vessels);
  const profiles = useWorkflowProfiles();
  const profileNames = useNameMap(profiles);
  const environments = useEnvironments();
  const releases = useReleases();
  const { confirm, dialog } = useConfirm('deployment-action-confirm');
  const [editing, setEditing] = useState(false);
  const [jsonOpen, setJsonOpen] = useState(false);
  const [syncing, setSyncing] = useState(false);

  const { data, loading, refreshing, error, reload, refresh, setData } = useLoad<DeploymentData>(async () => {
    const [deployment, executions] = await Promise.all([
      getDeployment(id),
      listRunbookExecutions({ ...ALL, deploymentId: id }).then((r) => r.objects ?? []).catch(() => [] as RunbookExecution[]),
    ]);
    return { deployment, executions };
  }, [id], { live: ['deployment.', 'runbook-execution.', 'check-run.'], fallbackError: t('Failed to load deployment.') });
  useReloadOnFocus(reload);

  if (!data) return <DetailPending loading={loading} error={error} onRetry={() => void reload()} />;
  const d = data.deployment;
  const setDeployment = (next: Deployment) => setData({ ...data, deployment: next });

  function act(action: DeploymentAction) {
    const titles: Record<DeploymentAction, string> = {
      approve: t('Approve Deployment'),
      deny: t('Deny Deployment'),
      verify: t('Run Verification'),
      rollback: t('Rollback Deployment'),
    };
    // Approvals lead with the environment ("Deploy to production: Release 2.3 hotfix"), like the inbox and the TUI.
    const approvalLabel = deploymentApprovalLabel(t, d.environmentName, d.title, d.id);
    const messages: Record<DeploymentAction, string> = {
      approve: t('Approve and execute "{{title}}"?', { title: approvalLabel }),
      deny: t('Deny "{{title}}" without executing it?', { title: approvalLabel }),
      verify: t('Re-run post-deploy verification for "{{title}}"?', { title: d.title }),
      rollback: t('Run rollback for "{{title}}"?', { title: d.title }),
    };
    const labels: Record<DeploymentAction, string> = { approve: t('Approve'), deny: t('Deny'), verify: t('Verify'), rollback: t('Rollback') };
    confirm({
      title: titles[action],
      message: messages[action],
      confirmLabel: labels[action],
      danger: action === 'deny' || action === 'rollback',
      onConfirm: async () => {
        try {
          let updated: Deployment;
          if (action === 'approve') updated = await approveDeployment(d.id);
          else if (action === 'deny') updated = await denyDeployment(d.id);
          else if (action === 'verify') updated = await verifyDeployment(d.id);
          else updated = await rollbackDeployment(d.id);
          setDeployment(updated);
          pushToast('success', t('Deployment "{{title}}" updated.', { title: updated.title }));
          onChanged?.();
        } catch (err: unknown) {
          pushToast('error', errorText(err, t('Action failed.')));
        }
      },
    });
  }

  function remove() {
    confirm({
      title: t('Delete Deployment'),
      message: t('Delete "{{title}}"? This removes only the deployment record.', { title: d.title }),
      confirmLabel: t('Delete'),
      danger: true,
      onConfirm: async () => {
        try {
          await deleteDeployment(d.id);
          pushToast('warning', t('Deployment "{{title}}" deleted.', { title: d.title }));
          if (onDeleted) onDeleted();
          else router.back();
        } catch (err: unknown) {
          pushToast('error', errorText(err, t('Delete failed.')));
        }
      },
    });
  }

  async function syncGitHub() {
    if (!d.vesselId) return;
    setSyncing(true);
    try {
      const result = await syncGitHubActions({
        vesselId: d.vesselId,
        workflowProfileId: d.workflowProfileId || null,
        deploymentId: d.id,
        environmentName: d.environmentName || null,
        branchName: d.sourceRef || null,
        runCount: 20,
      });
      setDeployment(await getDeployment(d.id));
      pushToast('success', t('GitHub Actions sync complete: {{created}} created, {{updated}} updated.', { created: result.createdCount, updated: result.updatedCount }));
    } catch (err: unknown) {
      pushToast('error', errorText(err, t('GitHub Actions sync failed.')));
    } finally {
      setSyncing(false);
    }
  }

  const runCheckLink = `/delivery${prefillQuery({
    tab: 'checks', run: '1', vesselId: d.vesselId, workflowProfileId: d.workflowProfileId, deploymentId: d.id, missionId: d.missionId,
    voyageId: d.voyageId, environmentName: d.environmentName, type: 'DeploymentVerification', label: `${d.title} verification`,
  })}`;
  const incidentLink = `/incidents/new${prefillQuery({
    title: `${d.title} Incident`, summary: d.latestMonitoringSummary || d.summary, vesselId: d.vesselId, environmentId: d.environmentId,
    environmentName: d.environmentName, deploymentId: d.id, releaseId: d.releaseId, missionId: d.missionId, voyageId: d.voyageId,
    severity: d.status === 'VerificationFailed' || d.status === 'Failed' ? 'High' : 'Medium',
  })}`;
  const runbookLink = `/delivery${prefillQuery({
    tab: 'runbooks', execTitle: `${d.title} Runbook`, execWorkflowProfileId: d.workflowProfileId, execEnvironmentId: d.environmentId,
    execEnvironmentName: d.environmentName, execDeploymentId: d.id, execCheckType: d.status === 'RolledBack' ? 'RollbackVerification' : 'DeploymentVerification',
    execNotes: d.summary || d.notes,
  })}`;
  const go = (href: string) => router.push(href as Href);
  const rel = (utc: string | null) => (utc ? formatRelativeTime(utc) : '-');
  const environment = environments.find((e) => e.id === d.environmentId);
  const summary = d.requestHistorySummary;

  return (
    <DetailBody embedded={embedded} refreshing={refreshing} onRefresh={() => void refresh()} testID="deployment-detail">
      {!embedded ? <Stack.Screen options={{ title: d.title }} /> : null}
      <DetailHeader
        title={d.title}
        subtitle={d.id}
        testID="deployment-title"
        badges={(
          <>
            <StatusBadge {...statusBadge(t, d.status)} />
            <StatusBadge {...statusBadge(t, d.verificationStatus)} />
            {d.approvalRequired ? <StatusBadge label={t('Approval required')} tone="warning" /> : null}
          </>
        )}
      />
      <ActionBar>
        {canManage && d.status === 'PendingApproval' ? (
          <>
            <Button label={t('Approve')} icon="checkmark" style={resourceStyles.action} onPress={() => act('approve')} testID="deployment-approve" />
            <Button label={t('Deny')} variant="secondary" style={resourceStyles.action} onPress={() => act('deny')} testID="deployment-deny" />
          </>
        ) : null}
        {canManage && d.status !== 'PendingApproval' ? (
          <>
            <Button label={t('Verify')} variant="secondary" style={resourceStyles.action} onPress={() => act('verify')} testID="deployment-verify" />
            <Button label={t('Rollback')} variant="secondary" style={resourceStyles.action} onPress={() => act('rollback')} testID="deployment-rollback" />
          </>
        ) : null}
        <Button label={t('Run Check')} variant="secondary" style={resourceStyles.action} onPress={() => go(runCheckLink)} />
        {d.vesselId ? <Button label={syncing ? t('Syncing GitHub...') : t('Sync GitHub Actions')} variant="secondary" busy={syncing} style={resourceStyles.action} onPress={() => void syncGitHub()} testID="deployment-sync-github" /> : null}
        <Button label={t('Runbook')} variant="secondary" style={resourceStyles.action} onPress={() => go(runbookLink)} />
        <Button label={t('Create Incident')} variant="secondary" style={resourceStyles.action} onPress={() => go(incidentLink)} />
        {d.releaseId ? <Button label={t('Open Release')} variant="secondary" style={resourceStyles.action} onPress={() => go(`/releases/${d.releaseId}`)} /> : null}
        {d.vesselId ? <Button label={t('Open Workspace')} variant="secondary" style={resourceStyles.action} onPress={() => go(`/workspace/${d.vesselId}`)} /> : null}
        <Button label={t('View JSON')} variant="ghost" style={resourceStyles.action} onPress={() => setJsonOpen(true)} />
        {canManage ? <Button label={t('Edit')} variant="secondary" icon="create-outline" style={resourceStyles.action} onPress={() => setEditing(true)} testID="deployment-edit" /> : null}
        {canManage ? <Button label={t('Delete')} variant="danger" style={resourceStyles.action} onPress={remove} testID="deployment-delete" /> : null}
      </ActionBar>

      <FieldCard title={t('Overview')}>
        <Field label={t('Status')} value={t(d.status)} testID="deployment-status" />
        <Field label={t('Verification')} value={t(d.verificationStatus)} />
        <Field label={t('Vessel')} value={d.vesselId ? (vesselNames.get(d.vesselId) || d.vesselId) : null} onPress={d.vesselId ? () => go(`/vessels/${d.vesselId}`) : undefined} />
        <Field label={t('Workflow Profile')} value={d.workflowProfileId ? (profileNames.get(d.workflowProfileId) || d.workflowProfileId) : t('Resolved default')} />
        <Field label={t('Environment')} value={environment?.name || d.environmentName} onPress={d.environmentId ? () => go(`/environments/${d.environmentId}`) : undefined} />
        <Field label={t('Source Ref')} value={d.sourceRef} mono />
        <Field label={t('Check Runs')} value={d.checkRunIds.length} />
        <Field label={t('Created')} value={formatDateTime(d.createdUtc)} />
        <Field label={t('Started')} value={rel(d.startedUtc)} />
        <Field label={t('Completed')} value={rel(d.completedUtc)} />
        <Field label={t('Approved By')} value={d.approvedByUserId} />
        <Field label={t('Monitoring Window')} value={rel(d.monitoringWindowEndsUtc)} />
        <Field label={t('Last Monitored')} value={rel(d.lastMonitoredUtc)} />
        <Field label={t('Regression Alerts')} value={d.monitoringFailureCount ?? 0} />
        <Field label={t('Last Alert')} value={rel(d.lastRegressionAlertUtc)} />
      </FieldCard>
      <TextBlock title={t('Summary')} text={d.summary} />
      <TextBlock title={t('Notes')} text={d.notes} />
      <TextBlock title={t('Verification Monitoring')} text={d.latestMonitoringSummary} emptyText={t('No rollout monitoring summary has been recorded yet for this deployment.')} />

      <FieldCard title={t('Linked Checks')} testID="deployment-checks">
        {d.checkRunIds.length === 0 ? <AppText muted style={resourceStyles.pad}>{t('No linked checks')}</AppText> : d.checkRunIds.map((checkId) => (
          <ResourceRow key={checkId} title={checkId} onPress={() => go(`/checks/${checkId}`)} />
        ))}
      </FieldCard>
      <FieldCard title={t('Runbook Executions')}>
        {data.executions.length === 0 ? <AppText muted style={resourceStyles.pad}>{t('No runbook executions are linked to this deployment yet.')}</AppText> : data.executions.map((x) => (
          <ResourceRow
            key={x.id}
            title={x.title}
            subtitle={`${x.environmentName || t('No environment')} \u2022 ${x.checkType || t('No check type')}`}
            badge={statusBadge(t, x.status)}
            onPress={() => go(`/runbooks/${x.runbookId}?executionId=${encodeURIComponent(x.id)}`)}
          />
        ))}
      </FieldCard>
      <FieldCard title={t('Request Summary')}>
        {summary ? (
          <>
            <Field label={t('Total Requests')} value={summary.totalCount} />
            <Field label={t('Success Rate')} value={`${summary.successRate}%`} />
            <Field label={t('Average Duration')} value={`${summary.averageDurationMs} ms`} />
            <Field label={t('Buckets')} value={summary.buckets.length} />
          </>
        ) : <AppText muted style={resourceStyles.pad}>{t('No request-history evidence is recorded yet for this deployment.')}</AppText>}
      </FieldCard>
      <FieldCard title={t('Identifiers')}>
        <Field label={t('Deployment ID')} value={d.id} mono />
        {d.releaseId ? <Field label={t('Release')} value={d.releaseId} mono onPress={() => go(`/releases/${d.releaseId}`)} /> : null}
        {d.missionId ? <Field label={t('Mission')} value={d.missionId} mono onPress={() => go(`/missions/${d.missionId}`)} /> : null}
        {d.voyageId ? <Field label={t('Voyage')} value={d.voyageId} mono onPress={() => go(`/voyages/${d.voyageId}`)} /> : null}
      </FieldCard>

      <FormSheet
        testID="deployment-form"
        open={editing}
        title={t('Edit Deployment')}
        initial={deploymentValues(d)}
        fields={(v) => deploymentFields(t, v, { vessels, profiles, environments, releases })}
        onChange={(next, prev) => linkDeploymentValues(next, prev, environments, releases)}
        submitLabel={t('Save Deployment')}
        onClose={() => setEditing(false)}
        onSubmit={async (values) => {
          const updated = await updateDeployment(d.id, deploymentPayload(values));
          setDeployment(updated);
          setEditing(false);
          pushToast('success', t('Deployment "{{title}}" saved.', { title: updated.title }));
          onChanged?.();
        }}
      />
      <JsonSheet open={jsonOpen} title={d.title} data={d} onClose={() => setJsonOpen(false)} />
      {dialog}
    </DetailBody>
  );
}

/** The prefill /deployments/new reads from its query string. */
export function deploymentPrefillFrom(params: Record<string, string | string[] | undefined>): DeploymentPrefill {
  const keys: (keyof DeploymentPrefill)[] = ['vesselId', 'workflowProfileId', 'environmentId', 'environmentName', 'releaseId', 'missionId', 'voyageId', 'title', 'sourceRef', 'summary', 'notes'];
  const out: DeploymentPrefill = {};
  for (const key of keys) {
    const value = param(params[key]);
    if (value) out[key] = value;
  }
  return out;
}

/** The /deployments/:id route; `new` is the create form, prefilled from the query (environment, release, ...). */
export function DeploymentDetailRoute() {
  const params = useLocalSearchParams<Record<string, string>>();
  const { t } = useLocale();
  const router = useRouter();
  const { pushToast } = useNotifications();
  const vessels = useVessels();
  const profiles = useWorkflowProfiles();
  const environments = useEnvironments();
  const releases = useReleases();
  if (params.id !== 'new') return <DeploymentDetailView id={params.id} />;
  return (
    <DetailBody testID="deployment-create">
      <Stack.Screen options={{ title: t('Create Deployment') }} />
      <FormSheet
        testID="deployment-form"
        open
        title={t('Create Deployment')}
        initial={newDeploymentValues(deploymentPrefillFrom(params))}
        fields={(v) => deploymentFields(t, v, { vessels, profiles, environments, releases })}
        onChange={(next, prev) => linkDeploymentValues(next, prev, environments, releases)}
        submitLabel={t('Create Deployment')}
        onClose={() => router.back()}
        onSubmit={async (values) => {
          const created = await createDeployment(deploymentPayload(values));
          pushToast('success', t('Deployment "{{title}}" created.', { title: created.title }));
          router.replace(`/deployments/${created.id}` as Href);
        }}
      />
    </DetailBody>
  );
}
