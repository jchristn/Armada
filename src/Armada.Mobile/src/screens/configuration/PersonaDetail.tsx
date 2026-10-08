import { Stack, useLocalSearchParams, useRouter, type Href } from 'expo-router';
import { useState } from 'react';
import {
  createPersona, deletePersona, getPersona, getPromptTemplate, listCaptains, listPromptTemplates, resetPromptTemplate, updatePersona, updatePromptTemplate,
} from '@dashboard/api/client';
import type { Captain, Persona, PromptTemplate } from '@dashboard/types/models';
import { buildPersonaDuplicatePayload } from '@dashboard/lib/duplicates';
import { canEdit } from '@dashboard/lib/scoping';
import { ActionBar, DetailBody, DetailHeader, DetailPending, Field, FieldCard, JsonSheet, TextBlock } from '../../components/resource/DetailParts';
import { FormSheet, str } from '../../components/resource/FormSheet';
import { useConfirm } from '../../components/resource/ResourceRow';
import { resourceStyles } from '../../components/resource/styles';
import { AppText } from '../../components/ui/AppText';
import { Button } from '../../components/ui/Button';
import { StatusBadge } from '../../components/ui/StatusBadge';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { param } from '../../resource/links';
import { ALL, useReference } from '../../resource/lookups';
import { errorText, useLoad, useReloadOnFocus } from '../../resource/useLoad';
import { personaPath, scopeBadge, templateOptions, useViewer } from './common';

interface PersonaData {
  persona: Persona;
  prompt: PromptTemplate | null;
  promptError: string;
}

export interface PersonaDetailViewProps {
  name: string;
  embedded?: boolean;
  onDeleted?: () => void;
  onChanged?: () => void;
}

/**
 * One persona (the dashboard's /personas/:name): its fields, default captain, and the backing prompt template, which
 * can be edited (and reset when built-in) right here. Edit / Duplicate / Delete follow the scope rules; built-in
 * personas cannot be deleted.
 */
