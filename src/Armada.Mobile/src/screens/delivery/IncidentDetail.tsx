import { Stack, useLocalSearchParams, useRouter, type Href } from 'expo-router';
import { useState } from 'react';
import { createIncident, deleteIncident, getIncident, listRunbookExecutions, rollbackDeployment, updateIncident } from '@dashboard/api/client';
import type { Incident, RunbookExecution } from '@dashboard/types/models';
import { buildIncidentPrompt } from '@dashboard/lib/deliveryForms';
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
import { useDeployments, useEnvironments, useReleases } from './deliveryLookups';
import { INCIDENT_PREFILL_KEYS, incidentFields, incidentPayload, incidentValues, linkIncidentValues, newIncidentValues, type IncidentPrefill } from './incidentForm';

export interface IncidentDetailViewProps {
  id: string;
  embedded?: boolean;
  onDeleted?: () => void;
  onChanged?: () => void;
}

interface IncidentData {
  incident: Incident;
  executions: RunbookExecution[];
}

/**
 * One incident (the dashboard's /incidents/:id): status, severity, links, impact, root cause, recovery notes,
 * postmortem, rescue attempts, and runbook executions, with Plan Hotfix, Dispatch Hotfix, Runbook, Rollback
 * Deployment, Open Deployment, Open Environment, View JSON, Edit, and Delete. Live: reloads on incident.* events.
 */
