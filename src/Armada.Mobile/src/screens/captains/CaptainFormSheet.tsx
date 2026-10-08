import { useEffect, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { createCaptain, listModelEndpoints, setCaptainCliPermissionPolicy, updateCaptain } from '@dashboard/api/client';
import { supportsAutoApproveSwitch } from '@dashboard/lib/captainApproval';
import {
  buildCaptainCreatePayload,
  buildCaptainPayload,
  CAPTAIN_RUNTIMES,
  CAPTAIN_TIERS,
  captainFormError,
  captainFormErrorMessage,
  captainFormFromCaptain,
  cliPolicyChanged,
  emptyCaptainForm,
  REASONING_EFFORTS,
  type CaptainFormState,
} from '@dashboard/lib/captainForm';
import type { Captain, ModelEndpoint } from '@dashboard/types/models';
import { SwitchField } from '../../build/fields';
import { errorMessage } from '../../build/useLiveResource';
import { useAuth } from '../../auth/AuthContext';
import { CliPermissionPolicyField } from '../../components/cliPermissions/CliPermissionPolicyField';
import { BottomSheet, Button, TextField } from '../../components/ui';
import { Banner } from '../../components/ui/Banner';
import { SelectField, type SelectOption } from '../../components/ui/SelectSheet';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { spacing } from '../../theme/typography';
import { MuxFields } from './MuxFields';

export interface CaptainFormSheetProps {
  open: boolean;
  /** The captain to edit, or null to create one. */
  captain: Captain | null;
  onClose: () => void;
  /** After a successful save (the created or updated captain when the server returned it). */
  onSaved: (captain: Captain | null) => void;
}

/**
 * Create or edit a captain (the dashboard's captain modals): name, runtime, model, inference endpoint for API-endpoint
 * captains, reasoning effort, capability tier, auto-approve, CLI tool permission policy (admins; saved through its
 * own endpoint on edit), Mux options, persona routing, and system instructions.
 */
export function CaptainFormSheet({ open, captain, onClose, onSaved }: CaptainFormSheetProps) {
  const { t } = useLocale();
  const { isAdmin, isTenantAdmin } = useAuth();
  const { pushToast } = useNotifications();
  const canManageCliPolicy = isAdmin || isTenantAdmin;
  const [form, setForm] = useState<CaptainFormState>(emptyCaptainForm());
  const [endpoints, setEndpoints] = useState<ModelEndpoint[]>([]);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;
    // eslint-disable-next-line react-hooks/set-state-in-effect -- opening the sheet resets the form to the captain
    setForm(captain ? captainFormFromCaptain(captain, { withEndpoint: true, withPersonas: true }) : { ...emptyCaptainForm(), allowedPersonas: '', preferredPersona: '' });
    setError(null);
    listModelEndpoints()
      .then((result) => setEndpoints((result ?? []).filter((e) => e.kind === 'Inference')))
      .catch(() => setEndpoints([]));
  }, [open, captain]);

  const patch = (next: Partial<CaptainFormState>) => setForm((current) => ({ ...current, ...next }));

  const runtimeOptions: SelectOption<string>[] = CAPTAIN_RUNTIMES.map((r) => ({ value: r.value, label: r.label }));
  if (form.runtime && !runtimeOptions.some((o) => o.value === form.runtime)) runtimeOptions.push({ value: form.runtime, label: form.runtime });
  const effortOptions: SelectOption<string>[] = [{ value: '', label: t('Runtime default') }, ...REASONING_EFFORTS.map((e) => ({ value: e, label: t(e) }))];
  const tierOptions: SelectOption<string>[] = [{ value: '', label: t('Auto (classify from model)') }, ...CAPTAIN_TIERS.map((e) => ({ value: e, label: t(e) }))];
  const endpointOptions: SelectOption<string>[] = endpoints.map((ep) => ({ value: ep.id, label: ep.name, description: `${ep.provider}${ep.model ? ` / ${ep.model}` : ''}` }));

  async function save() {
    if (saving) return;
    if (!form.name.trim()) { setError(t('Name is required.')); return; }
    if (!form.runtime) { setError(t('Select runtime...')); return; }
    const formError = captainFormError(form);
    if (formError) { setError(t(captainFormErrorMessage(formError))); return; }
    setSaving(true);
    setError(null);
    try {
      let saved: Captain | null;
      if (captain) {
        saved = (await updateCaptain(captain.id, buildCaptainPayload(form))) ?? null;
        if (cliPolicyChanged(captain, form)) await setCaptainCliPermissionPolicy(captain.id, form.cliPermissionPolicy);
        pushToast('success', t('Captain "{{name}}" saved.', { name: form.name }));
      } else {
        saved = (await createCaptain(buildCaptainCreatePayload(form))) ?? null;
        pushToast('success', t('Captain "{{name}}" created.', { name: form.name }));
      }
      onSaved(saved);
    } catch (e) {
      setError(errorMessage(e) || t('Save failed.'));
    } finally {
      setSaving(false);
    }
  }

  return (
    <BottomSheet open={open} title={captain ? t('Edit Captain') : t('Create Captain')} onClose={onClose} closeLabel={t('Close')} testID="captain-form">
      {error ? <Banner tone="danger" title={error} testID="captain-form-error" /> : null}
      <TextField label={t('Name')} value={form.name} onChangeText={(v) => patch({ name: v })} autoCapitalize="none" testID="captain-form-name" />
      <SelectField label={t('Runtime')} value={form.runtime} options={runtimeOptions} onChange={(v) => patch({ runtime: v })} closeLabel={t('Close')} placeholder={t('Select runtime...')} testID="captain-form-runtime" />
      <TextField
        label={t('Model')}
        value={form.model}
        onChangeText={(v) => patch({ model: v })}
        placeholder={form.runtime === 'ApiEndpoint' ? t('Optional; overrides the endpoint model') : t('e.g., gpt-5.4-mini')}
        hint={t('Optional AI model identifier. Leave blank to let the runtime choose its default model.')}
        autoCapitalize="none"
        autoCorrect={false}
        testID="captain-form-model"
      />
      {form.runtime === 'ApiEndpoint' ? (
        <SelectField
          label={t('Inference Endpoint')}
          value={form.modelEndpointId ?? ''}
          options={endpointOptions}
          onChange={(v) => patch({ modelEndpointId: v })}
          closeLabel={t('Close')}
          placeholder={t('Select an inference endpoint...')}
          hint={endpoints.length === 0 ? t('No inference endpoints configured. Add one under Configuration > Endpoints first.') : null}
          testID="captain-form-modelEndpointId"
        />
      ) : null}
      <SelectField label={t('Reasoning effort')} value={form.reasoningEffort} options={effortOptions} onChange={(v) => patch({ reasoningEffort: v })} closeLabel={t('Close')} testID="captain-form-reasoningEffort" />
      <SelectField
        label={t('Capability tier')}
        value={form.tier}
        options={tierOptions}
        onChange={(v) => patch({ tier: v })}
        closeLabel={t('Close')}
        hint={t('Missions requiring a tier route to captains at or above it. Leave on Auto to classify from the model name.')}
        testID="captain-form-tier"
      />
      {supportsAutoApproveSwitch(form.runtime) ? (
        <SwitchField
          label={t('Auto-approve agent tool use (runs the CLI with its permission-bypass flag)')}
          value={form.autoApprove}
          onChange={(v) => patch({ autoApprove: v })}
          testID="captain-form-autoApprove"
        />
      ) : null}
      <CliPermissionPolicyField
        label={t('CLI tool permissions')}
        value={form.cliPermissionPolicy}
        onChange={(v) => patch({ cliPermissionPolicy: v })}
        allowBypass={canManageCliPolicy}
        disabled={!canManageCliPolicy}
        hint={`${t('How this captain handles shell commands, file edits, and fetches that need permission. Inherit: missions follow the auto-approve option when it is set, then the server default; Ask conversations use the server default (Settings > CLI Tool Permissions). A conversation can override it.')}${canManageCliPolicy ? '' : ` ${t('Only admins can change this.')}`}`}
        testID="captain-form-cliPermissionPolicy"
      />
      <MuxFields runtime={form.runtime} form={form} onChange={patch} />
      <TextField
        label={t('Allowed Personas (JSON array)')}
        value={form.allowedPersonas ?? ''}
        onChangeText={(v) => patch({ allowedPersonas: v })}
        placeholder={t('["Worker", "Judge"]')}
        autoCapitalize="none"
        autoCorrect={false}
        testID="captain-form-allowedPersonas"
      />
      <TextField label={t('Preferred Persona')} value={form.preferredPersona ?? ''} onChangeText={(v) => patch({ preferredPersona: v })} placeholder={t('e.g., Worker')} autoCapitalize="none" testID="captain-form-preferredPersona" />
      <TextField
        label={t('System Instructions')}
        value={form.systemInstructions}
        onChangeText={(v) => patch({ systemInstructions: v })}
        placeholder={t('e.g., You are a testing specialist. Always run tests before committing...')}
        multiline
        numberOfLines={4}
        testID="captain-form-systemInstructions"
      />
      <View style={styles.actions}>
        <Button label={saving ? t('Saving...') : t('Save')} onPress={() => void save()} busy={saving} testID="captain-form-save" />
        <Button label={t('Cancel')} variant="ghost" onPress={onClose} disabled={saving} testID="captain-form-cancel" />
      </View>
    </BottomSheet>
  );
}

const styles = StyleSheet.create({ actions: { marginTop: spacing.sm } });
