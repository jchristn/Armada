import { useMemo, useState } from 'react';
import { createIncident, deleteIncident, listIncidents } from '@dashboard/api/client';
import type { Incident } from '@dashboard/types/models';
import { INCIDENT_SEVERITIES, INCIDENT_STATUSES } from '@dashboard/lib/deliveryForms';
import { useAuth } from '../../auth/AuthContext';
import { JsonSheet } from '../../components/resource/DetailParts';
import { FormSheet } from '../../components/resource/FormSheet';
import { MasterDetail, useSelection } from '../../components/resource/Hub';
import { ResourceList, StatRow } from '../../components/resource/ResourceList';
import { ResourceRow, useConfirm } from '../../components/resource/ResourceRow';
import { resourceStyles } from '../../components/resource/styles';
import { Button } from '../../components/ui/Button';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { ALL, useVessels, valueOptions } from '../../resource/lookups';
import { statusBadge } from '../../resource/status';
import { errorText, useLoad, useReloadOnFocus } from '../../resource/useLoad';
import { useDeployments, useEnvironments, useReleases } from './deliveryLookups';
import { IncidentDetailView } from './IncidentDetail';
import { incidentCreatePayload, incidentFields, newIncidentValues } from './incidentForm';

/**
 * Delivery > Incidents: incidents with summary counts, search, status / severity filters, create for tenant admins,
 * and row actions (open, view JSON, delete). Live: reloads on incident.* events.
 */
export function IncidentsTab() {
  const { t, formatRelativeTime } = useLocale();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { pushToast } = useNotifications();
  const canManage = isAdmin || isTenantAdmin;
  const vessels = useVessels();
  const environments = useEnvironments();
  const deployments = useDeployments();
  const releases = useReleases();
  const { confirm, dialog } = useConfirm('incident-confirm');
  const selection = useSelection((id) => `/incidents/${id}`);

  const { data, loading, refreshing, error, reload, refresh } = useLoad(async () => (await listIncidents(ALL)).objects ?? [], [], { live: ['incident.'], fallbackError: t('Failed to load incidents.') });
  useReloadOnFocus(reload);
  const incidents = useMemo(() => data ?? [], [data]);

  const [search, setSearch] = useState('');
  const [status, setStatus] = useState('all');
  const [severity, setSeverity] = useState('all');
  const [creating, setCreating] = useState(false);
  const [json, setJson] = useState<Incident | null>(null);

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    return incidents.filter((i) => (!term || [i.title, i.summary, i.environmentName, i.impact, i.id].some((v) => (v ?? '').toLowerCase().includes(term)))
      && (status === 'all' || i.status === status)
      && (severity === 'all' || i.severity === severity));
  }, [incidents, search, status, severity]);

  const envName = (i: Incident) => (i.environmentId ? (environments.find((e) => e.id === i.environmentId)?.name || i.environmentName || i.environmentId) : i.environmentName);

  function remove(i: Incident) {
    confirm({
      title: t('Delete Incident'),
      message: t('Delete "{{title}}"? This removes the incident snapshot chain but does not affect deployments, checks, or releases.', { title: i.title }),
      confirmLabel: t('Delete'),
      danger: true,
      onConfirm: async () => {
        try {
          await deleteIncident(i.id);
          pushToast('warning', t('Incident "{{title}}" deleted.', { title: i.title }));
          if (selection.selected === i.id) selection.clear();
          await reload();
        } catch (err: unknown) {
          pushToast('error', errorText(err, t('Delete failed.')));
        }
      },
    });
  }

  const list = (
    <ResourceList
      testID="incidents"
      items={filtered}
      keyOf={(i) => i.id}
      loading={loading}
      error={error}
      onRetry={() => void reload()}
      refreshing={refreshing}
      onRefresh={() => void refresh()}
      search={{ value: search, onChange: setSearch, placeholder: t('Search by title, summary, impact, environment, or ID...') }}
      filters={[
        { key: 'status', label: t('Status'), value: status, onChange: setStatus, options: [{ value: 'all', label: t('All statuses') }, ...valueOptions(INCIDENT_STATUSES, t)] },
        { key: 'severity', label: t('Severity'), value: severity, onChange: setSeverity, options: [{ value: 'all', label: t('All severities') }, ...valueOptions(INCIDENT_SEVERITIES, t)] },
      ]}
      header={(
        <>
          <StatRow stats={[
            { label: t('Total Incidents'), value: incidents.length },
            { label: t('Open'), value: incidents.filter((i) => i.status === 'Open').length, tone: 'danger' },
            { label: t('Monitoring'), value: incidents.filter((i) => i.status === 'Monitoring').length },
            { label: t('Mitigated'), value: incidents.filter((i) => i.status === 'Mitigated').length },
            { label: t('Closed / Rolled Back'), value: incidents.filter((i) => i.status === 'Closed' || i.status === 'RolledBack').length },
          ]} />
          {canManage ? <Button label={t('Create Incident')} icon="add" onPress={() => setCreating(true)} style={resourceStyles.create} testID="incidents-create" /> : null}
        </>
      )}
      emptyTitle={t('No incidents match the current filters.')}
      emptyMessage={canManage ? t('Create incidents from deployments or environments to preserve hotfix and rollback context.') : t('Ask a tenant administrator to create and manage incident records.')}
      renderItem={(i) => (
        <ResourceRow
          testID={`incident-row-${i.id}`}
          title={i.title}
          subtitle={[t(i.severity), envName(i), i.summary || i.impact].filter(Boolean).join(' \u2022 ')}
          badge={statusBadge(t, i.status)}
          meta={formatRelativeTime(i.lastUpdateUtc)}
          selected={selection.selected === i.id}
          onPress={() => selection.open(i.id)}
          actions={[
            { key: 'json', label: t('View JSON'), icon: 'code-outline' as const, onPress: () => setJson(i) },
            ...(canManage ? [{ key: 'delete', label: t('Delete'), icon: 'trash-outline' as const, tone: 'danger' as const, onPress: () => remove(i) }] : []),
          ]}
        />
      )}
    />
  );

  return (
    <>
      <MasterDetail
        list={list}
        detail={selection.selected ? <IncidentDetailView key={selection.selected} id={selection.selected} embedded onDeleted={() => { selection.clear(); void reload(); }} onChanged={() => void reload()} /> : null}
      />
      <FormSheet
        testID="incident-form"
        open={creating}
        title={t('Create Incident')}
        initial={newIncidentValues()}
        fields={() => incidentFields(t, { vessels, environments, deployments, releases }, false)}
        submitLabel={t('Create Incident')}
        onClose={() => setCreating(false)}
        onSubmit={async (values) => {
          const created = await createIncident(incidentCreatePayload(values, environments));
          pushToast('success', t('Incident "{{title}}" created.', { title: created.title }));
          setCreating(false);
          await reload();
        }}
      />
      <JsonSheet open={json !== null} title={json?.title ?? ''} data={json} onClose={() => setJson(null)} />
      {dialog}
    </>
  );
}
