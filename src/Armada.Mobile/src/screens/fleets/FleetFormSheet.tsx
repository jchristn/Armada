import { useState } from 'react';
import { View } from 'react-native';
import { createFleet, updateFleet } from '@dashboard/api/client';
import type { Fleet, Pipeline } from '@dashboard/types/models';
import { AppText, BottomSheet, Button, TextField } from '../../components/ui';
import { SelectField, type SelectOption } from '../../components/ui/SelectSheet';
import { errorMessage } from '../../build/useLiveResource';
import { useLocale } from '../../i18n/LocaleContext';

/** A pipeline as the dashboard lists it in pickers: name and its persona chain. */
export function pipelineLabel(p: Pipeline): string {
  const chain = (p.stages ?? []).map((s) => s.personaName).join(' -> ');
  return chain ? `${p.name} (${chain})` : p.name;
}

export interface FleetFormSheetProps {
  open: boolean;
  /** The fleet being edited, or null to create one. */
  fleet: Fleet | null;
  pipelines: Pipeline[];
  onClose: () => void;
  onSaved: (saved: Fleet | null, name: string) => void;
}

/** Create / edit a fleet (the dashboard's fleet modal): name, description, default pipeline. */
export function FleetFormSheet(props: FleetFormSheetProps) {
  const { t } = useLocale();
  const title = props.fleet ? t('Edit Fleet') : t('Create Fleet');
  return (
    <BottomSheet open={props.open} title={title} onClose={props.onClose} closeLabel={t('Close')} testID="fleet-form">
      {/* Mounted only while open, so every opening starts from the fleet's current values. */}
      {props.open ? <FleetForm {...props} /> : null}
    </BottomSheet>
  );
}

function FleetForm({ fleet, pipelines, onClose, onSaved }: FleetFormSheetProps) {
  const { t } = useLocale();
  const [name, setName] = useState(fleet?.name ?? '');
  const [description, setDescription] = useState(fleet?.description ?? '');
  const [pipelineId, setPipelineId] = useState(fleet?.defaultPipelineId ?? '');
  const [submitted, setSubmitted] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const nameError = submitted && !name.trim() ? t('Name is required.') : null;

  const options: SelectOption<string>[] = [
    { value: '', label: t('None (WorkerOnly)') },
    ...pipelines.map((p) => ({ value: p.id, label: pipelineLabel(p) })),
  ];

  async function save() {
    setSubmitted(true);
    if (!name.trim()) return;
    setSaving(true);
    setError(null);
    try {
      const payload: Partial<Fleet> = { name, description };
      if (pipelineId) payload.defaultPipelineId = pipelineId;
      else if (fleet) payload.defaultPipelineId = '';
      const saved = fleet ? await updateFleet(fleet.id, payload) : await createFleet(payload);
      onSaved(saved ?? null, name);
    } catch (e) {
      setError(errorMessage(e) || t('Save failed.'));
    } finally {
      setSaving(false);
    }
  }

  return (
    <View>
      {error ? <AppText color="danger" accessibilityRole="alert" testID="fleet-form-error">{error}</AppText> : null}
      <TextField label={t('Name')} value={name} onChangeText={setName} error={nameError} testID="fleet-form-name" autoCapitalize="none" />
      <TextField label={t('Description')} value={description} onChangeText={setDescription} testID="fleet-form-description" />
      <SelectField label={t('Default Pipeline')} value={pipelineId} options={options} onChange={setPipelineId} closeLabel={t('Close')} testID="fleet-form-pipeline" />
      <Button label={t('Save')} onPress={() => void save()} busy={saving} testID="fleet-form-save" />
      <Button label={t('Cancel')} variant="ghost" onPress={onClose} disabled={saving} testID="fleet-form-cancel" />
    </View>
  );
}
