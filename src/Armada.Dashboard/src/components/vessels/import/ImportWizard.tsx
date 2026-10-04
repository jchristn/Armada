import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  apiErrorCode,
  discoverVesselImport,
  getVesselImportBatch,
  importVessels,
  listFleets,
  listPipelines,
} from '../../../api/client';
import type {
  Fleet,
  Pipeline,
  VesselImportBatch,
  VesselImportHint,
  VesselImportItem,
} from '../../../types/models';
import { useLocale } from '../../../context/LocaleContext';
import { useNotifications } from '../../../context/NotificationContext';
import DialogShell from '../../shared/DialogShell';
import ConfirmDialog from '../../shared/ConfirmDialog';
import { EmptyState, ErrorState, LoadingState } from '../../shared/StateBlocks';
import BrowseTree from './BrowseTree';
import ImportReviewStep, { type ImportDefaults } from './ImportReviewStep';
import ImportResultsStep from './ImportResultsStep';
import ImportHistory from './ImportHistory';
import { importErrorLabel } from '../../../lib/vesselImportLabels';

/** Poll interval for a background import, in milliseconds. */
export const IMPORT_POLL_MS = 2000;

export interface ImportWizardProps {
  open: boolean;
  onClose: () => void;
  /** Called after vessels were created so the caller can refresh its list. */
  onImported?: () => void;
  /** Open directly on the import history view. */
  initialView?: 'source' | 'history';
}

type Step = 'source' | 'review' | 'results' | 'history' | 'batch';
type SourceTab = 'paste' | 'browse';

