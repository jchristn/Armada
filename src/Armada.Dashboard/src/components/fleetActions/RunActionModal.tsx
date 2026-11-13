import RunApprovalFields, { DEFAULT_RUN_APPROVALS } from '../shared/RunApprovalFields';
import { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  enumerateFleetActions,
  getVessel,
  listPipelines,
  runAdHocFleetAction,
  runFleetAction,
} from '../../api/client';
import { listAll } from '../../api/paging';
import type {
  FleetAction,
  FleetActionKind,
  FleetActionRunRequest,
  FleetActionRunStartResult,
  FleetActionTargetStatus,
  FleetActionUpsertRequest,
  Pipeline,
  Vessel,
} from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import { useNotifications } from '../../context/NotificationContext';
import DialogShell from '../shared/DialogShell';
import CodeStatusBadge from '../shared/CodeStatusBadge';
import TemplateVariableHelp from './TemplateVariableHelp';
import { KIND_DESCRIPTIONS, KIND_LABELS, TARGET_STATUS_META } from '../../lib/fleetActionLabels';
import { findUnknownTemplateVariables, renderTemplatePreview } from '../../lib/fleetActionTemplate';
import {
  DEFAULT_FOLLOW_UP_STATUSES,
  FOLLOW_UP_STATUSES,
  MAX_RUN_VESSELS,
  PACKAGE_INPUT_PATTERN,
  adHocFromDefinition,
  asFollowUpRequest,
  buildAdHocDefinition,
  buildSavedRunRequest,
  emptyAdHoc,
  validateInputValues,
  validateRunInputs,
  type AdHocRunForm,
  type RunMode,
} from '../../lib/fleetActionForm';

export { MAX_RUN_VESSELS, validateRunInputs };

export interface RunActionModalProps {
  /** Whether the modal is shown. */
  open: boolean;
  /** Vessels to run against (1-500). The first one is used for the rendered preview. */
  vesselIds: string[];
  /** Called when the modal is dismissed (also after a successful start). */
  onClose: () => void;
  /** Preselect a saved action by id. */
  initialActionId?: string | null;
  /** Preferred kind: preselects the first saved action of that kind and the ad hoc kind. */
  initialKind?: FleetActionKind;
  /** Open in ad hoc mode prefilled with this definition (e.g. re-running an ad hoc run). */
  initialDefinition?: FleetActionUpsertRequest | null;
  /** Called after the server accepted the run. When omitted the modal navigates to the run detail page. */
  onStarted?: (result: FleetActionRunStartResult) => void;
  /**
   * Follow-up mode: target the vessels of this earlier run by target status instead of `vesselIds` (send an empty
   * list). `statuses` preselects the status checkboxes (default Findings and Failed).
   */
  followUp?: { runId: string; statuses?: FleetActionTargetStatus[] } | null;
  /** Vessel used for the rendered preview when `vesselIds` is empty (follow-up mode). */
  previewVesselId?: string | null;
}

type Mode = RunMode;
type Step = 'configure' | 'confirm';
type AdHocForm = AdHocRunForm;

/**
 * Reusable "Run fleet action" modal. Given selected vessel ids it lets the operator pick a saved action or
 * define an ad hoc one, previews the rendered text for the first vessel, sets concurrency, confirms, and
 * starts the run. Used by the Vessels bulk bar, the Fleet Actions page, and (later) the Health page.
 */
