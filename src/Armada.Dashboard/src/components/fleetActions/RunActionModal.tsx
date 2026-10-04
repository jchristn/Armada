import { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  enumerateFleetActions,
  getVessel,
  listPipelines,
  runAdHocFleetAction,
  runFleetAction,
} from '../../api/client';
import type {
  FleetAction,
  FleetActionKind,
  FleetActionRunRequest,
  FleetActionRunStartResult,
  FleetActionUpsertRequest,
  Pipeline,
  Vessel,
} from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import { useNotifications } from '../../context/NotificationContext';
import DialogShell from '../shared/DialogShell';
import CodeStatusBadge from '../shared/CodeStatusBadge';
import TemplateVariableHelp from './TemplateVariableHelp';
import { KIND_DESCRIPTIONS, KIND_LABELS } from '../../lib/fleetActionLabels';
import { findUnknownTemplateVariables, renderTemplatePreview } from '../../lib/fleetActionTemplate';

/** Largest target set the server accepts in one run. */
export const MAX_RUN_VESSELS = 500;

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
}

type Mode = 'saved' | 'adhoc';
type Step = 'configure' | 'confirm';

interface AdHocForm {
  name: string;
  kind: FleetActionKind;
  commandText: string;
  promptTemplate: string;
  pipelineId: string;
  timeoutSeconds: string;
  requiresCleanWorkingTree: boolean;
}

interface FieldErrors {
  [field: string]: string;
}

function parseIntStrict(value: string): number | null {
  if (!/^\s*\d+\s*$/.test(value)) return null;
  return parseInt(value, 10);
}

/**
 * Validate the run modal inputs. Exported for unit tests; returns field -> English message (callers translate).
 */
export function validateRunInputs(args: {
  vesselCount: number;
  mode: Mode;
  action: FleetAction | null;
  concurrency: string;
  adHoc: AdHocForm;
}): FieldErrors {
  const errors: FieldErrors = {};
  if (args.vesselCount < 1) errors.vessels = 'Select at least one vessel.';
  else if (args.vesselCount > MAX_RUN_VESSELS) errors.vessels = 'A run can target at most 500 vessels.';

  const c = parseIntStrict(args.concurrency);
  if (c === null || c < 1 || c > 32) errors.concurrency = 'Concurrency must be a whole number from 1 to 32.';

  if (args.mode === 'saved') {
    if (!args.action) errors.action = 'Choose an action.';
    return errors;
  }

  const name = args.adHoc.name.trim();
  if (!name) errors.name = 'Name is required.';
  else if (name.length > 200) errors.name = 'Name must be 200 characters or fewer.';
  const body = args.adHoc.kind === 'Command' ? args.adHoc.commandText : args.adHoc.promptTemplate;
  if (!body.trim()) errors.body = args.adHoc.kind === 'Command' ? 'Command text is required.' : 'Prompt template is required.';
  else if (findUnknownTemplateVariables(body).length > 0) errors.body = 'unknown-variables';
  if (args.adHoc.kind === 'Command') {
    const timeout = parseIntStrict(args.adHoc.timeoutSeconds);
    if (timeout === null || timeout < 5 || timeout > 7200) errors.timeout = 'Timeout must be a whole number of seconds from 5 to 7200.';
  }
  return errors;
}

function emptyAdHoc(kind: FleetActionKind): AdHocForm {
  return { name: '', kind, commandText: '', promptTemplate: '', pipelineId: '', timeoutSeconds: '300', requiresCleanWorkingTree: kind === 'Command' };
}

/**
 * Reusable "Run fleet action" modal. Given selected vessel ids it lets the operator pick a saved action or
 * define an ad hoc one, previews the rendered text for the first vessel, sets concurrency, confirms, and
 * starts the run. Used by the Vessels bulk bar, the Fleet Actions page, and (later) the Health page.
 */
