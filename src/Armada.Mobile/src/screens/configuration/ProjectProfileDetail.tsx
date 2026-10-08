import { Stack, useLocalSearchParams, useRouter, type Href } from 'expo-router';
import { useState } from 'react';
import {
  createProjectProfile, deleteProjectProfile, getProjectProfile, listPersonas, listPromptTemplates, previewPersonaPrompt, updateProjectProfile,
  validateProjectProfile,
} from '@dashboard/api/client';
import type { PersonaOverride, PersonaPromptPreview, ProjectProfile, ProjectProfileValidationResult } from '@dashboard/types/models';
import { blankPersonaOverride, KNOWN_PERSONAS } from '@dashboard/lib/configuration';
import { ActionBar, DetailBody, DetailHeader, DetailPending, Field, FieldCard, JsonSheet, TextBlock } from '../../components/resource/DetailParts';
import { FormSheet, str } from '../../components/resource/FormSheet';
import { ResourceRow, useConfirm } from '../../components/resource/ResourceRow';
import { resourceStyles } from '../../components/resource/styles';
import { AppText } from '../../components/ui/AppText';
import { Banner } from '../../components/ui/Banner';
import { Button } from '../../components/ui/Button';
import { StatusBadge } from '../../components/ui/StatusBadge';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { param } from '../../resource/links';
import { ALL, useNameMap, useReference } from '../../resource/lookups';
import { errorText, useLoad, useReloadOnFocus } from '../../resource/useLoad';
import { canEditProfile, scopeBadge, useViewer } from './common';
import { blankProjectProfile, overrideFields, overrideFromValues, overrideValues, projectFields, projectPayload, projectValues } from './profileForms';
import { useProjectReferences } from './references';

export interface ProjectProfileDetailViewProps {
  id: string;
  embedded?: boolean;
  onDeleted?: () => void;
  onChanged?: () => void;
}

/**
 * One project profile (the dashboard's /project-profiles/:id): bindings, skills, persona overrides (each edited in a
 * sheet and saved), the persona prompt diff (base vs. effective), Validate, Edit, View JSON, and Delete.
 */
