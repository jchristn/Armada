import { useState } from 'react';
import { applyModelAlertAction, apiErrorMessage, ignoreModelAlert } from '../api/client';
import type { ModelAlert, ModelAlertApplyResult, ModelAlertSuggestedAction } from '../types/models';
import { useLocale } from '../context/LocaleContext';

interface ModelAlertActionsProps {
  alert: ModelAlert;
  /** Called after an action was applied (reload the inbox). */
  onApplied?: (result: ModelAlertApplyResult | null) => void;
}

/**
 * The suggested actions of a model scorecard alert (a model_alert inbox item). Nothing changes until the operator
 * clicks one: available actions apply through the REST API (Ignore hides the alert for the server default of 7 days),
 * unavailable ones are disabled with the server's reason as the tooltip. The latest applied action's result is shown.
 */
export default function ModelAlertActions({ alert, onApplied }: ModelAlertActionsProps) {
  const { t } = useLocale();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const suggested = alert.suggestedActions ?? [];
  const records = alert.actions ?? [];
  const last = records.length > 0 ? records[records.length - 1] : null;

  async function apply(action: ModelAlertSuggestedAction) {
    setBusy(true);
    setError('');
    try {
      const result = action.action === 'Ignore'
        ? await ignoreModelAlert(alert.id)
        : await applyModelAlertAction(alert.id, action.action);
      onApplied?.(result ?? null);
    } catch (err: unknown) {
      setError(t('The action could not be applied: {{message}}', { message: apiErrorMessage(err, String(err)) }));
    } finally {
      setBusy(false);
    }
  }

  if (suggested.length === 0 && !last) return null;

  return (
    <div className="model-alert-actions" onClick={(e) => e.stopPropagation()} style={{ marginTop: '0.4rem' }}>
      {suggested.length > 0 && (
        <div role="group" aria-label={t('Suggested actions')} style={{ display: 'flex', flexWrap: 'wrap', gap: '0.4rem', alignItems: 'center' }}>
          <span className="text-dim">{t('Suggested actions')}</span>
          {suggested.map((s) => (
            <button
              key={s.action}
              type="button"
              className={`btn btn-sm${s.recommended ? ' btn-primary' : ''}`}
              disabled={busy || !s.available}
              title={s.available ? s.description : (s.unavailableReason || s.description)}
              onClick={() => { void apply(s); }}
            >
              {s.label}{s.recommended ? ` (${t('Recommended')})` : ''}
            </button>
          ))}
        </div>
      )}
      {last && (
        <div className="text-dim model-alert-last-action" style={{ marginTop: '0.3rem' }}>
          {t('Last action: {{result}}', { result: last.result })}
        </div>
      )}
      {error && <div className="text-danger" role="alert" style={{ marginTop: '0.3rem' }}>{error}</div>}
    </div>
  );
}
