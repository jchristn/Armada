import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import type { MissionMode, Vessel } from '@dashboard/types/models';
import { BottomSheet, Button, SegmentedControl, SelectField, TextField } from '../../../components/ui';
import { useLocale } from '../../../i18n/LocaleContext';
import { spacing } from '../../../theme/typography';

export interface MissionFormValues {
  title: string;
  description: string;
  vesselId: string;
  priority: number;
  mode: MissionMode;
}

/** Pure: whether the form can be submitted (create needs a title and a vessel; edit needs a title). */
export function missionFormValid(values: MissionFormValues, requireVessel: boolean): boolean {
  return !!values.title.trim() && (!requireVessel || !!values.vesselId) && Number.isFinite(values.priority);
}

/**
 * Create Mission (title, description, vessel, mode, priority) and Edit Mission (title, description, priority), the
 * dashboard's two mission forms, in a bottom sheet.
 */
export function MissionFormSheet({ open, kind, initial, vessels, busy, onClose, onSubmit }: {
  open: boolean;
  kind: 'create' | 'edit';
  initial: MissionFormValues;
  vessels: Vessel[];
  busy?: boolean;
  onClose: () => void;
  onSubmit: (values: MissionFormValues) => void;
}) {
  const { t } = useLocale();
  return (
    <BottomSheet open={open} title={kind === 'create' ? t('Create Mission') : t('Edit Mission')} onClose={onClose} closeLabel={t('Close')} testID={`mission-${kind}-sheet`}>
      {open ? <MissionForm kind={kind} initial={initial} vessels={vessels} busy={busy} onClose={onClose} onSubmit={onSubmit} /> : null}
    </BottomSheet>
  );
}

function MissionForm({ kind, initial, vessels, busy, onClose, onSubmit }: {
  kind: 'create' | 'edit';
  initial: MissionFormValues;
  vessels: Vessel[];
  busy?: boolean;
  onClose: () => void;
  onSubmit: (values: MissionFormValues) => void;
}) {
  const { t } = useLocale();
  const [values, setValues] = useState<MissionFormValues>(initial);
  const [priorityText, setPriorityText] = useState(String(initial.priority));
  const set = (patch: Partial<MissionFormValues>) => setValues((v) => ({ ...v, ...patch }));
  const valid = missionFormValid(values, kind === 'create');
  return (
    <View>
      <TextField label={t('Title')} value={values.title} onChangeText={(title) => set({ title })} testID={`mission-${kind}-title`} />
      <TextField
        label={t('Description')}
        value={values.description}
        onChangeText={(description) => set({ description })}
        multiline
        numberOfLines={4}
        textAlignVertical="top"
        testID={`mission-${kind}-description`}
      />
      {kind === 'create' ? (
        <>
          <SelectField
            label={t('Vessel')}
            value={values.vesselId}
            onChange={(vesselId) => set({ vesselId })}
            placeholder={t('Select a vessel...')}
            closeLabel={t('Close')}
            searchLabel={t('Search...')}
            options={vessels.map((v) => ({ value: v.id, label: v.name }))}
            testID="mission-create-vessel"
          />
          <SegmentedControl<MissionMode>
            label={t('Mode')}
            value={values.mode}
            onChange={(mode) => set({ mode })}
            options={[
              { value: 'Implementation', label: t('Implementation') },
              { value: 'Audit', label: t('Audit (read-only)') },
              { value: 'Research', label: t('Research (read-only)') },
            ]}
          />
        </>
      ) : null}
      <TextField
        label={t('Priority')}
        value={priorityText}
        keyboardType="number-pad"
        onChangeText={(text) => { setPriorityText(text); set({ priority: text.trim() === '' ? Number.NaN : Number(text) }); }}
        testID={`mission-${kind}-priority`}
      />
      <View style={styles.actions}>
        <Button label={t('Cancel')} variant="ghost" onPress={onClose} />
        <Button
          label={kind === 'create' ? t('Create') : t('Save')}
          onPress={() => onSubmit({ ...values, title: values.title.trim() })}
          disabled={!valid}
          busy={busy}
          testID={`mission-${kind}-submit`}
        />
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  actions: { flexDirection: 'row', justifyContent: 'flex-end', gap: spacing.sm },
});
