import { useMemo, useState } from 'react';
import { createPipeline, deletePipeline, listPersonas, listPipelines, updatePipeline } from '@dashboard/api/client';
import type { Pipeline } from '@dashboard/types/models';
import { buildPipelineDuplicatePayload } from '@dashboard/lib/duplicates';
import { formatStages, splitList } from '@dashboard/lib/configuration';
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
import { pipelinePath, scopeBadge, scopeField, scopeValue, useViewer } from './common';
import { PipelineDetailView } from './PipelineDetail';
import { blankStage, stageEntries, stagesPayload } from './pipelineForm';

/**
 * Configuration > Pipelines: multi-stage workflows of personas. Create with the stage personas (one per line; review
 * gates and order are edited on the pipeline); edit name, description, and visibility; duplicate; delete non-built-in
 * pipelines where the scope rules allow.
 */
export function PipelinesTab() {
  const { t, formatRelativeTime } = useLocale();
  const { pushToast } = useNotifications();
  const viewer = useViewer();
  const { confirm, dialog } = useConfirm('pipeline-confirm');
  const selection = useSelection(pipelinePath);
  const personaNames = useReference(() => listPersonas(ALL)).map((p) => p.name);
  const { data, loading, refreshing, error, reload, refresh } = useLoad(async () => (await listPipelines(ALL)).objects ?? [], [], { fallbackError: t('Failed to load pipelines.') });
  useReloadOnFocus(reload);
  const pipelines = useMemo(() => [...(data ?? [])].sort((a, b) => a.name.localeCompare(b.name)), [data]);
  const [search, setSearch] = useState('');
  const [editing, setEditing] = useState<Pipeline | 'new' | null>(null);

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    return pipelines.filter((p) => !term || [p.name, p.description, p.id, formatStages(p.stages)].some((v) => (v ?? '').toLowerCase().includes(term)));
  }, [pipelines, search]);

  function remove(p: Pipeline) {
    confirm({
      title: t('Delete Pipeline'),
      message: t('Delete pipeline "{{name}}"? This cannot be undone.', { name: p.name }),
      confirmLabel: t('Delete'),
      danger: true,
      onConfirm: async () => {
        try {
          await deletePipeline(p.name);
          pushToast('warning', t('Pipeline "{{name}}" deleted.', { name: p.name }));
          if (selection.selected === p.name) selection.clear();
          await reload();
        } catch (err: unknown) {
          pushToast('error', errorText(err, t('Delete failed.')));
        }
      },
    });
  }

  async function duplicate(p: Pipeline) {
    try {
      const created = await createPipeline(buildPipelineDuplicatePayload(p));
      pushToast('success', t('Pipeline "{{name}}" duplicated.', { name: created.name }));
      await reload();
      selection.open(created.name);
    } catch (err: unknown) {
      pushToast('error', errorText(err, t('Duplicate failed.')));
    }
  }

  const isEdit = editing !== null && editing !== 'new';
  const initial: FormValues = isEdit
    ? { name: editing.name, description: editing.description ?? '', scope: editing.scope }
    : { name: '', description: '', stages: '', scope: resolveCreateScope(viewer) };

  const list = (
    <ResourceList
      testID="pipelines"
      items={filtered}
      keyOf={(p) => p.id}
      loading={loading}
      error={error}
      onRetry={() => void reload()}
      refreshing={refreshing}
      onRefresh={() => void refresh()}
      search={{ value: search, onChange: setSearch, placeholder: t('Search...') }}
      header={(
        <>
          <StatRow stats={[
            { label: t('Pipelines'), value: pipelines.length },
            { label: t('Built-in'), value: pipelines.filter((p) => p.isBuiltIn).length },
            { label: t('Active'), value: pipelines.filter((p) => p.active !== false).length },
          ]} />
          <Button label={t('Pipeline')} icon="add" onPress={() => setEditing('new')} style={resourceStyles.create} testID="pipelines-create" />
        </>
      )}
      emptyTitle={pipelines.length > 0 ? t('No pipelines match the current filters.') : t('No pipelines configured.')}
      renderItem={(p) => {
        const editable = canEdit(viewer, p);
        return (
          <ResourceRow
            testID={`pipeline-row-${p.name}`}
            title={p.name}
            subtitle={formatStages(p.stages)}
            badge={p.isBuiltIn ? { label: t('Built-in'), tone: 'info' } : scopeBadge(t, p.scope)}
            meta={`${p.active !== false ? t('Active') : t('Inactive')} \u2022 ${formatRelativeTime(p.createdUtc)}`}
            selected={selection.selected === p.name}
            onPress={() => selection.open(p.name)}
            actions={[
              ...(editable ? [{ key: 'edit', label: t('Edit'), icon: 'create-outline' as const, onPress: () => setEditing(p) }] : []),
              { key: 'duplicate', label: t('Duplicate'), icon: 'copy-outline' as const, onPress: () => void duplicate(p) },
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
        detail={selection.selected ? <PipelineDetailView key={selection.selected} name={selection.selected} embedded onDeleted={() => { selection.clear(); void reload(); }} onChanged={() => void reload()} /> : null}
        onBack={selection.clear}
      />
      <FormSheet
        testID="pipeline-form"
        open={editing !== null}
        title={isEdit ? t('Edit Pipeline') : t('Create Pipeline')}
        initial={initial}
        fields={() => [
          { kind: 'text', key: 'name', label: t('Name'), required: true },
          { kind: 'multiline', key: 'description', label: t('Description') },
          ...(isEdit ? [] : [{ kind: 'multiline' as const, key: 'stages', label: t('Stages'), hint: `${t('Persona Name')}: ${personaNames.join(', ')}`, placeholder: 'Architect\nWorker' }]),
          scopeField(t, viewer),
        ]}
        submitLabel={t('Save')}
        onClose={() => setEditing(null)}
        onSubmit={async (v) => {
          if (isEdit) {
            await updatePipeline(editing.name, { name: str(v, 'name'), description: str(v, 'description') || null, stages: stagesPayload(stageEntries(editing.stages)) as Pipeline['stages'], scope: scopeValue(viewer, v, editing.scope) });
            pushToast('success', t('Pipeline "{{name}}" saved.', { name: editing.name }));
          } else {
            const stages = splitList(str(v, 'stages')).map((personaName) => ({ ...blankStage(), personaName }));
            await createPipeline({ name: str(v, 'name'), description: str(v, 'description') || null, stages: stagesPayload(stages) as Pipeline['stages'], scope: scopeValue(viewer, v) });
            pushToast('success', t('Pipeline "{{name}}" created.', { name: str(v, 'name') }));
          }
          setEditing(null);
          await reload();
        }}
      />
      {dialog}
    </>
  );
}
