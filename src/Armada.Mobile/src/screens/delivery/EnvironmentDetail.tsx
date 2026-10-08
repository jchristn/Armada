import { Stack, useLocalSearchParams, useRouter, type Href } from 'expo-router';
import { useState } from 'react';
import { createEnvironment, deleteEnvironment, getEnvironment, updateEnvironment } from '@dashboard/api/client';
import type { DeploymentEnvironment, DeploymentVerificationDefinition } from '@dashboard/types/models';
import { createVerificationDefinition } from '@dashboard/lib/environmentForm';
import { buildEnvironmentDuplicatePayload } from '@dashboard/lib/duplicates';
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
import { prefillQuery } from '../../resource/links';
import { useNameMap, useVessels } from '../../resource/lookups';
import { errorText, useLoad, useReloadOnFocus } from '../../resource/useLoad';
import { applyVerification, environmentFields, environmentPayload, environmentValues, newEnvironmentValues, verificationFields, verificationValues } from './environmentForm';

export interface EnvironmentDetailViewProps {
  id: string;
  /** Inside the Delivery hub's split view on tablets. */
  embedded?: boolean;
  onDeleted?: () => void;
  onChanged?: () => void;
}

/**
 * One environment (the dashboard's /environments/:id): overview, notes, verification and monitoring settings, and
 * reusable verification definitions, with Deploy, Run Check, Create Incident, Runbook, Open Workspace, View JSON,
 * Edit, Duplicate, and Delete. Editing and the verification definitions are for tenant admins.
 */
