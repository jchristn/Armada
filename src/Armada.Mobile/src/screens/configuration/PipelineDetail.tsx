import { Stack, useLocalSearchParams, useRouter, type Href } from 'expo-router';
import { useState } from 'react';
import { createPipeline, createVoyage, deletePipeline, getPipeline, listPersonas, updatePipeline } from '@dashboard/api/client';
import type { Pipeline } from '@dashboard/types/models';
import { buildPipelineDuplicatePayload } from '@dashboard/lib/duplicates';
import { canEdit } from '@dashboard/lib/scoping';
import { ActionBar, DetailBody, DetailHeader, DetailPending, Field, FieldCard, JsonSheet } from '../../components/resource/DetailParts';
import { FormSheet, str } from '../../components/resource/FormSheet';
import { ResourceRow, useConfirm } from '../../components/resource/ResourceRow';
import { resourceStyles } from '../../components/resource/styles';
import { AppText } from '../../components/ui/AppText';
import { Button } from '../../components/ui/Button';
import { StatusBadge } from '../../components/ui/StatusBadge';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { param } from '../../resource/links';
import { ALL, useReference, useVessels } from '../../resource/lookups';
import { errorText, useLoad, useReloadOnFocus } from '../../resource/useLoad';
import { scopeBadge, useViewer } from './common';
import { blankStage, moveStage, stageEntries, stageFields, stageFromValues, stagesPayload, stageValues, type StageEntry } from './pipelineForm';

export interface PipelineDetailViewProps {
  name: string;
  embedded?: boolean;
  onDeleted?: () => void;
  onChanged?: () => void;
}

/**
 * One pipeline (the dashboard's /pipelines/:name): its stages in order with review gates, the stage editor (add, edit,
 * move, remove; each change saves), Run (dispatches a voyage with this pipeline against a vessel), Edit, Duplicate,
 * View JSON, and Delete (not for built-in pipelines).
 */
