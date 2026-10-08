import { Stack, useLocalSearchParams, useRouter, type Href } from 'expo-router';
import { useState } from 'react';
import {
  createWorkflowProfile, deleteWorkflowProfile, getWorkflowProfile, previewWorkflowProfileForVessel, updateWorkflowProfile, validateWorkflowProfile,
} from '@dashboard/api/client';
import type {
  WorkflowEnvironmentProfile, WorkflowInputReference, WorkflowProfile, WorkflowProfileCommandPreview, WorkflowProfileResolutionPreviewResult,
  WorkflowProfileValidationResult,
} from '@dashboard/types/models';
import { blankInputReference, blankWorkflowEnvironment, WORKFLOW_INPUT_PROVIDERS } from '@dashboard/lib/configuration';
import { buildWorkflowProfileDuplicatePayload } from '@dashboard/lib/duplicates';
import { ActionBar, DetailBody, DetailHeader, DetailPending, Field, FieldCard, JsonSheet } from '../../components/resource/DetailParts';
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
import { useFleets, useNameMap, useVessels } from '../../resource/lookups';
import { errorText, useLoad, useReloadOnFocus } from '../../resource/useLoad';
import { canEditProfile, scopeBadge, useViewer } from './common';
import {
  applyWorkflowValues, blankWorkflowProfile, environmentProfileFields, environmentProfileFromValues, environmentProfileValues, ENVIRONMENT_COMMANDS,
  inputFields, inputFromValues, inputValues, WORKFLOW_COMMANDS, workflowFields, workflowPayload, workflowValues,
} from './profileForms';

function CommandPreviews({ previews }: { previews: WorkflowProfileCommandPreview[] }) {
  const { t } = useLocale();
  if (previews.length === 0) return <AppText muted style={resourceStyles.pad}>{t('No resolved commands are available for this workflow profile.')}</AppText>;
  return <>{previews.map((p, i) => <Field key={`${p.checkType}-${p.environmentName ?? ''}-${i}`} label={p.environmentName ? `${t(p.checkType)} (${p.environmentName})` : t(p.checkType)} value={p.command} mono />)}</>;
}

export interface WorkflowProfileDetailViewProps {
  id: string;
  embedded?: boolean;
  onDeleted?: () => void;
  onChanged?: () => void;
}

/**
 * One workflow profile (the dashboard's /workflow-profiles/:id): overview, commands, required inputs and
 * environment commands (each edited in a sheet and saved), Validate (resolved commands, errors, warnings), the
 * resolution preview for a vessel, Edit, Duplicate, View JSON, and Delete. Editing follows the scope rules.
 */
