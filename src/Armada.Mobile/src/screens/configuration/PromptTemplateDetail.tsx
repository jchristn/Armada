import { Stack, useLocalSearchParams, useRouter, type Href } from 'expo-router';
import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { createPromptTemplate, getPromptTemplate, resetPromptTemplate, updatePromptTemplate } from '@dashboard/api/client';
import type { PromptTemplate } from '@dashboard/types/models';
import { PROMPT_TEMPLATE_CATEGORIES, PROMPT_TEMPLATE_PARAMETER_GROUPS } from '@dashboard/lib/configuration';
import { buildPromptTemplateDuplicatePayload } from '@dashboard/lib/duplicates';
import { canEdit } from '@dashboard/lib/scoping';
import { ActionBar, DetailBody, DetailHeader, DetailPending, Field, FieldCard, JsonSheet, TextBlock } from '../../components/resource/DetailParts';
import { FormSheet, str, type FormField, type FormValues } from '../../components/resource/FormSheet';
import { useConfirm } from '../../components/resource/ResourceRow';
import { resourceStyles } from '../../components/resource/styles';
import { AppText } from '../../components/ui/AppText';
import { Button } from '../../components/ui/Button';
import { Disclosure } from '../../components/ui/Disclosure';
import { StatusBadge } from '../../components/ui/StatusBadge';
import type { Translate } from '../../i18n/LocaleContext';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { param } from '../../resource/links';
import { errorText, useLoad, useReloadOnFocus } from '../../resource/useLoad';
import { spacing } from '../../theme/typography';
import { promptTemplatePath, scopeBadge, useViewer } from './common';

/** The parameter reference (the dashboard's side panel; on mobile a collapsible list whose names can be copied). */
export function PromptParameters() {
  const { t } = useLocale();
  return (
    <FieldCard title={t('Parameters')} testID="template-parameters">
      {PROMPT_TEMPLATE_PARAMETER_GROUPS.map((group) => (
        <View key={group.label} style={styles.group}>
          <Disclosure title={t(group.label)}>
            {group.params.map((p) => (
              <View key={p.name} style={styles.param}>
                <AppText variant="mono" selectable>{p.name}</AppText>
                <AppText variant="caption" muted>{t(p.description)}</AppText>
              </View>
            ))}
          </Disclosure>
        </View>
      ))}
    </FieldCard>
  );
}

function createFields(t: Translate): FormField[] {
  return [
    { kind: 'text', key: 'name', label: t('Name'), required: true },
    { kind: 'select', key: 'category', label: t('Category'), options: PROMPT_TEMPLATE_CATEGORIES.map((c) => ({ value: c, label: t(c) })) },
    { kind: 'text', key: 'description', label: t('Description') },
    { kind: 'multiline', key: 'content', label: t('Content'), required: true },
  ];
}

export interface PromptTemplateDetailViewProps {
  name: string;
  embedded?: boolean;
  onChanged?: () => void;
}

/**
 * One prompt template (the dashboard's /prompt-templates/:name): content, description, category, and the parameter
 * reference; Edit (content and description), Reset to Default (built-in), Duplicate, and View JSON. Editing follows
 * the scope rules.
 */
