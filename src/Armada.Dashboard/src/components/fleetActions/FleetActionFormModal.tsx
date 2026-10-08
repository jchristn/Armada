import { useEffect, useRef, useState } from 'react';
import { createFleetAction, listPersonas, listPipelines, updateFleetAction } from '../../api/client';
import type { FleetAction, FleetActionKind, FleetActionUpsertRequest, Persona, Pipeline } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import DialogShell from '../shared/DialogShell';
import TemplateVariableHelp from './TemplateVariableHelp';
import { KIND_DESCRIPTIONS, KIND_LABELS } from '../../lib/fleetActionLabels';
import { findUnknownTemplateVariables } from '../../lib/fleetActionTemplate';
import { buildActionUpsertPayload, formFromAction, validateActionForm, type FleetActionFormState } from '../../lib/fleetActionForm';

export interface FleetActionFormModalProps {
  open: boolean;
  /** `create` (blank or duplicate source) or `edit`. */
  mode: 'create' | 'edit';
  /** Action being edited, or the duplicate source in create mode. */
  source?: FleetAction | null;
  /** Default timeout for new actions (FleetActions.DefaultTimeoutSeconds). */
  defaultTimeoutSeconds?: number;
  onClose: () => void;
  onSaved: (action: FleetAction) => void;
}

type FormState = FleetActionFormState;

export { formFromAction, validateActionForm };

