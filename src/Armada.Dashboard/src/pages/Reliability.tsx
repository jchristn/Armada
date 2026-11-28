import { useCallback, useEffect, useState } from 'react';
import { apiErrorMessage, getGateReport, getReliabilitySummary } from '../api/client';
import type { GateReport, ReliabilitySort, ReliabilitySummary, ReliabilitySummaryRow } from '../types/reliability';
import DataTable, { type DataTableColumn, type DataTableSort } from '../components/shared/DataTable';
import LoadingIndicator from '../components/shared/LoadingIndicator';
import PageHeader from '../components/shared/PageHeader';
import ErrorModal from '../components/shared/ErrorModal';
import { useLocale } from '../context/LocaleContext';
import { usePersistedPageSize } from '../lib/usePersistedPageSize';
import { formatDurationMs, formatRate, summarizeKinds, windowStartUtc } from '../lib/reliability';

/** Days the summary covers; the server allows at most 90. */
export const RELIABILITY_WINDOWS = [7, 14, 30, 90];

const SORT_KEYS: Record<string, ReliabilitySort> = {
  model: 'Model', persona: 'Persona', attempts: 'Attempts', completion: 'CompletionRate', unclassified: 'UnclassifiedRate',
  repairs: 'Repairs', escalations: 'Escalations', failed: 'Failures',
};

/**
 * Reliability per model and persona: completion rate, failure kinds, repairs, escalations and the Unclassified rate
 * over a recent window, from recorded stage outcomes. The Admiral filters, sorts and pages. The latest acceptance gate
 * report is shown above the table for administrators. A Captains hub tab.
 */
