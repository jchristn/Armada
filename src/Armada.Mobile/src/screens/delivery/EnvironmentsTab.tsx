import { useMemo, useState } from 'react';
import { createEnvironment, deleteEnvironment, listEnvironments, updateEnvironment } from '@dashboard/api/client';
import type { DeploymentEnvironment } from '@dashboard/types/models';
import { ENVIRONMENT_KINDS } from '@dashboard/lib/environmentForm';
import { buildEnvironmentDuplicatePayload } from '@dashboard/lib/duplicates';
import { useAuth } from '../../auth/AuthContext';
import { FormSheet } from '../../components/resource/FormSheet';
import { MasterDetail, useSelection } from '../../components/resource/Hub';
import { ResourceList, StatRow } from '../../components/resource/ResourceList';
import { ResourceRow, useConfirm } from '../../components/resource/ResourceRow';
import { Button } from '../../components/ui/Button';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { ALL, useNameMap, useVessels, valueOptions } from '../../resource/lookups';
import { errorText, useLoad, useReloadOnFocus } from '../../resource/useLoad';
import { EnvironmentDetailView } from './EnvironmentDetail';
import { environmentFields, environmentPayload, environmentValues, newEnvironmentValues } from './environmentForm';
import { resourceStyles } from '../../components/resource/styles';

/**
 * Delivery > Environments: named deployment targets with summary counts, search, kind / vessel / state filters,
 * create, and row actions (open, edit, duplicate, delete) for tenant admins. Tablets show the selected environment
 * beside the list.
 */
