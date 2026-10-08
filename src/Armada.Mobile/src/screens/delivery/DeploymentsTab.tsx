import { useMemo, useState } from 'react';
import { createDeployment, deleteDeployment, listDeployments, updateDeployment } from '@dashboard/api/client';
import type { Deployment } from '@dashboard/types/models';
import { DEPLOYMENT_STATUSES, VERIFICATION_STATUSES } from '@dashboard/lib/deliveryForms';
import { useAuth } from '../../auth/AuthContext';
import { FormSheet } from '../../components/resource/FormSheet';
import { MasterDetail, useSelection } from '../../components/resource/Hub';
import { JsonSheet } from '../../components/resource/DetailParts';
import { ResourceList, StatRow } from '../../components/resource/ResourceList';
import { ResourceRow, useConfirm } from '../../components/resource/ResourceRow';
import { resourceStyles } from '../../components/resource/styles';
import { Button } from '../../components/ui/Button';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { ALL, useNameMap, useVessels, valueOptions } from '../../resource/lookups';
import { statusBadge } from '../../resource/status';
import { errorText, useLoad, useReloadOnFocus } from '../../resource/useLoad';
import { DeploymentDetailView } from './DeploymentDetail';
import { useEnvironments, useReleases, useWorkflowProfiles } from './deliveryLookups';
import { deploymentFields, deploymentPayload, deploymentValues, linkDeploymentValues, newDeploymentValues } from './deploymentForm';

/**
 * Delivery > Deployments: deployment records with summary counts, search, status / verification / vessel filters,
 * create and edit for tenant admins, row actions (open, edit, view JSON, delete), and live updates.
 */