export function ProjectProfileDetailView({ id, embedded, onDeleted, onChanged }: ProjectProfileDetailViewProps) {
  const { t, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();
  const router = useRouter();
  const viewer = useViewer();
  const refs = useProjectReferences();
  const fleetNames = useNameMap(refs.fleets);
  const vesselNames = useNameMap(refs.vessels);
  const personaNames = useReference(() => listPersonas(ALL)).map((p) => p.name);
  const templateNames = useReference(() => listPromptTemplates(ALL)).map((p) => p.name);
  const { confirm, dialog } = useConfirm('project-confirm');
  const [editing, setEditing] = useState(false);
  const [override, setOverride] = useState<{ index: number; value: PersonaOverride } | null>(null);
  const [previewOpen, setPreviewOpen] = useState(false);
  const [preview, setPreview] = useState<PersonaPromptPreview | null>(null);
  const [validation, setValidation] = useState<ProjectProfileValidationResult | null>(null);
  const [jsonOpen, setJsonOpen] = useState(false);
  const { data: profile, loading, refreshing, error, reload, refresh, setData } = useLoad(() => getProjectProfile(id), [id], { fallbackError: t('Failed to load project profile.') });
  useReloadOnFocus(reload);

  if (!profile) return <DetailPending loading={loading} error={error} onRetry={() => void reload()} />;
  const p: ProjectProfile = profile;
  const canManage = canEditProfile(viewer, p);
  const overrides = p.personaOverrides || [];
  const personaChoices = Array.from(new Set([...KNOWN_PERSONAS, ...personaNames]));

  async function saveOverrides(next: PersonaOverride[]) {
    try {
      const updated = await updateProjectProfile(p.id, projectPayload(viewer, projectValues(p), next, p.ownershipScope));
      setData(updated);
      pushToast('success', t('Project profile "{{name}}" saved.', { name: updated.name }));
      onChanged?.();
    } catch (err: unknown) {
      pushToast('error', errorText(err, t('Save failed.')));
    }
  }

  async function validate() {
    try {
      const result = await validateProjectProfile(projectPayload(viewer, projectValues(p), overrides, p.ownershipScope));
      setValidation(result);
    } catch (err: unknown) {
      pushToast('error', errorText(err, t('Validation failed.')));
    }
  }

  function remove() {
    confirm({
      title: t('Delete Project Profile'),
      message: t('Delete this project profile? This cannot be undone.'),
      confirmLabel: t('Delete'),
      danger: true,
      onConfirm: async () => {
        try {
          await deleteProjectProfile(p.id);
          pushToast('warning', t('Project profile deleted.'));
          if (onDeleted) onDeleted(); else router.back();
        } catch (err: unknown) {
          pushToast('error', errorText(err, t('Delete failed.')));
        }
      },
    });
  }

  const pipeline = refs.pipelines.find((x) => x.id === p.defaultPipelineId);
  const workflow = refs.workflowProfiles.find((x) => x.id === p.workflowProfileId);
  const target = p.scope === 'Fleet' ? (p.fleetId ? fleetNames.get(p.fleetId) || p.fleetId : null) : p.scope === 'Vessel' ? (p.vesselId ? vesselNames.get(p.vesselId) || p.vesselId : null) : null;
  return (
    <DetailBody embedded={embedded} refreshing={refreshing} onRefresh={() => void refresh()} testID="project-detail">
      {!embedded ? <Stack.Screen options={{ title: p.name }} /> : null}
      <DetailHeader
        title={p.name}
        subtitle={p.id}
        testID="project-title"
        badges={(
          <>
            <StatusBadge label={p.active ? t('Active') : t('Inactive')} tone={p.active ? 'success' : 'cancelled'} />
            <StatusBadge label={t(p.scope)} tone="info" />
            {p.isDefault ? <StatusBadge label={t('Default')} tone="info" /> : null}
            <StatusBadge {...scopeBadge(t, p.ownershipScope)} />
          </>
        )}
      />
      {!canManage ? <Banner tone="info" title={t('You can view this project profile, but only tenant administrators can change it.')} /> : null}
      <ActionBar>
        {canManage ? <Button label={t('Edit')} icon="create-outline" style={resourceStyles.action} onPress={() => setEditing(true)} testID="project-edit" /> : null}
        <Button label={t('Validate')} variant="secondary" style={resourceStyles.action} onPress={() => void validate()} testID="project-validate" />
        <Button label={t('Preview')} variant="secondary" style={resourceStyles.action} onPress={() => setPreviewOpen(true)} testID="project-preview" />
        <Button label={t('View JSON')} variant="ghost" style={resourceStyles.action} onPress={() => setJsonOpen(true)} />
        {canManage ? <Button label={t('Delete')} variant="danger" style={resourceStyles.action} onPress={remove} /> : null}
      </ActionBar>
      {validation ? (
        <FieldCard title={t('Validation')} testID="project-validation">
          <Field label={t('Status')} value={validation.isValid ? t('Valid') : t('Invalid')} />
          {validation.errors.map((e, i) => <Field key={`e${i}`} label={t('Errors')} value={e} />)}
          {validation.warnings.map((w, i) => <Field key={`w${i}`} label={t('Warnings')} value={w} />)}
        </FieldCard>
      ) : null}
      <FieldCard>
        <Field label={t('Description')} value={p.description} />
        <Field label={t('Scope')} value={target ? `${t(p.scope)}: ${target}` : t(p.scope)} />
        <Field label={t('Default Pipeline ID')} value={pipeline ? pipeline.name : p.defaultPipelineId} onPress={pipeline ? () => router.push(`/pipelines/${encodeURIComponent(pipeline.name)}` as Href) : undefined} />
        <Field label={t('Workflow Profile ID')} value={workflow ? workflow.name : p.workflowProfileId} onPress={p.workflowProfileId ? () => router.push(`/workflow-profiles/${p.workflowProfileId}` as Href) : undefined} />
        <Field label={t('Skills')} value={(p.skills || []).join(', ')} />
        <Field label={t('Created')} value={formatDateTime(p.createdUtc)} />
        <Field label={t('Last Updated')} value={formatDateTime(p.lastUpdateUtc)} />
      </FieldCard>

      <FieldCard title={t('Persona Overrides')} testID="project-overrides">
        <AppText variant="caption" muted style={resourceStyles.pad}>{t('Swap a persona prompt template and/or append per-project instructions. Applied to this project\'s pipeline personas at dispatch.')}</AppText>
        {overrides.length === 0 ? <AppText muted style={resourceStyles.pad}>{t('No persona overrides. Personas use their built-in prompts.')}</AppText> : overrides.map((o, i) => (
          <ResourceRow
            key={`${o.personaName}-${i}`}
            title={o.personaName}
            subtitle={[o.promptTemplateName, o.additionalInstructions].filter(Boolean).join(' \u2022 ') || null}
            badge={{ label: o.enabled ? t('Enabled') : t('Disabled'), tone: o.enabled ? 'success' : 'cancelled' }}
            onPress={canManage ? () => setOverride({ index: i, value: o }) : undefined}
            actions={canManage ? [{ key: 'remove', label: t('Remove'), icon: 'trash-outline', tone: 'danger', onPress: () => void saveOverrides(overrides.filter((_, x) => x !== i)) }] : []}
          />
        ))}
      </FieldCard>
      {canManage ? <Button label={t('Override')} variant="secondary" icon="add" style={resourceStyles.create} onPress={() => setOverride({ index: overrides.length, value: blankPersonaOverride() })} testID="project-add-override" /> : null}

      {preview ? (
        <>
          <FieldCard title={t('Persona Prompt Diff')} testID="project-preview-result">
            <Field label={t('Persona')} value={preview.personaName} />
            <Field label={t('Status')} value={preview.isOverridden ? t('Overridden: {{base}} to {{eff}}', { base: preview.baseTemplateName, eff: preview.effectiveTemplateName }) : t('No override applied for {{persona}}.', { persona: preview.personaName })} />
          </FieldCard>
          <TextBlock title={t('Base')} text={preview.basePrompt} mono />
          <TextBlock title={t('Effective')} text={preview.effectivePrompt} mono />
        </>
      ) : null}

      <FormSheet
        testID="project-form"
        open={editing}
        title={t('Edit Project Profile')}
        initial={projectValues(p)}
        fields={(v) => projectFields(t, viewer, v, refs.fleets, refs.vessels, refs.pipelines, refs.workflowProfiles)}
        submitLabel={t('Save Changes')}
        onClose={() => setEditing(false)}
        onSubmit={async (v) => {
          const updated = await updateProjectProfile(p.id, projectPayload(viewer, v, overrides, p.ownershipScope));
          setData(updated);
          setEditing(false);
          pushToast('success', t('Project profile "{{name}}" saved.', { name: updated.name }));
          onChanged?.();
        }}
      />
      <FormSheet
        testID="override-form"
        open={override !== null}
        title={t('Persona Overrides')}
        initial={override ? overrideValues(override.value) : {}}
        fields={() => overrideFields(t, personaChoices, templateNames)}
        submitLabel={t('Save Changes')}
        onClose={() => setOverride(null)}
        onSubmit={async (v) => {
          if (!override) return;
          const next = overrideFromValues(v);
          const list = override.index < overrides.length ? overrides.map((o, i) => (i === override.index ? next : o)) : [...overrides, next];
          setOverride(null);
          await saveOverrides(list);
        }}
      />
      <FormSheet
        testID="project-preview-form"
        open={previewOpen}
        title={t('Persona Prompt Diff')}
        initial={{ persona: 'Architect' }}
        fields={() => [{ kind: 'select', key: 'persona', label: t('Persona'), options: personaChoices.map((n) => ({ value: n, label: n })) }]}
        submitLabel={t('Preview')}
        onClose={() => setPreviewOpen(false)}
        onSubmit={async (v) => {
          try {
            setPreview(await previewPersonaPrompt(p.id, str(v, 'persona')));
            setPreviewOpen(false);
          } catch (err: unknown) {
            throw new Error(errorText(err, t('Preview failed.')));
          }
        }}
      />
      <JsonSheet open={jsonOpen} title={p.name} data={p} onClose={() => setJsonOpen(false)} />
      {dialog}
    </DetailBody>
  );
}

/** The /project-profiles/:id route; `new` is the create form. */
export function ProjectProfileDetailRoute() {
  const params = useLocalSearchParams<{ id: string }>();
  const { t } = useLocale();
  const router = useRouter();
  const viewer = useViewer();
  const refs = useProjectReferences();
  const { pushToast } = useNotifications();
  const id = param(params.id);
  if (id !== 'new') return <ProjectProfileDetailView id={id} />;
  return (
    <DetailBody testID="project-create">
      <Stack.Screen options={{ title: t('Create Project Profile') }} />
      <FormSheet
        testID="project-form"
        open
        title={t('Create Project Profile')}
        initial={projectValues(blankProjectProfile(viewer))}
        fields={(v) => projectFields(t, viewer, v, refs.fleets, refs.vessels, refs.pipelines, refs.workflowProfiles)}
        submitLabel={t('Create Project Profile')}
        onClose={() => router.back()}
        onSubmit={async (v) => {
          const created = await createProjectProfile(projectPayload(viewer, v, [], null));
          pushToast('success', t('Project profile "{{name}}" created.', { name: created.name }));
          router.replace(`/project-profiles/${created.id}` as Href);
        }}
      />
    </DetailBody>
  );
}