export default function Reliability() {
  const { t, formatDateTime } = useLocale();
  const [summary, setSummary] = useState<ReliabilitySummary | null>(null);
  const [gate, setGate] = useState<GateReport | null>(null);
  const [gateState, setGateState] = useState<'loading' | 'none' | 'forbidden' | 'ready'>('loading');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = usePersistedPageSize('reliability', 50);
  const [model, setModel] = useState('');
  const [persona, setPersona] = useState('');
  const [days, setDays] = useState(14);
  const [includeEvaluation, setIncludeEvaluation] = useState(false);
  const [sortField, setSortField] = useState('attempts');
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>('desc');

  const load = useCallback(async () => {
    try {
      setLoading(true);
      const result = await getReliabilitySummary({
        model: model.trim() || undefined, persona: persona.trim() || undefined, includeEvaluation,
        fromUtc: windowStartUtc(days), sort: SORT_KEYS[sortField], descending: sortDir === 'desc', pageNumber, pageSize,
      });
      setSummary(result);
      setError('');
    } catch (err: unknown) {
      setError(apiErrorMessage(err, t('Failed to load the reliability summary.')));
    } finally {
      setLoading(false);
    }
  }, [model, persona, includeEvaluation, days, sortField, sortDir, pageNumber, pageSize, t]);

  useEffect(() => { void load(); }, [load]);

  useEffect(() => {
    let cancelled = false;
    getGateReport('latest')
      .then((report) => { if (!cancelled) { setGate(report); setGateState('ready'); } })
      .catch((err: unknown) => {
        if (cancelled) return;
        const status = (err as { status?: number } | null)?.status;
        setGateState(status === 403 || status === 401 ? 'forbidden' : 'none');
      });
    return () => { cancelled = true; };
  }, []);

  const sort: DataTableSort = {
    field: sortField,
    dir: sortDir,
    onSort: (field) => {
      if (field === sortField) setSortDir(sortDir === 'asc' ? 'desc' : 'asc');
      else { setSortField(field); setSortDir(field === 'model' || field === 'persona' ? 'asc' : 'desc'); }
      setPageNumber(1);
    },
  };

  const columns: DataTableColumn<ReliabilitySummaryRow>[] = [
    {
      key: 'model', label: t('Model'), required: true, sortKey: 'model', cellClassName: 'cell-ident',
      headerTitle: t('Model as served. Type in the box below to filter every page.'),
      clearFilter: () => { setModel(''); setPageNumber(1); },
      filter: <input type="text" className="col-filter" aria-label={t('Model')} value={model} onChange={e => { setModel(e.target.value); setPageNumber(1); }} placeholder={t('Filter...')} />,
      render: (r) => <strong>{r.model || '-'}</strong>,
    },
    {
      key: 'persona', label: t('Persona'), sortKey: 'persona', cellClassName: 'cell-nowrap',
      headerTitle: t('Persona the stage ran as.'),
      clearFilter: () => { setPersona(''); setPageNumber(1); },
      filter: <input type="text" className="col-filter" aria-label={t('Persona')} value={persona} onChange={e => { setPersona(e.target.value); setPageNumber(1); }} placeholder={t('Filter...')} />,
      render: (r) => r.persona || '-',
    },
    { key: 'attempts', label: t('Attempts'), sortKey: 'attempts', cellClassName: 'cell-nowrap', headerTitle: t('Stage attempts recorded in the window.'), render: (r) => r.attempts.toLocaleString() },
    {
      key: 'completion', label: t('Completion'), sortKey: 'completion', cellClassName: 'cell-nowrap',
      headerTitle: t('Share of attempts that completed.'),
      cellTitle: (r) => t('{{completed}} of {{attempts}} completed', { completed: r.completed, attempts: r.attempts }),
      render: (r) => formatRate(r.completionRate, r.attempts),
    },
    { key: 'failed', label: t('Failed'), sortKey: 'failed', cellClassName: 'cell-nowrap', headerTitle: t('Attempts that failed.'), render: (r) => r.failed.toLocaleString() },
    { key: 'repairs', label: t('Repairs'), sortKey: 'repairs', cellClassName: 'cell-nowrap', headerTitle: t('Attempts Armada repaired: contract repairs and output repair reruns.'), render: (r) => r.repairs.toLocaleString() },
    { key: 'escalations', label: t('Escalations'), sortKey: 'escalations', cellClassName: 'cell-nowrap', headerTitle: t('Attempts that ended in an escalation to a stronger captain.'), render: (r) => r.escalations.toLocaleString() },
    {
      key: 'unclassified', label: t('Unclassified'), sortKey: 'unclassified', cellClassName: 'cell-nowrap',
      headerTitle: t('Share of attempts whose failure Armada could not classify. A high rate means the failure taxonomy is missing a case.'),
      cellTitle: (r) => t('{{count}} unclassified', { count: r.unclassified }),
      render: (r) => formatRate(r.unclassifiedRate, r.attempts),
    },
    {
      key: 'kinds', label: t('Failure kinds'),
      headerTitle: t('Most frequent failure kinds, with counts. Hover for runtime failure kinds.'),
      cellTitle: (r) => summarizeKinds(r.runtimeFailureKinds, 10) || undefined,
      render: (r) => summarizeKinds(r.failureKinds) || '-',
    },
    { key: 'duration', label: t('Median duration'), cellClassName: 'cell-nowrap', defaultHidden: true, headerTitle: t('Median attempt duration.'), render: (r) => formatDurationMs(r.medianDurationMs) },
  ];

  const totals = summary?.totals;

  return (
    <div>
      <PageHeader
        title={t('Reliability')}
        subtitle={t('How each model does per persona: completion, failure kinds, repairs, escalations and the Unclassified rate, from recorded stage outcomes.')}
      />
      <ErrorModal error={error} onClose={() => setError('')} />

      {gateState === 'ready' && gate && (
        <div className="card" style={{ marginBottom: '1rem' }} data-testid="gate-report">
          <h3 style={{ marginTop: 0 }} title={t('The newest acceptance gate report uploaded to this Admiral.')}>
            {t('Latest gate report')} <span className={`tag ${gate.passed ? 'passed' : 'failed'}`}>{gate.passed ? t('Passed') : t('Failed')}</span>
          </h3>
          <p className="text-dim" style={{ margin: '0.25rem 0' }}>
            {t('Finished {{time}}', { time: formatDateTime(gate.finishedUtc) })}
            {gate.armadaVersion ? ` · ${t('Armada {{version}}', { version: gate.armadaVersion })}` : ''}
            {gate.pipelines.length > 0 ? ` · ${t('{{count}} pipelines', { count: gate.pipelines.length })}` : ''}
          </p>
          {gate.measures.length > 0 && (
            <div className="table-wrap">
              <table className="table">
                <thead><tr><th>{t('Measure')}</th><th>{t('Value')}</th><th>{t('Threshold')}</th><th>{t('Status')}</th></tr></thead>
                <tbody>
                  {gate.measures.map(m => (
                    <tr key={m.id} title={m.detail || undefined}>
                      <td>{m.name}</td>
                      <td className="cell-nowrap">{m.value}</td>
                      <td className="cell-nowrap text-dim">{m.threshold}</td>
                      <td className="cell-nowrap"><span className={`tag ${m.status.toLowerCase()}`}>{t(m.status)}</span></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}
      {gateState === 'none' && (
        <p className="text-dim" data-testid="gate-report-none">{t('No gate report has been uploaded yet. The gate runner uploads one after each run.')}</p>
      )}

      {totals && (
        <p style={{ margin: '0.5rem 0' }} data-testid="reliability-totals">
          {t('{{attempts}} attempts, {{completion}} completed, {{repairs}} repairs, {{escalations}} escalations, {{unclassified}} unclassified', {
            attempts: totals.attempts.toLocaleString(), completion: formatRate(totals.completionRate, totals.attempts),
            repairs: totals.repairs.toLocaleString(), escalations: totals.escalations.toLocaleString(), unclassified: formatRate(totals.unclassifiedRate, totals.attempts),
          })}
          {summary?.truncated && <span className="text-dim" title={t('The window held more outcomes than the Admiral reads at once; narrow the window or filters for exact totals.')}> {t('(partial)')}</span>}
        </p>
      )}

      <DataTable
        tableKey="reliability"
        columns={columns}
        rows={summary?.rows ?? []}
        rowKey={(r) => `${r.model}|${r.persona}`}
        sort={sort}
        pagination={{
          pageNumber, pageSize, totalPages: summary?.totalPages || 1, totalRecords: summary?.totalRecords || 0,
          onPageChange: (p) => setPageNumber(p),
          onPageSizeChange: (size) => { setPageSize(size); setPageNumber(1); },
        }}
        onRefresh={load}
        refreshTitle={t('Reload the reliability summary now.')}
        loading={loading}
        ariaLabel={t('Reliability per model and persona')}
        toolbarExtra={(
          <>
            <label className="cell-nowrap" style={{ display: 'inline-flex', alignItems: 'center', gap: '0.35rem' }} title={t('How far back the summary reads outcomes.')}>
              {t('Window')}{' '}
              <select aria-label={t('Window')} value={days} onChange={e => { setDays(Number(e.target.value)); setPageNumber(1); }}>
                {RELIABILITY_WINDOWS.map(d => <option key={d} value={d}>{t('{{count}} days', { count: d })}</option>)}
              </select>
            </label>
            <label className="cell-nowrap" style={{ display: 'inline-flex', alignItems: 'center', gap: '0.35rem' }} title={t('Include captain evaluation and battery runs, which are left out by default.')}>
              <input type="checkbox" checked={includeEvaluation} onChange={e => { setIncludeEvaluation(e.target.checked); setPageNumber(1); }} /> {t('Include evaluations')}
            </label>
          </>
        )}
        emptyMessage={t('No stage outcomes match the current filters.')}
        placeholder={summary ? undefined : <LoadingIndicator compact label={t('Loading...')} />}
      />
    </div>
  );
}
