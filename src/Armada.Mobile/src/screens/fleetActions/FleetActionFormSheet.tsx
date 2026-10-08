import { useEffect, useState } from 'react';
import { View } from 'react-native';
import { createFleetAction, listPersonas, listPipelines, updateFleetAction } from '@dashboard/api/client';
import type { FleetAction, FleetActionKind, Persona, Pipeline } from '@dashboard/types/models';
import { KIND_DESCRIPTIONS, KIND_LABELS } from '@dashboard/lib/fleetActionLabels';
import { findUnknownTemplateVariables } from '@dashboard/lib/fleetActionTemplate';
import { buildActionUpsertPayload, formFromAction, validateActionForm, type FleetActionFormState } from '@dashboard/lib/fleetActionForm';
import { SwitchField } from '../../build/fields';
import { errorMessage } from '../../build/useLiveResource';
import { AppText, Banner, BottomSheet, Button, SegmentedControl, TextField } from '../../components/ui';
import { SelectField, type SelectOption } from '../../components/ui/SelectSheet';
import { useLocale } from '../../i18n/LocaleContext';
import { TemplateVariableHelp } from './common';

export interface FleetActionFormSheetProps {
  open: boolean;
  /** `create` (blank or duplicate of `source`) or `edit`. */
  mode: 'create' | 'edit';
  source: FleetAction | null;
  /** FleetActions.DefaultTimeoutSeconds for new actions. */
  defaultTimeoutSeconds: number;
  onClose: () => void;
  onSaved: (action: FleetAction) => void;
}

/** Create, edit, or duplicate a fleet action (the dashboard's FleetActionFormModal). */
export function FleetActionFormSheet(props: FleetActionFormSheetProps) {
  const { t } = useLocale();
  const title = props.mode === 'edit' ? t('Edit fleet action') : props.source ? t('Duplicate fleet action') : t('New fleet action');
  return (
    <BottomSheet open={props.open} title={title} onClose={props.onClose} closeLabel={t('Close')} testID="fleet-action-form">
      {props.open ? <ActionForm {...props} /> : null}
    </BottomSheet>
  );
}