export function WorkflowProfileDetailView({ id, embedded, onDeleted, onChanged }: WorkflowProfileDetailViewProps) {
  const { t, formatDateTime } = useLocale();
  const { pushToast } = useNotifications();
  const router = useRouter();
  const viewer = useViewer();
  const fleets = useFleets();
  const vessels = useVessels();
  const fleetNames = useNameMap(fleets);
  const vesselNames = useNameMap(vessels);
  const { confirm, dialog } = useConfirm('workflow-confirm');
  const [editing, setEditing] = useState(false);
  const [environment, setEnvironment] = useState<{ index: number; value: WorkflowEnvironmentProfile } | null>(null);
  const [input, setInput] = useState<{ index: number; value: WorkflowInputReference } | null>(null);
  const [validation, setValidation] = useState<WorkflowProfileValidationResult | null>(null);
  const [validating, setValidating] = useState(false);
  const [previewOpen, setPreviewOpen] = useState(false);
  const [preview, setPreview] = useState<WorkflowProfileResolutionPreviewResult | null>(null);
  const [jsonOpen, setJsonOpen] = useState(false);
  const { data: profile, loading, refreshing, error, reload, refresh, setData } = useLoad(() => getWorkflowProfile(id), [id], { fallbackError: t('Failed to load workflow profile.') });
  useReloadOnFocus(reload);

  if (!profile) return <DetailPending loading={loading} error={error} onRetry={() => void reload()} />;
  const p: WorkflowProfile = profile;
  const canManage = canEditProfile(viewer, p);
  const commands = WORKFLOW_COMMANDS.filter((c) => !!p[c.key]);

  async function save(next: WorkflowProfile) {
    try {
      const updated = await updateWorkflowProfile(p.id, workflowPayload(next));
      setData(updated);
      pushToast('success', t('Workflow profile "{{name}}" saved.', { name: updated.name }));
      onChanged?.();
    } catch (err: unknown) {
      pushToast('error', errorText(err, t('Save failed.')));
    }
  }

  async function validate() {
    setValidating(true);
    try {
      const result = await validateWorkflowProfile(workflowPayload(p));
      setValidation(result);
      if (result.isValid) pushToast('success', t('Workflow profile is valid.'));
      else pushToast('warning', t('Workflow profile has validation errors.'));
    } catch (err: unknown) {
      pushToast('error', errorText(err, t('Validation failed.')));
    } finally {
      setValidating(false);
    }
  }

  function remove() {
    confirm({
      title: t('Delete Workflow Profile'),
      message: t('Delete "{{name}}"? Existing check runs remain, but future runs will not be able to resolve this profile.', { name: p.name }),
      confirmLabel: t('Delete'),
      danger: true,
      onConfirm: async () => {
        try {
          await deleteWorkflowProfile(p.id);
          pushToast('warning', t('Workflow profile "{{name}}" deleted.', { name: p.name }));
          if (onDeleted) onDeleted(); else router.back();
        } catch (err: unknown) {
          pushToast('error', errorText(err, t('Delete failed.')));
        }
      },
    });
  }

  async function duplicate() {
    try {
      const created = await createWorkflowProfile(buildWorkflowProfileDuplicatePayload(p));
      pushToast('success', t('Workflow profile "{{name}}" duplicated.', { name: created.name }));
      onChanged?.();
      router.push(`/workflow-profiles/${created.id}` as Href);
    } catch (err: unknown) {
      pushToast('error', errorText(err, t('Duplicate failed.')));
    }
  }

  const target = p.scope === 'Fleet' ? (p.fleetId ? fleetNames.get(p.fleetId) || p.fleetId : null) : p.scope === 'Vessel' ? (p.vesselId ? vesselNames.get(p.vesselId) || p.vesselId : null) : null;
  return (
    <DetailBody embedded={embedded} refreshing={refreshing} onRefresh={() => void refresh()} testID="workflow-detail">
      {!embedded ? <Stack.Screen options={{ title: p.name }} /> : null}
      <DetailHeader
        title={p.name}
        subtitle={p.id}
        testID="workflow-title"
        badges={(
          <>
            <StatusBadge label={p.active ? t('Active') : t('Inactive')} tone={p.active ? 'success' : 'cancelled'} />
            <StatusBadge label={t(p.scope)} tone="info" />
            {p.isDefault ? <StatusBadge label={t('Default')} tone="info" /> : null}
            <StatusBadge {...scopeBadge(t, p.ownershipScope)} />
          </>
        )}
      />
      {!canManage ? <Banner tone="info" title={t('You can view this workflow profile, but only its owner or a tenant administrator can change it.')} /> : null}
      <ActionBar>
        {canManage ? <Button label={t('Edit')} icon="create-outline" style={resourceStyles.action} onPress={() => setEditing(true)} testID="workflow-edit" /> : null}
        <Button label={validating ? t('Validating...') : t('Validate')} variant="secondary" busy={validating} style={resourceStyles.action} onPress={() => void validate()} testID="workflow-validate" />
        <Button label={t('Preview for vessel')} variant="secondary" style={resourceStyles.action} onPress={() => setPreviewOpen(true)} testID="workflow-preview" />
        {canManage ? <Button label={t('Duplicate')} variant="ghost" style={resourceStyles.action} onPress={() => void duplicate()} /> : null}
        <Button label={t('View JSON')} variant="ghost" style={resourceStyles.action} onPress={() => setJsonOpen(true)} />
        {canManage ? <Button label={t('Delete')} variant="danger" style={resourceStyles.action} onPress={remove} /> : null}
      </ActionBar>

      {validation ? (
        <FieldCard title={t('Validation')} testID="workflow-validation">
          <Field label={t('Status')} value={validation.isValid ? t('Valid') : t('Invalid')} />
          <Field label={t('Available Check Types')} value={validation.availableCheckTypes.length > 0 ? validation.availableCheckTypes.join(', ') : t('None')} />
          {validation.errors.map((e, i) => <Field key={`e${i}`} label={t('Errors')} value={e} />)}
          {validation.warnings.map((w, i) => <Field key={`w${i}`} label={t('Warnings')} value={w} />)}
          <AppText variant="subheading" muted style={resourceStyles.pad}>{t('Resolved Commands')}</AppText>
          <CommandPreviews previews={validation.commandPreviews} />
        </FieldCard>
      ) : null}
      {preview ? (
        <FieldCard title={t('Preview for vessel')} testID="workflow-preview-result">
          <Field label={t('Resolution')} value={t(preview.resolutionMode)} />
          <Field label={t('Profile')} value={preview.resolvedProfile?.name ?? t('None')} />
          <Field label={t('Available Check Types')} value={preview.availableCheckTypes.join(', ') || t('None')} />
          <CommandPreviews previews={preview.commandPreviews} />
        </FieldCard>
      ) : null}

      <FieldCard title={t('Overview')}>
        <Field label={t('Description')} value={p.description} />
        <Field label={t('Scope')} value={target ? `${t(p.scope)}: ${target}` : t(p.scope)} />
        <Field label={t('Language / Runtime Hints')} value={p.languageHints.join(', ')} />
        <Field label={t('Expected Artifacts')} value={p.expectedArtifacts.join('\n')} mono />
        <Field label={t('Created')} value={formatDateTime(p.createdUtc)} />
        <Field label={t('Last Updated')} value={formatDateTime(p.lastUpdateUtc)} />
      </FieldCard>
      <FieldCard title={t('Commands')}>
        {commands.length === 0 ? <AppText muted style={resourceStyles.pad}>{t('None')}</AppText> : commands.map((c) => <Field key={c.key} label={t(c.label)} value={p[c.key] as string} mono />)}
      </FieldCard>

      <FieldCard title={t('Required Inputs')} testID="workflow-inputs">
        <AppText variant="caption" muted style={resourceStyles.pad}>{t('Store provider/key references here so Armada can warn before checks run. No secret values are stored in the workflow profile itself.')}</AppText>
        {p.requiredInputs.length === 0 ? <AppText muted style={resourceStyles.pad}>{t('No required inputs configured.')}</AppText> : p.requiredInputs.map((item, i) => (
          <ResourceRow
            key={`${item.provider}-${item.key}-${i}`}
            title={item.key}
            subtitle={[t(WORKFLOW_INPUT_PROVIDERS.find((x) => x.value === item.provider)?.label ?? item.provider), item.environmentName || t('All Environments'), item.description].filter(Boolean).join(' \u2022 ')}
            onPress={canManage ? () => setInput({ index: i, value: item }) : undefined}
            actions={canManage ? [{ key: 'remove', label: t('Remove'), icon: 'trash-outline', tone: 'danger', onPress: () => void save({ ...p, requiredInputs: p.requiredInputs.filter((_, x) => x !== i) }) }] : []}
          />
        ))}
      </FieldCard>
      {canManage ? <Button label={t('Input')} variant="secondary" icon="add" style={resourceStyles.create} onPress={() => setInput({ index: p.requiredInputs.length, value: blankInputReference() })} testID="workflow-add-input" /> : null}

      <FieldCard title={t('Environment Commands')} testID="workflow-environments">
        {p.environments.length === 0 ? <AppText muted style={resourceStyles.pad}>{t('No environment-specific commands configured yet.')}</AppText> : p.environments.map((e, i) => (
          <ResourceRow
            key={`${e.environmentName}-${i}`}
            title={e.environmentName || t('Environment')}
            subtitle={ENVIRONMENT_COMMANDS.filter((c) => !!e[c.key]).map((c) => t(c.label)).join(', ') || null}
            onPress={canManage ? () => setEnvironment({ index: i, value: e }) : undefined}
            actions={canManage ? [{ key: 'remove', label: t('Remove'), icon: 'trash-outline', tone: 'danger', onPress: () => void save({ ...p, environments: p.environments.filter((_, x) => x !== i) }) }] : []}
          />
        ))}
      </FieldCard>
      {canManage ? <Button label={t('Environment')} variant="secondary" icon="add" style={resourceStyles.create} onPress={() => setEnvironment({ index: p.environments.length, value: blankWorkflowEnvironment() })} testID="workflow-add-environment" /> : null}

      <FormSheet
        testID="workflow-form"
        open={editing}
        title={t('Edit Workflow Profile')}
        initial={workflowValues(p)}
        fields={(v) => workflowFields(t, viewer, v, fleets, vessels)}
        submitLabel={t('Save Changes')}
        onClose={() => setEditing(false)}
        onSubmit={async (v) => {
          const updated = await updateWorkflowProfile(p.id, workflowPayload(applyWorkflowValues(viewer, p, v, true)));
          setData(updated);
          setEditing(false);
          pushToast('success', t('Workflow profile "{{name}}" saved.', { name: updated.name }));
          onChanged?.();
        }}
      />
      <FormSheet
        testID="workflow-environment-form"
        open={environment !== null}
        title={t('Environment Commands')}
        initial={environment ? environmentProfileValues(environment.value) : {}}
        fields={() => environmentProfileFields(t)}
        submitLabel={t('Save Changes')}
        onClose={() => setEnvironment(null)}
        onSubmit={async (v) => {
          if (!environment) return;
          const next = environmentProfileFromValues(v);
          const environments = environment.index < p.environments.length ? p.environments.map((e, i) => (i === environment.index ? next : e)) : [...p.environments, next];
          setEnvironment(null);
          await save({ ...p, environments });
        }}
      />
      <FormSheet
        testID="workflow-input-form"
        open={input !== null}
        title={t('Required Inputs')}
        initial={input ? inputValues(input.value) : {}}
        fields={(v) => inputFields(t, v, p.environments.map((e) => e.environmentName).filter(Boolean))}
        submitLabel={t('Save Changes')}
        onClose={() => setInput(null)}
        onSubmit={async (v) => {
          if (!input) return;
          const next = inputFromValues(v);
          const requiredInputs = input.index < p.requiredInputs.length ? p.requiredInputs.map((x, i) => (i === input.index ? next : x)) : [...p.requiredInputs, next];
          setInput(null);
          await save({ ...p, requiredInputs });
        }}
      />
      <FormSheet
        testID="workflow-preview-form"
        open={previewOpen}
        title={t('Preview for vessel')}
        initial={{ vesselId: p.vesselId ?? '' }}
        fields={() => [{ kind: 'select', key: 'vesselId', label: t('Vessel'), required: true, placeholder: t('Select a vessel...'), options: vessels.map((x) => ({ value: x.id, label: x.name })) }]}
        submitLabel={t('Preview')}
        onClose={() => setPreviewOpen(false)}
        onSubmit={async (v) => {
          setPreview(await previewWorkflowProfileForVessel(str(v, 'vesselId'), p.id));
          setPreviewOpen(false);
        }}
      />
      <JsonSheet open={jsonOpen} title={p.name} data={p} onClose={() => setJsonOpen(false)} />
      {dialog}
    </DetailBody>
  );
}

