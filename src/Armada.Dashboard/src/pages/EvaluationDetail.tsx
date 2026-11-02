import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { getModelProbeRun } from '../api/client';
import type { ModelProbeRunWithBattery } from '../types/models';
import PageHeader from '../components/shared/PageHeader';
import StatusBadge from '../components/shared/StatusBadge';
import { FullReport, PROBE_LABELS, OUTCOME_LABELS } from '../components/evaluations/ModelProbeRunDetails';
import AutoRefreshSelect from '../components/shared/AutoRefreshSelect';
import { useAutoRefresh } from '../lib/useAutoRefresh';
import { useLocale } from '../context/LocaleContext';
import { evaluationsListPath } from '../lib/evaluationLinks';

function isFinished(run: ModelProbeRunWithBattery | null): boolean {
  return !!run && (run.status === 'Completed' || run.status === 'Failed' || run.status === 'Cancelled');
}

const BATTERY_OUTCOME_LABELS: Record<string, string> = {
  Passed: 'Passed',
  Failed: 'Failed',
  CouldNotRun: 'Could not run',
  PendingEvaluation: 'Pending evaluation',
};

interface EvaluationDetailProps {
  /** Poll interval override (tests). */
  pollMs?: number;
}

/**
 * One evaluation's results: target and provenance, per-probe results, and for Full runs the report plus the battery's
 * per-task results with links to their missions and voyages. Results refresh automatically at the selected interval, including after transient failures.
 */
