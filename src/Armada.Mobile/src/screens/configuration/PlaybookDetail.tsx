import { Stack, useLocalSearchParams, useRouter, type Href } from 'expo-router';
import { useState } from 'react';
import { createPlaybook, deletePlaybook, getPlaybook, updatePlaybook } from '@dashboard/api/client';
import type { Playbook } from '@dashboard/types/models';
import { buildPlaybookDuplicatePayload } from '@dashboard/lib/duplicates';
import { canEdit } from '@dashboard/lib/scoping';
import { ActionBar, DetailBody, DetailHeader, DetailPending, Field, FieldCard, JsonSheet, TextBlock } from '../../components/resource/DetailParts';
import { FormSheet } from '../../components/resource/FormSheet';
import { StatRow } from '../../components/resource/ResourceList';
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
import { playbookFields, playbookPayload, playbookStats, playbookValues } from './simpleForms';

export interface PlaybookDetailViewProps {
  id: string;
  embedded?: boolean;
  onDeleted?: () => void;
  onChanged?: () => void;
}

/** One playbook (the dashboard's /playbooks/:id): stats, the markdown, Edit, Duplicate, View JSON, and Delete. */
export function PlaybookDetailView({ id, embedded, onDeleted, onChanged }: PlaybookDetailViewProps) {
  const { t, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();
  const router = useRouter();
  const viewer = useViewer();
  const { confirm, dialog } = useConfirm('playbook-confirm');
  const [editing, setEditing] = useState(false);
  const [jsonOpen, setJsonOpen] = useState(false);
  const { data: playbook, loading, refreshing, error, reload, refresh, setData } = useLoad(() => getPlaybook(id), [id], { fallbackError: t('Failed to load playbook.') });
  useReloadOnFocus(reload);

  if (!playbook) return <DetailPending loading={loading} error={error} onRetry={() => void reload()} />;
  const p: Playbook = playbook;
  const canManage = canEdit(viewer, p);
  const stats = playbookStats(p.content);

  function remove() {
    confirm({
      title: t('Delete Playbook'),
      message: t('Delete "{{name}}"? Existing mission snapshots remain immutable, but future dispatches will no longer be able to select it.', { name: p.fileName }),
      confirmLabel: t('Delete'),
      danger: true,
      onConfirm: async () => {
        try {
          await deletePlaybook(p.id);
          pushToast('warning', t('Playbook "{{name}}" deleted.', { name: p.fileName }));
          if (onDeleted) onDeleted(); else router.back();
        } catch (err: unknown) {
          pushToast('error', errorText(err, t('Delete failed.')));
        }
      },
    });
  }

  async function duplicate() {
    try {
      const created = await createPlaybook(buildPlaybookDuplicatePayload(p));
      pushToast('success', t('Playbook "{{name}}" duplicated.', { name: created.fileName }));
      onChanged?.();
      router.push(`/playbooks/${created.id}` as Href);
    } catch (err: unknown) {
      pushToast('error', errorText(err, t('Duplicate failed.')));
    }
  }

  return (
    <DetailBody embedded={embedded} refreshing={refreshing} onRefresh={() => void refresh()} testID="playbook-detail">
      {!embedded ? <Stack.Screen options={{ title: p.fileName }} /> : null}
      <DetailHeader
        title={p.fileName}
        subtitle={p.id}
        testID="playbook-title"
        badges={(
          <>
            <StatusBadge label={p.active ? t('Active') : t('Inactive')} tone={p.active ? 'success' : 'cancelled'} />
            <StatusBadge {...scopeBadge(t, p.scope)} />
          </>
        )}
      />
      {!canManage ? <Banner tone="info" title={t('You can view this playbook, but only tenant administrators can change it.')} /> : null}
      <ActionBar>
        {canManage ? <Button label={t('Edit')} icon="create-outline" style={resourceStyles.action} onPress={() => setEditing(true)} testID="playbook-edit" /> : null}
        {canManage ? <Button label={t('Duplicate')} variant="secondary" style={resourceStyles.action} onPress={() => void duplicate()} /> : null}
        <Button label={t('View JSON')} variant="ghost" style={resourceStyles.action} onPress={() => setJsonOpen(true)} />
        {canManage ? <Button label={t('Delete')} variant="danger" style={resourceStyles.action} onPress={remove} /> : null}
      </ActionBar>
      <StatRow stats={[{ label: t('Characters'), value: stats.characters }, { label: t('Lines'), value: stats.lines }, { label: t('Headings'), value: stats.headings }]} />
      <FieldCard>
        <Field label={t('Description')} value={p.description} />
        <Field label={t('Created')} value={formatDateTime(p.createdUtc)} />
        <Field label={t('Last Updated')} value={formatDateTime(p.lastUpdateUtc)} />
      </FieldCard>
      <TextBlock title={t('Markdown Content')} text={p.content} mono />
      <FormSheet
        testID="playbook-form"
        open={editing}
        title={t('Edit Playbook')}
        initial={playbookValues(viewer, p)}
        fields={() => playbookFields(t, viewer)}
        submitLabel={t('Save Changes')}
        onClose={() => setEditing(false)}
        onSubmit={async (v) => {
          const updated = await updatePlaybook(p.id, playbookPayload(viewer, v, p));
          setData(updated);
          setEditing(false);
          pushToast('success', t('Playbook "{{name}}" saved.', { name: updated.fileName }));
          onChanged?.();
        }}
      />
      <JsonSheet open={jsonOpen} title={p.fileName} data={p} onClose={() => setJsonOpen(false)} />
      {dialog}
    </DetailBody>
  );
}

/** The /playbooks/:id route; `new` is the create form. */
export function PlaybookDetailRoute() {
  const params = useLocalSearchParams<{ id: string }>();
  const { t } = useLocale();
  const router = useRouter();
  const viewer = useViewer();
  const { pushToast } = useNotifications();
  const id = param(params.id);
  if (id !== 'new') return <PlaybookDetailView id={id} />;
  return (
    <DetailBody testID="playbook-create">
      <Stack.Screen options={{ title: t('Create Playbook') }} />
      <FormSheet
        testID="playbook-form"
        open
        title={t('Create Playbook')}
        initial={playbookValues(viewer, null)}
        fields={() => playbookFields(t, viewer)}
        submitLabel={t('Create Playbook')}
        onClose={() => router.back()}
        onSubmit={async (v) => {
          const created = await createPlaybook(playbookPayload(viewer, v, null));
          pushToast('success', t('Playbook "{{name}}" created.', { name: created.fileName }));
          router.replace(`/playbooks/${created.id}` as Href);
        }}
      />
    </DetailBody>
  );
}
