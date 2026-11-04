import { useState } from 'react';
import { getSettings, updateSettings } from '@dashboard/api/client';
import { buildUsageSettingsGroup, usageSettingsFormFrom, usageSettingsErrors, USAGE_SETTINGS_BOUNDS, type UsageSettingsNumericField } from '@dashboard/lib/usageSettingsForm';
import { useLiveResource } from '../../build/useLiveResource';
import { SwitchField } from '../../build/fields';
import { Banner, Button, TextField } from '../../components/ui';
import { AppText } from '../../components/ui/AppText';
import { SaveRow, SettingsSection } from '../system/settingsParts';
import { useLocale } from '../../i18n/LocaleContext';

/** Global capture configuration, displayed only to global admins by the settings screen. */
export function UsageSettings() {
  const { t } = useLocale();
  const labels: Record<UsageSettingsNumericField, string> = { minimumIntervalSeconds: 'Minimum interval', probeTimeoutSeconds: 'Probe timeout', maximumConcurrentProbes: 'Maximum concurrent probes', maximumConcurrentProbesPerHost: 'Maximum probes per host', maximumBackoffSeconds: 'Maximum backoff' };
  const [form, setForm] = useState(() => usageSettingsFormFrom(null));
  const [message, setMessage] = useState<{ error: boolean; text: string } | null>(null);
  const [busy, setBusy] = useState(false);
  const resource = useLiveResource(async () => { const settings = await getSettings(); setForm(usageSettingsFormFrom(settings.usageCapture)); return settings; }, []);
  async function save() {
    setBusy(true); setMessage(null);
    try { await updateSettings({ usageCapture: buildUsageSettingsGroup(form, resource.data?.usageCapture) }); setMessage({ error: false, text: t('Saved') }); }
    catch (error) { setMessage({ error: true, text: error instanceof Error ? error.message : t('Save failed.') }); }
    finally { setBusy(false); }
  }
  return <SettingsSection title={t('Usage capture')} testID="settings-usage-capture">
    {resource.error ? <><Banner tone="danger" title={resource.error} /><Button label={t('Retry')} variant="secondary" onPress={() => void resource.reload()} /></> : null}
    <SwitchField label={t('Capture account usage')} value={form.enabled ?? true} onChange={(enabled) => setForm({ ...form, enabled })} hint={form.enabled === null ? t('Inherit (enabled)') : undefined} />
    <SwitchField label={t('Allow PTY fallback')} value={form.allowPtyFallback ?? true} onChange={(allowPtyFallback) => setForm({ ...form, allowPtyFallback })} />
    {(Object.keys(USAGE_SETTINGS_BOUNDS) as UsageSettingsNumericField[]).map((key) => <TextField key={key} label={t(labels[key])} value={form.values[key]} onChangeText={(text) => setForm({ ...form, values: { ...form.values, [key]: text } })} keyboardType="numeric" />)}
    <SaveRow label={t('Save')} saving={busy} disabled={!resource.data || busy || usageSettingsErrors(form).length > 0} onSave={() => void save()} />
    {message ? <AppText variant="caption" color={message.error ? 'danger' : 'textMuted'} accessibilityRole={message.error ? 'alert' : undefined} accessibilityLiveRegion="polite">{message.text}</AppText> : null}
  </SettingsSection>;
}
