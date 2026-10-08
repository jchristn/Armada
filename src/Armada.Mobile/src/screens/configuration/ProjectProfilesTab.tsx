import { useMemo, useState } from 'react';
import {
  createProjectProfile, deleteProjectProfile, listProjectProfiles, resolveProjectProfileForVessel, updateProjectProfile,
} from '@dashboard/api/client';
import type { ProjectProfile } from '@dashboard/types/models';
import { useAuth } from '../../auth/AuthContext';
import { FormSheet, str } from '../../components/resource/FormSheet';
import { MasterDetail, useSelection } from '../../components/resource/Hub';
import { ResourceList, StatRow } from '../../components/resource/ResourceList';
import { ResourceRow, useConfirm } from '../../components/resource/ResourceRow';
import { resourceStyles } from '../../components/resource/styles';
import { Button } from '../../components/ui/Button';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { ALL } from '../../resource/lookups';
import { errorText, useLoad, useReloadOnFocus } from '../../resource/useLoad';
import { canEditProfile, scopeBadge, useViewer } from './common';
import { ProjectProfileDetailView } from './ProjectProfileDetail';
import { blankProjectProfile, projectFields, projectPayload, projectValues } from './profileForms';
import { useProjectReferences } from './references';

/**
 * Configuration > Project Profiles: per-project pipeline, workflow profile, persona overrides, and skills, resolved
 * global to fleet to vessel. Create, edit, and delete follow the scope rules; Resolve for a vessel opens the profile
 * that applies to it.
 */
export function ProjectProfilesTab() {
  const { t, formatRelativeTime } = useLocale();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { pushToast } = useNotifications();
  const viewer = useViewer();
  const canManage = isAdmin || isTenantAdmin;
  const refs = useProjectReferences();
  const { confirm, dialog } = useConfirm('project-confirm');
  const selection = useSelection((id) => `/project-profiles/${id}`);
  const { data, loading, refreshing, error, reload, refresh } = useLoad(async () => (await listProjectProfiles(ALL)).objects ?? [], [], { fallbackError: t('Failed to load project profiles.') });
  useReloadOnFocus(reload);
  const profiles = useMemo(() => data ?? [], [data]);
  const [search, setSearch] = useState('');
  const [scope, setScope] = useState('all');
  const [status, setStatus] = useState('all');
  const [editing, setEditing] = useState<ProjectProfile | 'new' | null>(null);
  const [resolving, setResolving] = useState(false);

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    return profiles.filter((p) => (!term || [p.name, p.description, p.id].some((v) => (v ?? '').toLowerCase().includes(term)))
      && (scope === 'all' || p.scope === scope)
      && (status === 'all' || (status === 'active' ? p.active : !p.active)));
  }, [profiles, search, scope, status]);

  function remove(p: ProjectProfile) {
    confirm({
      title: t('Delete Project Profile'),
      message: t('Delete "{{name}}"? Projects using it will fall back to their fleet or global profile.', { name: p.name }),
      confirmLabel: t('Delete'),
      danger: true,
      onConfirm: async () => {
        try {
          await deleteProjectProfile(p.id);
          pushToast('warning', t('Project profile "{{name}}" deleted.', { name: p.name }));
          if (selection.selected === p.id) selection.clear();
          await reload();
        } catch (err: unknown) {
          pushToast('error', errorText(err, t('Delete failed.')));
        }
      },
    });
  }

  const existing = editing && editing !== 'new' ? editing : null;
  const list = (
    <ResourceList
      testID="project-profiles"
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
            { label: t('Persona Overrides'), value: profiles.reduce((n, p) => n + (p.personaOverrides?.length || 0), 0) },
          ]} />
          <Button label={t('Project Profile')} icon="add" onPress={() => setEditing('new')} style={resourceStyles.create} testID="project-profiles-create" />
          <Button label={t('Resolve for vessel')} variant="secondary" icon="git-branch-outline" onPress={() => setResolving(true)} style={resourceStyles.create} testID="project-profiles-resolve" />
        </>
      )}
      emptyTitle={t('No project profiles match the current filters.')}
      emptyMessage={canManage ? t('Create a project profile to customize personas, pipeline, and skills for a project.') : t('Ask a tenant administrator to define project profiles.')}
      renderItem={(p) => {
        const editable = canEditProfile(viewer, p);
        return (
          <ResourceRow
            testID={`project-row-${p.id}`}
            title={p.name}
            subtitle={`${t(p.scope)}${p.isDefault ? ` \u2022 ${t('Default')}` : ''} \u2022 ${p.personaOverrides?.length || 0} ${t('personas')} \u2022 ${p.skills?.length || 0} ${t('skills')}`}
            badge={{ label: p.active ? t('Active') : t('Inactive'), tone: p.active ? 'success' : 'cancelled' }}
            meta={`${scopeBadge(t, p.ownershipScope).label} \u2022 ${formatRelativeTime(p.lastUpdateUtc)}`}
            selected={selection.selected === p.id}
            onPress={() => selection.open(p.id)}
            actions={editable ? [
              { key: 'edit', label: t('Edit'), icon: 'create-outline', onPress: () => setEditing(p) },
              { key: 'delete', label: t('Delete'), icon: 'trash-outline', tone: 'danger', onPress: () => remove(p) },
            ] : []}
          />
        );
      }}
    />
  );

  return (
    <>
      <MasterDetail
        list={list}
        detail={selection.selected ? <ProjectProfileDetailView key={selection.selected} id={selection.selected} embedded onDeleted={() => { selection.clear(); void reload(); }} onChanged={() => void reload()} /> : null}
      />
      <FormSheet
        testID="project-form"
        open={editing !== null}
        title={existing ? t('Edit Project Profile') : t('Create Project Profile')}
        initial={projectValues(existing ?? blankProjectProfile(viewer))}
        fields={(v) => projectFields(t, viewer, v, refs.fleets, refs.vessels, refs.pipelines, refs.workflowProfiles)}
        submitLabel={existing ? t('Save Changes') : t('Create Project Profile')}
        onClose={() => setEditing(null)}
        onSubmit={async (v) => {
          if (existing) {
            const updated = await updateProjectProfile(existing.id, projectPayload(viewer, v, existing.personaOverrides || [], existing.ownershipScope));
            pushToast('success', t('Project profile "{{name}}" saved.', { name: updated.name }));
          } else {
            const created = await createProjectProfile(projectPayload(viewer, v, [], null));
            pushToast('success', t('Project profile "{{name}}" created.', { name: created.name }));
          }
          setEditing(null);
          await reload();
        }}
      />
      <FormSheet
        testID="project-resolve-form"
        open={resolving}
        title={t('Resolve for vessel')}
        initial={{ vesselId: '' }}
        fields={() => [{ kind: 'select', key: 'vesselId', label: t('Vessel'), required: true, placeholder: t('Select a vessel...'), options: refs.vessels.map((x) => ({ value: x.id, label: x.name })) }]}
        submitLabel={t('Open')}
        onClose={() => setResolving(false)}
        onSubmit={async (v) => {
          const resolved = await resolveProjectProfileForVessel(str(v, 'vesselId'));
          setResolving(false);
          if (resolved?.profile?.id) {
            pushToast('info', `${t('Resolution')}: ${t(resolved.mode)}`);
            selection.open(resolved.profile.id);
          } else {
            pushToast('info', t('No project profile resolves for this vessel.'));
          }
        }}
      />
      {dialog}
    </>
  );
}