export function IncidentDetailView({ id, embedded, onDeleted, onChanged }: IncidentDetailViewProps) {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { pushToast } = useNotifications();
  const router = useRouter();
  const canManage = isAdmin || isTenantAdmin;
  const vessels = useVessels();
  const vesselNames = useNameMap(vessels);
  const environments = useEnvironments();
  const deployments = useDeployments();
  const releases = useReleases();
  const { confirm, dialog } = useConfirm('incident-confirm');
  const [editing, setEditing] = useState(false);
  const [jsonOpen, setJsonOpen] = useState(false);

  const { data, loading, refreshing, error, reload, refresh, setData } = useLoad<IncidentData>(async () => {
    const [incident, executions] = await Promise.all([
      getIncident(id),
      listRunbookExecutions({ ...ALL, incidentId: id }).then((r) => r.objects ?? []).catch(() => [] as RunbookExecution[]),
    ]);
    return { incident, executions };
  }, [id], { live: ['incident.', 'runbook-execution.'], fallbackError: t('Failed to load incident.') });
  useReloadOnFocus(reload);

  if (!data) return <DetailPending loading={loading} error={error} onRetry={() => void reload()} />;
  const i = data.incident;
  const setIncident = (next: Incident) => setData({ ...data, incident: next });
  const go = (href: string) => router.push(href as Href);
  const environment = environments.find((e) => e.id === i.environmentId) || null;
  const deployment = deployments.find((d) => d.id === i.deploymentId) || null;
  const hotfixVesselId = i.vesselId || environment?.vesselId || null;
  const prompt = buildIncidentPrompt(i.title, i.summary || '', i.impact || '', i.environmentName || '', i.deploymentId || '', i.releaseId || '');

  function rollback() {
    if (!i.deploymentId) return;
    const deploymentId = i.deploymentId;
    confirm({
      title: t('Rollback Deployment'),
      message: t('Rollback deployment "{{deploymentId}}" and attach the result to this incident?', { deploymentId }),
      confirmLabel: t('Rollback'),
      danger: true,
      onConfirm: async () => {
        try {
          await rollbackDeployment(deploymentId);
          const updated = await updateIncident(i.id, { rollbackDeploymentId: deploymentId, status: 'RolledBack' });
          setIncident(updated);
          pushToast('warning', t('Rollback started for "{{deploymentId}}".', { deploymentId }));
          onChanged?.();
        } catch (err: unknown) {
          pushToast('error', errorText(err, t('Rollback failed.')));
        }
      },
    });
  }

  function remove() {
    confirm({
      title: t('Delete Incident'),
      message: t('Delete "{{title}}"? This removes only the incident record.', { title: i.title }),
      confirmLabel: t('Delete'),
      danger: true,
      onConfirm: async () => {
        try {
          await deleteIncident(i.id);
          pushToast('warning', t('Incident "{{title}}" deleted.', { title: i.title }));
          if (onDeleted) onDeleted();
          else router.back();
        } catch (err: unknown) {
          pushToast('error', errorText(err, t('Delete failed.')));
        }
      },
    });
  }

  const runbookLink = `/delivery${prefillQuery({
    tab: 'runbooks', execTitle: `${i.title} Response`, execWorkflowProfileId: deployment?.workflowProfileId,
    execEnvironmentId: i.environmentId || deployment?.environmentId, execEnvironmentName: i.environmentName || deployment?.environmentName,
    execDeploymentId: i.deploymentId, execIncidentId: i.id, execCheckType: i.deploymentId ? 'DeploymentVerification' : 'Custom', execNotes: prompt,
  })}`;

  return (
    <DetailBody embedded={embedded} refreshing={refreshing} onRefresh={() => void refresh()} testID="incident-detail">
      {!embedded ? <Stack.Screen options={{ title: i.title }} /> : null}
      <DetailHeader title={i.title} subtitle={i.id} testID="incident-title" badges={<><StatusBadge {...statusBadge(t, i.status)} /><StatusBadge {...statusBadge(t, i.severity)} /></>} />
      <ActionBar>
        {hotfixVesselId ? (
          <>
            <Button label={t('Plan Hotfix')} variant="secondary" style={resourceStyles.action} onPress={() => go(`/planning${prefillQuery({ fromIncident: '1', vesselId: hotfixVesselId, title: `Hotfix: ${i.title}`, initialPrompt: prompt })}`)} />
            <Button label={t('Dispatch Hotfix')} variant="secondary" style={resourceStyles.action} onPress={() => go(`/dispatch${prefillQuery({ fromIncident: '1', vesselId: hotfixVesselId, voyageTitle: `Hotfix: ${i.title}`, prompt })}`)} />
          </>
        ) : null}
        <Button label={t('Runbook')} variant="secondary" style={resourceStyles.action} onPress={() => go(runbookLink)} />
        {i.deploymentId ? <Button label={t('Rollback Deployment')} variant="secondary" style={resourceStyles.action} onPress={rollback} testID="incident-rollback" /> : null}
        {i.deploymentId ? <Button label={t('Open Deployment')} variant="secondary" style={resourceStyles.action} onPress={() => go(`/deployments/${i.deploymentId}`)} /> : null}
        {i.environmentId ? <Button label={t('Open Environment')} variant="secondary" style={resourceStyles.action} onPress={() => go(`/environments/${i.environmentId}`)} /> : null}
        <Button label={t('View JSON')} variant="ghost" style={resourceStyles.action} onPress={() => setJsonOpen(true)} />
        {canManage ? <Button label={t('Edit')} variant="secondary" icon="create-outline" style={resourceStyles.action} onPress={() => setEditing(true)} testID="incident-edit" /> : null}
        {canManage ? <Button label={t('Delete')} variant="danger" style={resourceStyles.action} onPress={remove} testID="incident-delete" /> : null}
      </ActionBar>

      <FieldCard title={t('Overview')}>
        <Field label={t('Status')} value={t(i.status)} />
        <Field label={t('Severity')} value={t(i.severity)} />
        <Field label={t('Vessel')} value={i.vesselId ? (vesselNames.get(i.vesselId) || i.vesselId) : null} onPress={i.vesselId ? () => go(`/vessels/${i.vesselId}`) : undefined} />
        <Field label={t('Environment')} value={environment?.name || i.environmentName} onPress={i.environmentId ? () => go(`/environments/${i.environmentId}`) : undefined} />
        <Field label={t('Deployment')} value={deployment?.title || i.deploymentId} onPress={i.deploymentId ? () => go(`/deployments/${i.deploymentId}`) : undefined} />
        <Field label={t('Release')} value={i.releaseId} mono onPress={i.releaseId ? () => go(`/releases/${i.releaseId}`) : undefined} />
        <Field label={t('Mission ID')} value={i.missionId} mono onPress={i.missionId ? () => go(`/missions/${i.missionId}`) : undefined} />
        <Field label={t('Voyage ID')} value={i.voyageId} mono onPress={i.voyageId ? () => go(`/voyages/${i.voyageId}`) : undefined} />
        <Field label={t('Rollback Deployment')} value={i.rollbackDeploymentId} mono onPress={i.rollbackDeploymentId ? () => go(`/deployments/${i.rollbackDeploymentId}`) : undefined} />
        <Field label={t('Detected')} value={i.detectedUtc ? formatDateTime(i.detectedUtc) : null} />
        <Field label={t('Mitigated')} value={i.mitigatedUtc ? formatDateTime(i.mitigatedUtc) : null} />
        <Field label={t('Closed')} value={i.closedUtc ? formatDateTime(i.closedUtc) : null} />
        <Field label={t('Last Updated')} value={formatRelativeTime(i.lastUpdateUtc)} />
        {i.failureKind ? <Field label={t('Failure Kind')} value={i.failureKind} /> : null}
        {i.failureKind ? <Field label={t('Rescue Attempts')} value={i.recoveryAttempts ?? 0} /> : null}
        {i.failureKind ? <Field label={t('Rescue Missions')} value={i.rescueMissionIds?.length ?? 0} /> : null}
      </FieldCard>
      <TextBlock title={t('Summary')} text={i.summary} />
      <TextBlock title={t('Impact')} text={i.impact} />
      <TextBlock title={t('Root Cause')} text={i.rootCause} />
      <TextBlock title={t('Recovery Notes')} text={i.recoveryNotes} />
      <TextBlock title={t('Postmortem')} text={i.postmortem} />
      <FieldCard title={t('Runbook Executions')}>
        {data.executions.length === 0 ? <AppText muted style={resourceStyles.pad}>{t('No runbook executions are linked to this incident yet.')}</AppText> : data.executions.map((x) => (
          <ResourceRow
            key={x.id}
            title={x.title}
            subtitle={`${x.environmentName || t('No environment')} \u2022 ${x.checkType || t('No check type')} \u2022 ${x.completedStepIds.length} ${t('steps complete')}`}
            badge={statusBadge(t, x.status)}
            meta={`${t('Last updated')} ${formatRelativeTime(x.lastUpdateUtc)}`}
            onPress={() => go(`/runbooks/${x.runbookId}?executionId=${encodeURIComponent(x.id)}`)}
          />
        ))}
      </FieldCard>

      <FormSheet
        testID="incident-form"
        open={editing}
        title={t('Edit Incident')}
        initial={incidentValues(i)}
        fields={() => incidentFields(t, { vessels, environments, deployments, releases }, true)}
        onChange={(next, prev) => linkIncidentValues(next, prev, environments, deployments)}
        submitLabel={t('Save Incident')}
        onClose={() => setEditing(false)}
        onSubmit={async (values) => {
          const updated = await updateIncident(i.id, incidentPayload(values));
          setIncident(updated);
          setEditing(false);
          pushToast('success', t('Incident "{{title}}" saved.', { title: updated.title }));
          onChanged?.();
        }}
      />
      <JsonSheet open={jsonOpen} title={i.title} data={i} onClose={() => setJsonOpen(false)} />
      {dialog}
    </DetailBody>
  );
}