export default function RunActionModal({ open, vesselIds, onClose, initialActionId, initialKind, initialDefinition, onStarted }: RunActionModalProps) {
  const { t } = useLocale();
  const { pushToast } = useNotifications();
  const navigate = useNavigate();

  const [mode, setMode] = useState<Mode>(initialDefinition ? 'adhoc' : 'saved');
  const [step, setStep] = useState<Step>('configure');
  const [actions, setActions] = useState<FleetAction[]>([]);
  const [actionsLoading, setActionsLoading] = useState(false);
  const [actionsError, setActionsError] = useState('');
  const [selectedActionId, setSelectedActionId] = useState<string>(initialActionId ?? '');
  const [pipelines, setPipelines] = useState<Pipeline[]>([]);
  const [previewVessel, setPreviewVessel] = useState<Vessel | null>(null);
  const [concurrency, setConcurrency] = useState('4');
  const [cleanTreeOverride, setCleanTreeOverride] = useState<boolean | null>(null);
  const [adHoc, setAdHoc] = useState<AdHocForm>(() => emptyAdHoc(initialKind ?? 'Command'));
  const [submitted, setSubmitted] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [serverError, setServerError] = useState('');

  // Reset whenever the modal is (re)opened.
  useEffect(() => {
    if (!open) return;
    setStep('configure');
    setSubmitted(false);
    setServerError('');
    setCleanTreeOverride(null);
    setSelectedActionId(initialActionId ?? '');
    if (initialDefinition) {
      setMode('adhoc');
      setAdHoc({
        name: initialDefinition.Name ?? '',
        kind: initialDefinition.Kind ?? 'Command',
        commandText: initialDefinition.CommandText ?? '',
        promptTemplate: initialDefinition.PromptTemplate ?? '',
        pipelineId: initialDefinition.PipelineId ?? '',
        timeoutSeconds: String(initialDefinition.TimeoutSeconds ?? 300),
        requiresCleanWorkingTree: initialDefinition.RequiresCleanWorkingTree ?? (initialDefinition.Kind !== 'Mission'),
      });
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
    enumerateFleetActions({ pageNumber: 1, pageSize: 500 })
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
    listPipelines({ pageSize: 9999 }).then((r) => { if (!cancelled) setPipelines(r.objects || []); }).catch(() => undefined);
    return () => { cancelled = true; };
  }, [open, initialKind, t]);

  useEffect(() => {
    if (!open || vesselIds.length === 0) { setPreviewVessel(null); return; }
    let cancelled = false;
    getVessel(vesselIds[0]).then((v) => { if (!cancelled) setPreviewVessel(v); }).catch(() => { if (!cancelled) setPreviewVessel(null); });
    return () => { cancelled = true; };
  }, [open, vesselIds]);

  const selectedAction = useMemo(() => actions.find((a) => a.id === selectedActionId) ?? null, [actions, selectedActionId]);

  // Default concurrency follows the chosen action.
  useEffect(() => {
    if (mode === 'saved' && selectedAction) setConcurrency(String(Math.min(32, Math.max(1, selectedAction.defaultConcurrency || 4))));
    setCleanTreeOverride(null);
  }, [mode, selectedAction]);

  const effectiveKind: FleetActionKind = mode === 'saved' ? (selectedAction?.kind ?? 'Command') : adHoc.kind;
  const bodyText = mode === 'saved'
    ? (selectedAction ? (selectedAction.kind === 'Command' ? selectedAction.commandText : selectedAction.promptTemplate) ?? '' : '')
    : (adHoc.kind === 'Command' ? adHoc.commandText : adHoc.promptTemplate);
  const effectiveCleanTree = mode === 'saved'
    ? (cleanTreeOverride ?? selectedAction?.requiresCleanWorkingTree ?? true)
    : adHoc.requiresCleanWorkingTree;

  const errors = validateRunInputs({ vesselCount: vesselIds.length, mode, action: selectedAction, concurrency, adHoc });
  const unknownVars = findUnknownTemplateVariables(bodyText);
  const hasErrors = Object.keys(errors).length > 0;
  const showError = (field: string) => (submitted && errors[field] ? errors[field] : '');

  const preview = useMemo(() => {
    if (!previewVessel || !bodyText) return null;
    return renderTemplatePreview(bodyText, previewVessel, t('[health summary is rendered on the server for each vessel]'));
  }, [previewVessel, bodyText, t]);

  function goToConfirm() {
    setSubmitted(true);
    if (hasErrors) return;
    setServerError('');
    setStep('confirm');
  }

  async function start() {
    setSubmitting(true);
    setServerError('');
    const c = parseInt(concurrency, 10);
    try {
      let result: FleetActionRunStartResult;
      if (mode === 'saved' && selectedAction) {
        const request: FleetActionRunRequest = { VesselIds: vesselIds, Concurrency: c };
        if (selectedAction.kind === 'Command' && cleanTreeOverride !== null && cleanTreeOverride !== selectedAction.requiresCleanWorkingTree) {
          request.Overrides = { RequiresCleanWorkingTree: cleanTreeOverride };
        }
        result = await runFleetAction(selectedAction.id, request);
      } else {
        const definition: FleetActionUpsertRequest = {
          Name: adHoc.name.trim(),
          Kind: adHoc.kind,
          CommandText: adHoc.kind === 'Command' ? adHoc.commandText : null,
          PromptTemplate: adHoc.kind === 'Mission' ? adHoc.promptTemplate : null,
          PipelineId: adHoc.kind === 'Mission' && adHoc.pipelineId ? adHoc.pipelineId : null,
          TimeoutSeconds: adHoc.kind === 'Command' ? parseInt(adHoc.timeoutSeconds, 10) : null,
          RequiresCleanWorkingTree: adHoc.kind === 'Command' ? adHoc.requiresCleanWorkingTree : false,
        };
        result = await runAdHocFleetAction({ VesselIds: vesselIds, Concurrency: c, Definition: definition });
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

  const footer = step === 'configure' ? (
    <>
      <button type="button" className="btn" onClick={onClose}>{t('Cancel')}</button>
      <button type="button" className="btn btn-primary" onClick={goToConfirm} disabled={actionsLoading && mode === 'saved'}>
        {t('Review and run')}
      </button>
    </>
  ) : (
    <>
      <button type="button" className="btn" onClick={() => setStep('configure')} disabled={submitting}>{t('Back')}</button>
      <button type="button" className={`btn ${effectiveKind === 'Command' ? 'btn-danger' : 'btn-primary'}`} onClick={() => void start()} disabled={submitting}>
        {submitting ? t('Starting...') : t('{count, plural, one {Run on # vessel} other {Run on # vessels}}', { count })}
      </button>
    </>
  );

  return (
    <DialogShell
      open={open}
      onClose={onClose}
      dismissible={!submitting}
      title={t('Run fleet action')}
      subtitle={t('{count, plural, one {# vessel selected} other {# vessels selected}}', { count })}
      footer={footer}
      className="run-action-modal"
    >
      {serverError && <div className="alert alert-error" role="alert">{serverError}</div>}
      {showError('vessels') && <div className="alert alert-error" role="alert">{t(errors.vessels)}</div>}

      {step === 'configure' && (
        <>
          <div className="segmented" role="radiogroup" aria-label={t('Action source')}>
            <label className={`segmented-option${mode === 'saved' ? ' active' : ''}`}>
              <input type="radio" name="run-mode" checked={mode === 'saved'} onChange={() => setMode('saved')} />
              <span>{t('Saved action')}</span>
            </label>
            <label className={`segmented-option${mode === 'adhoc' ? ' active' : ''}`}>
              <input type="radio" name="run-mode" checked={mode === 'adhoc'} onChange={() => setMode('adhoc')} />
              <span>{t('Ad hoc')}</span>
            </label>
          </div>

          {mode === 'saved' && (
            <div className="form-stack">
              {actionsError && (
                <div className="alert alert-error" role="alert">
                  {actionsError}
                </div>
              )}
              <label className="form-field">
                <span className="form-label">{t('Action')}</span>
                <select
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
                  <pre className="code-block" data-i18n-skip="true">{bodyText}</pre>
                  {selectedAction.kind === 'Command' && (
                    <label className="checkbox-row">
                      <input type="checkbox" checked={effectiveCleanTree} onChange={(e) => setCleanTreeOverride(e.target.checked)} />
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
                <span className="form-label">{t('Name')}</span>
                <input value={adHoc.name} onChange={(e) => setAdHoc({ ...adHoc, name: e.target.value })} maxLength={200} placeholder={t('e.g. Show git status')} aria-invalid={Boolean(showError('name'))} />
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
                  <span className="form-label">{t('Command text')}</span>
                  <textarea className="mono" rows={4} value={adHoc.commandText} onChange={(e) => setAdHoc({ ...adHoc, commandText: e.target.value })} placeholder="git status -sb" spellCheck={false} aria-invalid={Boolean(showError('body'))} />
                </label>
              ) : (
                <>
                  <label className="form-field">
                    <span className="form-label">{t('Prompt template')}</span>
                    <textarea rows={6} value={adHoc.promptTemplate} onChange={(e) => setAdHoc({ ...adHoc, promptTemplate: e.target.value })} aria-invalid={Boolean(showError('body'))} />
                  </label>
                  <label className="form-field">
                    <span className="form-label">{t('Pipeline')}</span>
                    <select value={adHoc.pipelineId} onChange={(e) => setAdHoc({ ...adHoc, pipelineId: e.target.value })}>
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
                    <span className="form-label">{t('Timeout (seconds)')}</span>
                    <input type="number" min={5} max={7200} value={adHoc.timeoutSeconds} onChange={(e) => setAdHoc({ ...adHoc, timeoutSeconds: e.target.value })} aria-invalid={Boolean(showError('timeout'))} />
                    {showError('timeout') && <span className="field-error">{t(errors.timeout)}</span>}
                  </label>
                  <label className="checkbox-row form-checkbox-aligned">
                    <input type="checkbox" checked={adHoc.requiresCleanWorkingTree} onChange={(e) => setAdHoc({ ...adHoc, requiresCleanWorkingTree: e.target.checked })} />
                    <span>{t('Skip vessels with uncommitted changes (clean-tree check)')}</span>
                  </label>
                </div>
              )}
            </div>
          )}

          <label className="form-field run-action-concurrency">
            <span className="form-label">{t('Concurrency')}</span>
            <input type="number" min={1} max={32} value={concurrency} onChange={(e) => setConcurrency(e.target.value)} aria-invalid={Boolean(showError('concurrency'))} aria-describedby="run-action-concurrency-help" />
            <span id="run-action-concurrency-help" className="text-dim form-help">
              {effectiveKind === 'Command'
                ? t('How many vessels run the command at the same time (1-32). The Admiral-wide limit also applies.')
                : t('How many voyages from this run may be active at once (1-32).')}
            </span>
            {showError('concurrency') && <span className="field-error">{t(errors.concurrency)}</span>}
          </label>

          <div className="run-action-preview">
            <div className="form-label">
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
                {preview.missingBuildCommand && <p className="field-warning">{t('This vessel has no build command, so it will be skipped.')}</p>}
              </>
            )}
          </div>
        </>
      )}

      {step === 'confirm' && (
        <div className="run-action-confirm">
          <h4>{mode === 'saved' ? selectedAction?.name : adHoc.name}</h4>
          {effectiveKind === 'Command' ? (
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
            <dt>{t('Vessels')}</dt><dd>{count.toLocaleString()}</dd>
            <dt>{t('Concurrency')}</dt><dd>{concurrencyNumber.toLocaleString()}</dd>
          </dl>
        </div>
      )}
    </DialogShell>
  );
}