function ActionForm({ mode, source, defaultTimeoutSeconds, onClose, onSaved }: FleetActionFormSheetProps) {
  const { t } = useLocale();
  const [form, setForm] = useState<FleetActionFormState>(() => formFromAction(source, mode, defaultTimeoutSeconds, t('(copy)')));
  const [pipelines, setPipelines] = useState<Pipeline[]>([]);
  const [personas, setPersonas] = useState<Persona[]>([]);
  const [submitted, setSubmitted] = useState(false);
  const [saving, setSaving] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    listPipelines({ pageSize: 9999 }).then((r) => { if (!cancelled) setPipelines(r?.objects ?? []); }).catch(() => undefined);
    listPersonas({ pageSize: 9999 }).then((r) => { if (!cancelled) setPersonas(r?.objects ?? []); }).catch(() => undefined);
    return () => { cancelled = true; };
  }, []);

  const errors = validateActionForm(form);
  const bodyText = form.kind === 'Command' ? form.commandText : form.promptTemplate;
  const unknownVars = findUnknownTemplateVariables(bodyText);
  const show = (field: string) => (submitted && errors[field] ? t(errors[field]) : null);
  const setBody = (text: string) => setForm(form.kind === 'Command' ? { ...form, commandText: text } : { ...form, promptTemplate: text });

  function setKind(k: FleetActionKind) {
    setForm({ ...form, kind: k, requiresCleanWorkingTree: k === 'Command' ? (source?.kind === 'Command' ? source.requiresCleanWorkingTree : true) : false });
  }

  async function save() {
    setSubmitted(true);
    if (Object.keys(errors).length > 0 || unknownVars.length > 0) return;
    setSaving(true);
    setServerError(null);
    const payload = buildActionUpsertPayload(form, mode);
    try {
      const saved = mode === 'edit' && source ? await updateFleetAction(source.id, payload) : await createFleetAction(payload);
      onSaved(saved);
    } catch (e) {
      // The server names unknown template variables in its 400 message; show it next to the body.
      setServerError(errorMessage(e) || t('Save failed.'));
    } finally {
      setSaving(false);
    }
  }

  const pipelineOptions: SelectOption<string>[] = [{ value: '', label: t('Vessel default') }, ...pipelines.map((p) => ({ value: p.id, label: p.name }))];
  const personaOptions: SelectOption<string>[] = [{ value: '', label: t('None') }, ...personas.map((p) => ({ value: p.name, label: p.name }))];
  if (form.persona && !personas.some((p) => p.name === form.persona)) personaOptions.push({ value: form.persona, label: form.persona });
  const bodyError = unknownVars.length > 0 ? t('Unknown template variable: {{names}}', { names: unknownVars.join(', ') }) : show('body');

  return (
    <View>
      {serverError ? <Banner tone="danger" title={serverError} testID="fleet-action-form-error" /> : null}
      {submitted && Object.keys(errors).length > 0 ? <Banner tone="danger" title={t('Fix the highlighted fields before saving.')} /> : null}
      {mode === 'edit' && source?.isBuiltIn ? <Banner tone="info" title={t('This is a built-in action. Your edits are kept; it is never re-seeded over your changes.')} /> : null}
      {mode === 'edit' && source ? <AppText variant="mono" muted selectable>{source.id}</AppText> : <AppText muted>{t('Define a reusable action to run across many vessels.')}</AppText>}
      <TextField label={t('Name')} value={form.name} maxLength={200} onChangeText={(name) => setForm({ ...form, name })} error={show('name')} testID="fleet-action-form-name" />
      <TextField label={t('Description')} value={form.description} placeholder={t('Optional')} onChangeText={(description) => setForm({ ...form, description })} testID="fleet-action-form-description" />
      <AppText variant="label">{t('Kind')}</AppText>
      <SegmentedControl
        label={t('Kind')}
        value={form.kind}
        onChange={setKind}
        options={(['Command', 'Mission'] as FleetActionKind[]).map((k) => ({ value: k, label: t(KIND_LABELS[k]), testID: `fleet-action-form-kind-${k}` }))}
      />
      <AppText variant="caption" muted>{t(KIND_DESCRIPTIONS[form.kind])}</AppText>
      <TextField
        label={form.kind === 'Command' ? t('Command text') : t('Prompt template')}
        value={bodyText}
        onChangeText={setBody}
        multiline
        autoCapitalize="none"
        autoCorrect={false}
        placeholder={form.kind === 'Command' ? 'git pull --ff-only' : undefined}
        error={bodyError}
        hint={form.kind === 'Command' ? t('Runs through the platform shell (/bin/sh on Linux and macOS, PowerShell on Windows) in each vessel working directory.') : null}
        testID="fleet-action-form-body"
      />
      {form.kind === 'Mission' ? (
        <>
          <SelectField label={t('Pipeline')} value={form.pipelineId} options={pipelineOptions} onChange={(pipelineId) => setForm({ ...form, pipelineId })} closeLabel={t('Close')} testID="fleet-action-form-pipeline" />
          <SelectField
            label={t('Persona')}
            value={form.persona}
            options={personaOptions}
            onChange={(persona) => setForm({ ...form, persona })}
            closeLabel={t('Close')}
            hint={t('Stored with the action; not yet applied at dispatch. Use a pipeline to choose personas.')}
            testID="fleet-action-form-persona"
          />
        </>
      ) : null}
      <TemplateVariableHelp onInsert={(token) => setBody(bodyText + token)} />
      {form.kind === 'Command' ? (
        <TextField
          label={t('Timeout (seconds)')}
          value={form.timeoutSeconds}
          keyboardType="number-pad"
          onChangeText={(timeoutSeconds) => setForm({ ...form, timeoutSeconds })}
          error={show('timeout')}
          hint={t('5 to 7200 seconds')}
          testID="fleet-action-form-timeout"
        />
      ) : null}
      <TextField
        label={t('Default concurrency')}
        value={form.defaultConcurrency}
        keyboardType="number-pad"
        onChangeText={(defaultConcurrency) => setForm({ ...form, defaultConcurrency })}
        error={show('concurrency')}
        hint={t('1 to 32')}
        testID="fleet-action-form-concurrency"
      />
      {form.kind === 'Command' ? (
        <SwitchField label={t('Requires a clean working tree')} value={form.requiresCleanWorkingTree} onChange={(v) => setForm({ ...form, requiresCleanWorkingTree: v })} testID="fleet-action-form-clean" />
      ) : null}
      <Button label={saving ? t('Saving...') : t('Save')} busy={saving} onPress={() => void save()} testID="fleet-action-form-save" />
      <Button label={t('Cancel')} variant="ghost" disabled={saving} onPress={onClose} />
    </View>
  );
}
