import { useState } from 'react';
import { getSettings, updateSettings } from '@dashboard/api/client';
import { buildUsageSettingsGroup, usageSettingsFormFrom, usageSettingsErrors, USAGE_SETTINGS_BOUNDS, type UsageSettingsNumericField } from '@dashboard/lib/usageSettingsForm';
import { useLiveResource } from '../../build/useLiveResource';
import { SwitchField } from '../../build/fields';
import { AppText, Button, Section, TextField } from '../../components/ui';
import { useLocale } from '../../i18n/LocaleContext';

/** Global capture configuration, displayed only to global admins by the settings screen. */
export function UsageSettings() {
  const { t } = useLocale();
  const labels: Record<UsageSettingsNumericField, string> = { minimumIntervalSeconds: 'Minimum interval', probeTimeoutSeconds: 'Probe timeout', maximumConcurrentProbes: 'Maximum concurrent probes', maximumConcurrentProbesPerHost: 'Maximum probes per host', maximumBackoffSeconds: 'Maximum backoff' };
  const [form, setForm] = useState(() => usageSettingsFormFrom(null));
  const [message, setMessage] = useState('');
  const [busy, setBusy] = useState(false);
  const resource = useLiveResource(async () => { const settings = await getSettings(); setForm(usageSettingsFormFrom(settings.usageCapture)); return settings; }, []);
  async function save() {
    setBusy(true);
    try { await updateSettings({ usageCapture: buildUsageSettingsGroup(form, resource.data?.usageCapture) }); setMessage(t('Saved')); }
    catch (error) { setMessage(error instanceof Error ? error.message : t('Save failed.')); }
    finally { setBusy(false); }
  }
  return <Section title={t('Usage capture')}>
    {resource.error ? <><AppText>{resource.error}</AppText><Button label={t('Retry')} onPress={() => void resource.reload()} /></> : null}
    <SwitchField label={t('Capture account usage')} value={form.enabled ?? true} onChange={(enabled) => setForm({ ...form, enabled })} hint={form.enabled === null ? t('Inherit (enabled)') : undefined} />
    <SwitchField label={t('Allow PTY fallback')} value={form.allowPtyFallback ?? true} onChange={(allowPtyFallback) => setForm({ ...form, allowPtyFallback })} />
    {(Object.keys(USAGE_SETTINGS_BOUNDS) as UsageSettingsNumericField[]).map((key) => <TextField key={key} label={t(labels[key])} value={form.values[key]} onChangeText={(text) => setForm({ ...form, values: { ...form.values, [key]: text } })} keyboardType="numeric" />)}
    <Button label={t('Save')} disabled={!resource.data || busy || usageSettingsErrors(form).length > 0} onPress={() => void save()} />
    {message ? <AppText>{message}</AppText> : null}
  </Section>;
}