/** Purpose-built create/edit form for a fleet action (no raw JSON editing). */
export default function FleetActionFormModal({ open, mode, source, defaultTimeoutSeconds = 300, onClose, onSaved }: FleetActionFormModalProps) {
  const { t } = useLocale();
  const [form, setForm] = useState<FormState>(() => formFromAction(source, mode, defaultTimeoutSeconds, t('(copy)')));
  const [pipelines, setPipelines] = useState<Pipeline[]>([]);
  const [personas, setPersonas] = useState<Persona[]>([]);
  const [submitted, setSubmitted] = useState(false);
  const [saving, setSaving] = useState(false);
  const [serverError, setServerError] = useState('');
  const bodyRef = useRef<HTMLTextAreaElement>(null);

  useEffect(() => {
    if (!open) return;
    setForm(formFromAction(source, mode, defaultTimeoutSeconds, t('(copy)')));
    setSubmitted(false);
    setServerError('');
    let cancelled = false;
    listPipelines({ pageSize: 9999 }).then((r) => { if (!cancelled) setPipelines(r.objects || []); }).catch(() => undefined);
    listPersonas({ pageSize: 9999 }).then((r) => { if (!cancelled) setPersonas(r.objects || []); }).catch(() => undefined);
    return () => { cancelled = true; };
  }, [open, source, mode, defaultTimeoutSeconds, t]);

  const errors = validateActionForm(form);
  const bodyText = form.kind === 'Command' ? form.commandText : form.promptTemplate;
  const unknownVars = findUnknownTemplateVariables(bodyText);
  const show = (field: string) => (submitted && errors[field] ? t(errors[field]) : '');

  function insertToken(token: string) {
    const el = bodyRef.current;
    const current = bodyText;
    let next = current + token;
    if (el && typeof el.selectionStart === 'number') {
      next = current.slice(0, el.selectionStart) + token + current.slice(el.selectionEnd ?? el.selectionStart);
    }
    setForm(form.kind === 'Command' ? { ...form, commandText: next } : { ...form, promptTemplate: next });
  }

  async function save() {
    setSubmitted(true);
    if (Object.keys(errors).length > 0 || unknownVars.length > 0) return;
    setSaving(true);
    setServerError('');
    const payload: FleetActionUpsertRequest = buildActionUpsertPayload(form, mode);
    try {
      const saved = mode === 'edit' && source
        ? await updateFleetAction(source.id, payload)
        : await createFleetAction(payload);
      onSaved(saved);
    } catch (err: unknown) {
      // The server names unknown template variables in its 400 message; show it inline next to the body.
      setServerError(err instanceof Error ? err.message : t('Save failed.'));
    } finally {
      setSaving(false);
    }
  }

  const title = mode === 'edit' ? t('Edit fleet action') : source ? t('Duplicate fleet action') : t('New fleet action');

  return (
    <DialogShell
      open={open}
      onClose={onClose}
      dismissible={!saving}
      title={title}
      subtitle={mode === 'edit' && source ? <span className="mono">{source.id}</span> : t('Define a reusable action to run across many vessels.')}
      size="lg"
      footer={(
        <>
          <button type="button" className="btn" onClick={onClose} disabled={saving}>{t('Cancel')}</button>
          <button type="button" className="btn btn-primary" onClick={() => void save()} disabled={saving}>{saving ? t('Saving...') : t('Save')}</button>
        </>
      )}
    >
      {serverError && <div className="alert alert-error" role="alert">{serverError}</div>}
      {submitted && Object.keys(errors).length > 0 && (
        <div className="alert alert-error" role="alert">{t('Fix the highlighted fields before saving.')}</div>
      )}
      {mode === 'edit' && source?.isBuiltIn && (
        <div className="alert alert-info" role="note">{t('This is a built-in action. Your edits are kept; it is never re-seeded over your changes.')}</div>
      )}
      <div className="form-stack">
        <div className="form-grid-2">
          <label className="form-field">
            <span className="form-label">{t('Name')}</span>
            <input value={form.name} maxLength={200} onChange={(e) => setForm({ ...form, name: e.target.value })} aria-invalid={Boolean(show('name'))} />
            {show('name') && <span className="field-error">{show('name')}</span>}
          </label>
          <label className="form-field">
            <span className="form-label">{t('Description')}</span>
            <input value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} placeholder={t('Optional')} />
          </label>
        </div>

        <fieldset className="form-fieldset">
          <legend className="form-label">{t('Kind')}</legend>
          <div className="segmented" role="radiogroup" aria-label={t('Kind')}>
            {(['Command', 'Mission'] as FleetActionKind[]).map((k) => (
              <label key={k} className={`segmented-option${form.kind === k ? ' active' : ''}`}>
                <input
                  type="radio"
                  name="fleet-action-kind"
                  checked={form.kind === k}
                  onChange={() => setForm({ ...form, kind: k, requiresCleanWorkingTree: k === 'Command' ? (source?.kind === 'Command' ? source.requiresCleanWorkingTree : true) : false })}
                />
                <span>{t(KIND_LABELS[k])}</span>
              </label>
            ))}
          </div>
          <p className="text-dim form-help">{t(KIND_DESCRIPTIONS[form.kind])}</p>
        </fieldset>

        {form.kind === 'Command' ? (
          <label className="form-field">
            <span className="form-label">{t('Command text')}</span>
            <textarea
              ref={bodyRef}
              className="mono"
              rows={6}
              spellCheck={false}
              value={form.commandText}
              onChange={(e) => setForm({ ...form, commandText: e.target.value })}
              placeholder="git pull --ff-only"
              aria-invalid={Boolean(show('body')) || unknownVars.length > 0}
            />
            <span className="text-dim form-help">{t('Runs through the platform shell (/bin/sh on Linux and macOS, PowerShell on Windows) in each vessel working directory.')}</span>
          </label>
        ) : (
          <>
            <label className="form-field">
              <span className="form-label">{t('Prompt template')}</span>
              <textarea
                ref={bodyRef}
                rows={8}
                value={form.promptTemplate}
                onChange={(e) => setForm({ ...form, promptTemplate: e.target.value })}
                aria-invalid={Boolean(show('body')) || unknownVars.length > 0}
              />
            </label>
            <div className="form-grid-2">
              <label className="form-field">
                <span className="form-label">{t('Pipeline')}</span>
                <select value={form.pipelineId} onChange={(e) => setForm({ ...form, pipelineId: e.target.value })}>
                  <option value="">{t('Vessel default')}</option>
                  {pipelines.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
                </select>
              </label>
              <label className="form-field">
                <span className="form-label">{t('Persona')}</span>
                <select value={form.persona} onChange={(e) => setForm({ ...form, persona: e.target.value })}>
                  <option value="">{t('None')}</option>
                  {personas.map((p) => <option key={p.id} value={p.name}>{p.name}</option>)}
                  {form.persona && !personas.some((p) => p.name === form.persona) && <option value={form.persona}>{form.persona}</option>}
                </select>
                <span className="text-dim form-help">{t('Stored with the action; not yet applied at dispatch. Use a pipeline to choose personas.')}</span>
              </label>
            </div>
          </>
        )}
        {unknownVars.length > 0
          ? <span className="field-error">{t('Unknown template variable: {{names}}', { names: unknownVars.join(', ') })}</span>
          : show('body') ? <span className="field-error">{show('body')}</span> : null}

        <TemplateVariableHelp onInsert={insertToken} />

        <div className="form-grid-3">
          {form.kind === 'Command' && (
            <label className="form-field">
              <span className="form-label">{t('Timeout (seconds)')}</span>
              <input type="number" min={5} max={7200} value={form.timeoutSeconds} onChange={(e) => setForm({ ...form, timeoutSeconds: e.target.value })} aria-invalid={Boolean(show('timeout'))} />
              {show('timeout') ? <span className="field-error">{show('timeout')}</span> : <span className="text-dim form-help">{t('5 to 7200 seconds')}</span>}
            </label>
          )}
          <label className="form-field">
            <span className="form-label">{t('Default concurrency')}</span>
            <input type="number" min={1} max={32} value={form.defaultConcurrency} onChange={(e) => setForm({ ...form, defaultConcurrency: e.target.value })} aria-invalid={Boolean(show('concurrency'))} />
            {show('concurrency') ? <span className="field-error">{show('concurrency')}</span> : <span className="text-dim form-help">{t('1 to 32')}</span>}
          </label>
          {form.kind === 'Command' && (
            <label className="checkbox-row form-checkbox-aligned">
              <input type="checkbox" checked={form.requiresCleanWorkingTree} onChange={(e) => setForm({ ...form, requiresCleanWorkingTree: e.target.checked })} />
              <span>{t('Requires a clean working tree')}</span>
            </label>
          )}
        </div>
      </div>
    </DialogShell>
  );
}
