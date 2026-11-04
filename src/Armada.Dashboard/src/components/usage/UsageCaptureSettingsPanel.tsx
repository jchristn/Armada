import { useCallback, useEffect, useState } from 'react';
import { apiErrorMessage, getSettings, getUsageLatest, updateSettings } from '../../api/client';
import { useLocale } from '../../context/LocaleContext';
import type { SettingsData, UsageCaptureSettings } from '../../types/models';
import {
  USAGE_SETTINGS_BOUNDS,
  buildUsageSettingsGroup,
  usageSettingsErrors,
  usageSettingsFormFrom,
  type UsageSettingsFormState,
  type UsageSettingsNumericField,
} from '../../lib/usageSettingsForm';

interface UsageCaptureSettingsPanelProps {
  /** GlobalAdmin: may change the server-wide capture settings. */
  canEdit: boolean;
}

const FIELD_LABELS: Record<UsageSettingsNumericField, { label: string; hint: string }> = {
  minimumIntervalSeconds: { label: 'Minimum interval (seconds)', hint: 'No account is captured more often than this, whatever a captain requests. 60 to 86400.' },
  probeTimeoutSeconds: { label: 'Probe timeout (seconds)', hint: 'A capture that takes longer is stopped and recorded as Failed. 1 to 300.' },
  maximumConcurrentProbes: { label: 'Concurrent captures (server)', hint: 'Most captures running at once across every host. 1 to 64.' },
  maximumConcurrentProbesPerHost: { label: 'Concurrent captures per host', hint: 'Most captures running at once on one host. 1 to 64.' },
  maximumBackoffSeconds: { label: 'Maximum backoff (seconds)', hint: 'Longest wait after repeated failures before the next attempt. 60 to 86400.' },
};