export function PipelineDetailView({ name, embedded, onDeleted, onChanged }: PipelineDetailViewProps) {
  const { t, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();
  const router = useRouter();
  const viewer = useViewer();
  const { confirm, dialog } = useConfirm('pipeline-confirm');
  const personaNames = useReference(() => listPersonas(ALL)).map((p) => p.name);
  const vessels = useVessels();
  const [editing, setEditing] = useState(false);
  const [stage, setStage] = useState<{ index: number; entry: StageEntry } | null>(null);
  const [running, setRunning] = useState(false);
  const [jsonOpen, setJsonOpen] = useState(false);

  const { data: pipeline, loading, refreshing, error, reload, refresh, setData } = useLoad(() => getPipeline(name), [name], { fallbackError: t('Failed to load pipeline.') });
  useReloadOnFocus(reload);

  if (!pipeline) return <DetailPending loading={loading} error={error} onRetry={() => void reload()} />;
  const p: Pipeline = pipeline;
  const canManage = canEdit(viewer, p);
  const stages = stageEntries(p.stages);

  async function saveStages(next: StageEntry[]) {
    try {
      const updated = await updatePipeline(p.name, { description: p.description || null, stages: stagesPayload(next) as Pipeline['stages'] });
      setData(updated);
      pushToast('success', t('Pipeline "{{name}}" saved.', { name: p.name }));
      onChanged?.();
    } catch (err: unknown) {
      pushToast('error', errorText(err, t('Save failed.')));
    }
  }

  function remove() {
    if (p.isBuiltIn) {
      pushToast('error', t('Built-in pipelines cannot be deleted.'));
      return;
    }
    confirm({
      title: t('Delete Pipeline'),
      message: t('Delete pipeline "{{name}}"? This cannot be undone.', { name: p.name }),
      confirmLabel: t('Delete'),
      danger: true,
      onConfirm: async () => {
        try {
          await deletePipeline(p.name);
          pushToast('warning', t('Pipeline "{{name}}" deleted.', { name: p.name }));
          if (onDeleted) onDeleted(); else router.back();
        } catch (err: unknown) {
          pushToast('error', errorText(err, t('Delete failed.')));
        }
      },
    });
  }

  async function duplicate() {
    try {
      const created = await createPipeline(buildPipelineDuplicatePayload(p));
      pushToast('success', t('Pipeline "{{name}}" duplicated.', { name: created.name }));
      onChanged?.();
      router.push(`/pipelines/${encodeURIComponent(created.name)}` as Href);
    } catch (err: unknown) {
      pushToast('error', errorText(err, t('Duplicate failed.')));
    }
  }

  return (
    <DetailBody embedded={embedded} refreshing={refreshing} onRefresh={() => void refresh()} testID="pipeline-detail">
      {!embedded ? <Stack.Screen options={{ title: p.name }} /> : null}
      <DetailHeader
        title={p.name}
        subtitle={p.id}
        testID="pipeline-title"
        badges={(
          <>
            <StatusBadge label={p.active !== false ? t('Active') : t('Inactive')} tone={p.active !== false ? 'success' : 'cancelled'} />
            {p.isBuiltIn ? <StatusBadge label={t('Built-in')} tone="info" /> : null}
            <StatusBadge {...scopeBadge(t, p.scope)} />
          </>
        )}
      />
      <ActionBar>
        <Button label={t('Run')} icon="play" style={resourceStyles.action} disabled={p.stages.length === 0} onPress={() => setRunning(true)} testID="pipeline-run" />
        {canManage ? <Button label={t('Edit')} variant="secondary" icon="create-outline" style={resourceStyles.action} onPress={() => setEditing(true)} testID="pipeline-edit" /> : null}
        <Button label={t('Duplicate')} variant="secondary" style={resourceStyles.action} onPress={() => void duplicate()} />
        <Button label={t('View JSON')} variant="ghost" style={resourceStyles.action} onPress={() => setJsonOpen(true)} />
        {canManage ? <Button label={t('Delete')} variant="danger" style={resourceStyles.action} onPress={remove} disabled={p.isBuiltIn} /> : null}
      </ActionBar>
      <FieldCard>
        <Field label={t('Description')} value={p.description} />
        <Field label={t('Built-in')} value={p.isBuiltIn ? t('Yes') : t('No')} />
        <Field label={t('Created')} value={formatDateTime(p.createdUtc)} />
        <Field label={t('Last Updated')} value={formatDateTime(p.lastUpdateUtc)} />
      </FieldCard>
      <FieldCard title={t('Stages')} testID="pipeline-stages">
        {stages.length === 0 ? <AppText muted style={resourceStyles.pad}>{t('No stages defined.')}</AppText> : stages.map((s, i) => (
          <ResourceRow
            key={`${i}-${s.personaName}`}
            testID={`pipeline-stage-${i}`}
            title={`${i + 1}. ${s.personaName}`}
            subtitle={[s.isOptional ? t('Optional') : null, s.requiresReview ? `${t('Review')}: ${t('Required')} (${s.reviewDenyAction === 'FailPipeline' ? t('Fail pipeline') : t('Retry stage')})` : null, s.description || null].filter(Boolean).join(' \u2022 ') || null}
            onPress={canManage ? () => setStage({ index: i, entry: s }) : undefined}
            actions={canManage ? [
              ...(i > 0 ? [{ key: 'up', label: t('Move up'), icon: 'arrow-up' as const, onPress: () => void saveStages(moveStage(stages, i, -1)) }] : []),
              ...(i < stages.length - 1 ? [{ key: 'down', label: t('Move down'), icon: 'arrow-down' as const, onPress: () => void saveStages(moveStage(stages, i, 1)) }] : []),
              { key: 'remove', label: t('Remove stage'), icon: 'trash-outline' as const, tone: 'danger' as const, onPress: () => void saveStages(stages.filter((_, x) => x !== i)) },
            ] : []}
          />
        ))}
      </FieldCard>
      {canManage ? <Button label={t('Add Stage')} variant="secondary" icon="add" style={resourceStyles.create} onPress={() => setStage({ index: stages.length, entry: blankStage() })} testID="pipeline-add-stage" /> : null}

      <FormSheet
        testID="pipeline-form"
        open={editing}
        title={t('Edit Pipeline')}
        initial={{ description: p.description ?? '' }}
        fields={() => [{ kind: 'multiline', key: 'description', label: t('Description') }]}
        submitLabel={t('Save')}
        onClose={() => setEditing(false)}
        onSubmit={async (v) => {
          const updated = await updatePipeline(p.name, { description: str(v, 'description') || null, stages: stagesPayload(stages) as Pipeline['stages'] });
          setData(updated);
          setEditing(false);
          pushToast('success', t('Pipeline "{{name}}" saved.', { name: p.name }));
          onChanged?.();
        }}
      />
      <FormSheet
        testID="stage-form"
        open={stage !== null}
        title={t('Stages')}
        initial={stage ? stageValues(stage.entry) : {}}
        fields={(v) => stageFields(t, personaNames, v)}
        submitLabel={t('Save')}
        onClose={() => setStage(null)}
        onSubmit={async (v) => {
          if (!stage) return;
          const entry = stageFromValues(v);
          const next = stage.index < stages.length ? stages.map((s, i) => (i === stage.index ? entry : s)) : [...stages, entry];
          setStage(null);
          await saveStages(next);
        }}
      />
      <FormSheet
        testID="pipeline-run-form"
        open={running}
        title={t('Run Pipeline')}
        initial={{ vesselId: vessels[0]?.id ?? '', title: t('Run: {{name}}', { name: p.name }), description: '' }}
        fields={() => [
          { kind: 'note', key: 'note', label: t('Dispatch a voyage that runs this pipeline\'s stages against a vessel.') },
          { kind: 'select', key: 'vesselId', label: t('Vessel'), required: true, placeholder: t('Select a vessel...'), options: [{ value: '', label: t('Select a vessel...') }, ...vessels.map((x) => ({ value: x.id, label: x.name }))] },
          { kind: 'text', key: 'title', label: t('Title'), required: true },
          { kind: 'multiline', key: 'description', label: t('Objective / Description') },
        ]}
        validate={(v) => (str(v, 'vesselId') ? null : t('{{field}} is required.', { field: t('Vessel') }))}
        submitLabel={t('Launch Voyage')}
        onClose={() => setRunning(false)}
        onSubmit={async (v) => {
          const vesselId = str(v, 'vesselId');
          const title = str(v, 'title');
          try {
            const voyage = await createVoyage({
              title: title || t('Run: {{name}}', { name: p.name }),
              vesselId,
              pipeline: p.name,
              missions: [{ vesselId, title: title || p.name, description: str(v, 'description') || undefined }],
            });
            setRunning(false);
            pushToast('success', t('Voyage launched.'));
            router.push(`/voyages/${voyage.id}` as Href);
          } catch (err: unknown) {
            throw new Error(errorText(err, t('Run failed.')));
          }
        }}
      />
      <JsonSheet open={jsonOpen} title={t('Pipeline: {{name}}', { name: p.name })} data={p} onClose={() => setJsonOpen(false)} />
      {dialog}
    </DetailBody>
  );
}

/** The /pipelines/:name route. */
export function PipelineDetailRoute() {
  const params = useLocalSearchParams<{ name: string }>();
  return <PipelineDetailView name={param(params.name)} />;
}
