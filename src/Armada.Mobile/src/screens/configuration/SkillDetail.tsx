import { Stack, useLocalSearchParams, useRouter, type Href } from 'expo-router';
import { useState } from 'react';
import { createSkill, deleteSkill, getSkill, updateSkill } from '@dashboard/api/client';
import type { Skill } from '@dashboard/types/models';
import { canEdit } from '@dashboard/lib/scoping';
import { ActionBar, DetailBody, DetailHeader, DetailPending, Field, FieldCard, JsonSheet, TextBlock } from '../../components/resource/DetailParts';
import { FormSheet } from '../../components/resource/FormSheet';
import { useConfirm } from '../../components/resource/ResourceRow';
import { resourceStyles } from '../../components/resource/styles';
import { Banner } from '../../components/ui/Banner';
import { Button } from '../../components/ui/Button';
import { StatusBadge } from '../../components/ui/StatusBadge';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { param } from '../../resource/links';
import { errorText, useLoad, useReloadOnFocus } from '../../resource/useLoad';
import { scopeBadge, useViewer } from './common';
import { skillFields, skillPayload, skillValues } from './simpleForms';

export interface SkillDetailViewProps {
  id: string;
  embedded?: boolean;
  onDeleted?: () => void;
  onChanged?: () => void;
}

/** One skill (the dashboard's /skills/:id): fields, content, Edit, View JSON, and Delete. */
export function SkillDetailView({ id, embedded, onDeleted, onChanged }: SkillDetailViewProps) {
  const { t, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();
  const router = useRouter();
  const viewer = useViewer();
  const { confirm, dialog } = useConfirm('skill-confirm');
  const [editing, setEditing] = useState(false);
  const [jsonOpen, setJsonOpen] = useState(false);
  const { data: skill, loading, refreshing, error, reload, refresh, setData } = useLoad(() => getSkill(id), [id], { fallbackError: t('Failed to load skill.') });
  useReloadOnFocus(reload);

  if (!skill) return <DetailPending loading={loading} error={error} onRetry={() => void reload()} />;
  const p: Skill = skill;
  const canManage = canEdit(viewer, p);

  function remove() {
    confirm({
      title: t('Delete Skill'),
      message: t('Delete this skill? This cannot be undone.'),
      confirmLabel: t('Delete'),
      danger: true,
      onConfirm: async () => {
        try {
          await deleteSkill(p.id);
          pushToast('warning', t('Skill deleted.'));
          if (onDeleted) onDeleted(); else router.back();
        } catch (err: unknown) {
          pushToast('error', errorText(err, t('Delete failed.')));
        }
      },
    });
  }

  return (
    <DetailBody embedded={embedded} refreshing={refreshing} onRefresh={() => void refresh()} testID="skill-detail">
      {!embedded ? <Stack.Screen options={{ title: p.name }} /> : null}
      <DetailHeader
        title={p.name}
        subtitle={p.id}
        testID="skill-title"
        badges={(
          <>
            <StatusBadge label={p.active ? t('Active') : t('Inactive')} tone={p.active ? 'success' : 'cancelled'} />
            <StatusBadge {...scopeBadge(t, p.scope)} />
          </>
        )}
      />
      {!canManage ? <Banner tone="info" title={t('You can view this skill, but only tenant administrators can change it.')} /> : null}
      <ActionBar>
        {canManage ? <Button label={t('Edit')} icon="create-outline" style={resourceStyles.action} onPress={() => setEditing(true)} testID="skill-edit" /> : null}
        <Button label={t('View JSON')} variant="ghost" style={resourceStyles.action} onPress={() => setJsonOpen(true)} />
        {canManage ? <Button label={t('Delete')} variant="danger" style={resourceStyles.action} onPress={remove} /> : null}
      </ActionBar>
      <FieldCard>
        <Field label={t('Category')} value={p.category} />
        <Field label={t('Description')} value={p.description} />
        <Field label={t('Created')} value={formatDateTime(p.createdUtc)} />
        <Field label={t('Last Updated')} value={formatDateTime(p.lastUpdateUtc)} />
      </FieldCard>
      <TextBlock title={t('Content')} text={p.content} mono />
      <FormSheet
        testID="skill-form"
        open={editing}
        title={t('Edit Skill')}
        initial={skillValues(viewer, p)}
        fields={() => skillFields(t, viewer)}
        submitLabel={t('Save Changes')}
        onClose={() => setEditing(false)}
        onSubmit={async (v) => {
          const updated = await updateSkill(p.id, skillPayload(viewer, v, p));
          setData(updated);
          setEditing(false);
          pushToast('success', t('Skill "{{name}}" saved.', { name: updated.name }));
          onChanged?.();
        }}
      />
      <JsonSheet open={jsonOpen} title={p.name} data={p} onClose={() => setJsonOpen(false)} />
      {dialog}
    </DetailBody>
  );
}

/** The /skills/:id route; `new` is the create form. */
export function SkillDetailRoute() {
  const params = useLocalSearchParams<{ id: string }>();
  const { t } = useLocale();
  const router = useRouter();
  const viewer = useViewer();
  const { pushToast } = useNotifications();
  const id = param(params.id);
  if (id !== 'new') return <SkillDetailView id={id} />;
  return (
    <DetailBody testID="skill-create">
      <Stack.Screen options={{ title: t('Create Skill') }} />
      <FormSheet
        testID="skill-form"
        open
        title={t('Create Skill')}
        initial={skillValues(viewer, null)}
        fields={() => skillFields(t, viewer)}
        submitLabel={t('Create Skill')}
        onClose={() => router.back()}
        onSubmit={async (v) => {
          const created = await createSkill(skillPayload(viewer, v, null));
          pushToast('success', t('Skill "{{name}}" created.', { name: created.name }));
          router.replace(`/skills/${created.id}` as Href);
        }}
      />
    </DetailBody>
  );
}
