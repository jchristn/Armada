import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  apiErrorCode,
  discoverVesselImport,
  getFleetCategorizationDefaultPrompt,
  getVesselImportBatch,
  importVessels,
  listCaptains,
  listFleets,
  listPipelines,
} from '../../../api/client';
import type {
  Captain,
  Fleet,
  Pipeline,
  VesselImportBatch,
  VesselImportFleetRecommendation,
  VesselImportHint,
  VesselImportItem,
} from '../../../types/models';
import { useLocale } from '../../../context/LocaleContext';
import { useNotifications } from '../../../context/NotificationContext';
import DialogShell from '../../shared/DialogShell';
import ConfirmDialog from '../../shared/ConfirmDialog';
import { EmptyState, ErrorState, LoadingState } from '../../shared/StateBlocks';
import { notifyBackgroundActivity } from '../../shared/BackgroundActivityIndicator';
import BrowseTree from './BrowseTree';
import ImportReviewStep, { type ImportDefaults } from './ImportReviewStep';
import ImportResultsStep from './ImportResultsStep';
import ImportHistory from './ImportHistory';
import ImportCategorizationOptions from './ImportCategorizationOptions';
import { EMPTY_CATEGORIZATION, IMPORT_POLL_MS, parseMaxDepth, parsePastedPaths, validateCategorization, type CategorizationOptions } from '../../../lib/vesselImport';

export { IMPORT_POLL_MS, parsePastedPaths } from '../../../lib/vesselImport';
import FleetRecommendationsPanel from './FleetRecommendationsPanel';
import { importErrorLabel, isBatchBusy } from '../../../lib/vesselImportLabels';

export interface ImportWizardProps {
  open: boolean;
  onClose: () => void;
  /** Called after vessels were created so the caller can refresh its list. */
  onImported?: () => void;
  /** Open directly on the import history view. */
  initialView?: 'source' | 'history';
  /** Open directly on this batch (for example from the header activity indicator). */
  initialBatchId?: string | null;
  /** Caller's tenant; captains from other tenants (visible to global admins) are not offered for categorization. */
  tenantId?: string | null;
}

type Step = 'source' | 'discovering' | 'review' | 'results' | 'history' | 'batch';
type SourceTab = 'paste' | 'browse';

/**
 * Bulk import wizard: Source (paste paths or browse the Admiral host) -> Review (candidate table, selection,
 * fleet and defaults, optional captain-driven fleet recommendations) -> Results (per-item outcomes and fleet
 * recommendations). Discovery, larger imports, and fleet categorization run in the background: the wizard says so,
 * shows live progress while open, and can be closed at any time. Batches resume from the import history (or from the
 * header activity indicator) on the right step.
 */
