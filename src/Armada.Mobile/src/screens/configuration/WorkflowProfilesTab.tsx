import { useMemo, useState } from 'react';
import { createWorkflowProfile, deleteWorkflowProfile, listWorkflowProfiles, resolveWorkflowProfile, updateWorkflowProfile } from '@dashboard/api/client';
import type { WorkflowProfile } from '@dashboard/types/models';
import { countProfileCapabilities } from '@dashboard/lib/configuration';
import { buildWorkflowProfileDuplicatePayload } from '@dashboard/lib/duplicates';
import { useAuth } from '../../auth/AuthContext';
import { FormSheet, str } from '../../components/resource/FormSheet';
import { MasterDetail, useSelection } from '../../components/resource/Hub';
import { ResourceList, StatRow } from '../../components/resource/ResourceList';
import { ResourceRow, useConfirm } from '../../components/resource/ResourceRow';
import { resourceStyles } from '../../components/resource/styles';
import { Button } from '../../components/ui/Button';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { ALL, useFleets, useVessels } from '../../resource/lookups';
import { errorText, useLoad, useReloadOnFocus } from '../../resource/useLoad';
import { canEditProfile, scopeBadge, useViewer } from './common';
import { applyWorkflowValues, blankWorkflowProfile, workflowFields, workflowPayload, workflowValues } from './profileForms';
import { WorkflowProfileDetailView } from './WorkflowProfileDetail';

/**
 * Configuration > Workflow Profiles: how each project builds, tests, packages, releases, deploys, and verifies itself.
 * Create, edit, duplicate, and delete follow the scope rules; Resolve for a vessel opens the profile Armada would use.
 */
