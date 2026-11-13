import { useEffect, useState } from 'react';
import { apiErrorMessage, listHarbors, updateCaptain } from '../../api/client';
import { useLocale } from '../../context/LocaleContext';
import type { Captain, Harbor, UsageCaptureMode } from '../../types/models';
import {
  USAGE_CAPTURE_FORM_ERROR_TEXT,
  USAGE_INTERVAL_MAX,
  USAGE_INTERVAL_MIN,
  buildUsageCapturePayload,
  usageCaptureFormError,
  usageCaptureFormFromCaptain,
  type UsageCaptureFormState,
} from '../../lib/usageCaptureForm';

interface UsageCaptureFieldsProps {
  captain: Captain;
  /** Captain mutation permission; without it the section is read-only. */
  canEdit: boolean;
  onSaved: (captain: Captain) => void;
}

/** Captain usage capture configuration: switch, mode, interval and the Custom executable contract in one section. */
export default function UsageCaptureFields({ captain, canEdit, onSaved }: UsageCaptureFieldsProps) {
  const { t } = useLocale();
  const [form, setForm] = useState<UsageCaptureFormState>(() => usageCaptureFormFromCaptain(captain));
  const [harbors, setHarbors] = useState<Harbor[]>([]);
  const [hostError, setHostError] = useState('');
  const [saving, setSaving] = useState(false);
  const [serverError, setServerError] = useState('');
  const [saved, setSaved] = useState(false);
  const [touched, setTouched] = useState(false);

  useEffect(() => {
    if (!touched) setForm(usageCaptureFormFromCaptain(captain));
  }, [captain, touched]);

  useEffect(() => {
    let disposed = false;
    async function loadHosts() {
      try {
        const page = await listHarbors();
        if (!disposed) setHarbors((page ?? []).filter((host) => host.tenantId === captain.tenantId));
      } catch {
        if (!disposed) setHostError(t('Credential Harbors could not be loaded.'));
      }
    }
    void loadHosts();
    return () => { disposed = true; };
  // Locale changes do not change host identity or require another request.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [captain.tenantId]);

  const formError = usageCaptureFormError(form);
  const update = (patch: Partial<UsageCaptureFormState>) => { setForm((f) => ({ ...f, ...patch })); setTouched(true); setSaved(false); };

  async function save() {
    if (formError) return;
    setSaving(true);
    setServerError('');
    try {
      const updated = await updateCaptain(captain.id, buildUsageCapturePayload(form));
      setTouched(false);
      setSaved(true);
      onSaved(updated);
    } catch (err: unknown) {
      // Unsaved edits stay in the form so the operator can correct them.
      setServerError(apiErrorMessage(err, t('Usage capture settings could not be saved.')));
    } finally {
      setSaving(false);
    }
  }

  const fieldId = (name: string) => `usage-capture-${name}-${captain.id}`;
  return (
    <fieldset className="usage-capture-fields" disabled={!canEdit || saving}>
      <legend>{t('Capture settings')}</legend>
      {!canEdit && <p className="text-dim">{t('Only an administrator can change usage capture for this captain.')}</p>}
      <div className="form-group usage-capture-toggle">
        <label htmlFor={fieldId('enabled')}>
          <input id={fieldId('enabled')} type="checkbox" checked={form.enabled ?? true} onChange={(e) => update({ enabled: e.target.checked })} />
          {' '}{t('Capture account usage for this captain')}
        </label>
        <span className="text-dim">{t('Capture reads account limits without sending a prompt.')} {form.enabled === null && t('Inherit (enabled)')}</span>
      </div>
      <div className="usage-capture-grid">
        <div className="form-group">
          <label htmlFor={fieldId('mode')}>{t('Capture mode')}</label>
          <select id={fieldId('mode')} value={form.mode} onChange={(e) => update({ mode: e.target.value as UsageCaptureMode })}>
            <option value="Auto">{t('Auto (built-in reader for this runtime)')}</option>
            <option value="Custom">{t('Custom executable')}</option>
            <option value="Disabled">{t('Disabled')}</option>
          </select>
        </div>
        <div className="form-group">
          <label htmlFor={fieldId('interval')}>{t('Interval (seconds)')}</label>
          <input id={fieldId('interval')} type="number" inputMode="numeric" min={USAGE_INTERVAL_MIN} max={USAGE_INTERVAL_MAX} value={form.interval}
            aria-invalid={formError === 'intervalInvalid'} onChange={(e) => update({ interval: e.target.value })} />
          <span className="text-dim">{t('Requested interval. The server raises it to the global minimum and shares one schedule per account, so the effective interval can be longer.')}</span>
        </div>
      </div>
      <div className="form-field">
        <label htmlFor={fieldId('harbor')}>{t('Credential Harbor')}</label>
        <select id={fieldId('harbor')} value={form.harborId ?? ''} onChange={(event) => update({ harborId: event.target.value })}>
          <option value="">{t('Automatic (current dock or standalone Admiral)')}</option>
          {form.harborId && !harbors.some((host) => host.id === form.harborId) && <option value={form.harborId}>{form.harborId}</option>}
          {harbors.map((host) => <option key={host.id} value={host.id}>{host.name} ({host.enabled ? host.connectionStatus : t('Disabled')})</option>)}
        </select>
        <p className="text-dim">{t('Select the machine where this runtime is signed in. This choice is saved for scheduled capture while the captain is idle and does not change mission placement.')}</p>
        {hostError && <p className="usage-form-error" role="alert">{hostError}</p>}
      </div>
      {form.mode === 'Custom' && (
        <div className="usage-capture-custom">
          <div className="form-group">
            <label htmlFor={fieldId('executable')}>{t('Executable path')}</label>
            <input id={fieldId('executable')} type="text" className="mono" value={form.executable} placeholder="/opt/usage/read-quota"
              aria-invalid={formError === 'executableRequired' || formError === 'executableShellSyntax'} onChange={(e) => update({ executable: e.target.value })} />
          </div>
          <div className="form-group">
            <label htmlFor={fieldId('arguments')}>{t('Arguments (one per line)')}</label>
            <textarea id={fieldId('arguments')} className="mono" rows={3} value={form.argumentsText} onChange={(e) => update({ argumentsText: e.target.value })} />
          </div>
          <p className="text-dim">{t('Armada runs the executable directly, never through a shell, and expects one JSON object with schemaVersion 1 on standard output.')}</p>
        </div>
      )}
      <p className="text-dim">{t('Interactive terminal fallback follows the server setting and stops at login, trust or setup screens.')}</p>
      {formError && <p className="usage-form-error" role="alert">{t(USAGE_CAPTURE_FORM_ERROR_TEXT[formError])}</p>}
      {serverError && <p className="usage-form-error" role="alert">{serverError}</p>}
      {saved && !serverError && <p className="usage-form-saved" role="status">{t('Usage capture settings saved.')}</p>}
      {canEdit && (
        <div className="usage-capture-actions">
          <button type="button" className="btn btn-primary btn-sm" onClick={() => void save()} disabled={!!formError || saving || !touched}
            title={formError ? t(USAGE_CAPTURE_FORM_ERROR_TEXT[formError]) : t('Save the capture settings for this captain. Other captain settings are not changed.')}>
            {saving ? t('Saving...') : t('Save capture settings')}
          </button>
        </div>
      )}
    </fieldset>
  );
}
