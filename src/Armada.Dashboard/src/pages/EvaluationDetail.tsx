import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { getModelProbeRun } from '../api/client';
import type { ModelProbeRunWithBattery } from '../types/models';
import PageHeader from '../components/shared/PageHeader';
import StatusBadge from '../components/shared/StatusBadge';
import { FullReport, ModelProbeResultsList } from '../components/evaluations/ModelProbeRunDetails';
import { MODEL_PROBE_POLL_MS } from '../components/ModelProbeEvaluate';
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
 * per-task results with links to their missions and voyages. A queued or running evaluation is polled until it finishes.
 */
export default function EvaluationDetail({ pollMs = MODEL_PROBE_POLL_MS }: EvaluationDetailProps) {
  const { id = '' } = useParams<{ id: string }>();
  const { t, formatDateTime } = useLocale();
  const [run, setRun] = useState<ModelProbeRunWithBattery | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  useEffect(() => {
    let disposed = false;
    let timer: ReturnType<typeof setTimeout> | undefined;
    setRun(null);
    setLoading(true);
    setError('');
    const load = async () => {
      try {
        const latest = await getModelProbeRun(id, true, true);
        if (disposed) return;
        setRun(latest);
        setError('');
        if (!isFinished(latest)) timer = setTimeout(load, pollMs);
      } catch (err) {
        if (!disposed) setError(err instanceof Error ? err.message : t('Failed to load evaluation.'));
      } finally {
        if (!disposed) setLoading(false);
      }
    };
    void load();
    return () => {
      disposed = true;
      if (timer) clearTimeout(timer);
    };
    // t is stable for a locale; the run id and poll interval define the polling lifetime.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id, pollMs]);

  const breadcrumb = <Link to={evaluationsListPath(run?.captainId)}>{t('Evaluations')}</Link>;
  const battery = run?.batteryProgress;
  const batteryResults = run?.batteryResults ?? [];

  return (
    <div>
      <PageHeader
        breadcrumb={breadcrumb}
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
          <dl className="evaluation-detail-fields">
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
              <><dt>{t('Past evaluations')}</dt><dd><Link to={evaluationsListPath(run.captainId)}>{t('All evaluations of this captain')}</Link></dd></>
            )}
          </dl>
          {run.failureReason && <p className="text-danger">{run.failureReason}</p>}

          <h3>{t('Probe results')}</h3>
          {(run.results ?? []).length > 0
            ? <ModelProbeResultsList results={run.results ?? []} />
            : <p className="text-dim">{isFinished(run) ? t('No probe results were recorded.') : t('Probes are still running.')}</p>}

          {run.depth === 'Full' && (
            <>
              <h3>{t('Report')}</h3>
              {run.report
                ? <FullReport report={run.report} />
                : <p className="text-dim">{isFinished(run) ? t('No report was recorded.') : t('The report is written when the battery and grading finish.')}</p>}
              {run.profileError && <p role="alert" className="text-danger">{run.profileError}</p>}

              <h3>{t('Battery results')}</h3>
              {run.batteryError && <p role="alert" className="text-danger">{run.batteryError}</p>}
              {run.batteryResultsError && <p role="alert" className="text-danger">{run.batteryResultsError}</p>}
              {battery && (
                <p data-testid="evaluation-battery-summary">
                  <StatusBadge status={battery.status} />{' '}
                  {t('{{passed}} passed, {{failed}} failed, {{couldNotRun}} could not run, {{pending}} pending of {{total}} tasks', {
                    passed: battery.passedCount, failed: battery.failedCount, couldNotRun: battery.couldNotRunCount,
                    pending: battery.pendingEvaluationCount, total: battery.totalTasks,
                  })}
                  {battery.failureReason && <span className="text-danger">{' '}{battery.failureReason}</span>}
                </p>
              )}
              {!run.batteryRunId && <p className="text-dim">{t('The battery has not started yet.')}</p>}
              {batteryResults.length > 0 && (
                <table className="data-table" aria-label={t('Battery results')}>
                  <thead>
                    <tr>{['Pack', 'Task', 'Persona', 'Attempt', 'Outcome', 'Summary', 'Mission', 'Voyage'].map((label) => <th key={label}>{t(label)}</th>)}</tr>
                  </thead>
                  <tbody>
                    {batteryResults.map((result) => (
                      <tr key={result.id}>
                        <td>{result.packId}</td>
                        <td>{result.taskTitle || result.taskId}</td>
                        <td>{result.persona || '-'}</td>
                        <td>{result.attempt}</td>
                        <td><span className={`tag ${String(result.outcome).toLowerCase()}`}>{t(BATTERY_OUTCOME_LABELS[result.outcome] ?? result.outcome)}</span></td>
                        <td>{result.summary || result.reason || ''}</td>
                        <td>{result.missionId ? <Link to={`/missions/${encodeURIComponent(result.missionId)}`}>{result.missionId}</Link> : '-'}</td>
                        <td>{result.voyageId ? <Link to={`/voyages/${encodeURIComponent(result.voyageId)}`}>{result.voyageId}</Link> : '-'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </>
          )}
        </>
      )}
    </div>
  );
}
