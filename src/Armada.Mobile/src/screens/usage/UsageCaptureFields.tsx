import type { UsageCaptureFormState } from '@dashboard/lib/usageCaptureForm';
import { SwitchField } from '../../build/fields';
import { TextField } from '../../components/ui';
import { SelectField } from '../../components/ui/SelectSheet';
import { useLocale } from '../../i18n/LocaleContext';

/** Native capture fields preserve inherited null choices and unsaved edits on failure. */
export function UsageCaptureFields({ value, onChange }: { value: UsageCaptureFormState; onChange: (value: UsageCaptureFormState) => void }) {
  const { t } = useLocale();
  const update = (patch: Partial<UsageCaptureFormState>) => onChange({ ...value, ...patch });
  return <>
    <SwitchField label={t('Capture account usage for this captain')} value={value.enabled ?? true} hint={value.enabled === null ? t('Inherit (enabled)') : undefined} onChange={(enabled) => update({ enabled })} />
    <SelectField closeLabel={t("Close")} label={t('Capture mode')} value={value.mode} options={['Auto', 'Disabled', 'Custom'].map((mode) => ({ value: mode, label: t(mode) }))} onChange={(mode) => update({ mode: mode as UsageCaptureFormState['mode'] })} />
    <TextField label={t('Requested interval')} value={value.interval} onChangeText={(interval) => update({ interval })} keyboardType="numeric" />
    {value.mode === 'Custom' ? <><TextField label={t('Executable')} value={value.executable} onChangeText={(executable) => update({ executable })} /><TextField label={t('Arguments')} value={value.argumentsText} onChangeText={(argumentsText) => update({ argumentsText })} multiline /></> : null}
  </>;
}
