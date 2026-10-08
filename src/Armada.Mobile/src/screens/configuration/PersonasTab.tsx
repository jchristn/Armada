import { useRouter, type Href } from 'expo-router';
import { useMemo, useState } from 'react';
import { createPersona, deletePersona, listPersonas, listPromptTemplates, updatePersona } from '@dashboard/api/client';
import type { Persona } from '@dashboard/types/models';
import { buildPersonaDuplicatePayload } from '@dashboard/lib/duplicates';
import { canEdit, resolveCreateScope } from '@dashboard/lib/scoping';
import { FormSheet, str, type FormValues } from '../../components/resource/FormSheet';
import { MasterDetail, useSelection } from '../../components/resource/Hub';
import { ResourceList, StatRow } from '../../components/resource/ResourceList';
import { ResourceRow, useConfirm } from '../../components/resource/ResourceRow';
import { resourceStyles } from '../../components/resource/styles';
import { Button } from '../../components/ui/Button';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { ALL, useReference } from '../../resource/lookups';
import { errorText, useLoad, useReloadOnFocus } from '../../resource/useLoad';
import { personaPath, scopeBadge, scopeField, scopeValue, templateOptions, useViewer } from './common';
import { PersonaDetailView } from './PersonaDetail';

/**
 * Configuration > Personas: named captain behaviors with their backing prompt template. Create for everyone (personal
 * scope for regular users); edit and delete where the scope rules allow; built-in personas cannot be deleted.
 */