export default function RunActionModal({ open, vesselIds, onClose, initialActionId, initialKind, initialDefinition, onStarted, followUp, previewVesselId }: RunActionModalProps) {
  const { t } = useLocale();
  const { pushToast } = useNotifications();
  const navigate = useNavigate();

  const [approvalOptions, setApprovalOptions] = useState({ ...DEFAULT_RUN_APPROVALS });
  const [mode, setMode] = useState<Mode>(initialDefinition ? 'adhoc' : 'saved');
  const [step, setStep] = useState<Step>('configure');
  const [actions, setActions] = useState<FleetAction[]>([]);
  const [actionsLoading, setActionsLoading] = useState(false);
  const [actionsError, setActionsError] = useState('');
  const [selectedActionId, setSelectedActionId] = useState<string>(initialActionId ?? '');
  const [pipelines, setPipelines] = useState<Pipeline[]>([]);
  const [previewVessel, setPreviewVessel] = useState<Vessel | null>(null);
  const [inputValues, setInputValues] = useState<Record<string, string>>({});
  const [concurrency, setConcurrency] = useState('4');
  const [cleanTreeOverride, setCleanTreeOverride] = useState<boolean | null>(null);
  const [adHoc, setAdHoc] = useState<AdHocForm>(() => emptyAdHoc(initialKind ?? 'Command'));
  const [submitted, setSubmitted] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [serverError, setServerError] = useState('');
  const [followUpStatuses, setFollowUpStatuses] = useState<FleetActionTargetStatus[]>(followUp?.statuses ?? DEFAULT_FOLLOW_UP_STATUSES);
  const followUpRunId = followUp?.runId ?? null;
  const followUpInitialKey = (followUp?.statuses ?? DEFAULT_FOLLOW_UP_STATUSES).join(',');

  // Reset whenever the modal is (re)opened.
  useEffect(() => {
    if (!open) return;
    setFollowUpStatuses(FOLLOW_UP_STATUSES.filter((s) => followUpInitialKey.split(',').includes(s)));
  }, [open, followUpRunId, followUpInitialKey]);

  useEffect(() => {
    if (!open) return;
    setStep('configure');
    setApprovalOptions({ ...DEFAULT_RUN_APPROVALS });
    setSubmitted(false);
    setServerError('');
    setCleanTreeOverride(null);
    setSelectedActionId(initialActionId ?? '');
    if (initialDefinition) {
      setMode('adhoc');
      setAdHoc(adHocFromDefinition(initialDefinition));
    } else {
      setMode('saved');
      setAdHoc(emptyAdHoc(initialKind ?? 'Command'));
    }
  }, [open, initialActionId, initialDefinition, initialKind]);

  useEffect(() => {
    if (!open) return;
    let cancelled = false;
    setActionsLoading(true);
    setActionsError('');
    listAll(enumerateFleetActions)
      .then((result) => {
        if (cancelled) return;
        const list = result.objects || [];
        setActions(list);
        setSelectedActionId((current) => {
          if (current && list.some((a) => a.id === current)) return current;
          const preferred = initialKind ? list.find((a) => a.kind === initialKind) : undefined;
          return (preferred ?? list[0])?.id ?? '';
        });
      })
      .catch((err: unknown) => {
        if (!cancelled) setActionsError(err instanceof Error ? err.message : t('Failed to load fleet actions.'));
      })
      .finally(() => { if (!cancelled) setActionsLoading(false); });
    listAll(listPipelines).then((r) => { if (!cancelled) setPipelines(r.objects || []); }).catch(() => undefined);
    return () => { cancelled = true; };
  }, [open, initialKind, t]);

  const previewId = vesselIds[0] ?? previewVesselId ?? null;
  useEffect(() => {
    if (!open || !previewId) { setPreviewVessel(null); return; }
    let cancelled = false;
    getVessel(previewId).then((v) => { if (!cancelled) setPreviewVessel(v); }).catch(() => { if (!cancelled) setPreviewVessel(null); });
    return () => { cancelled = true; };
  }, [open, previewId]);

  const selectedAction = useMemo(() => actions.find((a) => a.id === selectedActionId) ?? null, [actions, selectedActionId]);

  // Default concurrency follows the chosen action.
  useEffect(() => {
    if (mode === 'saved' && selectedAction) setConcurrency(String(Math.min(32, Math.max(1, selectedAction.defaultConcurrency || 4))));
    setCleanTreeOverride(null);
  }, [mode, selectedAction]);

  const inputDefinitions = useMemo(() => mode === 'saved' ? selectedAction?.inputs ?? [] : (adHoc.extensions?.Inputs ?? []).map(i => ({ name: i.Name, type: i.Type, required: i.Required ?? false, default: i.Default, allowedValues: i.AllowedValues ?? [], description: i.Description })), [mode, selectedAction, adHoc.extensions]);
  useEffect(() => { setInputValues(Object.fromEntries(inputDefinitions.map(i => [i.name, i.default ?? '']))); }, [inputDefinitions]);

  const effectiveKind: FleetActionKind = mode === 'saved' ? (selectedAction?.kind ?? 'Command') : adHoc.kind;
  const bodyText = mode === 'saved'
    ? (selectedAction ? (selectedAction.kind === 'Command' ? selectedAction.commandText : selectedAction.promptTemplate) ?? '' : '')
    : (adHoc.kind === 'Command' ? adHoc.commandText : adHoc.promptTemplate);
  const effectiveCleanTree = mode === 'saved'
    ? (cleanTreeOverride ?? selectedAction?.requiresCleanWorkingTree ?? true)
    : adHoc.requiresCleanWorkingTree;

  const errors = validateRunInputs({ vesselCount: vesselIds.length, mode, action: selectedAction, concurrency, adHoc, followUp: followUpRunId ? { statuses: followUpStatuses } : null });
  const inputErrors = validateInputValues(inputDefinitions, inputValues);
  const unknownVars = findUnknownTemplateVariables(bodyText, inputDefinitions.map(i => i.name));
  const hasErrors = Object.keys(errors).length > 0 || Object.keys(inputErrors).length > 0;
  // A malformed package name shows as soon as it is typed; a missing required value only after Review and run.
  const inputError = (name: string, type: string) => {
    const message = inputErrors[name];
    if (!message) return '';
    return submitted || (type === 'package' && (inputValues[name] ?? '').trim() !== '') ? t(message) : '';
  };
  const toggleFollowUpStatus = (status: FleetActionTargetStatus, checked: boolean) => setFollowUpStatuses((current) => (checked
    ? FOLLOW_UP_STATUSES.filter((s) => s === status || current.includes(s))
    : current.filter((s) => s !== status)));
  const showError = (field: string) => (submitted && errors[field] ? errors[field] : '');

  const preview = useMemo(() => {
    if (!previewVessel || !bodyText) return null;
    return renderTemplatePreview(bodyText, previewVessel, t('[health summary is rendered on the server for each vessel]'));
  }, [previewVessel, bodyText, t]);

  function goToConfirm() {
    setSubmitted(true);
    if (hasErrors || !Number.isInteger(approvalOptions.timeoutSeconds) || approvalOptions.timeoutSeconds < 10 || approvalOptions.timeoutSeconds > 3600) return;
    setServerError('');
    setStep('confirm');
  }

  async function start() {
    setSubmitting(true);
    setServerError('');
    const c = parseInt(concurrency, 10);
    try {
      let result: FleetActionRunStartResult;
      const target = (request: FleetActionRunRequest) => (followUpRunId ? asFollowUpRequest(request, followUpRunId, followUpStatuses) : request);
      if (mode === 'saved' && selectedAction) {
        const request: FleetActionRunRequest = buildSavedRunRequest(vesselIds, c, selectedAction, cleanTreeOverride);
        if (selectedAction.inputs?.length) request.InputValues = inputValues;
        if (effectiveKind === 'Mission') request.Overrides = { ...request.Overrides, ApprovalOptions: approvalOptions };
        result = await runFleetAction(selectedAction.id, target(request));
      } else {
        const definition: FleetActionUpsertRequest = buildAdHocDefinition(adHoc);
        result = await runAdHocFleetAction(target({ VesselIds: vesselIds, Concurrency: c, Definition: definition, ...(inputDefinitions.length ? { InputValues: inputValues } : {}), ...(effectiveKind === 'Mission' ? { Overrides: { ApprovalOptions: approvalOptions } } : {}) }));
      }
      pushToast('success', t('{count, plural, one {Fleet action started on # vessel.} other {Fleet action started on # vessels.}}', { count: result.targetCount }));
      onClose();
      if (onStarted) onStarted(result);
      else navigate(`/fleet-actions/runs/${result.runId}`);
    } catch (err: unknown) {
      setServerError(err instanceof Error ? err.message : t('Failed to start the run.'));
      setStep('configure');
    } finally {
      setSubmitting(false);
    }
  }

  const count = vesselIds.length;
  const concurrencyNumber = parseInt(concurrency, 10) || 1;
  const followUpStatusText = followUpStatuses.map((s) => t(TARGET_STATUS_META[s].label)).join(', ');

  const footer = step === 'configure' ? (
    <>
      <button type="button" className="btn" onClick={onClose} title={t('Close without running anything.')}>{t('Cancel')}</button>
      <button type="button" className="btn btn-primary" onClick={goToConfirm} disabled={actionsLoading && mode === 'saved'} title={t('Check the inputs and show a summary to confirm. Nothing runs yet.')}>
        {t('Review and run')}
      </button>
    </>
  ) : (
    <>
      <button type="button" className="btn" onClick={() => setStep('configure')} disabled={submitting} title={t('Return to the options to change them.')}>{t('Back')}</button>
      <button type="button" className={`btn ${effectiveKind === 'Command' ? 'btn-danger' : 'btn-primary'}`} onClick={() => void start()} disabled={submitting} title={effectiveKind === 'Command' ? t('Start the run now. The command executes in each vessel checkout and cannot be undone; you can cancel the run to stop pending vessels.') : t('Start the run now. Captain voyages are dispatched per vessel; you can cancel the run later.')}>
        {submitting ? t('Starting...') : followUpRunId ? t('Start follow-up run') : t('{count, plural, one {Run on # vessel} other {Run on # vessels}}', { count })}
      </button>
    </>
  );

  return (
    <DialogShell
      open={open}
      onClose={onClose}
      dismissible={!submitting}
      title={t('Run fleet action')}
      subtitle={followUpRunId ? t('Follow-up of run {{id}}', { id: followUpRunId }) : t('{count, plural, one {# vessel selected} other {# vessels selected}}', { count })}
      footer={footer}
      className="run-action-modal"
    >
      {serverError && <div className="alert alert-error" role="alert">{serverError}</div>}
      {showError('vessels') && <div className="alert alert-error" role="alert">{t(errors.vessels)}</div>}

      {step === 'configure' && (
        <>
          {followUpRunId && (
            <fieldset className="form-fieldset run-action-follow-up">
              <legend className="form-label" title={t('The run targets the vessels of the earlier run whose result matches a checked status.')}>
                {t('Targets: vessels from run {{id}} with status', { id: followUpRunId })}
              </legend>
              <div className="checkbox-group">
                {FOLLOW_UP_STATUSES.map((s) => (
                  <label key={s} className="checkbox-row" title={t(TARGET_STATUS_META[s].description)}>
                    <input type="checkbox" checked={followUpStatuses.includes(s)} onChange={(e) => toggleFollowUpStatus(s, e.target.checked)} />
                    <span>{t(TARGET_STATUS_META[s].label)}</span>
                  </label>
                ))}
              </div>
            </fieldset>
          )}
          <div className="segmented" role="radiogroup" aria-label={t('Action source')}>
            <label className={`segmented-option${mode === 'saved' ? ' active' : ''}`} title={t('Run one of the saved fleet actions.')}>
              <input type="radio" name="run-mode" checked={mode === 'saved'} onChange={() => setMode('saved')} />
              <span>{t('Saved action')}</span>
            </label>
            <label className={`segmented-option${mode === 'adhoc' ? ' active' : ''}`} title={t('Type a one-off command or prompt for this run only. It is not saved as an action.')}>
              <input type="radio" name="run-mode" checked={mode === 'adhoc'} onChange={() => setMode('adhoc')} />
              <span>{t('Ad hoc')}</span>
            </label>
          </div>

          {effectiveKind === 'Mission' && <RunApprovalFields value={approvalOptions} onChange={setApprovalOptions} disabled={submitting} />}
          {mode === 'saved' && (
            <div className="form-stack">
              {actionsError && (
                <div className="alert alert-error" role="alert">
                  {actionsError}
                </div>
              )}
              <label className="form-field">
                <span className="form-label" title={t('The saved action to run on the selected vessels.')}>{t('Action')}</span>
                <select
                  title={t('The saved action to run on the selected vessels.')}
                  value={selectedActionId}
                  onChange={(e) => setSelectedActionId(e.target.value)}
                  disabled={actionsLoading}
                  aria-invalid={Boolean(showError('action'))}
                >
                  {actionsLoading && <option value="">{t('Loading...')}</option>}
                  {!actionsLoading && actions.length === 0 && <option value="">{t('No saved actions')}</option>}
                  {actions.map((a) => (
                    <option key={a.id} value={a.id}>{a.name} ({t(KIND_LABELS[a.kind])})</option>
                  ))}
                </select>
                {showError('action') && <span className="field-error">{t(errors.action)}</span>}
              </label>
              {selectedAction && (
                <div className="run-action-summary">
                  <div className="run-action-summary-row">
                    <CodeStatusBadge label={t(KIND_LABELS[selectedAction.kind])} tone={selectedAction.kind === 'Command' ? 'warning' : 'info'} icon={selectedAction.kind === 'Command' ? 'alert' : 'info'} title={t(KIND_DESCRIPTIONS[selectedAction.kind])} />
                    {selectedAction.description && <span className="text-dim">{selectedAction.description}</span>}
                  </div>
                  <pre className="code-block" data-i18n-skip="true" title={t('The command or prompt this action runs, before template variables are filled in.')}>{bodyText}</pre>
                  {selectedAction.kind === 'Command' && (
                    <label className="checkbox-row" title={t('When on, any vessel whose checkout has uncommitted or untracked changes is skipped, so the command never touches unsaved work.')}>
                      <input type="checkbox" title={t('When on, any vessel whose checkout has uncommitted or untracked changes is skipped, so the command never touches unsaved work.')} checked={effectiveCleanTree} onChange={(e) => setCleanTreeOverride(e.target.checked)} />
                      <span>{t('Skip vessels with uncommitted changes (clean-tree check)')}</span>
                    </label>
                  )}
                </div>
              )}
            </div>
          )}

          {mode === 'adhoc' && (
            <div className="form-stack">
              <label className="form-field">
                <span className="form-label" title={t('Label for this run in the Runs list, for example Show git status.')}>{t('Name')}</span>
                <input title={t('Label for this run in the Runs list, for example Show git status.')} value={adHoc.name} onChange={(e) => setAdHoc({ ...adHoc, name: e.target.value })} maxLength={200} placeholder={t('e.g. Show git status')} aria-invalid={Boolean(showError('name'))} />
                {showError('name') && <span className="field-error">{t(errors.name)}</span>}
              </label>
              <div className="segmented" role="radiogroup" aria-label={t('Kind')}>
                {(['Command', 'Mission'] as FleetActionKind[]).map((k) => (
                  <label key={k} className={`segmented-option${adHoc.kind === k ? ' active' : ''}`} title={t(KIND_DESCRIPTIONS[k])}>
                    <input type="radio" name="adhoc-kind" checked={adHoc.kind === k} onChange={() => setAdHoc({ ...adHoc, kind: k, requiresCleanWorkingTree: k === 'Command' })} />
                    <span>{t(KIND_LABELS[k])}</span>
                  </label>
                ))}
              </div>
              <p className="text-dim form-help">{t(KIND_DESCRIPTIONS[adHoc.kind])}</p>
              {adHoc.kind === 'Command' ? (
                <label className="form-field">
                  <span className="form-label" title={t('Shell command run in each vessel working directory, for example git status -sb. Template variables such as {{vessel.name}} are filled in per vessel.')}>{t('Command text')}</span>
                  <textarea title={t('Shell command run in each vessel working directory, for example git status -sb. Template variables such as {{vessel.name}} are filled in per vessel.')} className="mono" rows={4} value={adHoc.commandText} onChange={(e) => setAdHoc({ ...adHoc, commandText: e.target.value })} placeholder="git status -sb" spellCheck={false} aria-invalid={Boolean(showError('body'))} />
                </label>
              ) : (
                <>
                  <label className="form-field">
                    <span className="form-label" title={t('Instructions for the captain, used as the mission description for each vessel. Template variables are filled in per vessel.')}>{t('Prompt template')}</span>
                    <textarea title={t('Instructions for the captain, used as the mission description for each vessel. Template variables are filled in per vessel.')} rows={6} value={adHoc.promptTemplate} onChange={(e) => setAdHoc({ ...adHoc, promptTemplate: e.target.value })} aria-invalid={Boolean(showError('body'))} />
                  </label>
                  <label className="form-field">
                    <span className="form-label" title={t('Pipeline the voyages run through. Vessel default uses the vessel or fleet default pipeline.')}>{t('Pipeline')}</span>
                    <select title={t('Pipeline the voyages run through. Vessel default uses the vessel or fleet default pipeline.')} value={adHoc.pipelineId} onChange={(e) => setAdHoc({ ...adHoc, pipelineId: e.target.value })}>
                      <option value="">{t('Vessel default')}</option>
                      {pipelines.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
                    </select>
                  </label>
                </>
              )}
              {unknownVars.length > 0
                ? <span className="field-error">{t('Unknown template variable: {{names}}', { names: unknownVars.join(', ') })}</span>
                : showError('body') ? <span className="field-error">{t(errors.body)}</span> : null}
              <TemplateVariableHelp />
              {adHoc.kind === 'Command' && (
                <div className="form-grid-2">
                  <label className="form-field">
                    <span className="form-label" title={t('Seconds the command may run on one vessel before it is killed and marked Timed out (5-7200).')}>{t('Timeout (seconds)')}</span>
                    <input title={t('Seconds the command may run on one vessel before it is killed and marked Timed out (5-7200).')} type="number" min={5} max={7200} value={adHoc.timeoutSeconds} onChange={(e) => setAdHoc({ ...adHoc, timeoutSeconds: e.target.value })} aria-invalid={Boolean(showError('timeout'))} />
                    {showError('timeout') && <span className="field-error">{t(errors.timeout)}</span>}
                  </label>
                  <label className="checkbox-row form-checkbox-aligned" title={t('When on, any vessel whose checkout has uncommitted or untracked changes is skipped, so the command never touches unsaved work.')}>
                    <input type="checkbox" title={t('When on, any vessel whose checkout has uncommitted or untracked changes is skipped, so the command never touches unsaved work.')} checked={adHoc.requiresCleanWorkingTree} onChange={(e) => setAdHoc({ ...adHoc, requiresCleanWorkingTree: e.target.checked })} />
                    <span>{t('Skip vessels with uncommitted changes (clean-tree check)')}</span>
                  </label>
                </div>
              )}
            </div>
          )}

          {inputDefinitions.map(input => (
            <label className="form-field" key={input.name}>
              <span>{input.name}{input.required ? ' *' : ''}</span>
              {input.type === 'enum'
                ? <select required={input.required} value={inputValues[input.name] ?? ''} onChange={e => setInputValues(v => ({ ...v, [input.name]: e.target.value }))} aria-invalid={Boolean(inputError(input.name, input.type))}>
                    <option value="">-</option>{input.allowedValues.map(value => <option key={value} value={value}>{value}</option>)}
                  </select>
                : <input
                    required={input.required}
                    value={inputValues[input.name] ?? ''}
                    onChange={e => setInputValues(v => ({ ...v, [input.name]: e.target.value }))}
                    placeholder={input.type === 'semver' ? '1.2.3' : input.type === 'package' ? 'Newtonsoft.Json' : undefined}
                    pattern={input.type === 'package' ? PACKAGE_INPUT_PATTERN.source.slice(1, -1) : undefined}
                    spellCheck={input.type === 'package' ? false : undefined}
                    aria-invalid={Boolean(inputError(input.name, input.type))}
                  />}
              {input.description && <span className="text-dim">{input.description}</span>}
              {inputError(input.name, input.type) && <span className="field-error">{inputError(input.name, input.type)}</span>}
            </label>
          ))}
          <label className="form-field run-action-concurrency">
            <span className="form-label" title={effectiveKind === 'Command' ? t('How many vessels run the command at the same time (1-32). The Admiral-wide limit also applies.') : t('How many voyages from this run may be active at once (1-32).')}>{t('Concurrency')}</span>
            <input title={effectiveKind === 'Command' ? t('How many vessels run the command at the same time (1-32). The Admiral-wide limit also applies.') : t('How many voyages from this run may be active at once (1-32).')} type="number" min={1} max={32} value={concurrency} onChange={(e) => setConcurrency(e.target.value)} aria-invalid={Boolean(showError('concurrency'))} aria-describedby="run-action-concurrency-help" />
            <span id="run-action-concurrency-help" className="text-dim form-help">
              {effectiveKind === 'Command'
                ? t('How many vessels run the command at the same time (1-32). The Admiral-wide limit also applies.')
                : t('How many voyages from this run may be active at once (1-32).')}
            </span>
            {showError('concurrency') && <span className="field-error">{t(errors.concurrency)}</span>}
          </label>

          <div className="run-action-preview">
            <div className="form-label" title={t('The command or prompt with template variables filled in for the first selected vessel, so you can check it before running.')}>
              {previewVessel
                ? t('Preview for {{name}}', { name: previewVessel.name })
                : t('Preview')}
              {count > 1 && <span className="text-dim"> {t('{count, plural, one {(and # more vessel)} other {(and # more vessels)}}', { count: count - 1 })}</span>}
            </div>
            {!bodyText && <p className="text-dim">{t('Choose or define an action to see the rendered text.')}</p>}
            {bodyText && !previewVessel && <p className="text-dim">{t('Loading preview...')}</p>}
            {preview && (
              <>
                <pre className="code-block" data-i18n-skip="true">{preview.text}</pre>
                {preview.usesHealthSummary && <p className="text-dim form-help">{t('{{health.summary}} is rendered on the server from each vessel\'s latest health evaluation.')}</p>}
              </>
            )}
          </div>
        </>
      )}

      {step === 'confirm' && (
        <div className="run-action-confirm">
          <h4>{mode === 'saved' ? selectedAction?.name : adHoc.name}</h4>
          {followUpRunId && (
            <div className="alert alert-info" role="note">
              <p>{t('Targets the vessels from run {{id}} with status: {{statuses}}.', { id: followUpRunId, statuses: followUpStatusText })}</p>
            </div>
          )}
          {followUpRunId ? (
            effectiveKind === 'Command' ? (
              <div className="alert alert-warning" role="note">
                <p>{t('This runs the command below in the working directory of each matching vessel.')}</p>
                <p>{effectiveCleanTree
                  ? t('Vessels with uncommitted changes are skipped.')
                  : t('The clean-tree check is off: the command also runs in vessels with uncommitted changes.')}</p>
              </div>
            ) : (
              <div className="alert alert-info" role="note">
                <p>{t('{count, plural, one {At most # voyage from this run is active at a time.} other {At most # voyages from this run are active at a time.}}', { count: concurrencyNumber })}</p>
              </div>
            )
          ) : effectiveKind === 'Command' ? (
            <div className="alert alert-warning" role="note">
              <p>{t('{count, plural, one {This runs the command below in the working directory of # vessel, on the Admiral host or the vessel\'s preferred Harbor.} other {This runs the command below in the working directory of each of # vessels, on the Admiral host or each vessel\'s preferred Harbor.}}', { count })}</p>
              <p>{effectiveCleanTree
                ? t('Vessels with uncommitted changes are skipped.')
                : t('The clean-tree check is off: the command also runs in vessels with uncommitted changes.')}</p>
            </div>
          ) : (
            <div className="alert alert-info" role="note">
              <p>{t('{count, plural, one {This dispatches # voyage, one per vessel.} other {This dispatches # voyages, one per vessel.}}', { count })}</p>
              <p>{t('{count, plural, one {At most # voyage from this run is active at a time.} other {At most # voyages from this run are active at a time.}}', { count: concurrencyNumber })}</p>
            </div>
          )}
          <pre className="code-block" data-i18n-skip="true">{bodyText}</pre>
          <dl className="run-action-confirm-facts">
            {effectiveKind === 'Mission' && <><dt>{t('Approve all')}</dt><dd>{approvalOptions.approveAll ? t('Yes') : t('No')}</dd>
              {!approvalOptions.approveAll && <><dt>{t('Approval timeout (seconds)')}</dt><dd>{approvalOptions.timeoutSeconds}</dd><dt>{t('Default answer on timeout')}</dt><dd>{t(approvalOptions.timeoutDecision)}</dd></>}
            </>}
            {followUpRunId
              ? <><dt title={t('The run targets the vessels of the earlier run whose result matches a checked status.')}>{t('Vessels')}</dt><dd>{t('From run {{id}}', { id: followUpRunId })}</dd></>
              : <><dt title={t('Number of vessels this run will act on.')}>{t('Vessels')}</dt><dd>{count.toLocaleString()}</dd></>}
            <dt title={t('How many vessels are worked on at once.')}>{t('Concurrency')}</dt><dd>{concurrencyNumber.toLocaleString()}</dd>
          </dl>
        </div>
      )}
    </DialogShell>
  );
}
