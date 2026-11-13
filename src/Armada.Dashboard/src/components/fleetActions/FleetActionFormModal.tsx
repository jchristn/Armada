import { useEffect, useRef, useState } from 'react';
import { createFleetAction, listPersonas, listPipelines, updateFleetAction } from '../../api/client';
import { listAll } from '../../api/paging';
import type { FleetAction, FleetActionKind, FleetActionUpsertRequest, MissionMode, Persona, Pipeline } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import DialogShell from '../shared/DialogShell';
import TemplateVariableHelp from './TemplateVariableHelp';
import { KIND_DESCRIPTIONS, KIND_LABELS, MISSION_MODES, MISSION_MODE_LABELS } from '../../lib/fleetActionLabels';
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
    listAll(listPipelines).then((r) => { if (!cancelled) setPipelines(r.objects || []); }).catch(() => undefined);
    listAll(listPersonas).then((r) => { if (!cancelled) setPersonas(r.objects || []); }).catch(() => undefined);
    return () => { cancelled = true; };
  }, [open, source, mode, defaultTimeoutSeconds, t]);

  const errors = validateActionForm(form);
  const bodyText = form.kind === 'Command' ? form.commandText : form.promptTemplate;
  const inputNames = form.extensions?.Inputs?.map(i => i.Name);
  const unknownVars = findUnknownTemplateVariables(bodyText, inputNames);
  const unknownCheckVars = findUnknownTemplateVariables(`${form.applicabilityCommand}\n${form.applicabilityCommandWindows}`, inputNames);
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
    if (Object.keys(errors).length > 0 || unknownVars.length > 0 || unknownCheckVars.length > 0) return;
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
          <button type="button" className="btn" onClick={onClose} disabled={saving} title={t('Close without saving.')}>{t('Cancel')}</button>
          <button type="button" className="btn btn-primary" onClick={() => void save()} disabled={saving} title={t('Save the action. Runs already started keep the definition they began with.')}>{saving ? t('Saving...') : t('Save')}</button>
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
            <span className="form-label" title={t('Name shown when picking an action to run, for example Fast-forward default branch. Required, up to 200 characters.')}>{t('Name')}</span>
            <input title={t('Name shown when picking an action to run, for example Fast-forward default branch. Required, up to 200 characters.')} value={form.name} maxLength={200} onChange={(e) => setForm({ ...form, name: e.target.value })} aria-invalid={Boolean(show('name'))} />
            {show('name') && <span className="field-error">{show('name')}</span>}
          </label>
          <label className="form-field">
            <span className="form-label" title={t('Optional note on what the action does, shown in the actions list.')}>{t('Description')}</span>
            <input title={t('Optional note on what the action does, shown in the actions list.')} value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} placeholder={t('Optional')} />
          </label>
        </div>

        <fieldset className="form-fieldset">
          <legend className="form-label" title={t('Command runs a shell command in each vessel working directory on the Admiral or its Harbor. Mission asks a captain to do the work in a voyage per vessel.')}>{t('Kind')}</legend>
          <div className="segmented" role="radiogroup" aria-label={t('Kind')}>
            {(['Command', 'Mission'] as FleetActionKind[]).map((k) => (
              <label key={k} className={`segmented-option${form.kind === k ? ' active' : ''}`} title={t(KIND_DESCRIPTIONS[k])}>
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
            <span className="form-label" title={t('Shell command run in each vessel working directory, for example git pull --ff-only. Template variables such as {{vessel.defaultBranch}} are filled in per vessel.')}>{t('Command text')}</span>
            <textarea
              title={t('Shell command run in each vessel working directory, for example git pull --ff-only. Template variables such as {{vessel.defaultBranch}} are filled in per vessel.')}
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
              <span className="form-label" title={t('Instructions for the captain, used as the mission description for each vessel. Template variables such as {{health.summary}} are filled in per vessel.')}>{t('Prompt template')}</span>
              <textarea
                title={t('Instructions for the captain, used as the mission description for each vessel. Template variables such as {{health.summary}} are filled in per vessel.')}
                ref={bodyRef}
                rows={8}
                value={form.promptTemplate}
                onChange={(e) => setForm({ ...form, promptTemplate: e.target.value })}
                aria-invalid={Boolean(show('body')) || unknownVars.length > 0}
              />
            </label>
            <div className="form-grid-2">
              <label className="form-field">
                <span className="form-label" title={t('Pipeline the voyages run through. Vessel default uses the vessel or fleet default pipeline, unless a persona is set below.')}>{t('Pipeline')}</span>
                <select title={t('Pipeline the voyages run through. Vessel default uses the vessel or fleet default pipeline, unless a persona is set below.')} value={form.pipelineId} onChange={(e) => setForm({ ...form, pipelineId: e.target.value })}>
                  <option value="">{t('Vessel default')}</option>
                  {pipelines.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
                </select>
              </label>
              <label className="form-field">
                <span className="form-label" title={t('Persona the captain works as. With no pipeline, each mission runs as one stage with it; a single-stage pipeline uses it instead of its own; multi-stage pipelines ignore it.')}>{t('Persona')}</span>
                <select title={t('Persona the captain works as. With no pipeline, each mission runs as one stage with it; a single-stage pipeline uses it instead of its own; multi-stage pipelines ignore it.')} value={form.persona} onChange={(e) => setForm({ ...form, persona: e.target.value })}>
                  <option value="">{t('None')}</option>
                  {personas.map((p) => <option key={p.id} value={p.name}>{p.name}</option>)}
                  {form.persona && !personas.some((p) => p.name === form.persona) && <option value={form.persona}>{form.persona}</option>}
                </select>
                <span className="text-dim form-help">{t('Applied at dispatch. Ignored by multi-stage pipelines, which choose their own personas.')}</span>
              </label>
            </div>
            <label className="form-field">
              <span className="form-label" title={t('Audit and Research are report-only: nothing lands, and the report appears on the run.')}>{t('Mission mode')}</span>
              <select title={t('Audit and Research are report-only: nothing lands, and the report appears on the run.')} value={form.missionMode} onChange={(e) => setForm({ ...form, missionMode: e.target.value as MissionMode })}>
                {MISSION_MODES.map((m) => <option key={m} value={m}>{t(MISSION_MODE_LABELS[m])}</option>)}
              </select>
              <span className="text-dim form-help">{t('Audit and Research are report-only: nothing lands, and the report appears on the run.')}</span>
            </label>
          </>
        )}
        {unknownVars.length > 0
          ? <span className="field-error">{t('Unknown template variable: {{names}}', { names: unknownVars.join(', ') })}</span>
          : show('body') ? <span className="field-error">{show('body')}</span> : null}

        <TemplateVariableHelp onInsert={insertToken} />

        <div className="form-grid-2">
          <label className="form-field">
            <span className="form-label" title={t('Optional shell check run in each vessel first. A non-zero exit skips the vessel as Not applicable. Needs tenant admin.')}>{t('Applicability check')}</span>
            <textarea
              title={t('Optional shell check run in each vessel first. A non-zero exit skips the vessel as Not applicable. Needs tenant admin.')}
              className="mono"
              rows={2}
              spellCheck={false}
              value={form.applicabilityCommand}
              onChange={(e) => setForm({ ...form, applicabilityCommand: e.target.value })}
              placeholder="test -f package.json"
              aria-invalid={unknownCheckVars.length > 0}
            />
            <span className="text-dim form-help">{t('Optional. A non-zero exit skips the vessel as Not applicable. Needs tenant admin.')}</span>
          </label>
          <label className="form-field">
            <span className="form-label" title={t('Optional check used instead on Windows hosts. A non-zero exit skips the vessel as Not applicable.')}>{t('Applicability check (Windows)')}</span>
            <textarea
              title={t('Optional check used instead on Windows hosts. A non-zero exit skips the vessel as Not applicable.')}
              className="mono"
              rows={2}
              spellCheck={false}
              value={form.applicabilityCommandWindows}
              onChange={(e) => setForm({ ...form, applicabilityCommandWindows: e.target.value })}
              placeholder="if (!(Test-Path package.json)) { exit 1 }"
              aria-invalid={unknownCheckVars.length > 0}
            />
            <span className="text-dim form-help">{t('Optional. Used on Windows hosts instead of the check above.')}</span>
          </label>
        </div>
        {unknownCheckVars.length > 0 && <span className="field-error">{t('Unknown template variable: {{names}}', { names: unknownCheckVars.join(', ') })}</span>}

        <div className="form-grid-3">
          {form.kind === 'Command' && (
            <label className="form-field">
              <span className="form-label" title={t('Seconds the command may run on one vessel before it and its child processes are killed and the vessel is marked Timed out (5-7200).')}>{t('Timeout (seconds)')}</span>
              <input title={t('Seconds the command may run on one vessel before it and its child processes are killed and the vessel is marked Timed out (5-7200).')} type="number" min={5} max={7200} value={form.timeoutSeconds} onChange={(e) => setForm({ ...form, timeoutSeconds: e.target.value })} aria-invalid={Boolean(show('timeout'))} />
              {show('timeout') ? <span className="field-error">{show('timeout')}</span> : <span className="text-dim form-help">{t('5 to 7200 seconds')}</span>}
            </label>
          )}
          <label className="form-field">
            <span className="form-label" title={t('How many vessels a run works on at once unless the run overrides it (1-32). For Mission actions, how many voyages are active at once.')}>{t('Default concurrency')}</span>
            <input title={t('How many vessels a run works on at once unless the run overrides it (1-32). For Mission actions, how many voyages are active at once.')} type="number" min={1} max={32} value={form.defaultConcurrency} onChange={(e) => setForm({ ...form, defaultConcurrency: e.target.value })} aria-invalid={Boolean(show('concurrency'))} />
            {show('concurrency') ? <span className="field-error">{show('concurrency')}</span> : <span className="text-dim form-help">{t('1 to 32')}</span>}
          </label>
          {form.kind === 'Command' && (
            <label className="checkbox-row form-checkbox-aligned" title={t('Skip any vessel whose checkout has uncommitted or untracked changes, so a command never touches unsaved work. Recommended.')}>
              <input type="checkbox" title={t('Skip any vessel whose checkout has uncommitted or untracked changes, so a command never touches unsaved work. Recommended.')} checked={form.requiresCleanWorkingTree} onChange={(e) => setForm({ ...form, requiresCleanWorkingTree: e.target.checked })} />
              <span>{t('Requires a clean working tree')}</span>
            </label>
          )}
        </div>
      </div>
    </DialogShell>
  );
}