/** Split pasted text into trimmed, non-empty, de-duplicated lines. */
export function parsePastedPaths(text: string): string[] {
  const seen = new Set<string>();
  const out: string[] = [];
  for (const raw of text.split(/\r?\n/)) {
    const line = raw.trim().replace(/^["']|["']$/g, '');
    if (!line || seen.has(line)) continue;
    seen.add(line);
    out.push(line);
  }
  return out;
}

/**
 * Bulk import wizard: Source (paste paths or browse the Admiral host) -> Review (candidate table, selection,
 * fleet and defaults) -> Results (per-item outcomes; background imports are polled). Also exposes the import
 * history with batch detail, and resumes a discovered batch from history so a review survives a reload.
 */
export default function ImportWizard({ open, onClose, onImported, initialView = 'source' }: ImportWizardProps) {
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

  const [importing, setImporting] = useState(false);
  const [importError, setImportError] = useState('');
  const [importErrorCode, setImportErrorCode] = useState<string | null>(null);
  const [resultItems, setResultItems] = useState<VesselImportItem[]>([]);
  const [jobId, setJobId] = useState<string | null>(null);
  const [polling, setPolling] = useState(false);
  const [pollError, setPollError] = useState('');
  const [selectedCount, setSelectedCount] = useState(0);

  const [historyBatch, setHistoryBatch] = useState<{ batch: VesselImportBatch; items: VesselImportItem[] } | null>(null);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [historyError, setHistoryError] = useState('');
  const [confirmClose, setConfirmClose] = useState(false);

  const pollTimer = useRef<number | null>(null);

  const pasted = parsePastedPaths(pasteText);
  const sourcePaths = sourceTab === 'paste' ? pasted : browseSelected;
  const depthNumber = maxDepth.trim() === '' ? null : Number(maxDepth);
  const depthInvalid = depthNumber !== null && (!Number.isInteger(depthNumber) || depthNumber < 1 || depthNumber > 16);

  useEffect(() => {
    if (!open) return;
    setStep(initialView === 'history' ? 'history' : 'source');
    let cancelled = false;
    listFleets({ pageSize: 9999 }).then((r) => { if (!cancelled) setFleets(r.objects || []); }).catch(() => undefined);
    listPipelines({ pageSize: 9999 }).then((r) => { if (!cancelled) setPipelines(r.objects || []); }).catch(() => undefined);
    return () => { cancelled = true; };
  }, [open, initialView]);

  const stopPolling = useCallback(() => {
    if (pollTimer.current !== null) {
      window.clearTimeout(pollTimer.current);
      pollTimer.current = null;
    }
    setPolling(false);
  }, []);

  useEffect(() => () => stopPolling(), [stopPolling]);
  useEffect(() => { if (!open) stopPolling(); }, [open, stopPolling]);

  function resetAll() {
    stopPolling();
    setStep('source');
    setBatch(null);
    setCandidates([]);
    setTruncated(false);
    setHints([]);
    setSelected([]);
    setResultItems([]);
    setJobId(null);
    setImportError('');
    setImportErrorCode(null);
    setDiscoverError('');
    setPollError('');
  }

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

  async function discover() {
    if (sourcePaths.length === 0 || depthInvalid) return;
    setDiscovering(true);
    setDiscoverError('');
    try {
      const result = await discoverVesselImport({
        Directories: sourcePaths,
        ...(depthNumber !== null ? { MaxDepth: depthNumber } : {}),
      });
      loadReview(result.batch, result.candidates || [], result.truncated, result.hints || []);
    } catch (err: unknown) {
      const fallback = err instanceof Error ? err.message : t('Discovery failed.');
      setDiscoverError(importErrorLabel(t, apiErrorCode(err), fallback) + (apiErrorCode(err) && err instanceof Error ? ` (${err.message})` : ''));
    } finally {
      setDiscovering(false);
    }
  }

  const poll = useCallback(async (batchId: string) => {
    try {
      const detail = await getVesselImportBatch(batchId);
      setBatch(detail.batch);
      setResultItems(detail.items || []);
      setPollError('');
      if (detail.batch.status === 'Importing') {
        pollTimer.current = window.setTimeout(() => void poll(batchId), IMPORT_POLL_MS);
      } else {
        stopPolling();
        if (detail.batch.createdCount > 0) onImported?.();
        pushToast(detail.batch.failedCount > 0 ? 'warning' : 'success', t('{count, plural, one {Import finished: # vessel created.} other {Import finished: # vessels created.}}', { count: detail.batch.createdCount }));
      }
    } catch (err: unknown) {
      setPollError(err instanceof Error ? err.message : t('Failed to refresh import progress.'));
      pollTimer.current = window.setTimeout(() => void poll(batchId), IMPORT_POLL_MS * 2);
    }
  }, [onImported, pushToast, stopPolling, t]);

  async function runImport() {
    if (!batch || selected.length === 0) return;
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
      });
      setSelectedCount(selected.length);
      setBatch(response.batch);
      setResultItems(response.items || []);
      setJobId(response.jobId);
      setStep('results');
      if (response.runsInBackground) {
        setPolling(true);
        pollTimer.current = window.setTimeout(() => void poll(response.batchId), IMPORT_POLL_MS);
      } else {
        if (response.batch.createdCount > 0) onImported?.();
        pushToast(response.batch.failedCount > 0 ? 'warning' : 'success', t('{count, plural, one {Import finished: # vessel created.} other {Import finished: # vessels created.}}', { count: response.batch.createdCount }));
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

  async function openHistoryBatch(b: VesselImportBatch) {
    setHistoryLoading(true);
    setHistoryError('');
    setStep('batch');
    try {
      const detail = await getVesselImportBatch(b.id);
      if (detail.batch.status === 'Discovered') {
        loadReview(detail.batch, detail.items || [], false, []);
        setHistoryBatch(null);
      } else {
        setHistoryBatch({ batch: detail.batch, items: detail.items || [] });
        if (detail.batch.status === 'Importing') {
          setBatch(detail.batch);
          setResultItems(detail.items || []);
          setJobId(detail.batch.jobId);
          setSelectedCount(detail.items.filter((i) => i.outcome !== 'SkippedNotSelected').length);
          setStep('results');
          setPolling(true);
          pollTimer.current = window.setTimeout(() => void poll(detail.batch.id), IMPORT_POLL_MS);
        }
      }
    } catch (err: unknown) {
      setHistoryError(importErrorLabel(t, apiErrorCode(err), err instanceof Error ? err.message : t('Failed to load the batch.')));
    } finally {
      setHistoryLoading(false);
    }
  }

  function requestClose() {
    if (step === 'review' && candidates.length > 0) {
      setConfirmClose(true);
      return;
    }
    onClose();
  }

  const stepIndex = step === 'source' ? 0 : step === 'review' ? 1 : step === 'results' ? 2 : -1;
  const steps = [t('Source'), t('Review'), t('Results')];

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
        {step === 'batch' && <button type="button" className="btn" onClick={() => setStep('history')}>{t('Back to history')}</button>}
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
          {(step === 'source' || step === 'results') && (
            <button type="button" className="btn btn-sm" onClick={() => setStep('history')}>{t('Import history')}</button>
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

        {step === 'review' && batch && (
          <>
            {importError && (
              <div className="alert alert-error" role="alert">
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
            )}
          </>
        )}

        {step === 'results' && batch && (
          <ImportResultsStep batch={batch} items={resultItems} selectedCount={selectedCount} polling={polling} jobId={jobId} pollError={pollError} />
        )}

        {step === 'history' && <ImportHistory onOpen={(b) => void openHistoryBatch(b)} />}

        {step === 'batch' && (
          <>
            {historyLoading && <LoadingState />}
            {historyError && <ErrorState message={historyError} />}
            {historyBatch && <ImportResultsStep batch={historyBatch.batch} items={historyBatch.items} />}
          </>
        )}
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
