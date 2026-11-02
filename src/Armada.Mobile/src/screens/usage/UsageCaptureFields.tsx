import { useEffect, useState } from 'react';
import { listHarbors } from '@dashboard/api/client';
import type { Harbor } from '@dashboard/types/models';
import type { UsageCaptureFormState } from '@dashboard/lib/usageCaptureForm';
import { SwitchField } from '../../build/fields';
import { TextField } from '../../components/ui';
import { SelectField } from '../../components/ui/SelectSheet';
import { useLocale } from '../../i18n/LocaleContext';

/** Native capture fields preserve inherited null choices and unsaved edits on failure. */
export function UsageCaptureFields({ value, onChange, tenantId }: { value: UsageCaptureFormState; tenantId?: string | null; onChange: (value: UsageCaptureFormState) => void }) {
  const { t } = useLocale();
  const [harbors, setHarbors] = useState<Harbor[]>([]);
  useEffect(() => {
    let disposed = false;
    async function loadHosts() {
      try {
        const page = await listHarbors();
        if (!disposed) setHarbors((page ?? []).filter((host) => !tenantId || host.tenantId === tenantId));
      } catch { /* A saved choice stays visible if the list is unavailable. */ }
    }
    void loadHosts();
    return () => { disposed = true; };
  }, [tenantId]);
  const update = (patch: Partial<UsageCaptureFormState>) => onChange({ ...value, ...patch });
  return <>
    <SwitchField label={t('Capture account usage for this captain')} value={value.enabled ?? true} hint={value.enabled === null ? t('Inherit (enabled)') : undefined} onChange={(enabled) => update({ enabled })} />
    <SelectField closeLabel={t("Close")} label={t('Capture mode')} value={value.mode} options={['Auto', 'Disabled', 'Custom'].map((mode) => ({ value: mode, label: t(mode) }))} onChange={(mode) => update({ mode: mode as UsageCaptureFormState['mode'] })} />
    <SelectField closeLabel={t('Close')} label={t('Credential Harbor')} value={value.harborId ?? ''} options={[
      { value: '', label: t('Automatic (current dock or standalone Admiral)') },
      ...harbors.map((host) => ({ value: host.id, label: host.name })),
      ...(value.harborId && !harbors.some((host) => host.id === value.harborId) ? [{ value: value.harborId, label: value.harborId }] : []),
    ]} onChange={(harborId) => update({ harborId })} />
    <TextField label={t('Requested interval')} value={value.interval} onChangeText={(interval) => update({ interval })} keyboardType="numeric" />
    {value.mode === 'Custom' ? <><TextField label={t('Executable')} value={value.executable} onChangeText={(executable) => update({ executable })} /><TextField label={t('Arguments')} value={value.argumentsText} onChangeText={(argumentsText) => update({ argumentsText })} multiline /></> : null}
  </>;
}
