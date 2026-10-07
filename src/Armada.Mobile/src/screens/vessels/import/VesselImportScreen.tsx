import { Stack, useLocalSearchParams, useRouter, type Href } from 'expo-router';
import { useCallback, useEffect, useRef, useState } from 'react';
import { StyleSheet, View } from 'react-native';
import {
  apiErrorCode,
  discoverVesselImport,
  getFleetCategorizationDefaultPrompt,
  getVesselImportBatch,
  importVessels,
  listCaptains,
  listFleets,
  listPipelines,
} from '@dashboard/api/client';
import type { Captain, Fleet, Pipeline, VesselImportBatch, VesselImportFleetRecommendation, VesselImportHint, VesselImportItem } from '@dashboard/types/models';
import { importErrorLabel, isBatchBusy } from '@dashboard/lib/vesselImportLabels';
import { EMPTY_CATEGORIZATION, IMPORT_POLL_MS, parseMaxDepth, parsePastedPaths, validateCategorization, type CategorizationOptions } from '@dashboard/lib/vesselImport';
import { ActionRow, SwitchField } from '../../../build/fields';
import { errorMessage } from '../../../build/useLiveResource';
import { useAuth } from '../../../auth/AuthContext';
import { AppText, Banner, Button, ConfirmDialog, EmptyState, LoadingState, Screen, SegmentedControl, TextField } from '../../../components/ui';
import { useLocale } from '../../../i18n/LocaleContext';
import { useNotifications } from '../../../notifications/NotificationContext';
import { useTheme } from '../../../theme/ThemeContext';
import { radius, spacing, typography } from '../../../theme/typography';
import { BrowseTree } from './BrowseTree';
import { FleetRecommendationsPanel } from './FleetRecommendationsPanel';
import { ImportHistory } from './ImportHistory';
import { ImportResultsStep } from './ImportResultsStep';
import { ImportReviewStep, type ImportDefaults } from './ImportReviewStep';

type Step = 'source' | 'discovering' | 'review' | 'results' | 'history' | 'batch';
type SourceTab = 'paste' | 'browse';

/**
 * The vessel import wizard at /vessels/import (the dashboard's ImportWizard as a full screen): Source (paste
 * paths or browse the Admiral host) -> Review (candidates, selection, defaults, optional fleet recommendations) ->
 * Results (outcomes and fleet recommendations). Discovery, larger imports, and categorization run in the background
 * and are polled while the screen is open; batches resume from the import history. `?batch=vib_...` opens a batch
 * (the dashboard's job links) and `?view=history` opens the history.
 */