export default function ImportWizard({ open, onClose, onImported, initialView = 'source', initialBatchId = null, tenantId = null }: ImportWizardProps) {
  const { t } = useLocale();
  const { pushToast } = useNotifications();
  const navigate = useNavigate();

  const [step, setStep] = useState<Step>(initialView === 'history' ? 'history' : 'source');
  const [sourceTab, setSourceTab] = useState<SourceTab>('paste');
  const [pasteText, setPasteText] = useState('');
  const [browseSelected, setBrowseSelected] = useState<string[]>([]);
  const [allowWorktrees, setAllowWorktrees] = useState(false);
  const [maxDepth, setMaxDepth] = useState('');
  const [discovering, setDiscovering] = useState(false);
  const [discoverError, setDiscoverError] = useState('');

  const [batch, setBatch] = useState<VesselImportBatch | null>(null);
  const [candidates, setCandidates] = useState<VesselImportItem[]>([]);
  const [truncated, setTruncated] = useState(false);
  const [hints, setHints] = useState<VesselImportHint[]>([]);
  const [selected, setSelected] = useState<string[]>([]);
  const [defaults, setDefaults] = useState<ImportDefaults>({ fleetId: '', pipelineId: '', landingMode: '' });
  const [fleets, setFleets] = useState<Fleet[]>([]);
  const [pipelines, setPipelines] = useState<Pipeline[]>([]);

  const [captains, setCaptains] = useState<Captain[]>([]);
  const [captainsLoading, setCaptainsLoading] = useState(false);
  const [defaultPrompt, setDefaultPrompt] = useState('');
  const [defaultPromptError, setDefaultPromptError] = useState('');
  const [timeoutMinutes, setTimeoutMinutes] = useState<number | undefined>(undefined);
  const [categorization, setCategorization] = useState<CategorizationOptions>(EMPTY_CATEGORIZATION);
  const [showCategorizationErrors, setShowCategorizationErrors] = useState(false);

  const [importing, setImporting] = useState(false);
  const [importError, setImportError] = useState('');
  const [importErrorCode, setImportErrorCode] = useState<string | null>(null);
  const [resultItems, setResultItems] = useState<VesselImportItem[]>([]);
  const [recommendations, setRecommendations] = useState<VesselImportFleetRecommendation[]>([]);
  const [jobId, setJobId] = useState<string | null>(null);
  const [polling, setPolling] = useState(false);
  const [pollError, setPollError] = useState('');
  const [selectedCount, setSelectedCount] = useState(0);

  const [historyLoading, setHistoryLoading] = useState(false);
  const [historyError, setHistoryError] = useState('');
  const [confirmClose, setConfirmClose] = useState(false);

  const pollTimer = useRef<number | null>(null);
  const importErrorRef = useRef<HTMLDivElement | null>(null);
  const lastStatus = useRef<string | null>(null);
  // Callers often pass a fresh onImported arrow on every render; keep it in a ref so the poll loop and the
  // open effect stay stable and never reset the wizard mid-flow.
  const onImportedRef = useRef(onImported);
  onImportedRef.current = onImported;

  const pasted = parsePastedPaths(pasteText);
  const sourcePaths = sourceTab === 'paste' ? pasted : browseSelected;
  const { value: depthNumber, invalid: depthInvalid } = parseMaxDepth(maxDepth);

  const stopPolling = useCallback(() => {
    if (pollTimer.current !== null) {
      window.clearTimeout(pollTimer.current);
      pollTimer.current = null;
    }
    setPolling(false);
  }, []);

  useEffect(() => () => stopPolling(), [stopPolling]);
  useEffect(() => { if (!open) stopPolling(); }, [open, stopPolling]);

  function loadReview(nextBatch: VesselImportBatch, items: VesselImportItem[], nextTruncated: boolean, nextHints: VesselImportHint[]) {
    setBatch(nextBatch);
    setCandidates(items);
    setTruncated(nextTruncated);
    setHints(nextHints);
    setSelected(items.filter((c) => c.candidateStatus === 'New').map((c) => c.path));
    setDefaults((d) => ({ ...d, fleetId: nextBatch.fleetId ?? d.fleetId }));
    setImportError('');
    setImportErrorCode(null);
    setStep('review');
  }

  /** Poll a batch while discovery, the import, or fleet categorization runs; stops when nothing is running. */
  const poll = useCallback(async (batchId: string) => {
    pollTimer.current = null;
    try {
      const detail = await getVesselImportBatch(batchId);
      const previous = lastStatus.current;
      lastStatus.current = detail.batch.status;
      setPollError('');

      if (detail.batch.status === 'Discovering') {
        setBatch(detail.batch);
        pollTimer.current = window.setTimeout(() => void poll(batchId), IMPORT_POLL_MS);
        return;
      }

      if (previous === 'Discovering') {
        stopPolling();
        if (detail.batch.status === 'Discovered') {
          loadReview(detail.batch, detail.items || [], !!detail.batch.truncated, detail.hints || []);
        } else {
          setDiscoverError(detail.batch.errorMessage || t('Discovery failed.'));
          setStep('source');
        }
        return;
      }

      setBatch(detail.batch);
      setResultItems(detail.items || []);
      setRecommendations(detail.fleetRecommendations || []);

      if (previous === 'Importing' && detail.batch.status !== 'Importing') {
        if (detail.batch.createdCount > 0) onImportedRef.current?.();
        pushToast(detail.batch.failedCount > 0 ? 'warning' : 'success', t('{count, plural, one {Import finished: # vessel created.} other {Import finished: # vessels created.}}', { count: detail.batch.createdCount }));
      }

      if (isBatchBusy(detail.batch)) {
        setPolling(true);
        pollTimer.current = window.setTimeout(() => void poll(batchId), IMPORT_POLL_MS);
      } else {
        stopPolling();
      }
    } catch (err: unknown) {
      setPollError(err instanceof Error ? err.message : t('Failed to refresh import progress.'));
      pollTimer.current = window.setTimeout(() => void poll(batchId), IMPORT_POLL_MS * 2);
    }
    // loadReview only sets state; leaving it out keeps the poll callback stable.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pushToast, stopPolling, t]);

  const startPolling = useCallback((batchId: string, status: string) => {
    if (pollTimer.current !== null) window.clearTimeout(pollTimer.current);
    lastStatus.current = status;
    setPolling(true);
    pollTimer.current = window.setTimeout(() => void poll(batchId), IMPORT_POLL_MS);
  }, [poll]);

  const openBatch = useCallback(async (batchId: string) => {
    stopPolling();
    setHistoryLoading(true);
    setHistoryError('');
    setStep('batch');
    try {
      const detail = await getVesselImportBatch(batchId);
      lastStatus.current = detail.batch.status;
      if (detail.batch.status === 'Discovering') {
        setBatch(detail.batch);
        setStep('discovering');
        startPolling(detail.batch.id, 'Discovering');
      } else if (detail.batch.status === 'Discovered') {
        loadReview(detail.batch, detail.items || [], !!detail.batch.truncated, detail.hints || []);
      } else {
        setBatch(detail.batch);
        setResultItems(detail.items || []);
        setRecommendations(detail.fleetRecommendations || []);
        setJobId(detail.batch.jobId);
        setSelectedCount(detail.items.filter((i) => i.selected || (i.outcome !== 'SkippedNotSelected' && i.outcome !== 'Pending')).length);
        if (detail.batch.status === 'Importing') setStep('results');
        if (isBatchBusy(detail.batch)) startPolling(detail.batch.id, detail.batch.status);
      }
    } catch (err: unknown) {
      setHistoryError(importErrorLabel(t, apiErrorCode(err), err instanceof Error ? err.message : t('Failed to load the batch.')));
    } finally {
      setHistoryLoading(false);
    }
    // loadReview only sets state.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [startPolling, stopPolling, t]);

  const openBatchRef = useRef(openBatch);
  openBatchRef.current = openBatch;

  // Runs only when the dialog opens or the requested view/batch changes, never on unrelated re-renders.
  useEffect(() => {
    if (!open) return;
    if (initialBatchId) void openBatchRef.current(initialBatchId);
    else setStep(initialView === 'history' ? 'history' : 'source');
    let cancelled = false;
    listFleets({ pageSize: 9999 }).then((r) => { if (!cancelled) setFleets(r.objects || []); }).catch(() => undefined);
    listPipelines({ pageSize: 9999 }).then((r) => { if (!cancelled) setPipelines(r.objects || []); }).catch(() => undefined);
    return () => { cancelled = true; };
  }, [open, initialView, initialBatchId]);

  // Captains and the default prompt are only needed once the operator opts into fleet recommendations.
  useEffect(() => {
    if (!open || !categorization.enabled || captains.length > 0 || captainsLoading) return;
    let cancelled = false;
    setCaptainsLoading(true);
    listCaptains({ pageSize: 9999 })
      .then((r) => { if (!cancelled) setCaptains((r.objects || []).filter((c) => !tenantId || c.tenantId === tenantId)); })
      .catch(() => undefined)
      .finally(() => { if (!cancelled) setCaptainsLoading(false); });
    if (!defaultPrompt) {
      getFleetCategorizationDefaultPrompt()
        .then((r) => {
          if (cancelled) return;
          setDefaultPrompt(r.prompt);
          setTimeoutMinutes(r.timeoutMinutes);
          setCategorization((c) => (c.prompt ? c : { ...c, prompt: r.prompt }));
        })
        .catch((err: unknown) => { if (!cancelled) setDefaultPromptError(err instanceof Error ? err.message : t('unknown error')); });
    }
    return () => { cancelled = true; };
    // Load once per opt-in; captains/defaultPrompt guard against reloading.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, categorization.enabled]);

  function resetAll() {
    stopPolling();
    lastStatus.current = null;
    setStep('source');
    setBatch(null);
    setCandidates([]);
    setTruncated(false);
    setHints([]);
    setSelected([]);
    setResultItems([]);
    setRecommendations([]);
    setJobId(null);
    setImportError('');
    setImportErrorCode(null);
    setDiscoverError('');
    setPollError('');
    setShowCategorizationErrors(false);
  }

  async function discover() {
    if (sourcePaths.length === 0 || depthInvalid) return;
    setDiscovering(true);
    setDiscoverError('');
    try {
      const result = await discoverVesselImport({
        Directories: sourcePaths,
        ...(depthNumber !== null ? { MaxDepth: depthNumber } : {}),
        RunInBackground: true,
      });
      if (result.runsInBackground) {
        setBatch(result.batch);
        setStep('discovering');
        notifyBackgroundActivity();
        startPolling(result.batchId, 'Discovering');
      } else {
        loadReview(result.batch, result.candidates || [], result.truncated, result.hints || []);
      }
    } catch (err: unknown) {
      const fallback = err instanceof Error ? err.message : t('Discovery failed.');
      setDiscoverError(importErrorLabel(t, apiErrorCode(err), fallback) + (apiErrorCode(err) && err instanceof Error ? ` (${err.message})` : ''));
    } finally {
      setDiscovering(false);
    }
  }

  async function runImport() {
    if (!batch || selected.length === 0) return;
    const categorizationErrors = validateCategorization(categorization);
    if (Object.keys(categorizationErrors).length > 0) {
      setShowCategorizationErrors(true);
      return;
    }
    setImporting(true);
    setImportError('');
    setImportErrorCode(null);
    try {
      const response = await importVessels({
        BatchId: batch.id,
        Paths: selected,
        FleetId: defaults.fleetId || null,
        Defaults: (defaults.pipelineId || defaults.landingMode)
          ? { DefaultPipelineId: defaults.pipelineId || null, LandingMode: defaults.landingMode || null }
          : null,
        ...(categorization.enabled
          ? {
            Categorization: {
              Enabled: true,
              CaptainId: categorization.captainId,
              Prompt: categorization.prompt === defaultPrompt ? null : categorization.prompt,
              ApplyAutomatically: categorization.applyAutomatically,
            },
          }
          : {}),
      });
      setSelectedCount(selected.length);
      setBatch(response.batch);
      setResultItems(response.items || []);
      setRecommendations([]);
      setJobId(response.jobId);
      setStep('results');
      if (response.runsInBackground) {
        notifyBackgroundActivity();
        startPolling(response.batchId, 'Importing');
      } else {
        if (response.batch.createdCount > 0) onImported?.();
        pushToast(response.batch.failedCount > 0 ? 'warning' : 'success', t('{count, plural, one {Import finished: # vessel created.} other {Import finished: # vessels created.}}', { count: response.batch.createdCount }));
        if (isBatchBusy(response.batch)) {
          notifyBackgroundActivity();
          startPolling(response.batchId, response.batch.status);
        }
      }
    } catch (err: unknown) {
      const code = apiErrorCode(err);
      const fallback = err instanceof Error ? err.message : t('Import failed.');
      setImportErrorCode(code);
      setImportError(importErrorLabel(t, code, fallback) + (code && err instanceof Error ? ` (${err.message})` : ''));
    } finally {
      setImporting(false);
    }
  }

  useEffect(() => {
    if (importError && importErrorRef.current && typeof importErrorRef.current.scrollIntoView === 'function') {
      importErrorRef.current.scrollIntoView({ block: 'nearest' });
    }
  }, [importError]);

  function requestClose() {
    if (step === 'review' && candidates.length > 0) {
      setConfirmClose(true);
      return;
    }
    onClose();
  }

  function refreshAfterChange() {
    if (batch) {
      notifyBackgroundActivity();
      void poll(batch.id);
    }
  }

  const stepIndex = step === 'source' || step === 'discovering' ? 0 : step === 'review' ? 1 : step === 'results' ? 2 : -1;
  const steps = [t('Source'), t('Review'), t('Results')];
  const importPolling = polling && batch?.status === 'Importing';

  let footer: ReactNode = null;
  if (step === 'source') {
    footer = (
      <>
        <button type="button" className="btn" onClick={requestClose}>{t('Cancel')}</button>
        <button type="button" className="btn btn-primary" onClick={() => void discover()} disabled={discovering || sourcePaths.length === 0 || depthInvalid}>
          {discovering ? t('Discovering...') : t('Discover')}
        </button>
      </>
    );
  } else if (step === 'discovering') {
    footer = (
      <>
        <button type="button" className="btn" onClick={resetAll}>{t('Start over')}</button>
        <button type="button" className="btn btn-primary" onClick={onClose}>{t('Close')}</button>
      </>
    );
  } else if (step === 'review') {
    footer = (
      <>
        <button type="button" className="btn" onClick={() => setStep('source')} disabled={importing}>{t('Back')}</button>
        <button type="button" className="btn btn-primary" onClick={() => void runImport()} disabled={importing || selected.length === 0}>
          {importing ? t('Importing...') : t('{count, plural, =0 {Import repositories} one {Import # repository} other {Import # repositories}}', { count: selected.length })}
        </button>
      </>
    );
  } else if (step === 'results') {
    footer = (
      <>
        <button type="button" className="btn" onClick={resetAll}>{t('Import more')}</button>
        <button type="button" className="btn" onClick={() => { onClose(); navigate('/vessels'); }}>{t('View vessels')}</button>
        <button type="button" className="btn btn-primary" onClick={onClose}>{t('Close')}</button>
      </>
    );
  } else {
    footer = (
      <>
        {step === 'batch' && <button type="button" className="btn" onClick={() => { stopPolling(); setStep('history'); }}>{t('Back to history')}</button>}
        <button type="button" className="btn" onClick={resetAll}>{t('New import')}</button>
        <button type="button" className="btn btn-primary" onClick={onClose}>{t('Close')}</button>
      </>
    );
  }

  return (
    <>
      <DialogShell
        open={open}
        onClose={requestClose}
        dismissible={!discovering && !importing}
        size="xl"
        title={t('Import repositories')}
        subtitle={t('Onboard existing local git repositories as vessels in bulk. Nothing is created until you confirm.')}
        footer={footer}
        className="import-wizard"
      >
        <div className="wizard-header-row">
          {stepIndex >= 0 ? (
            <ol className="wizard-steps" aria-label={t('Import steps')}>
              {steps.map((label, i) => (
                <li key={label} className={`wizard-step${i === stepIndex ? ' active' : ''}${i < stepIndex ? ' done' : ''}`} aria-current={i === stepIndex ? 'step' : undefined}>
                  <span className="wizard-step-num" aria-hidden="true">{i + 1}</span>
                  <span>{label}</span>
                </li>
              ))}
            </ol>
          ) : (
            <h4 className="wizard-view-title">{t('Import history')}</h4>
          )}
          {(step === 'source' || step === 'results' || step === 'discovering') && (
            <button type="button" className="btn btn-sm" onClick={() => { stopPolling(); setStep('history'); }}>{t('Import history')}</button>
          )}
        </div>

        {step === 'source' && (
          <div className="import-source">
            {discoverError && <ErrorState message={discoverError} onRetry={() => void discover()} />}
            <div className="page-tabs-list import-source-tabs" role="tablist" aria-label={t('Source')}>
              <button type="button" role="tab" aria-selected={sourceTab === 'paste'} className={`page-tab${sourceTab === 'paste' ? ' active' : ''}`} onClick={() => setSourceTab('paste')}>{t('Paste paths')}</button>
              <button type="button" role="tab" aria-selected={sourceTab === 'browse'} className={`page-tab${sourceTab === 'browse' ? ' active' : ''}`} onClick={() => setSourceTab('browse')}>{t('Browse')}</button>
            </div>
            {sourceTab === 'paste' ? (
              <label className="form-field">
                <span className="form-label">{t('Folders on the Admiral host, one per line')}</span>
                <textarea
                  className="mono"
                  rows={8}
                  value={pasteText}
                  onChange={(e) => setPasteText(e.target.value)}
                  placeholder={'/Users/alex/Code\n/Users/alex/Code/api'}
                  spellCheck={false}
                  aria-describedby="import-paste-help"
                />
                <span id="import-paste-help" className="text-dim form-help">
                  {t('{count, plural, one {# path} other {# paths}}', { count: pasted.length })}
                  {' - '}
                  {t('A repository folder becomes one candidate; any other folder is scanned for repositories below it.')}
                </span>
              </label>
            ) : (
              <>
                <BrowseTree
                  selected={browseSelected}
                  allowWorktrees={allowWorktrees}
                  onToggle={(p) => setBrowseSelected((s) => (s.includes(p) ? s.filter((x) => x !== p) : [...s, p]))}
                />
                <label className="checkbox-row">
                  <input type="checkbox" checked={allowWorktrees} onChange={(e) => setAllowWorktrees(e.target.checked)} />
                  <span>{t('Allow selecting worktrees')}</span>
                </label>
                {browseSelected.length > 0 && (
                  <div className="browse-selected">
                    <span className="form-label">{t('{count, plural, one {# folder selected} other {# folders selected}}', { count: browseSelected.length })}</span>
                    <ul>
                      {browseSelected.map((p) => (
                        <li key={p}>
                          <span className="mono" data-i18n-skip="true">{p}</span>
                          <button type="button" className="btn btn-sm" onClick={() => setBrowseSelected((s) => s.filter((x) => x !== p))} aria-label={t('Remove {{path}}', { path: p })} title={t('Remove {{path}}', { path: p })}>
                            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" aria-hidden="true"><path d="M18 6 6 18" /><path d="m6 6 12 12" /></svg>
                          </button>
                        </li>
                      ))}
                    </ul>
                  </div>
                )}
              </>
            )}
            <label className="form-field import-depth">
              <span className="form-label">{t('Max depth')}</span>
              <input type="number" min={1} max={16} value={maxDepth} onChange={(e) => setMaxDepth(e.target.value)} placeholder={t('Server default')} aria-invalid={depthInvalid} />
              {depthInvalid
                ? <span className="field-error">{t('Max depth must be a whole number from 1 to 16.')}</span>
                : <span className="text-dim form-help">{t('Folder levels searched below each folder that is not itself a repository (1-16). Leave empty for the server default.')}</span>}
            </label>
          </div>
        )}

        {step === 'discovering' && (
          <div className="alert alert-info import-background-note" role="status" aria-live="polite">
            <span className="spinner-inline" aria-hidden="true" />
            <div>
              <p><strong>{t('Scanning for repositories in the background.')}</strong></p>
              <p>{t('You can close this dialog; discovery keeps running and the header shows it while it runs. Reopen it from the import history to review the candidates.')}</p>
              {batch && <p className="text-dim mono" data-i18n-skip="true">{batch.id}</p>}
              {pollError && <p className="field-error">{pollError}</p>}
            </div>
          </div>
        )}

        {step === 'review' && batch && (
          <>
            {importError && (
              <div className="alert alert-error" role="alert" ref={importErrorRef}>
                <span>{importError}</span>{' '}
                <button type="button" className="btn btn-sm" onClick={() => void runImport()}>{t('Retry')}</button>
                {importErrorCode === 'BatchBusy' && <button type="button" className="btn btn-sm" onClick={() => setStep('history')}>{t('Open import history')}</button>}
                {importErrorCode === 'BatchNotFound' && <button type="button" className="btn btn-sm" onClick={() => void discover()}>{t('Discover again')}</button>}
              </div>
            )}
            {candidates.length === 0 ? (
              <EmptyState title={t('No repositories found')}>
                {hints.map((h) => <p key={h.code}>{h.code === 'PathNotVisibleToAdmiral' ? t('None of the requested paths exist on the Admiral host. If the Admiral runs in a container it cannot see your host directories: mount them into the container, or run discovery through a Harbor on that machine.') : h.message}</p>)}
                <p>{t('Discovery searches each folder up to the max depth, skips excluded names (such as node_modules, bin, obj) and anything starting with a dot, and never descends into a repository it already found.')}</p>
                <p>{t('Try a higher max depth, a folder closer to the repositories, or check the excluded names under Settings > Import.')}</p>
              </EmptyState>
            ) : (
              <>
                <ImportReviewStep
                  candidates={candidates}
                  truncated={truncated}
                  hints={hints}
                  selected={selected}
                  onSelectedChange={setSelected}
                  fleets={fleets}
                  pipelines={pipelines}
                  defaults={defaults}
                  onDefaultsChange={setDefaults}
                />
                <ImportCategorizationOptions
                  value={categorization}
                  onChange={setCategorization}
                  captains={captains}
                  captainsLoading={captainsLoading}
                  defaultPrompt={defaultPrompt}
                  defaultPromptError={defaultPromptError}
                  timeoutMinutes={timeoutMinutes}
                  showErrors={showCategorizationErrors}
                />
              </>
            )}
          </>
        )}

        {(step === 'results' || step === 'batch') && (
          <>
            {step === 'batch' && historyLoading && <LoadingState />}
            {step === 'batch' && historyError && <ErrorState message={historyError} />}
            {batch && !(step === 'batch' && (historyLoading || historyError)) && (
              <>
                <ImportResultsStep batch={batch} items={resultItems} selectedCount={selectedCount} polling={importPolling} jobId={jobId} pollError={pollError} />
                <FleetRecommendationsPanel batch={batch} items={resultItems} recommendations={recommendations} onChanged={refreshAfterChange} />
              </>
            )}
          </>
        )}

        {step === 'history' && <ImportHistory onOpen={(b) => void openBatch(b.id)} />}
      </DialogShell>
      <ConfirmDialog
        open={confirmClose}
        title={t('Leave the import review?')}
        message={t('Nothing has been imported yet. The discovered batch stays in the import history, so you can continue it later.')}
        confirmLabel={t('Leave')}
        cancelLabel={t('Stay')}
        onConfirm={() => { setConfirmClose(false); onClose(); }}
        onCancel={() => setConfirmClose(false)}
      />
    </>
  );
}