/**
 * The /workflow-profiles/:id route. `new` is the create form, prefilled from ?scope=&fleetId=&vesselId= (the setup
 * wizard links it); saving opens the new profile.
 */
export function WorkflowProfileDetailRoute() {
  const params = useLocalSearchParams<{ id: string; scope?: string; fleetId?: string; vesselId?: string }>();
  const { t } = useLocale();
  const router = useRouter();
  const viewer = useViewer();
  const fleets = useFleets();
  const vessels = useVessels();
  const { pushToast } = useNotifications();
  const id = param(params.id);
  if (id !== 'new') return <WorkflowProfileDetailView id={id} />;
  const base = blankWorkflowProfile(viewer, { scope: param(params.scope), fleetId: param(params.fleetId), vesselId: param(params.vesselId) });
  return (
    <DetailBody testID="workflow-create">
      <Stack.Screen options={{ title: t('Create Workflow Profile') }} />
      <FormSheet
        testID="workflow-form"
        open
        title={t('Create Workflow Profile')}
        initial={workflowValues(base)}
        fields={(v) => workflowFields(t, viewer, v, fleets, vessels)}
        submitLabel={t('Create Workflow Profile')}
        onClose={() => router.back()}
        onSubmit={async (v) => {
          const created = await createWorkflowProfile(workflowPayload(applyWorkflowValues(viewer, base, v, false)));
          pushToast('success', t('Workflow profile "{{name}}" created.', { name: created.name }));
          router.replace(`/workflow-profiles/${created.id}` as Href);
        }}
      />
    </DetailBody>
  );
}