export function EnvironmentDetailView({ id, embedded, onDeleted, onChanged }: EnvironmentDetailViewProps) {
  const { t, formatDateTime, formatRelativeTime } = useLocale();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { pushToast } = useNotifications();
  const router = useRouter();
  const canManage = isAdmin || isTenantAdmin;
  const vessels = useVessels();
  const vesselNames = useNameMap(vessels);
  const { confirm, dialog } = useConfirm('environment-confirm');
  const [editing, setEditing] = useState(false);
  const [jsonOpen, setJsonOpen] = useState(false);
  const [definition, setDefinition] = useState<DeploymentVerificationDefinition | null>(null);
  const [busy, setBusy] = useState(false);

  const { data: env, loading, refreshing, error, reload, refresh, setData } = useLoad(() => getEnvironment(id), [id], { fallbackError: t('Failed to load environment.') });
  useReloadOnFocus(reload);

  if (!env) return <DetailPending loading={loading} error={error} onRetry={() => void reload()} />;
  const e: DeploymentEnvironment = env;
  const vesselName = e.vesselId ? (vesselNames.get(e.vesselId) || e.vesselId) : null;

  async function save(definitions: DeploymentVerificationDefinition[], message: string) {
    const updated = await updateEnvironment(e.id, environmentPayload(environmentValues(e), definitions));
    setData(updated);
    pushToast('success', message);
    onChanged?.();
  }

  function remove() {
    confirm({
      title: t('Delete Environment'),
      message: t('Delete "{{name}}"? This removes only the environment record.', { name: e.name }),
      confirmLabel: t('Delete'),
      danger: true,
      onConfirm: async () => {
        try {
          await deleteEnvironment(e.id);
          pushToast('warning', t('Environment "{{name}}" deleted.', { name: e.name }));
          if (onDeleted) onDeleted();
          else router.back();
        } catch (err: unknown) {
          pushToast('error', errorText(err, t('Delete failed.')));
        }
      },
    });
  }

  async function duplicate() {
    setBusy(true);
    try {
      const created = await createEnvironment(buildEnvironmentDuplicatePayload(e));
      pushToast('success', t('Environment "{{name}}" duplicated.', { name: created.name }));
      onChanged?.();
      router.push(`/environments/${created.id}` as Href);
    } catch (err: unknown) {
      pushToast('error', errorText(err, t('Duplicate failed.')));
    } finally {
      setBusy(false);
    }
  }

  function removeDefinition(d: DeploymentVerificationDefinition) {
    void save(e.verificationDefinitions.filter((x) => x.id !== d.id), t('Environment "{{name}}" saved.', { name: e.name }))
      .catch((err: unknown) => pushToast('error', errorText(err, t('Save failed.'))));
  }

  return (
    <DetailBody embedded={embedded} refreshing={refreshing} onRefresh={() => void refresh()} testID="environment-detail">
      {!embedded ? <Stack.Screen options={{ title: e.name }} /> : null}
      <DetailHeader
        title={e.name}
        subtitle={e.id}
        testID="environment-title"
        badges={(
          <>
            <StatusBadge label={e.active ? t('Active') : t('Inactive')} tone={e.active ? 'success' : 'cancelled'} />
            <StatusBadge label={e.kind} tone="info" />
            {e.requiresApproval ? <StatusBadge label={t('Approval required')} tone="warning" /> : null}
          </>
        )}
      />
      <ActionBar>
        <Button label={t('Deploy')} icon="rocket-outline" style={resourceStyles.action} testID="environment-deploy"
          onPress={() => router.push(`/deployments/new${prefillQuery({ vesselId: e.vesselId, environmentId: e.id, environmentName: e.name, title: `${e.name} Deploy` })}` as Href)} />
        <Button label={t('Run Check')} variant="secondary" style={resourceStyles.action}
          onPress={() => router.push(`/delivery${prefillQuery({ tab: 'checks', run: '1', vesselId: e.vesselId, label: e.name, environmentName: e.name })}` as Href)} />
        <Button label={t('Create Incident')} variant="secondary" style={resourceStyles.action}
          onPress={() => router.push(`/incidents/new${prefillQuery({ vesselId: e.vesselId, environmentId: e.id, environmentName: e.name, title: `${e.name} Incident`, severity: e.kind === 'Production' ? 'High' : 'Medium' })}` as Href)} />
        <Button label={t('Runbook')} variant="secondary" style={resourceStyles.action}
          onPress={() => router.push(`/delivery${prefillQuery({ tab: 'runbooks', execEnvironmentId: e.id, execEnvironmentName: e.name, execCheckType: e.verificationDefinitions.length > 0 ? 'DeploymentVerification' : 'HealthCheck' })}` as Href)} />
        {e.vesselId ? <Button label={t('Open Workspace')} variant="secondary" style={resourceStyles.action} onPress={() => router.push(`/workspace/${e.vesselId}` as Href)} /> : null}
        <Button label={t('View JSON')} variant="ghost" style={resourceStyles.action} onPress={() => setJsonOpen(true)} />
        {canManage ? <Button label={t('Edit')} variant="secondary" icon="create-outline" style={resourceStyles.action} onPress={() => setEditing(true)} testID="environment-edit" /> : null}
        {canManage ? <Button label={t('Duplicate')} variant="ghost" style={resourceStyles.action} busy={busy} onPress={() => void duplicate()} /> : null}
        {canManage ? <Button label={t('Delete')} variant="danger" style={resourceStyles.action} onPress={remove} testID="environment-delete" /> : null}
      </ActionBar>

      <FieldCard title={t('Overview')}>
        <Field label={t('Vessel')} value={vesselName} onPress={e.vesselId ? () => router.push(`/vessels/${e.vesselId}` as Href) : undefined} />
        <Field label={t('Kind')} value={e.kind} />
        <Field label={t('Default')} value={e.isDefault ? t('Yes') : t('No')} />
        <Field label={t('Approval')} value={e.requiresApproval ? t('Required') : t('Not required')} />
        <Field label={t('Status')} value={e.active ? t('Active') : t('Inactive')} />
        <Field label={t('Base URL')} value={e.baseUrl} mono />
        <Field label={t('Health')} value={e.healthEndpoint} mono />
        <Field label={t('Configuration Source')} value={e.configurationSource} />
        <Field label={t('Monitoring Window')} value={`${e.rolloutMonitoringWindowMinutes} ${t('minutes')}`} />
        <Field label={t('Monitoring Interval')} value={`${e.rolloutMonitoringIntervalSeconds} ${t('seconds')}`} />
        <Field label={t('Regression Alerts')} value={e.alertOnRegression ? t('Enabled') : t('Disabled')} />
        <Field label={t('Created')} value={`${formatRelativeTime(e.createdUtc)} (${formatDateTime(e.createdUtc)})`} />
        <Field label={t('Last Updated')} value={`${formatRelativeTime(e.lastUpdateUtc)} (${formatDateTime(e.lastUpdateUtc)})`} />
      </FieldCard>
      <TextBlock title={t('Description')} text={e.description} />
      <TextBlock title={t('Access Notes')} text={e.accessNotes} />
      <TextBlock title={t('Deployment Rules')} text={e.deploymentRules} />

      <FieldCard title={t('Reusable Verification Definitions')} testID="environment-verifications">
        {e.verificationDefinitions.length === 0 ? (
          <AppText muted style={resourceStyles.pad}>{t('No reusable verification definitions are configured for this environment yet.')}</AppText>
        ) : e.verificationDefinitions.map((d) => (
          <ResourceRow
            key={d.id}
            title={d.name}
            subtitle={`${d.method} ${d.path} \u2192 ${d.expectedStatusCode ?? '-'}`}
            badge={{ label: d.active ? t('Active') : t('Inactive'), tone: d.active ? 'success' : 'cancelled' }}
            onPress={canManage ? () => setDefinition(d) : undefined}
            actions={canManage ? [{ key: 'remove', label: t('Remove'), icon: 'trash-outline', tone: 'danger', onPress: () => removeDefinition(d) }] : []}
          />
        ))}
      </FieldCard>
      {canManage ? (
        <Button label={t('Add Verification')} variant="secondary" icon="add" style={resourceStyles.create} onPress={() => setDefinition(createVerificationDefinition())} testID="environment-add-verification" />
      ) : null}

      <FormSheet
        testID="environment-form"
        open={editing}
        title={t('Edit Environment')}
        initial={environmentValues(e)}
        fields={() => environmentFields(t, vessels)}
        submitLabel={t('Save Environment')}
        onClose={() => setEditing(false)}
        onSubmit={async (values) => {
          const updated = await updateEnvironment(e.id, environmentPayload(values, e.verificationDefinitions));
          setData(updated);
          setEditing(false);
          pushToast('success', t('Environment "{{name}}" saved.', { name: updated.name }));
          onChanged?.();
        }}
      />
      <FormSheet
        testID="verification-form"
        open={definition !== null}
        title={t('Verification')}
        initial={definition ? verificationValues(definition) : {}}
        fields={() => verificationFields(t)}
        submitLabel={t('Save Environment')}
        onClose={() => setDefinition(null)}
        onSubmit={async (values) => {
          if (!definition) return;
          const next = applyVerification(definition, values);
          const exists = e.verificationDefinitions.some((d) => d.id === next.id);
          const definitions = exists ? e.verificationDefinitions.map((d) => (d.id === next.id ? next : d)) : [...e.verificationDefinitions, next];
          await save(definitions, t('Environment "{{name}}" saved.', { name: e.name }));
          setDefinition(null);
        }}
      />
      <JsonSheet open={jsonOpen} title={e.name} data={e} onClose={() => setJsonOpen(false)} />
      {dialog}
    </DetailBody>
  );
}

/**
 * The /environments/:id route. `new` is the create form (prefilled from ?vesselId=&kind=&name=, as the setup
 * wizard links it); saving opens the new environment.
 */
export function EnvironmentDetailRoute() {
  const params = useLocalSearchParams<{ id: string; vesselId?: string; kind?: string; name?: string }>();
  const { t } = useLocale();
  const router = useRouter();
  const vessels = useVessels();
  const { pushToast } = useNotifications();
  if (params.id !== 'new') return <EnvironmentDetailView id={params.id} />;
  return (
    <DetailBody testID="environment-create">
      <Stack.Screen options={{ title: t('Create Environment') }} />
      <FormSheet
        testID="environment-form"
        open
        title={t('Create Environment')}
        initial={newEnvironmentValues({ vesselId: params.vesselId, kind: params.kind, name: params.name })}
        fields={() => environmentFields(t, vessels)}
        submitLabel={t('Create Environment')}
        onClose={() => router.back()}
        onSubmit={async (values) => {
          const created = await createEnvironment(environmentPayload(values, []));
          pushToast('success', t('Environment "{{name}}" created.', { name: created.name }));
          router.replace(`/environments/${created.id}` as Href);
        }}
      />
    </DetailBody>
  );
}
