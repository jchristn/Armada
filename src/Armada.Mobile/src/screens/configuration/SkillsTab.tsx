import { useMemo, useState } from 'react';
import { createSkill, deleteSkill, listSkills, updateSkill } from '@dashboard/api/client';
import type { Skill } from '@dashboard/types/models';
import { canEdit } from '@dashboard/lib/scoping';
import { useAuth } from '../../auth/AuthContext';
import { FormSheet } from '../../components/resource/FormSheet';
import { MasterDetail, useSelection } from '../../components/resource/Hub';
import { ResourceList, StatRow } from '../../components/resource/ResourceList';
import { ResourceRow, useConfirm } from '../../components/resource/ResourceRow';
import { resourceStyles } from '../../components/resource/styles';
import { Button } from '../../components/ui/Button';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { ALL } from '../../resource/lookups';
import { errorText, useLoad, useReloadOnFocus } from '../../resource/useLoad';
import { scopeBadge, useViewer } from './common';
import { SkillDetailView } from './SkillDetail';
import { skillFields, skillPayload, skillValues } from './simpleForms';

/**
 * Configuration > Skills: reusable capability snippets attached to projects, with category filters. Anyone may create
 * their own; edit and delete follow the scope rules.
 */
export function SkillsTab() {
  const { t, formatRelativeTime } = useLocale();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { pushToast } = useNotifications();
  const viewer = useViewer();
  const canManage = isAdmin || isTenantAdmin;
  const { confirm, dialog } = useConfirm('skill-confirm');
  const selection = useSelection((id) => `/skills/${id}`);
  const { data, loading, refreshing, error, reload, refresh } = useLoad(async () => (await listSkills(ALL)).objects ?? [], [], { fallbackError: t('Failed to load skills.') });
  useReloadOnFocus(reload);
  const skills = useMemo(() => data ?? [], [data]);
  const categories = useMemo(() => Array.from(new Set(skills.map((s) => s.category).filter((c): c is string => !!c))).sort(), [skills]);
  const [search, setSearch] = useState('');
  const [category, setCategory] = useState('all');
  const [editing, setEditing] = useState<Skill | 'new' | null>(null);

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    return skills.filter((s) => (!term || [s.name, s.description, s.id].some((v) => (v ?? '').toLowerCase().includes(term)))
      && (category === 'all' || s.category === category));
  }, [skills, search, category]);

  function remove(s: Skill) {
    confirm({
      title: t('Delete Skill'),
      message: t('Delete "{{name}}"? Project profiles referencing it will simply skip it.', { name: s.name }),
      confirmLabel: t('Delete'),
      danger: true,
      onConfirm: async () => {
        try {
          await deleteSkill(s.id);
          pushToast('warning', t('Skill "{{name}}" deleted.', { name: s.name }));
          if (selection.selected === s.id) selection.clear();
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
      testID="skills"
      items={filtered}
      keyOf={(s) => s.id}
      loading={loading}
      error={error}
      onRetry={() => void reload()}
      refreshing={refreshing}
      onRefresh={() => void refresh()}
      search={{ value: search, onChange: setSearch, placeholder: t('Search by name, description, or ID...') }}
      filters={[{ key: 'category', label: t('Category'), value: category, onChange: setCategory, options: [{ value: 'all', label: t('All categories') }, ...categories.map((c) => ({ value: c, label: c }))] }]}
      header={(
        <>
          <StatRow stats={[
            { label: t('Total Skills'), value: skills.length },
            { label: t('Active'), value: skills.filter((s) => s.active).length },
            { label: t('Inactive'), value: skills.filter((s) => !s.active).length },
            { label: t('Categories'), value: categories.length },
          ]} />
          <Button label={t('Skill')} icon="add" onPress={() => setEditing('new')} style={resourceStyles.create} testID="skills-create" />
        </>
      )}
      emptyTitle={t('No skills match the current filters.')}
      emptyMessage={canManage ? t('Create a skill to capture a reusable habit that can be attached to projects.') : t('Ask a tenant administrator to define skills.')}
      renderItem={(s) => {
        const editable = canEdit(viewer, s);
        return (
          <ResourceRow
            testID={`skill-row-${s.id}`}
            title={s.name}
            subtitle={[s.category, s.description].filter(Boolean).join(' \u2022 ')}
            badge={{ label: s.active ? t('Active') : t('Inactive'), tone: s.active ? 'success' : 'cancelled' }}
            meta={`${scopeBadge(t, s.scope).label} \u2022 ${formatRelativeTime(s.lastUpdateUtc)}`}
            selected={selection.selected === s.id}
            onPress={() => selection.open(s.id)}
            actions={editable ? [
              { key: 'edit', label: t('Edit'), icon: 'create-outline', onPress: () => setEditing(s) },
              { key: 'delete', label: t('Delete'), icon: 'trash-outline', tone: 'danger', onPress: () => remove(s) },
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
        detail={selection.selected ? <SkillDetailView key={selection.selected} id={selection.selected} embedded onDeleted={() => { selection.clear(); void reload(); }} onChanged={() => void reload()} /> : null}
        onBack={selection.clear}
      />
      <FormSheet
        testID="skill-form"
        open={editing !== null}
        title={existing ? t('Edit Skill') : t('Create Skill')}
        initial={skillValues(viewer, existing)}
        fields={() => skillFields(t, viewer)}
        submitLabel={existing ? t('Save Changes') : t('Create Skill')}
        onClose={() => setEditing(null)}
        onSubmit={async (v) => {
          if (existing) {
            const updated = await updateSkill(existing.id, skillPayload(viewer, v, existing));
            pushToast('success', t('Skill "{{name}}" saved.', { name: updated.name }));
          } else {
            const created = await createSkill(skillPayload(viewer, v, null));
            pushToast('success', t('Skill "{{name}}" created.', { name: created.name }));
          }
          setEditing(null);
          await reload();
        }}
      />
      {dialog}
    </>
  );
}