export function EnvironmentsTab() {
  const { t, formatRelativeTime } = useLocale();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { pushToast } = useNotifications();
  const canManage = isAdmin || isTenantAdmin;
  const vessels = useVessels();
  const vesselNames = useNameMap(vessels);
  const { confirm, dialog } = useConfirm('environment-confirm');
  const selection = useSelection((id) => `/environments/${id}`);

  const { data, loading, refreshing, error, reload, refresh } = useLoad(async () => (await listEnvironments(ALL)).objects ?? [], [], { fallbackError: t('Failed to load environments.') });
  useReloadOnFocus(reload);
  const environments = useMemo(() => data ?? [], [data]);

  const [search, setSearch] = useState('');
  const [kind, setKind] = useState('all');
  const [vessel, setVessel] = useState('all');
  const [state, setState] = useState('all');
  const [editing, setEditing] = useState<DeploymentEnvironment | 'new' | null>(null);

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    return environments.filter((e) => {
      const matchesSearch = !term || [e.name, e.description, e.baseUrl, e.configurationSource, e.id].some((v) => (v ?? '').toLowerCase().includes(term));
      return matchesSearch
        && (kind === 'all' || e.kind === kind)
        && (vessel === 'all' || e.vesselId === vessel)
        && (state === 'all' || (state === 'active' ? e.active : !e.active));
    });
  }, [environments, search, kind, vessel, state]);

  function remove(e: DeploymentEnvironment) {
    confirm({
      title: t('Delete Environment'),
      message: t('Delete "{{name}}"? This removes only the environment record.', { name: e.name }),
      confirmLabel: t('Delete'),
      danger: true,
      onConfirm: async () => {
        try {
          await deleteEnvironment(e.id);
          pushToast('warning', t('Environment "{{name}}" deleted.', { name: e.name }));
          if (selection.selected === e.id) selection.clear();
          await reload();
        } catch (err: unknown) {
          pushToast('error', errorText(err, t('Delete failed.')));
        }
      },
    });
  }

  async function duplicate(e: DeploymentEnvironment) {
    try {
      const created = await createEnvironment(buildEnvironmentDuplicatePayload(e));
      pushToast('success', t('Environment "{{name}}" duplicated.', { name: created.name }));
      await reload();
      selection.open(created.id);
    } catch (err: unknown) {
      pushToast('error', errorText(err, t('Duplicate failed.')));
    }
  }

  const list = (
    <ResourceList
      testID="environments"
      items={filtered}
      keyOf={(e) => e.id}
      loading={loading}
      error={error}
      onRetry={() => void reload()}
      refreshing={refreshing}
      onRefresh={() => void refresh()}
      search={{ value: search, onChange: setSearch, placeholder: t('Search by name, description, base URL, configuration source, or ID...') }}
      filters={[
        { key: 'kind', label: t('Kind'), value: kind, onChange: setKind, options: [{ value: 'all', label: t('All kinds') }, ...valueOptions(ENVIRONMENT_KINDS)] },
        { key: 'vessel', label: t('Vessel'), value: vessel, onChange: setVessel, options: [{ value: 'all', label: t('All vessels') }, ...vessels.map((v) => ({ value: v.id, label: v.name }))] },
        { key: 'state', label: t('State'), value: state, onChange: setState, options: [{ value: 'all', label: t('All states') }, { value: 'active', label: t('Active') }, { value: 'inactive', label: t('Inactive') }] },
      ]}
      header={(
        <>
          <StatRow stats={[
            { label: t('Total Environments'), value: environments.length },
            { label: t('Active'), value: environments.filter((e) => e.active).length },
            { label: t('Default Targets'), value: environments.filter((e) => e.isDefault).length },
            { label: t('Require Approval'), value: environments.filter((e) => e.requiresApproval).length },
          ]} />
          {canManage ? <Button label={t('Create Environment')} icon="add" onPress={() => setEditing('new')} style={resourceStyles.create} testID="environments-create" /> : null}
        </>
      )}
      emptyTitle={t('No environments match the current filters.')}
      emptyMessage={canManage ? t('Create an environment to capture deployment metadata, URLs, and approval rules for a vessel.') : t('Ask a tenant administrator to create and manage environment records.')}
      renderItem={(e) => (
        <ResourceRow
          testID={`environment-row-${e.id}`}
          title={e.name}
          subtitle={[e.kind, e.vesselId ? (vesselNames.get(e.vesselId) || e.vesselId) : null, e.baseUrl].filter(Boolean).join(' • ')}
          badge={{ label: `${e.active ? t('Active') : t('Inactive')}${e.isDefault ? ` • ${t('Default target')}` : ''}`, tone: e.active ? 'success' : 'cancelled' }}
          meta={e.requiresApproval ? t('Approval required') : formatRelativeTime(e.lastUpdateUtc)}
          selected={selection.selected === e.id}
          onPress={() => selection.open(e.id)}
          actions={canManage ? [
            { key: 'edit', label: t('Edit'), icon: 'create-outline', onPress: () => setEditing(e) },
            { key: 'duplicate', label: t('Duplicate'), icon: 'copy-outline', onPress: () => void duplicate(e) },
            { key: 'delete', label: t('Delete'), icon: 'trash-outline', tone: 'danger', onPress: () => remove(e) },
          ] : []}
        />
      )}
    />
  );

  return (
    <>
      <MasterDetail
        list={list}
        detail={selection.selected ? <EnvironmentDetailView key={selection.selected} id={selection.selected} embedded onDeleted={() => { selection.clear(); void reload(); }} onChanged={() => void reload()} /> : null}
      />
      <FormSheet
        testID="environment-form"
        open={editing !== null}
        title={editing === 'new' ? t('Create Environment') : t('Edit Environment')}
        initial={editing && editing !== 'new' ? environmentValues(editing) : newEnvironmentValues()}
        fields={() => environmentFields(t, vessels)}
        submitLabel={editing === 'new' ? t('Create Environment') : t('Save Changes')}
        onClose={() => setEditing(null)}
        onSubmit={async (values) => {
          if (editing && editing !== 'new') {
            const updated = await updateEnvironment(editing.id, environmentPayload(values, editing.verificationDefinitions));
            pushToast('success', t('Environment "{{name}}" saved.', { name: updated.name }));
          } else {
            const created = await createEnvironment(environmentPayload(values, []));
            pushToast('success', t('Environment "{{name}}" created.', { name: created.name }));
          }
          setEditing(null);
          await reload();
        }}
      />
      {dialog}
    </>
  );
}