export function DeploymentsTab() {
  const { t, formatRelativeTime } = useLocale();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { pushToast } = useNotifications();
  const canManage = isAdmin || isTenantAdmin;
  const vessels = useVessels();
  const vesselNames = useNameMap(vessels);
  const environments = useEnvironments();
  const releases = useReleases();
  const profiles = useWorkflowProfiles();
  const { confirm, dialog } = useConfirm('deployment-confirm');
  const selection = useSelection((id) => `/deployments/${id}`);

  const { data, loading, refreshing, error, reload, refresh } = useLoad(async () => (await listDeployments(ALL)).objects ?? [], [], { live: ['deployment.'], fallbackError: t('Failed to load deployments.') });
  useReloadOnFocus(reload);
  const deployments = useMemo(() => data ?? [], [data]);

  const [search, setSearch] = useState('');
  const [status, setStatus] = useState('all');
  const [verification, setVerification] = useState('all');
  const [vessel, setVessel] = useState('all');
  const [editing, setEditing] = useState<Deployment | 'new' | null>(null);
  const [json, setJson] = useState<Deployment | null>(null);

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    return deployments.filter((d) => (!term || [d.title, d.environmentName, d.summary, d.sourceRef, d.id].some((v) => (v ?? '').toLowerCase().includes(term)))
      && (status === 'all' || d.status === status)
      && (verification === 'all' || d.verificationStatus === verification)
      && (vessel === 'all' || d.vesselId === vessel));
  }, [deployments, search, status, verification, vessel]);

  function remove(d: Deployment) {
    confirm({
      title: t('Delete Deployment'),
      message: t('Delete "{{title}}"? This removes only the deployment record and leaves linked checks, releases, and environments intact.', { title: d.title }),
      confirmLabel: t('Delete'),
      danger: true,
      onConfirm: async () => {
        try {
          await deleteDeployment(d.id);
          pushToast('warning', t('Deployment "{{title}}" deleted.', { title: d.title }));
          if (selection.selected === d.id) selection.clear();
          await reload();
        } catch (err: unknown) {
          pushToast('error', errorText(err, t('Delete failed.')));
        }
      },
    });
  }

  const failedCount = deployments.filter((d) => d.status === 'Failed' || d.status === 'VerificationFailed').length;
  const list = (
    <ResourceList
      testID="deployments"
      items={filtered}
      keyOf={(d) => d.id}
      loading={loading}
      error={error}
      onRetry={() => void reload()}
      refreshing={refreshing}
      onRefresh={() => void refresh()}
      search={{ value: search, onChange: setSearch, placeholder: t('Search by title, environment, source ref, summary, or ID...') }}
      filters={[
        { key: 'status', label: t('Status'), value: status, onChange: setStatus, options: [{ value: 'all', label: t('All statuses') }, ...valueOptions(DEPLOYMENT_STATUSES, t)] },
        { key: 'verification', label: t('Verification'), value: verification, onChange: setVerification, options: [{ value: 'all', label: t('All verification states') }, ...valueOptions(VERIFICATION_STATUSES, t)] },
        { key: 'vessel', label: t('Vessel'), value: vessel, onChange: setVessel, options: [{ value: 'all', label: t('All vessels') }, ...vessels.map((v) => ({ value: v.id, label: v.name }))] },
      ]}
      header={(
        <>
          <StatRow stats={[
            { label: t('Total Deployments'), value: deployments.length },
            { label: t('Pending Approval'), value: deployments.filter((d) => d.status === 'PendingApproval').length, tone: 'warning' },
            { label: t('Running'), value: deployments.filter((d) => d.status === 'Running').length },
            { label: t('Succeeded'), value: deployments.filter((d) => d.status === 'Succeeded').length, tone: 'success' },
            { label: t('Failed / Verification Failed'), value: failedCount, tone: failedCount > 0 ? 'danger' : undefined },
          ]} />
          {canManage ? <Button label={t('Create Deployment')} icon="add" onPress={() => setEditing('new')} style={resourceStyles.create} testID="deployments-create" /> : null}
        </>
      )}
      emptyTitle={t('No deployments match the current filters.')}
      emptyMessage={canManage ? t('Create a deployment from an environment or release to track approval, execution, verification, and rollback in one record.') : t('Ask a tenant administrator to create and manage deployment records.')}
      renderItem={(d) => (
        <ResourceRow
          testID={`deployment-row-${d.id}`}
          title={d.title}
          subtitle={[d.environmentName, d.vesselId ? (vesselNames.get(d.vesselId) || d.vesselId) : null, d.sourceRef, t(d.verificationStatus)].filter(Boolean).join(' \u2022 ')}
          badge={statusBadge(t, d.status)}
          meta={formatRelativeTime(d.lastUpdateUtc)}
          selected={selection.selected === d.id}
          onPress={() => selection.open(d.id)}
          actions={[
            ...(canManage ? [{ key: 'edit', label: t('Edit'), icon: 'create-outline' as const, onPress: () => setEditing(d) }] : []),
            { key: 'json', label: t('View JSON'), icon: 'code-outline' as const, onPress: () => setJson(d) },
            ...(canManage ? [{ key: 'delete', label: t('Delete'), icon: 'trash-outline' as const, tone: 'danger' as const, onPress: () => remove(d) }] : []),
          ]}
        />
      )}
    />
  );

  return (
    <>
      <MasterDetail
        list={list}
        detail={selection.selected ? <DeploymentDetailView key={selection.selected} id={selection.selected} embedded onDeleted={() => { selection.clear(); void reload(); }} onChanged={() => void reload()} /> : null}
      />
      <FormSheet
        testID="deployment-form"
        open={editing !== null}
        title={editing === 'new' ? t('Create Deployment') : t('Edit Deployment')}
        initial={editing && editing !== 'new' ? deploymentValues(editing) : newDeploymentValues()}
        fields={(v) => deploymentFields(t, v, { vessels, profiles, environments, releases })}
        onChange={(next, prev) => linkDeploymentValues(next, prev, environments, releases)}
        submitLabel={editing === 'new' ? t('Create Deployment') : t('Save Changes')}
        onClose={() => setEditing(null)}
        onSubmit={async (values) => {
          if (editing && editing !== 'new') {
            const updated = await updateDeployment(editing.id, deploymentPayload(values));
            pushToast('success', t('Deployment "{{title}}" saved.', { title: updated.title }));
          } else {
            const created = await createDeployment(deploymentPayload(values));
            pushToast('success', t('Deployment "{{title}}" created.', { title: created.title }));
          }
          setEditing(null);
          await reload();
        }}
      />
      <JsonSheet open={json !== null} title={json?.title ?? ''} data={json} onClose={() => setJson(null)} />
      {dialog}
    </>
  );
}
