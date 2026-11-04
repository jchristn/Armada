import { useCallback, useEffect, useState } from 'react';
import { apiErrorMessage, getSettings, isApiStatus, updateSettings } from '../../api/client';
import { useLocale } from '../../context/LocaleContext';
import { USAGE_SELECTION_BOUNDS, USAGE_SELECTION_NUMERIC_FIELDS, USAGE_SELECTION_STRATEGY_KEYS, usageSelectionModeDescriptionKey, usageSelectionReadOnly, usageSelectionValid } from '../../lib/usageSelectionForm';
import type { CaptainSelectionStrategy, SettingsData, UsageSelectionSettings } from '../../types/models';

/** Uses the same server settings controls as usage capture, with optimistic revision checks. */
export default function UsageSelectionSettingsPanel({ canEdit }: { canEdit: boolean }) {
  const { t } = useLocale();
  const [settings, setSettings] = useState<SettingsData | null>(null);
  const [form, setForm] = useState<UsageSelectionSettings | null>(null);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState<{ error: boolean; text: string } | null>(null);
  const load = useCallback(async () => {
    setLoading(true); setMessage(null);
    try {
      const result = await getSettings();
      if (!result.usageSelection) throw new Error('Policy unavailable');
      setSettings(result); setForm({ ...result.usageSelection });
    } catch { setMessage({ error: true, text: t('usageSelection.loadFailed') }); }
    finally { setLoading(false); }
  }, [t]);
  useEffect(() => { if (canEdit) void load(); }, [canEdit, load]);
  if (!canEdit) return <section className="settings-section usage-settings"><h3>{t('usageSelection.title')}</h3><p className="text-dim">{t('usageSelection.adminOnly')}</p></section>;
  const capability = settings?.usageSelectionCapabilities;
  const readOnly = usageSelectionReadOnly(capability);
  const invalid = form && !usageSelectionValid(form);
  async function save() {
    if (!form || invalid || readOnly) return;
    setSaving(true); setMessage(null);
    try {
      const result = await updateSettings({ usageSelection: form, expectedUsageSelectionRevision: settings?.usageSelectionRevision });
      setSettings(result); setForm(result.usageSelection ? { ...result.usageSelection } : form);
      setMessage({ error: false, text: t('usageSelection.saved') });
    } catch (error) { setMessage({ error: true, text: isApiStatus(error, 409) ? t('usageSelection.conflict') : apiErrorMessage(error, t('usageSelection.saveFailed')) }); }
    finally { setSaving(false); }
  }
  return <section className="settings-section usage-settings usage-selection-settings" style={{ marginTop: '1.5rem' }} aria-labelledby="usage-selection-title">
    <div className="usage-panel-heading"><h3 id="usage-selection-title">{t('usageSelection.title')}</h3>
      <button type="button" className="btn btn-sm" onClick={() => void load()} disabled={loading || saving}>{t('Refresh')}</button></div>
    <p className="text-dim">{t(usageSelectionModeDescriptionKey(form?.mode))}</p>
    {loading && <p role="status">{t('Loading...')}</p>}
    {message && <p className={message.error ? 'usage-form-error' : 'text-dim'} role={message.error ? 'alert' : 'status'}>{message.text}</p>}
    {readOnly && <p className="text-dim">{t('usageSelection.environment')}</p>}
    {form && <>
      <fieldset className="usage-capture-fields" disabled={saving || loading || readOnly}>
        <legend>{t('usageSelection.title')}</legend>
        <div className="usage-capture-grid">
          <div className="form-group"><label htmlFor="usage-selection-mode">{t('usageSelection.mode')}</label>
            <select id="usage-selection-mode" value={form.mode} onChange={e => setForm({ ...form, mode: e.target.value as UsageSelectionSettings['mode'] })}>
              <option value="Disabled">{t('usageSelection.disabled')}</option><option value="Observe">{t('usageSelection.observe')}</option>
              <option value="Enforce" disabled={!capability?.enforcementAvailable}>{t('usageSelection.enforce')}</option>
            </select>{!capability?.enforcementAvailable && <span className="text-dim">{t('usageSelection.enforcementUnavailable')}</span>}</div>
          <div className="form-group"><label htmlFor="usage-selection-strategy">{t('usageSelection.strategy')}</label>
            <select id="usage-selection-strategy" aria-describedby="usage-selection-strategy-hint" value={form.strategy} onChange={e => setForm({ ...form, strategy: e.target.value as CaptainSelectionStrategy })}>
              {(capability?.strategies ?? Object.keys(USAGE_SELECTION_STRATEGY_KEYS) as CaptainSelectionStrategy[]).map(strategy => <option key={strategy} value={strategy}>{t(USAGE_SELECTION_STRATEGY_KEYS[strategy])}</option>)}
            </select><span className="text-dim" id="usage-selection-strategy-hint">{t('usageSelection.strategyHint')}</span></div>
          <div className="form-group"><label htmlFor="usage-selection-unknown">{t('usageSelection.unknown')}</label>
            <select id="usage-selection-unknown" value={form.unknownUsageBehavior} onChange={e => setForm({ ...form, unknownUsageBehavior: e.target.value as UsageSelectionSettings['unknownUsageBehavior'] })}>
              <option value="AllowDeprioritized">{t('usageSelection.allowUnknown')}</option><option value="Wait">{t('usageSelection.waitUnknown')}</option>
            </select></div>
        </div>
        <details><summary>{t('usageSelection.advanced')}</summary><div className="usage-capture-grid">
          {USAGE_SELECTION_NUMERIC_FIELDS.map(key => <div className="form-group" key={key}>
            <label htmlFor={`usage-selection-${key}`}>{t(`usageSelection.${key}`)}</label>
            <input id={`usage-selection-${key}`} type="number" min={USAGE_SELECTION_BOUNDS[key].min} max={USAGE_SELECTION_BOUNDS[key].max} step={USAGE_SELECTION_BOUNDS[key].step} value={Number.isNaN(form[key]) ? '' : form[key]}
              onChange={e => setForm({ ...form, [key]: e.target.value === '' ? NaN : Number(e.target.value) })} />
          </div>)}
        </div></details>
      </fieldset>
      {invalid && <p className="usage-form-error" role="alert">{t('usageSelection.invalid')}</p>}
      <button type="button" className="btn btn-primary" onClick={() => void save()} disabled={saving || loading || readOnly || !!invalid}>{t(saving ? 'Saving...' : 'Save')}</button>
    </>}
  </section>;
}
