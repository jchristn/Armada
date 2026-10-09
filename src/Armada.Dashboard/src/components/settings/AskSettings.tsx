import { useEffect, useState } from 'react';
import { updateSettings } from '../../api/client';
import type { AskSettingsData } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import {
  ASK_RANGES,
  askDraftFrom,
  askSettingsPayload,
  validateAskDraft,
  type AskDraft,
  type AskNumberField,
  type AskToggleField,
} from '../../lib/settingsRanges';

interface AskSettingsProps {
  /** The server's Ask group; undefined on servers older than it (the defaults are shown). */
  ask: AskSettingsData | null | undefined;
  /** Disable editing (remote proxy mode or insufficient permission). */
  locked: boolean;
  /** Called with the full settings payload the server returned after a save. */
  onSaved: (updated: Record<string, unknown>) => void;
  /** Toast helper from the host page. */
  notify: (severity: 'success' | 'error', message: string) => void;
}

export { ASK_DEFAULTS, ASK_RANGES, validateAskDraft } from '../../lib/settingsRanges';

/**
 * Settings page section for Ask Armada conversations (`Ask`): result reports, milestone narration, history window,
 * proposal expiry, work tracking interval, and timeouts. Keeps a local draft so the page's auto-refresh never
 * overwrites unsaved edits, validates each number, and saves the whole group (the server replaces it), carrying
 * captainAutoApprove over unchanged since it is governed by the CLI permission policy rather than edited here.
 */
export default function AskSettings({ ask, locked, onSaved, notify }: AskSettingsProps) {
  const { t } = useLocale();
  const [draft, setDraft] = useState<AskDraft>(() => askDraftFrom(ask));
  const [dirty, setDirty] = useState(false);
  const [saving, setSaving] = useState(false);

  useEffect(() => { if (!dirty) setDraft(askDraftFrom(ask)); }, [ask, dirty]);

  const errors = validateAskDraft(draft);
  const valid = Object.keys(errors).length === 0;

  async function save() {
    if (!valid) return;
    setSaving(true);
    try {
      const updated = await updateSettings({ ask: askSettingsPayload(draft, ask) });
      setDirty(false);
      onSaved(updated as Record<string, unknown>);
      notify('success', t('Ask Armada settings saved and applied.'));
    } catch (e: unknown) {
      notify('error', t('Failed: {{message}}', { message: e instanceof Error ? e.message : t('Unknown error') }));
    } finally {
      setSaving(false);
    }
  }

  function setNumber(field: AskNumberField, value: string) {
    setDraft((d) => ({ ...d, [field]: value }));
    setDirty(true);
  }

  function setToggle(field: AskToggleField, value: boolean) {
    setDraft((d) => ({ ...d, [field]: value }));
    setDirty(true);
  }

  function numberInput(id: string, field: AskNumberField, label: string, help: string) {
    const error = errors[field];
    const range = ASK_RANGES[field];
    return (
      <div className="form-group">
        <label htmlFor={id}>{label}</label>
        <input id={id} type="number" min={range.min} max={range.max} value={draft[field]} onChange={(e) => setNumber(field, e.target.value)} aria-invalid={Boolean(error)} aria-describedby={`${id}-help`} />
        <span id={`${id}-help`} className={error ? 'field-error' : 'text-dim form-help'}>
          {error ? t('Must be a whole number from {{min}} to {{max}}.', { min: range.min.toLocaleString(), max: range.max.toLocaleString() }) : help}
        </span>
      </div>
    );
  }

  function toggle(id: string, field: AskToggleField, label: string, help: string) {
    return (
      <div className="form-group">
        <label className="settings-checkbox" htmlFor={id}>
          <input id={id} type="checkbox" checked={draft[field]} onChange={(e) => setToggle(field, e.target.checked)} aria-describedby={`${id}-help`} />
          {' '}{label}
        </label>
        <span id={`${id}-help`} className="text-dim form-help">{help}</span>
      </div>
    );
  }

  return (
    <div className="settings-section" style={{ marginTop: '1.5rem' }}>
      <h3>{t('Ask Armada')}</h3>
      <p className="text-muted" style={{ marginBottom: '0.75rem' }}>
        {t('How Ask Armada conversations follow and report on the work they start. Changes apply immediately.')}
      </p>
      <fieldset disabled={locked} className="settings-fieldset">
        <div className="settings-grid">
          {toggle('ask-report-results', 'reportResultsOnCompletion', t('Report results when work finishes'), t('When work started from a conversation finishes, its captain posts a short report of the outcome. The final progress update always includes the outcome.'))}
          {toggle('ask-narrate-milestones', 'narrateMilestones', t('Narrate milestones'), t('The captain writes progress updates in its own words when it is idle; otherwise a plain sentence is posted.'))}
          {numberInput('ask-history-turns', 'historyTurns', t('Conversation history (messages)'), t('Recent messages replayed to the captain each turn; older history is represented by the thread summary (2-200, default 20).'))}
          {numberInput('ask-proposal-expiry', 'proposalExpiryMinutes', t('Proposal expiry (minutes)'), t('How long a pending action proposal waits for a decision before it expires (1-1440, default 60).'))}
          {numberInput('ask-tracker-interval', 'trackerIntervalSeconds', t('Work tracking interval (seconds)'), t('Seconds between checks of tracked work; changes are also picked up immediately (2-300, default 5).'))}
          {numberInput('ask-narration-timeout', 'narrationTimeoutSeconds', t('Narration timeout (seconds)'), t('Longest a milestone narration may take before the plain sentence is used instead (10-600, default 60).'))}
          {numberInput('ask-turn-timeout', 'turnTimeoutMinutes', t('Turn timeout (minutes)'), t('Longest a captain turn may run before it is stopped and marked failed (1-120, default 15).'))}
        </div>
        <div className="settings-actions">
          <button type="button" className="btn-primary btn-sm" onClick={() => void save()} disabled={!valid || saving || !dirty}>
            {saving ? t('Saving...') : t('Save Ask Armada Settings')}
          </button>
          {dirty && <button type="button" className="btn btn-sm" onClick={() => { setDirty(false); setDraft(askDraftFrom(ask)); }}>{t('Discard changes')}</button>}
          {dirty && <span className="text-dim">{t('Unsaved changes')}</span>}
        </div>
      </fieldset>
    </div>
  );
}
