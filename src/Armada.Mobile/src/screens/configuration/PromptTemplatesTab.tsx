import { useRouter, type Href } from 'expo-router';
import { useMemo, useState } from 'react';
import { createPromptTemplate, listPromptTemplates, resetPromptTemplate } from '@dashboard/api/client';
import type { PromptTemplate } from '@dashboard/types/models';
import { PROMPT_TEMPLATE_CATEGORIES } from '@dashboard/lib/configuration';
import { buildPromptTemplateDuplicatePayload } from '@dashboard/lib/duplicates';
import { canEdit } from '@dashboard/lib/scoping';
import { MasterDetail, useSelection } from '../../components/resource/Hub';
import { ResourceList, StatRow } from '../../components/resource/ResourceList';
import { ResourceRow, useConfirm } from '../../components/resource/ResourceRow';
import { resourceStyles } from '../../components/resource/styles';
import { Button } from '../../components/ui/Button';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { ALL } from '../../resource/lookups';
import { errorText, useLoad, useReloadOnFocus } from '../../resource/useLoad';
import { promptTemplatePath, useViewer } from './common';
import { PromptTemplateDetailView } from './PromptTemplateDetail';

/**
 * Configuration > Prompts: the prompt templates captains and missions are built from, filtered by category. Opening a
 * template edits it; built-in templates can be reset to their default; New Prompt Template opens the create form.
 */
export function PromptTemplatesTab() {
  const { t, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const router = useRouter();
  const viewer = useViewer();
  const { confirm, dialog } = useConfirm('template-confirm');
  const selection = useSelection(promptTemplatePath);
  const { data, loading, refreshing, error, reload, refresh } = useLoad(async () => (await listPromptTemplates(ALL)).objects ?? [], [], { fallbackError: t('Failed to load prompt templates.') });
  useReloadOnFocus(reload);
  const templates = useMemo(() => [...(data ?? [])].sort((a, b) => a.category.localeCompare(b.category) || a.name.localeCompare(b.name)), [data]);
  const [search, setSearch] = useState('');
  const [category, setCategory] = useState('all');

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    return templates.filter((x) => (!term || [x.name, x.description, x.id].some((v) => (v ?? '').toLowerCase().includes(term)))
      && (category === 'all' || x.category.toLowerCase() === category.toLowerCase()));
  }, [templates, search, category]);

  function reset(x: PromptTemplate) {
    confirm({
      title: t('Reset to Default'),
      message: t('Reset template "{{name}}" to its built-in default content? Any custom edits will be lost.', { name: x.name }),
      confirmLabel: t('Reset to Default'),
      danger: true,
      onConfirm: async () => {
        try {
          await resetPromptTemplate(x.name);
          pushToast('success', t('Template "{{name}}" reset to default.', { name: x.name }));
          await reload();
        } catch {
          pushToast('error', t('Reset failed.'));
        }
      },
    });
  }

  async function duplicate(x: PromptTemplate) {
    try {
      const created = await createPromptTemplate(buildPromptTemplateDuplicatePayload(x));
      pushToast('success', t('Template "{{name}}" duplicated.', { name: created.name }));
      await reload();
      selection.open(created.name);
    } catch (err: unknown) {
      pushToast('error', errorText(err, t('Duplicate failed.')));
    }
  }

  const list = (
    <ResourceList
      testID="prompt-templates"
      items={filtered}
      keyOf={(x) => x.id}
      loading={loading}
      error={error}
      onRetry={() => void reload()}
      refreshing={refreshing}
      onRefresh={() => void refresh()}
      search={{ value: search, onChange: setSearch, placeholder: t('Search...') }}
      filters={[{ key: 'category', label: t('Category'), value: category, onChange: setCategory, options: [{ value: 'all', label: t('All') }, ...PROMPT_TEMPLATE_CATEGORIES.map((c) => ({ value: c, label: t(c) }))] }]}
      header={(
        <>
          <StatRow stats={[
            { label: t('Prompt Templates'), value: templates.length },
            { label: t('Built-in'), value: templates.filter((x) => x.isBuiltIn).length },
          ]} />
          <Button label={t('Prompt Template')} icon="add" onPress={() => router.push('/prompt-templates/create' as Href)} style={resourceStyles.create} testID="prompt-templates-create" />
        </>
      )}
      emptyTitle={templates.length > 0 ? t('No prompt templates match the current filters.') : t('No prompt templates found.')}
      renderItem={(x) => (
        <ResourceRow
          testID={`template-row-${x.name}`}
          title={x.name}
          subtitle={[x.description, `${x.content.length} ${t('characters')}`].filter(Boolean).join(' \u2022 ')}
          badge={{ label: t(x.category), tone: 'info' }}
          meta={`${x.isBuiltIn ? t('Built-in') : t('Custom')} \u2022 ${formatRelativeTime(x.lastUpdateUtc)}`}
          selected={selection.selected === x.name}
          onPress={() => selection.open(x.name)}
          actions={[
            { key: 'duplicate', label: t('Duplicate'), icon: 'copy-outline', onPress: () => void duplicate(x) },
            ...(x.isBuiltIn && canEdit(viewer, x) ? [{ key: 'reset', label: t('Reset to Default'), icon: 'refresh-outline' as const, tone: 'danger' as const, onPress: () => reset(x) }] : []),
          ]}
        />
      )}
    />
  );

  return (
    <>
      <MasterDetail
        list={list}
        detail={selection.selected ? <PromptTemplateDetailView key={selection.selected} name={selection.selected} embedded onChanged={() => void reload()} /> : null}
        onBack={selection.clear}
      />
      {dialog}
    </>
  );
}
