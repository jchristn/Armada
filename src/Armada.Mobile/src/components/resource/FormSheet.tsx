import { useState } from 'react';
import { StyleSheet, View } from 'react-native';
import { useLocale } from '../../i18n/LocaleContext';
import { errorText } from '../../resource/useLoad';
import { spacing } from '../../theme/typography';
import { AppText } from '../ui/AppText';
import { BottomSheet } from '../ui/BottomSheet';
import { Button } from '../ui/Button';
import { SelectField, type SelectOption } from '../ui/SelectSheet';
import { SwitchField } from '../ui/SwitchField';
import { TextField } from '../ui/TextField';

/** A form value: text fields hold strings (numbers too, parsed by the caller), switches hold booleans. */
export type FormValue = string | boolean;
export type FormValues = Record<string, FormValue>;

interface BaseField {
  key: string;
  label: string;
  hint?: string | null;
  /** Text fields: blank is an error. */
  required?: boolean;
  disabled?: boolean;
}

export type FormField =
  | (BaseField & { kind: 'text' | 'multiline' | 'secret' | 'number' | 'integer'; placeholder?: string })
  | (BaseField & { kind: 'switch' })
  | (BaseField & { kind: 'select'; options: SelectOption<string>[]; placeholder?: string })
  | (BaseField & { kind: 'note' });

/** String value of a form field ('' when missing or a switch). */
export function str(values: FormValues, key: string): string {
  const v = values[key];
  return typeof v === 'string' ? v : '';
}

/** Trimmed string value, or null when blank (the dashboard's `value.trim() || null`). */
export function strOrNull(values: FormValues, key: string): string | null {
  return str(values, key).trim() || null;
}

/** Boolean value of a switch. */
export function bool(values: FormValues, key: string): boolean {
  return values[key] === true;
}

/** Integer value of a number field, or the fallback when blank or not a number. */
export function int(values: FormValues, key: string, fallback: number): number {
  const n = parseInt(str(values, key), 10);
  return Number.isNaN(n) ? fallback : n;
}

/** Number value of a number field, or null when blank or not a number. */
export function numOrNull(values: FormValues, key: string): number | null {
  const raw = str(values, key).trim();
  if (!raw) return null;
  const n = Number(raw);
  return Number.isFinite(n) ? n : null;
}

/** Newline- or comma-separated list field to an array of trimmed, non-empty strings. */
export function list(values: FormValues, key: string): string[] {
  return str(values, key).split(/[\n,]/).map((s) => s.trim()).filter(Boolean);
}

export interface FormSheetProps {
  open: boolean;
  title: string;
  /** Initial values; read when the sheet opens. */
  initial: FormValues;
  /** Fields, computed from the current values so one field can depend on another. */
  fields: (values: FormValues) => FormField[];
  submitLabel: string;
  /** Saves; a thrown error is shown in the sheet and keeps it open. Resolve to close it. */
  onSubmit: (values: FormValues) => Promise<void>;
  onClose: () => void;
  /** Extra validation: return an error message to block saving. */
  validate?: (values: FormValues) => string | null;
  /** Called on every change (for example to prefill one field from another). Return the values to use. */
  onChange?: (next: FormValues, previous: FormValues) => FormValues;
  testID?: string;
}

/**
 * The mobile form of the dashboard's create / edit modals: a bottom sheet with labelled fields, Save and Cancel,
 * required-field checks, and the server's error shown in place.
 */
export function FormSheet(props: FormSheetProps) {
  const { t } = useLocale();
  return (
    <BottomSheet open={props.open} title={props.title} onClose={props.onClose} closeLabel={t('Close')} testID={props.testID}>
      {/* Mounted only while open, so every opening starts from the caller's initial values. */}
      {props.open ? <FormBody {...props} /> : null}
    </BottomSheet>
  );
}

function FormBody({ initial, fields, submitLabel, onSubmit, onClose, validate, onChange, testID }: FormSheetProps) {
  const { t } = useLocale();
  const [values, setValues] = useState<FormValues>(initial);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const set = (key: string, value: FormValue) => {
    setValues((prev) => {
      const next = { ...prev, [key]: value };
      return onChange ? onChange(next, prev) : next;
    });
  };

  const current = fields(values);

  async function submit() {
    for (const f of current) {
      if (f.required && f.kind !== 'switch' && f.kind !== 'note' && !str(values, f.key).trim()) {
        setError(t('{{field}} is required.', { field: f.label }));
        return;
      }
    }
    const invalid = validate?.(values);
    if (invalid) {
      setError(invalid);
      return;
    }
    setSaving(true);
    setError('');
    try {
      await onSubmit(values);
    } catch (err: unknown) {
      setError(errorText(err, t('Save failed.')));
    } finally {
      setSaving(false);
    }
  }

  return (
    <>
      {current.map((f) => {
        const id = testID ? `${testID}-${f.key}` : undefined;
        switch (f.kind) {
          case 'note':
            return <AppText key={f.key} variant="caption" muted style={styles.note}>{f.label}</AppText>;
          case 'switch':
            return <SwitchField key={f.key} label={f.label} hint={f.hint} value={bool(values, f.key)} onChange={(v) => set(f.key, v)} disabled={f.disabled} testID={id} />;
          case 'select':
            return (
              <SelectField
                key={f.key}
                label={f.label}
                hint={f.hint}
                value={str(values, f.key)}
                options={f.options}
                placeholder={f.placeholder}
                onChange={(v) => set(f.key, v)}
                closeLabel={t('Close')}
                disabled={f.disabled}
                testID={id}
              />
            );
          default:
            return (
              <TextField
                key={f.key}
                label={f.required ? `${f.label} *` : f.label}
                hint={f.hint}
                value={str(values, f.key)}
                onChangeText={(v) => set(f.key, v)}
                placeholder={f.placeholder}
                editable={!f.disabled}
                secret={f.kind === 'secret'}
                revealLabel={t('Show')}
                hideLabel={t('Hide')}
                multiline={f.kind === 'multiline'}
                numberOfLines={f.kind === 'multiline' ? 4 : undefined}
                keyboardType={f.kind === 'number' ? 'decimal-pad' : f.kind === 'integer' ? 'number-pad' : 'default'}
                autoCapitalize={f.kind === 'multiline' ? 'sentences' : 'none'}
                autoCorrect={false}
                testID={id}
              />
            );
        }
      })}
      {error ? <AppText color="danger" accessibilityRole="alert" style={styles.error} testID={testID ? `${testID}-error` : undefined}>{error}</AppText> : null}
      <View style={styles.actions}>
        <Button label={t('Cancel')} variant="ghost" onPress={onClose} disabled={saving} />
        <Button label={saving ? t('Saving...') : submitLabel} onPress={() => void submit()} busy={saving} testID={testID ? `${testID}-submit` : undefined} />
      </View>
    </>
  );
}

const styles = StyleSheet.create({
  note: { marginBottom: spacing.md },
  error: { marginBottom: spacing.md },
  actions: { flexDirection: 'row', justifyContent: 'flex-end', flexWrap: 'wrap', gap: spacing.sm },
});