export default function EvaluationDetail({ pollMs }: EvaluationDetailProps) {
  const { id = '' } = useParams<{ id: string }>();
  const { t, formatDateTime } = useLocale();
  const [run, setRun] = useState<ModelProbeRunWithBattery | null>(null);
  const [loading, setLoading] = useState(true);
  const [fetching, setFetching] = useState(true);
  const [error, setError] = useState('');
  const [refreshVersion, setRefreshVersion] = useState(0);
  const { seconds, setSeconds } = useAutoRefresh('evaluation-detail', () => setRefreshVersion(v => v + 1), {
    paused: fetching,
    intervalSeconds: pollMs === undefined ? undefined : pollMs / 1000,
  });

  const [pageSelection, setPageSelection] = useState({ id, page: 1 });
  const page = pageSelection.id === id ? pageSelection.page : 1;

  useEffect(() => {
    setRun(null);
    setLoading(true);
    setPageSelection({ id, page: 1 });
  }, [id]);

  useEffect(() => {
    let disposed = false;
    setFetching(true);
    setError('');
    const load = async () => {
      try {
        const latest = await getModelProbeRun(id, true, true, page);
        if (disposed) return;
        setRun(latest);
        setError('');
      } catch (err) {
        if (!disposed) setError(err instanceof Error ? err.message : t('Failed to load evaluation.'));
      } finally {
        if (!disposed) {
          setLoading(false);
          setFetching(false);
        }
      }
    };
    void load();
    return () => {
      disposed = true;
    };
    // Each run, page and refresh owns its request; cleanup ignores stale responses.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id, refreshVersion, page]);

  const breadcrumb = [
    ...(run?.targetKind === 'Captain' && run.captainId ? [
      { label: t('Captains'), to: '/captains' },
      { label: run.captainName || run.captainId, to: `/captains/${encodeURIComponent(run.captainId)}` },
    ] : []),
    { label: t('Evaluations'), to: evaluationsListPath(run?.captainId) },
    { label: id },
  ];
  const battery = run?.batteryProgress;
  const batteryResults = run?.batteryResults ?? [];
  const totalPages = Math.max(1, run?.batteryResultsPage?.totalPages ?? 1);

  return (
    <div className="evaluation-detail">
      <PageHeader
        breadcrumb={breadcrumb}
        actions={<>
          <AutoRefreshSelect seconds={seconds} onChange={setSeconds} />
          <button type="button" className="btn btn-sm" disabled={fetching} onClick={() => setRefreshVersion(v => v + 1)}>{t('Refresh')}</button>
        </>}
        title={t('Evaluation {{runId}}', { runId: id })}
        subtitle={run ? (
          <span data-testid="evaluation-summary">
            <StatusBadge status={run.status} />{' '}
            {t('{{passed}} passed, {{failed}} failed, {{couldNotRun}} could not run', {
              passed: run.passedCount, failed: run.failedCount, couldNotRun: run.couldNotRunCount,
            })}
          </span>
        ) : undefined}
      />
      {error && <p role="alert" className="text-danger">{error}</p>}
      {loading && !run && <p className="text-dim">{t('Loading...')}</p>}
      {run && (
        <>
          <dl className="detail-kv evaluation-detail-fields">
            <dt>{run.targetKind === 'Captain' ? t('Captain') : t('Model endpoint')}</dt>
            <dd>
              {run.targetKind === 'Captain' && run.captainId
                ? <Link to={`/captains/${encodeURIComponent(run.captainId)}`}>{run.captainName || run.captainId}</Link>
                : run.modelEndpointId || '-'}
            </dd>
            <dt>{t('Model')}</dt><dd>{run.model || '-'}{run.runtime ? ` (${run.runtime})` : ''}</dd>
            <dt>{t('Depth')}</dt><dd>{t(run.depth)}</dd>
            <dt>{t('Started')}</dt><dd>{formatDateTime(run.startedUtc ?? run.createdUtc)}</dd>
            {run.completedUtc && <><dt>{t('Completed')}</dt><dd>{formatDateTime(run.completedUtc)}</dd></>}
            <dt>{t('Tokens')}</dt><dd>{run.tokensUsed} / {run.tokenBudget}</dd>
            {run.targetKind === 'Captain' && run.captainId && (
              <><dt>{t('Past evaluations')}</dt><dd><Link className="btn btn-sm" to={evaluationsListPath(run.captainId)}>{t('All evaluations of this captain')}</Link></dd></>
            )}
          </dl>
          {run.failureReason && <p className="text-danger">{run.failureReason}</p>}

          <section className="evaluation-section" aria-labelledby="evaluation-probes-title">
            <h3 id="evaluation-probes-title">{t('Probe results')}</h3>
            {(run.results ?? []).length > 0 ? (
              <div className="table-wrap evaluation-table-wrap" role="region" aria-label={t('Probe results')} tabIndex={0}>
                <table className="data-table evaluation-probes-table" aria-label={t('Probe results')}>
                  <thead><tr>{['Probe type', 'Pass/fail', 'Details'].map(label => <th scope="col" key={label}>{t(label)}</th>)}</tr></thead>
                  <tbody>{(run.results ?? []).map(result => <tr key={result.id || result.probe}>
                    <th scope="row">{t(PROBE_LABELS[result.probe] ?? result.probe)}</th>
                    <td><span className={`tag ${result.outcome.toLowerCase()}`}>{t(OUTCOME_LABELS[result.outcome] ?? result.outcome)}</span></td>
                    <td>
                      {result.summary && <div>{result.summary}</div>}
                      {result.reason && <div className="text-dim">{result.reason}</div>}
                      {!result.summary && !result.reason && <span className="text-dim">—</span>}
                    </td>
                  </tr>)}</tbody>
                </table>
              </div>
            ) : <p className="text-dim evaluation-empty">{isFinished(run) ? t('No probe results were recorded.') : t('Probes are still running.')}</p>}
          </section>

          {run.depth === 'Full' && (
            <>
              <section className="evaluation-section" aria-labelledby="evaluation-report-title">
                <h3 id="evaluation-report-title">{t('Report')}</h3>
                {run.report
                  ? <FullReport report={run.report} />
                  : <p className="text-dim">{isFinished(run) ? t('No report was recorded.') : t('The report is written when the battery and grading finish.')}</p>}
                {run.profileError && <p role="alert" className="text-danger">{run.profileError}</p>}

              </section>

              <section className="evaluation-section" aria-labelledby="evaluation-battery-title">
                <h3 id="evaluation-battery-title">{t('Battery results')}</h3>
                {run.batteryError && <p role="alert" className="text-danger">{run.batteryError}</p>}
                {run.batteryResultsError && <p role="alert" className="text-danger">{run.batteryResultsError}</p>}
                {battery && (
                  <p className="evaluation-battery-summary" data-testid="evaluation-battery-summary">
                    <StatusBadge status={battery.status} />{' '}
                    {t('{{passed}} passed, {{failed}} failed, {{couldNotRun}} could not run, {{pending}} pending of {{total}} tasks', {
                      passed: battery.passedCount, failed: battery.failedCount, couldNotRun: battery.couldNotRunCount,
                      pending: battery.pendingEvaluationCount, total: battery.totalTasks,
                    })}
                    {battery.failureReason && <span className="text-danger">{' '}{battery.failureReason}</span>}
                  </p>
                )}
                {!run.batteryRunId && <p className="text-dim">{t('The battery has not started yet.')}</p>}
                {run.batteryResultsPage && (
                  <div className="evaluation-pager">
                    <button type="button" className="btn btn-sm" disabled={loading || page <= 1} onClick={() => { setLoading(true); setPageSelection({ id, page: page - 1 }); }}>{t('Previous')}</button>{' '}
                    <span>{t('Page {{page}} of {{total}}', { page, total: totalPages })}</span>{' '}
                    <button type="button" className="btn btn-sm" disabled={loading || page >= totalPages} onClick={() => { setLoading(true); setPageSelection({ id, page: page + 1 }); }}>{t('Next')}</button>
                  </div>
                )}
                {batteryResults.length > 0 && (
                  <div className="table-wrap evaluation-table-wrap" role="region" aria-label={t('Battery results')} tabIndex={0}>
                  <table className="data-table" aria-label={t('Battery results')}>
                    <thead>
                      <tr>{['Pack', 'Task', 'Persona', 'Attempt', 'Outcome', 'Summary', 'Mission', 'Voyage'].map((label) => <th scope="col" key={label}>{t(label)}</th>)}</tr>
                    </thead>
                    <tbody>
                      {batteryResults.map((result) => (
                        <tr key={result.id}>
                          <td>{result.packId}</td>
                          <td>{result.taskTitle || result.taskId}</td>
                          <td>{result.persona || '-'}</td>
                          <td>{result.attempt}</td>
                          <td><span className={`tag ${String(result.outcome).toLowerCase()}`}>{t(BATTERY_OUTCOME_LABELS[result.outcome] ?? result.outcome)}</span></td>
                          <td>
                            {result.summary && <div>{result.summary}</div>}
                            {result.reason && <div className="text-dim">{result.reason}</div>}
                            {result.details && Object.keys(result.details).length > 0 && <details><summary>{t('Details')}</summary><pre>{JSON.stringify(result.details, null, 2)}</pre></details>}
                            {result.output && <details key={result.id}><summary>{t('Output')}</summary><pre>{result.output}</pre></details>}
                          </td>
                          <td>{result.missionId ? <Link to={`/missions/${encodeURIComponent(result.missionId)}`}>{result.missionId}</Link> : '-'}</td>
                          <td>{result.voyageId ? <Link to={`/voyages/${encodeURIComponent(result.voyageId)}`}>{result.voyageId}</Link> : '-'}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                  </div>
                )}
              </section>
            </>
          )}
        </>
      )}
    </div>
  );
}