export function PersonaDetailView({ name, embedded, onDeleted, onChanged }: PersonaDetailViewProps) {
  const { t, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();
  const router = useRouter();
  const viewer = useViewer();
  const { confirm, dialog } = useConfirm('persona-confirm');
  const captains = useReference<Captain>(() => listCaptains(ALL));
  const templateNames = useReference(() => listPromptTemplates(ALL)).map((x) => x.name);
  const [editing, setEditing] = useState(false);
  const [promptEditing, setPromptEditing] = useState(false);
  const [jsonOpen, setJsonOpen] = useState(false);

  const { data, loading, refreshing, error, reload, refresh, setData } = useLoad<PersonaData>(async () => {
    const persona = await getPersona(name);
    if (!persona.promptTemplateName) return { persona, prompt: null, promptError: '' };
    try {
      return { persona, prompt: await getPromptTemplate(persona.promptTemplateName), promptError: '' };
    } catch (err: unknown) {
      return { persona, prompt: null, promptError: errorText(err, t('Failed to load backing prompt template.')) };
    }
  }, [name], { fallbackError: t('Failed to load persona.') });
  useReloadOnFocus(reload);

  if (!data) return <DetailPending loading={loading} error={error} onRetry={() => void reload()} />;
  const { persona: p, prompt, promptError } = data;
  const editable = canEdit(viewer, p);
  const captainName = p.defaultCaptainId ? (captains.find((c) => c.id === p.defaultCaptainId)?.name ?? p.defaultCaptainId) : t('None (default routing)');

  function remove() {
    if (p.isBuiltIn) {
      pushToast('error', t('Built-in personas cannot be deleted.'));
      return;
    }
    confirm({
      title: t('Delete Persona'),
      message: t('Delete persona "{{name}}"? This cannot be undone.', { name: p.name }),
      confirmLabel: t('Delete'),
      danger: true,
      onConfirm: async () => {
        try {
          await deletePersona(p.name);
          pushToast('warning', t('Persona "{{name}}" deleted.', { name: p.name }));
          if (onDeleted) onDeleted(); else router.back();
        } catch (err: unknown) {
          pushToast('error', errorText(err, t('Delete failed.')));
        }
      },
    });
  }

  async function duplicate() {
    try {
      const created = await createPersona(buildPersonaDuplicatePayload(p));
      pushToast('success', t('Persona "{{name}}" duplicated.', { name: created.name }));
      onChanged?.();
      router.push(personaPath(created.name) as Href);
    } catch (err: unknown) {
      pushToast('error', errorText(err, t('Duplicate failed.')));
    }
  }

  function resetPrompt() {
    if (!prompt?.isBuiltIn) return;
    confirm({
      title: t('Reset Backing Prompt'),
      message: t('Reset prompt template "{{name}}" to its built-in default content? Your customizations will be lost.', { name: prompt.name }),
      confirmLabel: t('Reset'),
      danger: true,
      onConfirm: async () => {
        try {
          const result = await resetPromptTemplate(prompt.name);
          setData({ persona: p, prompt: result, promptError: '' });
          pushToast('success', t('Prompt template "{{name}}" reset to default.', { name: result.name }));
        } catch (err: unknown) {
          pushToast('error', errorText(err, t('Prompt reset failed.')));
        }
      },
    });
  }

  return (
    <DetailBody embedded={embedded} refreshing={refreshing} onRefresh={() => void refresh()} testID="persona-detail">
      {!embedded ? <Stack.Screen options={{ title: p.name }} /> : null}
      <DetailHeader
        title={p.name}
        subtitle={p.id}
        testID="persona-title"
        badges={(
          <>
            <StatusBadge label={p.active ? t('Active') : t('Inactive')} tone={p.active ? 'success' : 'cancelled'} />
            {p.isBuiltIn ? <StatusBadge label={t('Built-in')} tone="info" /> : null}
            <StatusBadge {...scopeBadge(t, p.scope)} />
          </>
        )}
      />
      <ActionBar>
        {editable ? <Button label={t('Edit')} icon="create-outline" style={resourceStyles.action} onPress={() => setEditing(true)} testID="persona-edit" /> : null}
        <Button label={t('Duplicate')} variant="secondary" style={resourceStyles.action} onPress={() => void duplicate()} />
        {p.promptTemplateName ? <Button label={t('Open Backing Prompt')} variant="secondary" style={resourceStyles.action} onPress={() => router.push(`/prompt-templates/${encodeURIComponent(p.promptTemplateName)}` as Href)} /> : null}
        <Button label={t('View JSON')} variant="ghost" style={resourceStyles.action} onPress={() => setJsonOpen(true)} />
        {editable ? <Button label={t('Delete')} variant="danger" style={resourceStyles.action} onPress={remove} testID="persona-delete" /> : null}
      </ActionBar>

      <FieldCard>
        <Field label={t('Description')} value={p.description} testID="persona-description" />
        <Field label={t('Prompt Template Name')} value={p.promptTemplateName} onPress={p.promptTemplateName ? () => router.push(`/prompt-templates/${encodeURIComponent(p.promptTemplateName)}` as Href) : undefined} />
        <Field label={t('Default Captain')} value={captainName} onPress={p.defaultCaptainId ? () => router.push(`/captains/${p.defaultCaptainId}` as Href) : undefined} />
        <Field label={t('Built-in')} value={p.isBuiltIn ? t('Yes') : t('No')} />
        <Field label={t('Created')} value={formatDateTime(p.createdUtc)} />
        <Field label={t('Last Updated')} value={formatDateTime(p.lastUpdateUtc)} />
      </FieldCard>

      <FieldCard title={t('Backing Prompt')} testID="persona-prompt">
        <AppText variant="caption" muted style={resourceStyles.pad}>{t('This is the prompt template Armada resolves when this persona is assigned to a mission. Edit it here to change future mission instructions.')}</AppText>
        {prompt ? (
          <>
            <Field label={t('Template Name')} value={prompt.name} />
            <Field label={t('Template Type')} value={prompt.isBuiltIn ? t('Built-in') : t('Custom')} />
            <Field label={t('Template Description')} value={prompt.description} />
          </>
        ) : <AppText color={promptError ? 'danger' : undefined} muted={!promptError} style={resourceStyles.pad}>{promptError || t('No prompt template is linked to this persona.')}</AppText>}
      </FieldCard>
      {prompt ? <TextBlock title={t('Prompt Content')} text={prompt.content} mono /> : null}
      {prompt ? (
        <ActionBar>
          <Button label={`${t('Edit')}: ${t('Backing Prompt')}`} variant="secondary" icon="create-outline" style={resourceStyles.action} onPress={() => setPromptEditing(true)} testID="persona-prompt-edit" />
          {prompt.isBuiltIn ? <Button label={t('Reset to Default')} variant="ghost" style={resourceStyles.action} onPress={resetPrompt} /> : null}
        </ActionBar>
      ) : null}

      <FormSheet
        testID="persona-form"
        open={editing}
        title={t('Edit Persona')}
        initial={{ description: p.description ?? '', promptTemplateName: p.promptTemplateName ?? '', defaultCaptainId: p.defaultCaptainId ?? '' }}
        fields={() => [
          { kind: 'multiline', key: 'description', label: t('Description') },
          { kind: 'select', key: 'promptTemplateName', label: t('Prompt Template Name'), required: true, placeholder: t('Select a template...'), options: templateOptions(t, templateNames) },
          {
            kind: 'select', key: 'defaultCaptainId', label: t('Default Captain'),
            hint: t('Pre-fills the per-step captain at dispatch and becomes the preferred captain for missions of this persona.'),
            options: [{ value: '', label: t('None (default routing)') }, ...captains.map((c) => ({ value: c.id, label: c.name }))],
          },
        ]}
        validate={(v) => (str(v, 'promptTemplateName') ? null : t('{{field}} is required.', { field: t('Prompt Template Name') }))}
        submitLabel={t('Save')}
        onClose={() => setEditing(false)}
        onSubmit={async (v) => {
          await updatePersona(p.name, { description: str(v, 'description'), promptTemplateName: str(v, 'promptTemplateName'), defaultCaptainId: str(v, 'defaultCaptainId') || null });
          setEditing(false);
          pushToast('success', t('Persona "{{name}}" saved.', { name: p.name }));
          onChanged?.();
          await reload();
        }}
      />
      <FormSheet
        testID="persona-prompt-form"
        open={promptEditing}
        title={t('Backing Prompt')}
        initial={{ description: prompt?.description ?? '', content: prompt?.content ?? '' }}
        fields={() => [
          { kind: 'text', key: 'description', label: t('Template Description'), placeholder: t('Optional prompt template description...') },
          { kind: 'multiline', key: 'content', label: t('Prompt Content') },
        ]}
        submitLabel={t('Save Prompt')}
        onClose={() => setPromptEditing(false)}
        onSubmit={async (v) => {
          if (!prompt) return;
          try {
            const result = await updatePromptTemplate(prompt.name, { content: str(v, 'content'), description: str(v, 'description').trim() || undefined });
            setData({ persona: p, prompt: result, promptError: '' });
            setPromptEditing(false);
            pushToast('success', t('Prompt template "{{name}}" saved.', { name: result.name }));
          } catch (err: unknown) {
            throw new Error(errorText(err, t('Prompt save failed.')));
          }
        }}
      />
      <JsonSheet open={jsonOpen} title={t('Persona: {{name}}', { name: p.name })} data={p} onClose={() => setJsonOpen(false)} />
      {dialog}
    </DetailBody>
  );
}

/** The /personas/:name route. */
export function PersonaDetailRoute() {
  const params = useLocalSearchParams<{ name: string }>();
  return <PersonaDetailView name={param(params.name)} />;
}