export function PersonasTab() {
  const { t, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const router = useRouter();
  const viewer = useViewer();
  const { confirm, dialog } = useConfirm('persona-confirm');
  const selection = useSelection(personaPath);
  const templateNames = useReference(() => listPromptTemplates(ALL)).map((x) => x.name);

  const { data, loading, refreshing, error, reload, refresh } = useLoad(async () => (await listPersonas(ALL)).objects ?? [], [], { fallbackError: t('Failed to load personas.') });
  useReloadOnFocus(reload);
  const personas = useMemo(() => [...(data ?? [])].sort((a, b) => a.name.localeCompare(b.name)), [data]);
  const [search, setSearch] = useState('');
  const [state, setState] = useState('all');
  const [editing, setEditing] = useState<Persona | 'new' | null>(null);

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    return personas.filter((p) => (!term || [p.name, p.description, p.promptTemplateName, p.id].some((v) => (v ?? '').toLowerCase().includes(term)))
      && (state === 'all' || (state === 'active' ? p.active : !p.active)));
  }, [personas, search, state]);

  function remove(p: Persona) {
    confirm({
      title: t('Delete Persona'),
      message: t('Delete persona "{{name}}"? This cannot be undone.', { name: p.name }),
      confirmLabel: t('Delete'),
      danger: true,
      onConfirm: async () => {
        try {
          await deletePersona(p.name);
          pushToast('warning', t('Persona "{{name}}" deleted.', { name: p.name }));
          if (selection.selected === p.name) selection.clear();
          await reload();
        } catch (err: unknown) {
          pushToast('error', errorText(err, t('Delete failed.')));
        }
      },
    });
  }

  async function duplicate(p: Persona) {
    try {
      const created = await createPersona(buildPersonaDuplicatePayload(p));
      pushToast('success', t('Persona "{{name}}" duplicated.', { name: created.name }));
      await reload();
      selection.open(created.name);
    } catch (err: unknown) {
      pushToast('error', errorText(err, t('Duplicate failed.')));
    }
  }

  const initial: FormValues = editing && editing !== 'new'
    ? { name: editing.name, description: editing.description ?? '', promptTemplateName: editing.promptTemplateName, scope: editing.scope }
    : { name: '', description: '', promptTemplateName: '', scope: resolveCreateScope(viewer) };

  const list = (
    <ResourceList
      testID="personas"
      items={filtered}
      keyOf={(p) => p.id}
      loading={loading}
      error={error}
      onRetry={() => void reload()}
      refreshing={refreshing}
      onRefresh={() => void refresh()}
      search={{ value: search, onChange: setSearch, placeholder: t('Search...') }}
      filters={[{ key: 'state', label: t('Active'), value: state, onChange: setState, options: [{ value: 'all', label: t('All statuses') }, { value: 'active', label: t('Active') }, { value: 'inactive', label: t('Inactive') }] }]}
      header={(
        <>
          <StatRow stats={[
            { label: t('Personas'), value: personas.length },
            { label: t('Built-in'), value: personas.filter((p) => p.isBuiltIn).length },
            { label: t('Active'), value: personas.filter((p) => p.active).length },
          ]} />
          <Button label={t('Persona')} icon="add" onPress={() => setEditing('new')} style={resourceStyles.create} testID="personas-create" />
        </>
      )}
      emptyTitle={personas.length > 0 ? t('No personas match the current filters.') : t('No personas configured.')}
      renderItem={(p) => {
        const editable = canEdit(viewer, p);
        return (
          <ResourceRow
            testID={`persona-row-${p.name}`}
            title={p.name}
            subtitle={[p.promptTemplateName, p.description].filter(Boolean).join(' \u2022 ')}
            badge={p.isBuiltIn ? { label: t('Built-in'), tone: 'info' } : scopeBadge(t, p.scope)}
            meta={`${p.active ? t('Active') : t('Inactive')} \u2022 ${formatRelativeTime(p.createdUtc)}`}
            selected={selection.selected === p.name}
            onPress={() => selection.open(p.name)}
            actions={[
              ...(editable ? [{ key: 'edit', label: t('Edit'), icon: 'create-outline' as const, onPress: () => setEditing(p) }] : []),
              { key: 'duplicate', label: t('Duplicate'), icon: 'copy-outline' as const, onPress: () => void duplicate(p) },
              { key: 'prompt', label: t('Edit Backing Prompt'), icon: 'document-text-outline' as const, onPress: () => router.push(`/prompt-templates/${encodeURIComponent(p.promptTemplateName)}` as Href) },
              ...(!p.isBuiltIn && editable ? [{ key: 'delete', label: t('Delete'), icon: 'trash-outline' as const, tone: 'danger' as const, onPress: () => remove(p) }] : []),
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
        detail={selection.selected ? <PersonaDetailView key={selection.selected} name={selection.selected} embedded onDeleted={() => { selection.clear(); void reload(); }} onChanged={() => void reload()} /> : null}
        onBack={selection.clear}
      />
      <FormSheet
        testID="persona-form"
        open={editing !== null}
        title={editing === 'new' ? t('Create Persona') : t('Edit Persona')}
        initial={initial}
        fields={() => [
          { kind: 'text', key: 'name', label: t('Name'), required: true, disabled: editing !== 'new' },
          { kind: 'multiline', key: 'description', label: t('Description'), placeholder: t('Optional description of this persona...') },
          { kind: 'select', key: 'promptTemplateName', label: t('Prompt Template Name'), required: true, placeholder: t('Select a template...'), options: templateOptions(t, templateNames) },
          scopeField(t, viewer),
        ]}
        validate={(v) => (str(v, 'promptTemplateName') ? null : t('{{field}} is required.', { field: t('Prompt Template Name') }))}
        submitLabel={t('Save')}
        onClose={() => setEditing(null)}
        onSubmit={async (v) => {
          const payload: Partial<Persona> = { name: str(v, 'name'), promptTemplateName: str(v, 'promptTemplateName'), scope: scopeValue(viewer, v, editing && editing !== 'new' ? editing.scope : null) };
          if (str(v, 'description')) payload.description = str(v, 'description');
          if (editing && editing !== 'new') await updatePersona(editing.name, payload);
          else await createPersona(payload);
          pushToast('success', editing && editing !== 'new' ? t('Persona "{{name}}" saved.', { name: editing.name }) : t('Persona "{{name}}" created.', { name: str(v, 'name') }));
          setEditing(null);
          await reload();
        }}
      />
      {dialog}
    </>
  );
}