/** Server-wide usage capture switch and limits (GlobalAdmin). Ordinary users see a read-only explanation. */
export default function UsageCaptureSettingsPanel({ canEdit }: UsageCaptureSettingsPanelProps) {
  const { t } = useLocale();
  const [loaded, setLoaded] = useState<UsageCaptureSettings | null>(null);
  const [form, setForm] = useState<UsageSettingsFormState>(() => usageSettingsFormFrom(null));
  const [status, setStatus] = useState<'loading' | 'error' | 'ready'>('loading');
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState<{ kind: 'success' | 'error'; text: string } | null>(null);
  const [accounts, setAccounts] = useState<number | null>(null);

  const load = useCallback(async () => {
    if (!canEdit) return;
    setStatus('loading');
    try {
      const settings = await getSettings();
      const group = (settings.usageCapture ?? null) as UsageCaptureSettings | null;
      setLoaded(group);
      setForm(usageSettingsFormFrom(group));
      setStatus('ready');
    } catch {
      setStatus('error');
    }
    getUsageLatest({ pageSize: 1 }).then((r) => setAccounts(r.totalRecords ?? 0)).catch(() => setAccounts(null));
  }, [canEdit]);

  useEffect(() => { void load(); }, [load]);

  if (!canEdit) {
    return (
      <div className="settings-section usage-settings">
        <h3>{t('Usage capture')}</h3>
        <p className="text-dim">{t('Only a global administrator can change server-wide usage capture settings.')}</p>
      </div>
    );
  }

  const errors = usageSettingsErrors(form);
  const disabling = (loaded?.enabled ?? true) && form.enabled === false;
  const setValue = (key: UsageSettingsNumericField, value: string) => { setForm((f) => ({ ...f, values: { ...f.values, [key]: value } })); setMessage(null); };

  async function save() {
    if (errors.length > 0) return;
    setSaving(true);
    setMessage(null);
    try {
      const group = buildUsageSettingsGroup(form, loaded);
      const updated = await updateSettings({ usageCapture: group } as SettingsData);
      const next = (updated.usageCapture ?? group) as UsageCaptureSettings;
      setLoaded(next);
      setForm(usageSettingsFormFrom(next));
      setMessage({ kind: 'success', text: t('Usage capture settings saved.') });
    } catch (err: unknown) {
      // Keep the operator's edits so they can correct and retry.
      setMessage({ kind: 'error', text: apiErrorMessage(err, t('Usage capture settings could not be saved.')) });
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="settings-section usage-settings" style={{ marginTop: '1.5rem' }}>
      <h3 title={t('Server-wide switch and limits for reading provider account limits. Per-captain capture is configured on each captain.')}>{t('Usage capture')}</h3>
      {status === 'loading' && <p className="text-dim" role="status">{t('Loading usage capture settings...')}</p>}
      {status === 'error' && (
        <div className="usage-state usage-state-error" role="alert">
          <strong>{t('Usage capture settings could not be loaded.')}</strong>
          <button type="button" className="btn btn-sm" onClick={() => void load()}>{t('Retry')}</button>
        </div>
      )}
      {status === 'ready' && (
        <fieldset className="usage-capture-fields" disabled={saving}>
          <legend>{t('Server switch')}</legend>
          <div className="form-group usage-capture-toggle">
            <label htmlFor="usage-settings-enabled">
              <input id="usage-settings-enabled" type="checkbox" checked={form.enabled ?? true} onChange={(e) => { setForm((f) => ({ ...f, enabled: e.target.checked })); setMessage(null); }} />
              {' '}{t('Enable account usage capture on this server')}
            </label>
            <span className="text-dim">{form.enabled === null ? t('Inherit (enabled)') : t('Capture reads account limits without sending a prompt.')}</span>
          </div>
          {disabling && (
            <p className="usage-form-error" role="status">
              {accounts !== null
                ? t('Saving stops scheduled capture for {{count}} observed accounts and cancels captures in progress. Stored history is kept.', { count: accounts })
                : t('Saving stops every scheduled capture and cancels captures in progress. Stored history is kept.')}
            </p>
          )}
          <div className="usage-capture-grid">
            {(Object.keys(USAGE_SETTINGS_BOUNDS) as UsageSettingsNumericField[]).map((key) => (
              <div className="form-group" key={key}>
                <label htmlFor={`usage-settings-${key}`}>{t(FIELD_LABELS[key].label)}</label>
                <input id={`usage-settings-${key}`} type="number" inputMode="numeric" min={USAGE_SETTINGS_BOUNDS[key].min} max={USAGE_SETTINGS_BOUNDS[key].max}
                  value={form.values[key]} aria-invalid={errors.includes(key)} aria-describedby={`usage-settings-${key}-hint`} onChange={(e) => setValue(key, e.target.value)} />
                <span id={`usage-settings-${key}-hint`} className="text-dim">{t(FIELD_LABELS[key].hint)}</span>
              </div>
            ))}
          </div>
          <div className="form-group usage-capture-toggle">
            <label htmlFor="usage-settings-pty">
              <input id="usage-settings-pty" type="checkbox" checked={form.allowPtyFallback ?? true} onChange={(e) => { setForm((f) => ({ ...f, allowPtyFallback: e.target.checked })); setMessage(null); }} />
              {' '}{t('Allow interactive terminal (PTY) fallback')}
            </label>
            <span className="text-dim">{t('When automatic capture cannot read usage, Armada may open the CLI briefly and read its usage screen.')}</span>
          </div>
          <p className="text-dim">{t('Snapshot and rollup retention are set with the other retention settings.')}</p>
          {errors.length > 0 && <p className="usage-form-error" role="alert">{t('Fix the highlighted values: each must be a whole number inside its range.')}</p>}
          {message && <p className={message.kind === 'success' ? 'usage-form-saved' : 'usage-form-error'} role={message.kind === 'success' ? 'status' : 'alert'}>{message.text}</p>}
          <div className="usage-capture-actions">
            <button type="button" className="btn btn-primary btn-sm" disabled={errors.length > 0 || saving} onClick={() => void save()}>
              {saving ? t('Saving...') : t('Save usage capture settings')}
            </button>
          </div>
        </fieldset>
      )}
    </div>
  );
}
