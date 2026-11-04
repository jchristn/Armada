import { useFocusEffect } from 'expo-router';
import { useCallback, useEffect, useRef, useState } from 'react';
import { AppState, StyleSheet, View } from 'react-native';
import { ApiError, getCaptainSelection, getCaptainSelectionHistory, getCaptainSelectionDecision, getCaptainSelectionCandidates, previewCaptainSelection } from '@dashboard/api/client';
import { selectionOutcomeKey, selectionReasonKey, selectionStrategyKey } from '@dashboard/lib/usageSelection';
import type { CaptainSelectionCandidateView, CaptainSelectionDecisionView, CaptainSelectionSummary, CaptainSelectionDecisionDetail, CaptainSelectionPreview, EnumerationResult } from '@dashboard/types/models';
import { JsonSheet } from '../../../components/app/JsonSheet';
import { AppText, Button, IconButton, KeyValueRow, Section, SegmentedControl } from '../../../components/ui';
import { useLocale } from '../../../i18n/LocaleContext';
import { useTheme } from '../../../theme/ThemeContext';
import { spacing } from '../../../theme/typography';
import { useSocket } from '../../../socket/SocketContext';
import { SelectionRecoverySheet } from './SelectionRecoverySheet';

type ViewMode = 'comparison' | 'history' | 'preview';
/** Mission-owned inspection with shared native controls; retained names never create dead captain links. */
interface SelectionProps { missionId: string; status: string; revision?: string; onOpen: (path: string) => void }
export function CaptainSelectionSection(props: SelectionProps) {
  return <CaptainSelectionBody key={props.missionId} {...props} />;
}
function CaptainSelectionBody({ missionId, status, revision, onOpen }: SelectionProps) {
  const { t, locale, formatDateTime } = useLocale();
  const { colors } = useTheme();
  const [summary, setSummary] = useState<CaptainSelectionSummary | null>(null);
  const [expanded, setExpanded] = useState(false);
  const [view, setView] = useState<ViewMode>('comparison');
  const [page, setPage] = useState(1);
  const [outcome, setOutcome] = useState('');
  const [selected, setSelected] = useState<CaptainSelectionDecisionView | null>(null);
  const [record, setRecord] = useState<CaptainSelectionDecisionView | null>(null);
  const [candidates, setCandidates] = useState<EnumerationResult<CaptainSelectionCandidateView> | null>(null);
  const [history, setHistory] = useState<EnumerationResult<CaptainSelectionDecisionView> | null>(null);
  const [evidence, setEvidence] = useState<CaptainSelectionDecisionDetail | CaptainSelectionPreview | null>(null);
  const [error, setError] = useState(false);
  const [busy, setBusy] = useState(false);
  const [json, setJson] = useState<unknown>(null);
  const [recoveryOpen, setRecoveryOpen] = useState(false);
  const { subscribe } = useSocket();
  const abort = useRef<AbortController | null>(null);
  const loading = useRef(false);
  const focused = useRef(true);
  const initialFocus = useRef(true);
  const latest = record ?? summary?.currentAttempt ?? summary?.latestDecision;
  const percent = (value?: number | null) => value == null || !Number.isFinite(value) ? t('Unknown')
    : new Intl.NumberFormat(locale, { style: 'percent', maximumFractionDigits: 1 }).format(value / 100);
  const load = useCallback(async () => {
    if (!focused.current) return;
    abort.current?.abort();
    const request = new AbortController(); abort.current = request; loading.current = true; setBusy(true); setError(false);
    try {
      const current = await getCaptainSelection(missionId, request.signal);
      if (request.signal.aborted) return;
      setSummary(current);
      if (!expanded) return;
      if (view === 'history') {
        const result = await getCaptainSelectionHistory(missionId, { outcome: outcome || undefined, pageNumber: page, pageSize: 10 }, request.signal);
        if (!request.signal.aborted) setHistory(result);
      } else if (view === 'preview') {
        const result = await previewCaptainSelection(missionId, page, 10, request.signal);
        if (!request.signal.aborted) { setCandidates(result.candidates); setRecord(null); setEvidence(result); }
      } else {
        const target = selected ?? current.currentAttempt ?? current.latestDecision;
        if (!target) { setCandidates(null); setRecord(null); return; }
        const detail = await getCaptainSelectionDecision(target.id, target.tenantId, 1, 10, request.signal);
        const rows = page === 1 ? detail.candidates : await getCaptainSelectionCandidates(target.id, target.tenantId, page, 10, request.signal);
        if (!request.signal.aborted) { setRecord(detail.decision); setCandidates(rows); setEvidence({ ...detail, candidates: rows }); }
      }
    } catch (failure) {
      if (!request.signal.aborted) {
        setError(true);
        if (failure instanceof ApiError && [401, 403, 404].includes(failure.status)) {
          setSummary(null); setRecord(null); setCandidates(null); setHistory(null); setSelected(null); setJson(null); setEvidence(null);
        }
      }
    } finally { if (!request.signal.aborted) { setBusy(false); loading.current = false; } }
  }, [missionId, expanded, view, selected, page, outcome]);
  const reload = useRef(load);
  useEffect(() => { reload.current = load; }, [load]);
  useFocusEffect(useCallback(() => {
    focused.current = true;
    if (initialFocus.current) initialFocus.current = false;
    else void reload.current();
    const timer = setInterval(() => {
      if (AppState.currentState === 'active' && !loading.current) void reload.current();
    }, 15000);
    return () => { clearInterval(timer); focused.current = false; loading.current = false; abort.current?.abort(); };
  }, []));
  // captain-selection.changed carries identifiers only; re-read authorized evidence for this mission while focused.
  useEffect(() => subscribe((msg) => {
    const data = msg.data as { missionId?: unknown } | undefined;
    if (msg.type === 'captain-selection.changed' && data?.missionId === missionId && focused.current && !loading.current) void reload.current();
  }), [subscribe, missionId]);
  // eslint-disable-next-line react-hooks/set-state-in-effect -- fetch scoped evidence when its query or mission revision changes
  useEffect(() => { void load(); return () => abort.current?.abort(); }, [load, revision]);
  const choose = (value: ViewMode) => { setView(value); setPage(1); setRecord(null); setExpanded(true); };
  const rows = view === 'history' ? history : candidates;
  return <Section title={t('usageSelection.title')}>
    <View style={styles.content}>
      <View style={styles.actions}>
        <Button label={t('usageSelection.compare')} variant="secondary" onPress={() => { setExpanded(!expanded); setView('comparison'); setPage(1); }} />
        {status === 'Pending' ? <Button label={t('usageSelection.preview')} variant="secondary" onPress={() => choose('preview')} /> : null}
        {summary?.canManageRecovery && status === 'Pending' ? <Button label={t('usageSelection.recovery.title')} variant="secondary" onPress={() => setRecoveryOpen(true)} testID="selection-recovery-open" /> : null}
        <IconButton icon="refresh" label={t('usageSelection.refresh')} onPress={() => void load()} />
      </View>
      {error ? <AppText color="danger" accessibilityRole="alert">{t('usageSelection.historyLoadFailed')}</AppText> : null}
      {!summary && !error ? <AppText muted>{t('Loading...')}</AppText> : null}
      {summary && !latest ? <AppText muted>{t('usageSelection.noHistory')}</AppText> : null}
      {latest ? <>
        <AppText variant="caption" muted>{t(`usageSelection.${latest.mode.toLowerCase()}`)} · {t(selectionStrategyKey[latest.strategy])}</AppText>
        {!summary?.currentAttempt ? <AppText variant="caption" muted>{t('usageSelection.latestRetained')}</AppText> : null}
        <KeyValueRow label={t('usageSelection.selectedCaptain')} value={latest.selectedCaptainName ?? latest.selectedCaptainId ?? t('usageSelection.notSelected')}
          onPress={latest.selectedCaptainAvailable && latest.selectedCaptainId ? () => onOpen(`/captains/${encodeURIComponent(latest.selectedCaptainId!)}`) : undefined} />
        <KeyValueRow label={t('usageSelection.intendedModel')} value={latest.intendedModel ?? t('Unknown')} />
        <KeyValueRow label={t('usageSelection.reportedModel')} value={latest.reportedModel ?? t('Unknown')} />
        <AppText>{t(latest.mode === 'Observe' && latest.outcome === 'Admitted' ? 'usageSelection.observeChoice' : selectionReasonKey(latest.reasonCode))}</AppText>
        <AppText variant="caption" muted>{formatDateTime(latest.evaluatedUtc)}</AppText>
      </> : null}
      {summary?.recovery ? <View style={[styles.item, { borderColor: colors.border }]}>
        <AppText variant="label">{t('usageSelection.recovery.title')} · {t('usageSelection.recovery.record', { revision: summary.recovery.revision })}</AppText>
        <AppText>{t(summary.recovery.allowFallback ? 'usageSelection.recovery.fallbackOn' : 'usageSelection.recovery.fallbackOff')}</AppText>
        <AppText>{t(summary.recovery.reserveException ? 'usageSelection.recovery.reserveOn' : 'usageSelection.recovery.reserveOff')}</AppText>
        <AppText selectable variant="caption" muted>{summary.recovery.reason}</AppText>
      </View> : null}
      {expanded ? <>
        <SegmentedControl label={t('usageSelection.inspectionView')} value={view} onChange={choose} options={[
          { value: 'comparison', label: t('usageSelection.comparison') }, { value: 'history', label: t('History') },
          ...(status === 'Pending' ? [{ value: 'preview' as const, label: t('usageSelection.preview') }] : []),
        ]} />
        {view === 'preview' && evidence && 'reasonCode' in evidence ? <AppText>{t(selectionReasonKey(evidence.reasonCode))}</AppText> : null}
        {view === 'comparison' && evidence && 'policy' in evidence ? <AppText variant="caption" muted>{t('usageSelection.policyBudget', { reserve: percent(evidence.policy.reservePercent), estimate: percent(evidence.policy.perAttemptReservationPercent) })}</AppText> : null}
        {view === 'history' ? <>
          <SegmentedControl label={t('usageSelection.historyFilter')} value={outcome} onChange={value => { setOutcome(value); setPage(1); }} options={[
            { value: '', label: t('All') }, { value: 'Admitted', label: t('usageSelection.admitted') }, { value: 'Waiting', label: t('usageSelection.waiting') },
          ]} />
          {history?.objects.map(row => <View key={row.id} style={[styles.item, { borderColor: colors.border }]}>
            <AppText variant="label">{t(row.outcome === 'Waiting' ? 'usageSelection.waiting' : 'usageSelection.admitted')} · {formatDateTime(row.evaluatedUtc)}</AppText>
            <AppText variant="caption" muted>{t(selectionStrategyKey[row.strategy])}</AppText>
            <AppText>{t(selectionReasonKey(row.reasonCode))}</AppText>
            <Button label={t('View')} variant="secondary" onPress={() => { setSelected(row); choose('comparison'); }} />
          </View>)}
        </> : candidates?.objects.map(row => <View key={row.captainId} style={[styles.item, { borderColor: colors.border }]}>
          <KeyValueRow label={t('Captain')} value={row.captainName || row.captainId}
            onPress={row.captainAvailable ? () => onOpen(`/captains/${encodeURIComponent(row.captainId)}`) : undefined} />
          <AppText selectable variant="caption" muted>{row.model || t('Unknown')}</AppText>
          <AppText variant="label">{t(selectionOutcomeKey(row, view === 'preview'))}</AppText>
          <AppText>{t(selectionReasonKey(row.reasonCode))}</AppText>
          <AppText variant="caption" muted>{t('usageSelection.headroomValue', { value: percent(row.availablePercent) })}</AppText>
          {row.capturedUtc ? <AppText variant="caption" muted>{t('usageSelection.observedAt', { time: formatDateTime(row.capturedUtc) })}</AppText> : null}
          {row.nextReconsiderUtc ? <AppText variant="caption" muted>{t('usageSelection.reconsiderAt', { time: formatDateTime(row.nextReconsiderUtc) })}</AppText> : null}
        </View>)}
        {!busy && rows?.objects.length === 0 ? <AppText muted>{t(view === 'history' ? 'usageSelection.noMatchingHistory' : 'usageSelection.noCandidates')}</AppText> : null}
        <View style={styles.actions}>
          <Button label={t('Prev')} variant="secondary" disabled={busy || page <= 1} onPress={() => setPage(page - 1)} />
          <AppText variant="caption">{page} / {Math.max(1, rows?.totalPages ?? 1)}</AppText>
          <Button label={t('Next')} variant="secondary" disabled={busy || page >= (rows?.totalPages ?? 1)} onPress={() => setPage(page + 1)} />
          <Button label={t('View JSON')} variant="secondary" disabled={!rows} onPress={() => setJson(view === 'history' ? history : evidence)} />
        </View>
      </> : null}
    </View>
    <JsonSheet open={json != null} title={t('usageSelection.title')} data={json} onClose={() => setJson(null)} />
    <SelectionRecoverySheet open={recoveryOpen} missionId={missionId} current={summary?.recovery} onClose={() => setRecoveryOpen(false)} onSaved={() => void load()} />
  </Section>;
}
const styles = StyleSheet.create({
  content: { padding: spacing.md, gap: spacing.md },
  actions: { flexDirection: 'row', flexWrap: 'wrap', alignItems: 'center', gap: spacing.sm },
  item: { borderTopWidth: StyleSheet.hairlineWidth, paddingTop: spacing.md, gap: spacing.sm },
});