export function VesselImportScreen() {
  const { t } = useLocale();
  const { colors } = useTheme();
  const { pushToast } = useNotifications();
  const { user } = useAuth();
  const router = useRouter();
  const params = useLocalSearchParams<{ batch?: string; view?: string }>();
  const initialBatchId = typeof params.batch === 'string' && params.batch ? params.batch : null;
  const tenantId = user?.user?.tenantId ?? null;

  const [step, setStep] = useState<Step>(params.view === 'history' ? 'history' : 'source');
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
  const [confirmLeave, setConfirmLeave] = useState(false);

  const pollTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const lastStatus = useRef<string | null>(null);
  const mounted = useRef(true);
  /** The latest poll function, so a scheduled poll calls itself without a self-referencing callback. */
  const pollRef = useRef<(batchId: string) => Promise<void>>(async () => undefined);

  const pasted = parsePastedPaths(pasteText);
  const sourcePaths = sourceTab === 'paste' ? pasted : browseSelected;
  const { value: depthNumber, invalid: depthInvalid } = parseMaxDepth(maxDepth);

  const stopPolling = useCallback(() => {
    if (pollTimer.current !== null) {
      clearTimeout(pollTimer.current);
      pollTimer.current = null;
    }
    setPolling(false);
  }, []);

  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
      if (pollTimer.current !== null) clearTimeout(pollTimer.current);
    };
  }, []);

  const loadReview = useCallback((nextBatch: VesselImportBatch, items: VesselImportItem[], nextTruncated: boolean, nextHints: VesselImportHint[]) => {
    setBatch(nextBatch);
    setCandidates(items);
    setTruncated(nextTruncated);
    setHints(nextHints);
    setSelected(items.filter((c) => c.candidateStatus === 'New').map((c) => c.path));
    setDefaults((d) => ({ ...d, fleetId: nextBatch.fleetId ?? d.fleetId }));
    setImportError('');
    setImportErrorCode(null);
    setStep('review');
  }, []);

  /** Poll a batch while discovery, the import, or categorization runs; stops when nothing runs. */
  const poll = useCallback(async (batchId: string): Promise<void> => {
    pollTimer.current = null;
    if (!mounted.current) return;
    try {
      const detail = await getVesselImportBatch(batchId);
      if (!mounted.current) return;
      const previous = lastStatus.current;
      lastStatus.current = detail.batch.status;
      setPollError('');
      if (detail.batch.status === 'Discovering') {
        setBatch(detail.batch);
        pollTimer.current = setTimeout(() => void pollRef.current(batchId), IMPORT_POLL_MS);
        return;
      }
      if (previous === 'Discovering') {
        stopPolling();
        if (detail.batch.status === 'Discovered') loadReview(detail.batch, detail.items || [], !!detail.batch.truncated, detail.hints || []);
        else {
          setDiscoverError(detail.batch.errorMessage || t('Discovery failed.'));
          setStep('source');
        }
        return;
      }
      setBatch(detail.batch);
      setResultItems(detail.items || []);
      setRecommendations(detail.fleetRecommendations || []);
      if (previous === 'Importing' && detail.batch.status !== 'Importing') {
        pushToast(detail.batch.failedCount > 0 ? 'warning' : 'success', t('{count, plural, one {Import finished: # vessel created.} other {Import finished: # vessels created.}}', { count: detail.batch.createdCount }));
      }
      if (isBatchBusy(detail.batch)) {
        setPolling(true);
        pollTimer.current = setTimeout(() => void pollRef.current(batchId), IMPORT_POLL_MS);
      } else {
        stopPolling();
      }
    } catch (e) {
      if (!mounted.current) return;
      setPollError(errorMessage(e) || t('Failed to refresh import progress.'));
      pollTimer.current = setTimeout(() => void pollRef.current(batchId), IMPORT_POLL_MS * 2);
    }
  }, [loadReview, pushToast, stopPolling, t]);
  useEffect(() => { pollRef.current = poll; }, [poll]);

  const startPolling = useCallback((batchId: string, status: string) => {
    if (pollTimer.current !== null) clearTimeout(pollTimer.current);
    lastStatus.current = status;
    setPolling(true);
    pollTimer.current = setTimeout(() => void pollRef.current(batchId), IMPORT_POLL_MS);
  }, []);

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
    } catch (e) {
      setHistoryError(importErrorLabel(t, apiErrorCode(e), errorMessage(e) || t('Failed to load the batch.')));
    } finally {
      setHistoryLoading(false);
    }
  }, [loadReview, startPolling, stopPolling, t]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- opens the batch named by the route once; it sets loading state first
    if (initialBatchId) void openBatch(initialBatchId);
    let cancelled = false;
    listFleets({ pageSize: 9999 }).then((r) => { if (!cancelled) setFleets(r?.objects ?? []); }).catch(() => undefined);
    listPipelines({ pageSize: 9999 }).then((r) => { if (!cancelled) setPipelines(r?.objects ?? []); }).catch(() => undefined);
    return () => { cancelled = true; };
    // The route's batch is read once when the screen opens.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Captains and the default prompt are loaded once the operator opts into fleet recommendations.
  useEffect(() => {
    if (!categorization.enabled || captains.length > 0 || captainsLoading) return undefined;
    let cancelled = false;
    // eslint-disable-next-line react-hooks/set-state-in-effect -- a fetch on opt-in; it sets loading state first
    setCaptainsLoading(true);
    listCaptains({ pageSize: 9999 })
      .then((r) => { if (!cancelled) setCaptains((r?.objects ?? []).filter((c) => !tenantId || c.tenantId === tenantId)); })
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
        .catch((e: unknown) => { if (!cancelled) setDefaultPromptError(errorMessage(e) || t('unknown error')); });
    }
    return () => { cancelled = true; };
    // Load once per opt-in.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [categorization.enabled]);

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
      const result = await discoverVesselImport({ Directories: sourcePaths, ...(depthNumber !== null ? { MaxDepth: depthNumber } : {}), RunInBackground: true });
      if (result.runsInBackground) {
        setBatch(result.batch);
        setStep('discovering');
        startPolling(result.batchId, 'Discovering');
      } else {
        loadReview(result.batch, result.candidates || [], result.truncated, result.hints || []);
      }
    } catch (e) {
      const code = apiErrorCode(e);
      setDiscoverError(importErrorLabel(t, code, errorMessage(e) || t('Discovery failed.')) + (code ? ` (${errorMessage(e)})` : ''));
    } finally {
      setDiscovering(false);
    }
  }

  async function runImport() {
    if (!batch || selected.length === 0) return;
    if (Object.keys(validateCategorization(categorization)).length > 0) {
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
        Defaults: defaults.pipelineId || defaults.landingMode ? { DefaultPipelineId: defaults.pipelineId || null, LandingMode: defaults.landingMode || null } : null,
        ...(categorization.enabled ? {
          Categorization: {
            Enabled: true,
            CaptainId: categorization.captainId,
            Prompt: categorization.prompt === defaultPrompt ? null : categorization.prompt,
            ApplyAutomatically: categorization.applyAutomatically,
          },
        } : {}),
      });
      setSelectedCount(selected.length);
      setBatch(response.batch);
      setResultItems(response.items || []);
      setRecommendations([]);
      setJobId(response.jobId);
      setStep('results');
      if (response.runsInBackground) {
        startPolling(response.batchId, 'Importing');
      } else {
        pushToast(response.batch.failedCount > 0 ? 'warning' : 'success', t('{count, plural, one {Import finished: # vessel created.} other {Import finished: # vessels created.}}', { count: response.batch.createdCount }));
        if (isBatchBusy(response.batch)) startPolling(response.batchId, response.batch.status);
      }
    } catch (e) {
      const code = apiErrorCode(e);
      setImportErrorCode(code);
      setImportError(importErrorLabel(t, code, errorMessage(e) || t('Import failed.')) + (code ? ` (${errorMessage(e)})` : ''));
    } finally {
      setImporting(false);
    }
  }

  function leave() {
    if (step === 'review' && candidates.length > 0) {
      setConfirmLeave(true);
      return;
    }
    if (router.canGoBack()) router.back();
    else router.replace('/vessels' as Href);
  }

  const stepIndex = step === 'source' || step === 'discovering' ? 0 : step === 'review' ? 1 : step === 'results' ? 2 : -1;
  const steps = [t('Source'), t('Review'), t('Results')];
  const importPolling = polling && batch?.status === 'Importing';
  const showHistoryButton = step === 'source' || step === 'results' || step === 'discovering';

  return (
    <Screen testID="vessel-import" maxWidth={900}>
      <Stack.Screen options={{ title: t('Import repositories') }} />
      <AppText muted style={styles.pad}>{t('Onboard existing local git repositories as vessels in bulk. Nothing is created until you confirm.')}</AppText>
      <View style={[styles.stepsRow, styles.pad]}>
        {stepIndex >= 0 ? (
          <View style={styles.steps} accessibilityLabel={t('Import steps')}>
            {steps.map((label, i) => (
              <View key={label} style={[styles.step, { borderColor: i === stepIndex ? colors.primary : colors.border }]} accessibilityState={{ selected: i === stepIndex }}>
                <AppText variant="caption" color={i === stepIndex ? 'primary' : i < stepIndex ? 'success' : 'textMuted'}>{`${i + 1}. ${label}`}</AppText>
              </View>
            ))}
          </View>
        ) : <AppText variant="heading" accessibilityRole="header">{t('Import history')}</AppText>}
        {showHistoryButton ? <Button label={t('Import history')} variant="ghost" onPress={() => { stopPolling(); setStep('history'); }} testID="import-open-history" /> : null}
      </View>

      {step === 'source' ? (
        <View style={styles.pad}>
          {discoverError ? <Banner tone="danger" title={discoverError} /> : null}
          <SegmentedControl<SourceTab>
            label={t('Source')}
            value={sourceTab}
            onChange={setSourceTab}
            options={[{ value: 'paste', label: t('Paste paths'), testID: 'import-source-paste' }, { value: 'browse', label: t('Browse'), testID: 'import-source-browse' }]}
          />
          {sourceTab === 'paste' ? (
            <TextField
              label={t('Folders on the Admiral host, one per line')}
              value={pasteText}
              onChangeText={setPasteText}
              placeholder={'/Users/alex/Code\n/Users/alex/Code/api'}
              multiline
              autoCapitalize="none"
              autoCorrect={false}
              hint={`${t('{count, plural, one {# path} other {# paths}}', { count: pasted.length })} - ${t('A repository folder becomes one candidate; any other folder is scanned for repositories below it.')}`}
              testID="import-paste"
            />
          ) : (
            <View>
              <BrowseTree selected={browseSelected} allowWorktrees={allowWorktrees} onToggle={(p) => setBrowseSelected((s) => (s.includes(p) ? s.filter((x) => x !== p) : [...s, p]))} />
              <SwitchField label={t('Allow selecting worktrees')} value={allowWorktrees} onChange={setAllowWorktrees} />
              {browseSelected.length > 0 ? (
                <View style={styles.gap}>
                  <AppText variant="label">{t('{count, plural, one {# folder selected} other {# folders selected}}', { count: browseSelected.length })}</AppText>
                  {browseSelected.map((p) => (
                    <View key={p} style={styles.selectedRow}>
                      <AppText variant="caption" style={[typography.mono, styles.flex]}>{p}</AppText>
                      <Button label={t('Remove')} variant="ghost" accessibilityHint={t('Remove {{path}}', { path: p })} onPress={() => setBrowseSelected((s) => s.filter((x) => x !== p))} />
                    </View>
                  ))}
                </View>
              ) : null}
            </View>
          )}
          <TextField
            label={t('Max depth')}
            value={maxDepth}
            onChangeText={setMaxDepth}
            keyboardType="number-pad"
            placeholder={t('Server default')}
            error={depthInvalid ? t('Max depth must be a whole number from 1 to 16.') : null}
            hint={t('Folder levels searched below each folder that is not itself a repository (1-16). Leave empty for the server default.')}
          />
          <ActionRow>
            <Button label={t('Cancel')} variant="ghost" onPress={leave} />
            <Button
              label={discovering ? t('Discovering...') : t('Discover')}
              busy={discovering}
              disabled={sourcePaths.length === 0 || depthInvalid}
              onPress={() => void discover()}
              testID="import-discover"
            />
          </ActionRow>
        </View>
      ) : null}

      {step === 'discovering' ? (
        <View style={[styles.box, { borderColor: colors.info }]} accessibilityLiveRegion="polite" testID="import-discovering">
          <AppText variant="label">{t('Scanning for repositories in the background.')}</AppText>
          <AppText variant="caption" muted>{t('You can leave this screen; discovery keeps running. Reopen it from the import history to review the candidates.')}</AppText>
          {batch ? <AppText variant="caption" muted selectable>{batch.id}</AppText> : null}
          {pollError ? <AppText variant="caption" color="danger">{pollError}</AppText> : null}
          <ActionRow>
            <Button label={t('Start over')} variant="ghost" onPress={resetAll} />
            <Button label={t('Close')} onPress={leave} />
          </ActionRow>
        </View>
      ) : null}

      {step === 'review' && batch ? (
        <View>
          {importError ? (
            <View>
              <Banner tone="danger" title={importError} />
              <ActionRow>
                <Button label={t('Retry')} variant="secondary" onPress={() => void runImport()} />
                {importErrorCode === 'BatchBusy' ? <Button label={t('Open import history')} variant="ghost" onPress={() => setStep('history')} /> : null}
                {importErrorCode === 'BatchNotFound' ? <Button label={t('Discover again')} variant="ghost" onPress={() => void discover()} /> : null}
              </ActionRow>
            </View>
          ) : null}
          {candidates.length === 0 ? (
            <EmptyState
              title={t('No repositories found')}
              message={[
                ...hints.map((h) => (h.code === 'PathNotVisibleToAdmiral' ? t('None of the requested paths exist on the Admiral host. If the Admiral runs in a container it cannot see your host directories: mount them into the container, or run discovery through a Harbor on that machine.') : h.message)),
                t('Discovery searches each folder up to the max depth, skips excluded names (such as node_modules, bin, obj) and anything starting with a dot, and never descends into a repository it already found.'),
                t('Try a higher max depth, a folder closer to the repositories, or check the excluded names under Settings > Import.'),
              ].join('\n\n')}
            />
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
              categorization={categorization}
              onCategorizationChange={setCategorization}
              captains={captains}
              captainsLoading={captainsLoading}
              defaultPrompt={defaultPrompt}
              defaultPromptError={defaultPromptError}
              timeoutMinutes={timeoutMinutes}
              showCategorizationErrors={showCategorizationErrors}
            />
          )}
          <ActionRow>
            <Button label={t('Back')} variant="ghost" disabled={importing} onPress={() => setStep('source')} />
            <Button
              label={importing ? t('Importing...') : t('{count, plural, =0 {Import repositories} one {Import # repository} other {Import # repositories}}', { count: selected.length })}
              busy={importing}
              disabled={selected.length === 0}
              onPress={() => void runImport()}
              testID="import-run"
            />
          </ActionRow>
        </View>
      ) : null}

      {step === 'results' || step === 'batch' ? (
        <View>
          {step === 'batch' && historyLoading ? <LoadingState label={t('Loading...')} /> : null}
          {step === 'batch' && historyError ? <Banner tone="danger" title={historyError} /> : null}
          {batch && !(step === 'batch' && (historyLoading || historyError)) ? (
            <>
              <ImportResultsStep batch={batch} items={resultItems} selectedCount={selectedCount} polling={importPolling} jobId={jobId} pollError={pollError} />
              <FleetRecommendationsPanel batch={batch} items={resultItems} recommendations={recommendations} onChanged={() => { if (batch) void poll(batch.id); }} />
            </>
          ) : null}
          <ActionRow>
            {step === 'batch' ? <Button label={t('Back to history')} variant="ghost" onPress={() => { stopPolling(); setStep('history'); }} /> : null}
            <Button label={step === 'results' ? t('Import more') : t('New import')} variant="secondary" onPress={resetAll} />
            <Button label={t('View vessels')} onPress={() => router.replace('/vessels' as Href)} testID="import-view-vessels" />
          </ActionRow>
        </View>
      ) : null}

      {step === 'history' ? (
        <View>
          <ImportHistory onOpen={(b) => void openBatch(b.id)} />
          <ActionRow>
            <Button label={t('New import')} variant="secondary" onPress={resetAll} />
          </ActionRow>
        </View>
      ) : null}

      <ConfirmDialog
        open={confirmLeave}
        title={t('Leave the import review?')}
        message={t('Nothing has been imported yet. The discovered batch stays in the import history, so you can continue it later.')}
        confirmLabel={t('Leave')}
        cancelLabel={t('Stay')}
        onConfirm={() => { setConfirmLeave(false); if (router.canGoBack()) router.back(); else router.replace('/vessels' as Href); }}
        onCancel={() => setConfirmLeave(false)}
        testID="import-leave-confirm"
      />
    </Screen>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  pad: { paddingHorizontal: spacing.lg },
  gap: { gap: spacing.xs, marginBottom: spacing.md },
  stepsRow: { flexDirection: 'row', flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between', gap: spacing.sm, marginVertical: spacing.md },
  steps: { flexDirection: 'row', gap: spacing.xs, flexWrap: 'wrap' },
  step: { borderBottomWidth: 2, paddingHorizontal: spacing.xs, paddingVertical: 2, borderRadius: radius.sm },
  box: { borderWidth: 1, borderRadius: radius.md, padding: spacing.md, marginHorizontal: spacing.md, gap: spacing.xs },
  selectedRow: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm },
});