export function incidentPrefillFrom(params: Record<string, string | string[] | undefined>): IncidentPrefill {
  const out: IncidentPrefill = {};
  for (const key of INCIDENT_PREFILL_KEYS) {
    const value = param(params[key]);
    if (value) out[key] = value;
  }
  return out;
}

/** The /incidents/:id route; `new` is the full create form, prefilled from the query (environment, deployment, ...). */
export function IncidentDetailRoute() {
  const params = useLocalSearchParams<Record<string, string>>();
  const { t } = useLocale();
  const router = useRouter();
  const { pushToast } = useNotifications();
  const vessels = useVessels();
  const environments = useEnvironments();
  const deployments = useDeployments();
  const releases = useReleases();
  if (params.id !== 'new') return <IncidentDetailView id={params.id} />;
  return (
    <DetailBody testID="incident-create">
      <Stack.Screen options={{ title: t('Create Incident') }} />
      <FormSheet
        testID="incident-form"
        open
        title={t('Create Incident')}
        initial={newIncidentValues(incidentPrefillFrom(params))}
        fields={() => incidentFields(t, { vessels, environments, deployments, releases }, true)}
        onChange={(next, prev) => linkIncidentValues(next, prev, environments, deployments)}
        submitLabel={t('Create Incident')}
        onClose={() => router.back()}
        onSubmit={async (values) => {
          const created = await createIncident(incidentPayload(values));
          pushToast('success', t('Incident "{{title}}" created.', { title: created.title }));
          router.replace(`/incidents/${created.id}` as Href);
        }}
      />
    </DetailBody>
  );
}
