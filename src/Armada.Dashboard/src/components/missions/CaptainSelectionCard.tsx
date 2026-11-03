import { useCallback, useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { ApiError, getCaptainSelection, getCaptainSelectionHistory, getCaptainSelectionDecision, previewCaptainSelection } from '../../api/client';
import { useLocale } from '../../context/LocaleContext';
import { selectionOutcomeKey, selectionReasonKey, selectionStrategyKey } from '../../lib/usageSelection';
import type { CaptainSelectionCandidateView, CaptainSelectionDecisionDetail, CaptainSelectionDecisionView, CaptainSelectionPreview, CaptainSelectionSummary, EnumerationResult } from '../../types/models';
import DataTable, { type DataTableColumn } from '../shared/DataTable';
import RefreshButton from '../shared/RefreshButton';
import CopyButton from '../shared/CopyButton';
import JsonViewer from '../shared/JsonViewer';

interface CaptainSelectionCardProps { missionId: string; status: string; revision?: string }
type View = 'comparison' | 'history' | 'preview';

/** Mission-owned selection inspection. No new route; all tables and refresh controls reuse dashboard components. */
export default function CaptainSelectionCard({ missionId, status, revision }: CaptainSelectionCardProps) {
  const { t, locale, formatDateTime } = useLocale();
  const [summary, setSummary] = useState<CaptainSelectionSummary | null>(null);
  const [history, setHistory] = useState<EnumerationResult<CaptainSelectionDecisionView> | null>(null);
  const [detail, setDetail] = useState<CaptainSelectionDecisionDetail | null>(null);
  const [preview, setPreview] = useState<CaptainSelectionPreview | null>(null);
  const [expanded, setExpanded] = useState(false);
  const [view, setView] = useState<View>('comparison');
  const [selected, setSelected] = useState<{ id: string; tenantId: string } | null>(null);
  const [historyPage, setHistoryPage] = useState(1);
  const [candidatePage, setCandidatePage] = useState(1);
  const [historySize, setHistorySize] = useState(10);
  const [candidateSize, setCandidateSize] = useState(10);
  const [outcome, setOutcome] = useState('');
  const [refreshSeconds, setRefreshSeconds] = useState(10);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(false);
  const [json, setJson] = useState<unknown>(null);
  const request = useRef<AbortController | null>(null);
  const loading = useRef(false);
  const selectionHeading = useRef<HTMLHeadingElement | null>(null);
  const latest = expanded && view === 'comparison' && detail ? detail.decision : summary?.currentAttempt ?? summary?.latestDecision;
  const percent = (value?: number | null) => value == null || !Number.isFinite(value) ? t('Unknown')
    : new Intl.NumberFormat(locale, { style: 'percent', maximumFractionDigits: 1 }).format(value / 100);

  const load = useCallback(async () => {
    request.current?.abort();
    const controller = new AbortController(); request.current = controller;
    loading.current = true; setBusy(true); setError(false);
    try {
      const next = await getCaptainSelection(missionId, controller.signal);
      if (controller.signal.aborted) return;
      setSummary(next);
      if (!expanded) return;
      if (view === 'history') {
        const rows = await getCaptainSelectionHistory(missionId, { outcome: outcome || undefined, pageNumber: historyPage, pageSize: historySize }, controller.signal);
        if (!controller.signal.aborted) setHistory(rows);
      } else if (view === 'preview') {
        const result = await previewCaptainSelection(missionId, candidatePage, candidateSize, controller.signal);
        if (!controller.signal.aborted) setPreview(result);
      } else {
        const target = selected ?? next.currentAttempt ?? next.latestDecision;
        if (!target) { setDetail(null); return; }
        const result = await getCaptainSelectionDecision(target.id, target.tenantId, candidatePage, candidateSize, controller.signal);
        if (!controller.signal.aborted) setDetail(result);
      }
    } catch (failure) {
      if (controller.signal.aborted) return;
      if (failure instanceof ApiError && [401, 403, 404].includes(failure.status)) {
        setSummary(null); setHistory(null); setDetail(null); setPreview(null); setJson(null);
      }
      setError(true);
      throw failure;
    } finally { if (request.current === controller) loading.current = false; if (!controller.signal.aborted) setBusy(false); }
  }, [missionId, expanded, view, selected, outcome, historyPage, historySize, candidatePage, candidateSize]);

  useEffect(() => {
    void load().catch(() => {});
    return () => request.current?.abort();
  }, [load, revision]);
  useEffect(() => {
    if (refreshSeconds <= 0) return;
    const timer = setInterval(() => { if (!document.hidden && !loading.current) void load().catch(() => {}); }, refreshSeconds * 1000);
    return () => clearInterval(timer);
  }, [load, refreshSeconds]);
  useEffect(() => {
    setSelected(null); setDetail(null); setPreview(null); setHistory(null); setSummary(null);
    setHistoryPage(1); setCandidatePage(1); setExpanded(false);
  }, [missionId]);

  const openView = (next: View) => { setView(next); setExpanded(true); setCandidatePage(1); };
  const candidateColumns: DataTableColumn<CaptainSelectionCandidateView>[] = [
    { key: 'captain', label: t('Captain'), required: true, className: 'selection-captain-column', render: row => <div className="selection-captain-cell">
      {row.captainAvailable ? <Link to={`/captains/${encodeURIComponent(row.captainId)}`} title={row.captainName}>{row.captainName || row.captainId}</Link>
        : <strong title={row.captainName}>{row.captainName || row.captainId}</strong>}
      <span className="text-dim" title={row.model ?? undefined}>{row.model || t('Unknown')}</span>
    </div> },
    { key: 'outcome', label: t('Status'), className: 'selection-status-column', render: row => <span className={`readiness-pill ${row.actualSelection ? 'ready' : row.band === 'Blocked' || row.band === 'Unknown' ? 'warning' : ''}`}>{t(selectionOutcomeKey(row, view === 'preview'))}</span> },
    { key: 'details', label: t('Details'), render: row => <div className="selection-detail-cell"><span>{t(selectionReasonKey(row.reasonCode))}</span>
      <span className="text-dim" title={row.availablePercent == null || !Number.isFinite(row.availablePercent) ? undefined : new Intl.NumberFormat(locale, { style: 'percent', maximumFractionDigits: 6 }).format(row.availablePercent / 100)}>{t('usageSelection.headroomValue', { value: percent(row.availablePercent) })}</span>
      {row.capturedUtc && <span className="text-dim">{t('usageSelection.observedAt', { time: formatDateTime(row.capturedUtc) })}</span>}
      {row.nextReconsiderUtc && <span className="text-dim">{t('usageSelection.reconsiderAt', { time: formatDateTime(row.nextReconsiderUtc) })}</span>}
    </div> },
  ];
  const historyColumns: DataTableColumn<CaptainSelectionDecisionView>[] = [
    { key: 'time', label: t('Time'), required: true, className: 'selection-time-column', render: row => <span title={row.evaluatedUtc}>{formatDateTime(row.evaluatedUtc)}</span> },
    { key: 'outcome', label: t('Status'), className: 'selection-status-column', render: row => <span className={`readiness-pill ${row.outcome === 'Waiting' ? 'warning' : 'ready'}`}>{t(row.outcome === 'Waiting' ? 'usageSelection.waiting' : 'usageSelection.admitted')}</span> },
    { key: 'strategy', label: t('usageSelection.strategy'), className: 'selection-strategy-column', render: row => <span>{t(selectionStrategyKey[row.strategy])}</span> },
    { key: 'details', label: t('Details'), render: row => <span>{t(selectionReasonKey(row.reasonCode))}</span> },
    { key: 'actions', label: t('Actions'), fixed: true, className: 'selection-actions-column', render: row => <button type="button" className="btn btn-sm" onClick={() => {
      setDetail(null); setSelected({ id: row.id, tenantId: row.tenantId }); setCandidatePage(1); setView('comparison');
      selectionHeading.current?.focus();
    }}>{t('View')}</button> },
  ];
  const comparison = view === 'preview' ? preview?.candidates : detail?.candidates;
  const recorded = detail?.decision;
  return <section className="card captain-selection-card" aria-label={t('usageSelection.title')}>
    <header className="selection-heading"><div><h3 ref={selectionHeading} tabIndex={-1}>{t('usageSelection.title')}</h3>
      {summary && <div className="selection-meta text-dim"><span>{t(`usageSelection.${(latest?.mode ?? summary.currentMode).toLowerCase()}`)}</span><span>{t(selectionStrategyKey[latest?.strategy ?? summary.currentStrategy])}</span></div>}
    </div><div className="selection-actions">
      <button type="button" className="btn btn-sm" aria-expanded={expanded} onClick={() => { setExpanded(!expanded); if (!expanded) setView('comparison'); }}>{t('usageSelection.compare')}</button>
      {status === 'Pending' && <button type="button" className="btn btn-sm" onClick={() => openView('preview')}>{t('usageSelection.preview')}</button>}
      <RefreshButton onRefresh={load} title="usageSelection.refresh" />
    </div></header>
    {error && <p className="usage-form-error" role="alert">{t('usageSelection.historyLoadFailed')}</p>}
    {!summary && !error && <p className="text-dim" role="status">{t('Loading...')}</p>}
    {summary && !latest && <p className="text-dim selection-empty">{t('usageSelection.noHistory')}</p>}
    {latest && <>
      {!summary?.currentAttempt && <p className="text-dim selection-summary">{t('usageSelection.latestRetained')}</p>}
      <dl className="selection-facts"><div><dt>{t('usageSelection.selectedCaptain')}</dt><dd>
        {latest.selectedCaptainId ? <>{latest.selectedCaptainAvailable
          ? <Link to={`/captains/${encodeURIComponent(latest.selectedCaptainId)}`}>{latest.selectedCaptainName || latest.selectedCaptainId}</Link>
          : <span>{latest.selectedCaptainName || latest.selectedCaptainId}</span>} <CopyButton text={latest.selectedCaptainId} /></> : t('usageSelection.notSelected')}
      </dd></div><div><dt>{t('usageSelection.intendedModel')}</dt><dd title={latest.intendedModel ?? undefined}><span>{latest.intendedModel || t('Unknown')}</span></dd></div>
        <div><dt>{t('usageSelection.reportedModel')}</dt><dd title={latest.reportedModel ?? undefined}><span>{latest.reportedModel || t('Unknown')}</span></dd></div></dl>
      <p className="selection-summary">{t(latest.mode === 'Observe' && latest.outcome === 'Admitted' ? 'usageSelection.observeChoice' : selectionReasonKey(latest.reasonCode))} <span className="text-dim">{formatDateTime(latest.evaluatedUtc)}</span></p>
    </>}
    {expanded && <div className="selection-inspection">
      <div className="selection-view-controls" role="group" aria-label={t('usageSelection.inspectionView')}>
        <button type="button" className={`btn btn-sm ${view === 'comparison' ? 'btn-primary' : ''}`} aria-pressed={view === 'comparison'} onClick={() => openView('comparison')}>{t('usageSelection.comparison')}</button>
        <button type="button" className={`btn btn-sm ${view === 'history' ? 'btn-primary' : ''}`} aria-pressed={view === 'history'} onClick={() => openView('history')}>{t('History')}</button>
        {status === 'Pending' && <button type="button" className={`btn btn-sm ${view === 'preview' ? 'btn-primary' : ''}`} aria-pressed={view === 'preview'} onClick={() => openView('preview')}>{t('usageSelection.preview')}</button>}
      </div>
      {view === 'history' ? <DataTable tableKey="captain-selection-history" className="selection-history-table" columns={historyColumns} rows={history?.objects ?? []} rowKey={row => row.id}
        busy={busy} ariaLabel={t('usageSelection.history')} emptyMessage={t('usageSelection.noMatchingHistory')}
        pagination={{ pageNumber: historyPage, pageSize: historySize, totalPages: history?.totalPages ?? 0, totalRecords: history?.totalRecords ?? 0,
          onPageChange: setHistoryPage, onPageSizeChange: size => { setHistorySize(size); setHistoryPage(1); } }}
        toolbarExtra={<label className="selection-outcome-filter">{t('Status')}<select aria-label={t('usageSelection.historyFilter')} value={outcome} onChange={e => { setOutcome(e.target.value); setHistoryPage(1); }}>
          <option value="">{t('All')}</option><option value="Admitted">{t('usageSelection.admitted')}</option><option value="Waiting">{t('usageSelection.waiting')}</option></select></label>}
        autoRefresh={{ seconds: refreshSeconds, onChange: setRefreshSeconds }} onRefresh={load} refreshTitle="usageSelection.refresh" /> : <>
        {recorded && view === 'comparison' && <div className="selection-record-heading"><p className="text-dim">{formatDateTime(recorded.evaluatedUtc)} · {t(`usageSelection.${recorded.mode.toLowerCase()}`)} · {t(selectionStrategyKey[recorded.strategy])}</p>
          <button type="button" className="btn btn-sm" onClick={() => setJson(detail)}>{t('View JSON')}</button></div>}
        {detail && view === 'comparison' && <p className="selection-budget text-dim">{t('usageSelection.policyBudget', { reserve: percent(detail.policy.reservePercent), estimate: percent(detail.policy.perAttemptReservationPercent) })}</p>}
        {view === 'comparison' && recorded?.nextReconsiderUtc && <p className="text-dim">{t('usageSelection.reconsiderAt', { time: formatDateTime(recorded.nextReconsiderUtc) })}</p>}
        {view === 'preview' && preview && <p className="selection-summary">{t(selectionReasonKey(preview.reasonCode))} <span className="text-dim">{formatDateTime(preview.evaluatedUtc)}</span></p>}
        <DataTable tableKey={view === 'preview' ? 'captain-selection-preview' : 'captain-selection-comparison'} columns={candidateColumns} rows={comparison?.objects ?? []} rowKey={row => row.captainId}
          className="selection-comparison-table" busy={busy} ariaLabel={t('usageSelection.comparison')} emptyMessage={t(summary?.historyAvailable || view === 'preview' ? 'usageSelection.noCandidates' : 'usageSelection.noHistory')}
          pagination={{ pageNumber: candidatePage, pageSize: candidateSize, totalPages: comparison?.totalPages ?? 0, totalRecords: comparison?.totalRecords ?? 0,
            onPageChange: setCandidatePage, onPageSizeChange: size => { setCandidateSize(size); setCandidatePage(1); } }}
          autoRefresh={{ seconds: refreshSeconds, onChange: setRefreshSeconds }} onRefresh={load} refreshTitle="usageSelection.refresh" />
      </>}
    </div>}
    <JsonViewer open={json !== null} title={t('usageSelection.title')} data={json} onClose={() => setJson(null)} />
  </section>;
}
