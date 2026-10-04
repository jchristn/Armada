import { useEffect, useState } from 'react';
import { updateSettings } from '../../api/client';
import type { RetentionSettingsData } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import { rangeError } from './ImportFleetActionSettings';

interface RetentionSettingsProps {
  retention: RetentionSettingsData | null | undefined;
  /** Disable editing (remote proxy mode or insufficient permission). */
  locked: boolean;
  /** Called with the full settings payload the server returned after a save. */
  onSaved: (updated: Record<string, unknown>) => void;
  /** Toast helper from the host page. */
  notify: (severity: 'success' | 'error', message: string) => void;
}

/** Every retention field is a number of days; 0 means never. Mirrors the backend clamp (RetentionSettings). */
export const RETENTION_RANGE = { key: 'days', min: 0, max: 3650 };

export const RETENTION_DEFAULTS: RetentionSettingsData = {
  askThreadArchiveAfterDays: 90,
  askThreadDeleteAfterDays: 0,
  jobRetentionDays: 30,
  importBatchRetentionDays: 90,
};

type RetentionField = keyof RetentionSettingsData;
type RetentionDraft = Record<RetentionField, string>;

const FIELDS: RetentionField[] = ['askThreadArchiveAfterDays', 'askThreadDeleteAfterDays', 'jobRetentionDays', 'importBatchRetentionDays'];

function toDraft(s: RetentionSettingsData | null | undefined): RetentionDraft {
  const v = { ...RETENTION_DEFAULTS, ...(s ?? {}) };
  return {
    askThreadArchiveAfterDays: String(v.askThreadArchiveAfterDays),
    askThreadDeleteAfterDays: String(v.askThreadDeleteAfterDays),
    jobRetentionDays: String(v.jobRetentionDays),
    importBatchRetentionDays: String(v.importBatchRetentionDays),
  };
}

/** Field errors for a draft: 'range' for a value that is not a whole number from 0 to 3650. */
export function validateRetentionDraft(draft: RetentionDraft): Partial<Record<RetentionField, string>> {
  const errors: Partial<Record<RetentionField, string>> = {};
  for (const field of FIELDS) {
    const error = rangeError(draft[field], RETENTION_RANGE);
    if (error) errors[field] = error;
  }
  return errors;
}

/**
 * Settings page section for data retention (`Retention`): how long Ask threads, finished background jobs, and finished
 * vessel import batches are kept. Keeps a local draft so the page's auto-refresh never overwrites unsaved edits,
 * validates each field, and saves the whole group, which the server replaces and applies live.
 */
export default function RetentionSettings({ retention, locked, onSaved, notify }: RetentionSettingsProps) {
  const { t } = useLocale();
  const [draft, setDraft] = useState<RetentionDraft>(() => toDraft(retention));
  const [dirty, setDirty] = useState(false);
  const [saving, setSaving] = useState(false);

  useEffect(() => { if (!dirty) setDraft(toDraft(retention)); }, [retention, dirty]);

  const errors = validateRetentionDraft(draft);
  const valid = Object.keys(errors).length === 0;
  const rangeText = t('Must be a whole number from {{min}} to {{max}}.', { min: RETENTION_RANGE.min.toLocaleString(), max: RETENTION_RANGE.max.toLocaleString() });

  async function save() {
    if (!valid) return;
    setSaving(true);
    try {
      const updated = await updateSettings({
        retention: {
          askThreadArchiveAfterDays: Number(draft.askThreadArchiveAfterDays),
          askThreadDeleteAfterDays: Number(draft.askThreadDeleteAfterDays),
          jobRetentionDays: Number(draft.jobRetentionDays),
          importBatchRetentionDays: Number(draft.importBatchRetentionDays),
        },
      });
      setDirty(false);
      onSaved(updated as Record<string, unknown>);
      notify('success', t('Retention settings saved and applied.'));
    } catch (e: unknown) {
      notify('error', t('Failed: {{message}}', { message: e instanceof Error ? e.message : t('Unknown error') }));
    } finally {
      setSaving(false);
    }
  }

  function set(field: RetentionField, value: string) {
    setDraft((d) => ({ ...d, [field]: value }));
    setDirty(true);
  }

  function numberInput(id: string, field: RetentionField, label: string, help: string) {
    const error = errors[field];
    return (
      <div className="form-group">
        <label htmlFor={id}>{label}</label>
        <input id={id} type="number" min={RETENTION_RANGE.min} max={RETENTION_RANGE.max} value={draft[field]} onChange={(e) => set(field, e.target.value)} aria-invalid={Boolean(error)} aria-describedby={`${id}-help`} />
        <span id={`${id}-help`} className={error ? 'field-error' : 'text-dim form-help'}>{error ? rangeText : help}</span>
      </div>
    );
  }

  return (
    <div className="settings-section" style={{ marginTop: '1.5rem' }}>
      <h3>{t('Data Retention')}</h3>
      <p className="text-muted" style={{ marginBottom: '0.75rem' }}>
        {t('How long Armada keeps Ask threads, finished background jobs, and import history. 0 means never. Changes apply immediately; pruning runs in the background about once an hour.')}
      </p>
      <fieldset disabled={locked} className="settings-fieldset">
        <div className="settings-grid">
          {numberInput('retention-ask-archive', 'askThreadArchiveAfterDays', t('Archive Ask threads after (days)'), t('Threads with no activity for this long are archived; pinned threads never are (0-3650, default 90).'))}
          {numberInput('retention-ask-delete', 'askThreadDeleteAfterDays', t('Delete Ask threads after (days)'), t('Threads with no activity for this long are deleted with their messages; pinned threads never are (0-3650, default 0).'))}
          {numberInput('retention-jobs', 'jobRetentionDays', t('Job retention (days)'), t('Finished background jobs older than this are deleted; the latest of each kind is kept (0-3650, default 30).'))}
          {numberInput('retention-imports', 'importBatchRetentionDays', t('Import history retention (days)'), t('Finished vessel import batches older than this are deleted; imported vessels are not affected (0-3650, default 90).'))}
        </div>
        <div className="settings-actions">
          <button type="button" className="btn-primary btn-sm" onClick={() => void save()} disabled={!valid || saving || !dirty}>
            {saving ? t('Saving...') : t('Save Retention Settings')}
          </button>
          {dirty && <button type="button" className="btn btn-sm" onClick={() => { setDirty(false); setDraft(toDraft(retention)); }}>{t('Discard changes')}</button>}
          {dirty && <span className="text-dim">{t('Unsaved changes')}</span>}
        </div>
      </fieldset>
    </div>
  );
}