export function PromptTemplateDetailView({ name, embedded, onChanged }: PromptTemplateDetailViewProps) {
  const { t, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();
  const router = useRouter();
  const viewer = useViewer();
  const { confirm, dialog } = useConfirm('template-confirm');
  const [editing, setEditing] = useState(false);
  const [jsonOpen, setJsonOpen] = useState(false);
  const { data: template, loading, refreshing, error, reload, refresh, setData } = useLoad(() => getPromptTemplate(name), [name], { fallbackError: t('Failed to load prompt template.') });
  useReloadOnFocus(reload);

  if (!template) return <DetailPending loading={loading} error={error} onRetry={() => void reload()} />;
  const x: PromptTemplate = template;
  const editable = canEdit(viewer, x);

  function reset() {
    confirm({
      title: t('Reset to Default'),
      message: t('Reset "{{name}}" to its built-in default content? Your customizations will be lost.', { name: x.name }),
      confirmLabel: t('Reset to Default'),
      danger: true,
      onConfirm: async () => {
        try {
          setData(await resetPromptTemplate(x.name));
          pushToast('success', t('Template reset to default.'));
          onChanged?.();
        } catch {
          pushToast('error', t('Reset failed.'));
        }
      },
    });
  }

  async function duplicate() {
    try {
      const created = await createPromptTemplate(buildPromptTemplateDuplicatePayload(x));
      pushToast('success', t('Template "{{name}}" duplicated.', { name: created.name }));
      onChanged?.();
      router.push(promptTemplatePath(created.name) as Href);
    } catch (err: unknown) {
      pushToast('error', errorText(err, t('Duplicate failed.')));
    }
  }

  return (
    <DetailBody embedded={embedded} refreshing={refreshing} onRefresh={() => void refresh()} testID="template-detail">
      {!embedded ? <Stack.Screen options={{ title: x.name }} /> : null}
      <DetailHeader
        title={x.name}
        subtitle={x.id}
        testID="template-title"
        badges={(
          <>
            <StatusBadge label={t(x.category)} tone="info" />
            <StatusBadge label={x.isBuiltIn ? t('Built-in') : t('Custom')} tone={x.isBuiltIn ? 'info' : 'pending'} />
            <StatusBadge {...scopeBadge(t, x.scope)} />
          </>
        )}
      />
      <ActionBar>
        {editable ? <Button label={t('Edit')} icon="create-outline" style={resourceStyles.action} onPress={() => setEditing(true)} testID="template-edit" /> : null}
        {x.isBuiltIn && editable ? <Button label={t('Reset to Default')} variant="secondary" style={resourceStyles.action} onPress={reset} testID="template-reset" /> : null}
        <Button label={t('Duplicate')} variant="secondary" style={resourceStyles.action} onPress={() => void duplicate()} />
        <Button label={t('View JSON')} variant="ghost" style={resourceStyles.action} onPress={() => setJsonOpen(true)} />
      </ActionBar>
      <FieldCard>
        <Field label={t('Description')} value={x.description} />
        <Field label={t('Category')} value={t(x.category)} />
        <Field label={t('Active')} value={x.active ? t('Yes') : t('No')} />
        <Field label={t('Created')} value={formatDateTime(x.createdUtc)} />
        <Field label={t('Last Updated')} value={formatDateTime(x.lastUpdateUtc)} />
      </FieldCard>
      <TextBlock title={t('Content')} text={x.content} mono testID="template-content" />
      <PromptParameters />
      <FormSheet
        testID="template-form"
        open={editing}
        title={t('Edit')}
        initial={{ description: x.description ?? '', content: x.content }}
        fields={() => [
          { kind: 'text', key: 'description', label: t('Description') },
          { kind: 'multiline', key: 'content', label: t('Content') },
        ]}
        submitLabel={t('Save')}
        onClose={() => setEditing(false)}
        onSubmit={async (v) => {
          const updated = await updatePromptTemplate(x.name, { content: str(v, 'content'), description: str(v, 'description').trim() || undefined });
          setData(updated);
          setEditing(false);
          pushToast('success', t('Template saved.'));
          onChanged?.();
        }}
      />
      <JsonSheet open={jsonOpen} title={`${t('Template')}: ${x.name}`} data={x} onClose={() => setJsonOpen(false)} />
      {dialog}
    </DetailBody>
  );
}

/** Values check of the create form (the dashboard's messages). */
export function validateTemplate(t: Translate, v: FormValues): string | null {
  if (!str(v, 'name').trim()) return t('Template name is required.');
  if (!str(v, 'category').trim()) return t('Template category is required.');
  if (!str(v, 'content').trim()) return t('Template content is required.');
  return null;
}

/** /prompt-templates/create: the create form; saving opens the new template. */
export function PromptTemplateCreateRoute() {
  const { t } = useLocale();
  const router = useRouter();
  const { pushToast } = useNotifications();
  return (
    <DetailBody testID="template-create">
      <Stack.Screen options={{ title: t('Create Prompt Template') }} />
      <PromptParameters />
      <FormSheet
        testID="template-form"
        open
        title={t('Create Prompt Template')}
        initial={{ name: '', category: 'mission', description: '', content: '' }}
        fields={() => createFields(t)}
        validate={(v) => validateTemplate(t, v)}
        submitLabel={t('Create')}
        onClose={() => router.back()}
        onSubmit={async (v) => {
          try {
            const result = await createPromptTemplate({
              name: str(v, 'name').trim(),
              category: str(v, 'category').trim(),
              content: str(v, 'content'),
              description: str(v, 'description').trim() || undefined,
              active: true,
            });
            pushToast('success', t('Template "{{name}}" created.', { name: result.name }));
            router.replace(promptTemplatePath(result.name) as Href);
          } catch (err: unknown) {
            throw new Error(errorText(err, t('Create failed.')));
          }
        }}
      />
    </DetailBody>
  );
}

/** The /prompt-templates/:name route. */
export function PromptTemplateDetailRoute() {
  const params = useLocalSearchParams<{ name: string }>();
  return <PromptTemplateDetailView name={param(params.name)} />;
}

const styles = StyleSheet.create({
  group: { paddingHorizontal: spacing.lg, paddingVertical: spacing.xs },
  param: { paddingVertical: spacing.xs, gap: 2 },
});