export function WorkflowProfilesTab() {
  const { t, formatRelativeTime } = useLocale();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { pushToast } = useNotifications();
  const viewer = useViewer();
  const canManage = isAdmin || isTenantAdmin;
  const fleets = useFleets();
  const vessels = useVessels();
  const { confirm, dialog } = useConfirm('workflow-confirm');
  const selection = useSelection((id) => `/workflow-profiles/${id}`);
  const { data, loading, refreshing, error, reload, refresh } = useLoad(async () => (await listWorkflowProfiles(ALL)).objects ?? [], [], { fallbackError: t('Failed to load workflow profiles.') });
  useReloadOnFocus(reload);
  const profiles = useMemo(() => data ?? [], [data]);
  const [search, setSearch] = useState('');
  const [scope, setScope] = useState('all');
  const [status, setStatus] = useState('all');
  const [editing, setEditing] = useState<WorkflowProfile | 'new' | null>(null);
  const [resolving, setResolving] = useState(false);

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    return profiles.filter((p) => (!term || [p.name, p.description, p.id].some((v) => (v ?? '').toLowerCase().includes(term)))
      && (scope === 'all' || p.scope === scope)
      && (status === 'all' || (status === 'active' ? p.active : !p.active)));
  }, [profiles, search, scope, status]);

  function remove(p: WorkflowProfile) {
    confirm({
      title: t('Delete Workflow Profile'),
      message: t('Delete "{{name}}"? Existing check runs remain, but this profile will no longer be available.', { name: p.name }),
      confirmLabel: t('Delete'),
      danger: true,
      onConfirm: async () => {
        try {
          await deleteWorkflowProfile(p.id);
          pushToast('warning', t('Workflow profile "{{name}}" deleted.', { name: p.name }));
          if (selection.selected === p.id) selection.clear();
          await reload();
        } catch (err: unknown) {
          pushToast('error', errorText(err, t('Delete failed.')));
        }
      },
    });
  }

  async function duplicate(p: WorkflowProfile) {
    try {
      const created = await createWorkflowProfile(buildWorkflowProfileDuplicatePayload(p));
      pushToast('success', t('Workflow profile "{{name}}" duplicated.', { name: created.name }));
      await reload();
      selection.open(created.id);
    } catch (err: unknown) {
      pushToast('error', errorText(err, t('Duplicate failed.')));
    }
  }

  const existing = editing && editing !== 'new' ? editing : null;
  const base = existing ?? blankWorkflowProfile(viewer);
  const list = (
    <ResourceList
      testID="workflow-profiles"
      items={filtered}
      keyOf={(p) => p.id}
      loading={loading}
      error={error}
      onRetry={() => void reload()}
      refreshing={refreshing}
      onRefresh={() => void refresh()}
      search={{ value: search, onChange: setSearch, placeholder: t('Search by name, description, or ID...') }}
      filters={[
        { key: 'scope', label: t('Scope'), value: scope, onChange: setScope, options: [{ value: 'all', label: t('All scopes') }, { value: 'Global', label: t('Global') }, { value: 'Fleet', label: t('Fleet') }, { value: 'Vessel', label: t('Vessel') }] },
        { key: 'status', label: t('Status'), value: status, onChange: setStatus, options: [{ value: 'all', label: t('All statuses') }, { value: 'active', label: t('Active only') }, { value: 'inactive', label: t('Inactive only') }] },
      ]}
      header={(
        <>
          <StatRow stats={[
            { label: t('Total Profiles'), value: profiles.length },
            { label: t('Active'), value: profiles.filter((p) => p.active).length },
            { label: t('Defaults'), value: profiles.filter((p) => p.isDefault).length },
            { label: t('Environment Targets'), value: profiles.reduce((n, p) => n + p.environments.length, 0) },
          ]} />
          <Button label={t('Workflow Profile')} icon="add" onPress={() => setEditing('new')} style={resourceStyles.create} testID="workflow-profiles-create" />
          <Button label={t('Resolve for vessel')} variant="secondary" icon="git-branch-outline" onPress={() => setResolving(true)} style={resourceStyles.create} testID="workflow-profiles-resolve" />
        </>
      )}
      emptyTitle={t('No workflow profiles match the current filters.')}
      emptyMessage={canManage ? t('Create a workflow profile to teach Armada how a project actually builds and ships.') : t('Ask a tenant administrator to define workflow profiles for shared build and deploy actions.')}
      renderItem={(p) => {
        const editable = canEditProfile(viewer, p);
        return (
          <ResourceRow
            testID={`workflow-row-${p.id}`}
            title={p.name}
            subtitle={`${t(p.scope)}${p.isDefault ? ` \u2022 ${t('Default')}` : ''} \u2022 ${countProfileCapabilities(p)} ${t('commands')} \u2022 ${p.environments.length} ${t('environments')}`}
            badge={{ label: p.active ? t('Active') : t('Inactive'), tone: p.active ? 'success' : 'cancelled' }}
            meta={`${scopeBadge(t, p.ownershipScope).label} \u2022 ${formatRelativeTime(p.lastUpdateUtc)}`}
            selected={selection.selected === p.id}
            onPress={() => selection.open(p.id)}
            actions={[
              ...(editable ? [{ key: 'edit', label: t('Edit'), icon: 'create-outline' as const, onPress: () => setEditing(p) }] : []),
              { key: 'duplicate', label: t('Duplicate'), icon: 'copy-outline' as const, onPress: () => void duplicate(p) },
              ...(editable ? [{ key: 'delete', label: t('Delete'), icon: 'trash-outline' as const, tone: 'danger' as const, onPress: () => remove(p) }] : []),
            ]}
          />
        );
      }}
    />
  );

  return (
    <>
      <MasterDetail
        list={list}
        detail={selection.selected ? <WorkflowProfileDetailView key={selection.selected} id={selection.selected} embedded onDeleted={() => { selection.clear(); void reload(); }} onChanged={() => void reload()} /> : null}
        onBack={selection.clear}
      />
      <FormSheet
        testID="workflow-form"
        open={editing !== null}
        title={existing ? t('Edit Workflow Profile') : t('Create Workflow Profile')}
        initial={workflowValues(base)}
        fields={(v) => workflowFields(t, viewer, v, fleets, vessels)}
        submitLabel={existing ? t('Save Changes') : t('Create Workflow Profile')}
        onClose={() => setEditing(null)}
        onSubmit={async (v) => {
          const payload = workflowPayload(applyWorkflowValues(viewer, base, v, !!existing));
          if (existing) {
            const updated = await updateWorkflowProfile(existing.id, payload);
            pushToast('success', t('Workflow profile "{{name}}" saved.', { name: updated.name }));
            setEditing(null);
            await reload();
          } else {
            const created = await createWorkflowProfile(payload);
            pushToast('success', t('Workflow profile "{{name}}" created.', { name: created.name }));
            setEditing(null);
            await reload();
            selection.open(created.id);
          }
        }}
      />
      <FormSheet
        testID="workflow-resolve-form"
        open={resolving}
        title={t('Resolve for vessel')}
        initial={{ vesselId: '' }}
        fields={() => [{ kind: 'select', key: 'vesselId', label: t('Vessel'), required: true, placeholder: t('Select a vessel...'), options: vessels.map((x) => ({ value: x.id, label: x.name })) }]}
        submitLabel={t('Open')}
        onClose={() => setResolving(false)}
        onSubmit={async (v) => {
          const resolved = await resolveWorkflowProfile(str(v, 'vesselId'));
          setResolving(false);
          if (resolved?.id) selection.open(resolved.id);
          else pushToast('info', t('No workflow profile resolves for this vessel.'));
        }}
      />
      {dialog}
    </>
  );
}
