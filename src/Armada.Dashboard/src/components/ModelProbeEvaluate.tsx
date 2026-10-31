import { useEffect, useRef, useState } from 'react';
import { cancelModelProbeRun, evaluateCaptain, evaluateModelEndpoint, getModelProbeRun, listModelProbeRuns } from '../api/client';
import type { ModelEvaluationOptions, ModelEvaluationReport, ModelProbeDepth, ModelProbeKind, ModelProbeOutcome, ModelProbeRun } from '../types/models';
import { useLocale } from '../context/LocaleContext';
import ModelEvaluateDialog from './ModelEvaluateDialog';
import StatusBadge from './shared/StatusBadge';

/** How often a queued or running probe run is polled. */
export const MODEL_PROBE_POLL_MS = 3000;

const PROBE_LABELS: Record<ModelProbeKind, string> = {
  ServedContext: 'Served context',
  OutputContract: 'Output contract',
  AsciiFormat: 'ASCII and format',
  ToolCalling: 'Tool calling',
  Speed: 'Speed',
  Determinism: 'Determinism',
};

const OUTCOME_LABELS: Record<ModelProbeOutcome, string> = {
  Passed: 'Passed',
  Failed: 'Failed',
  CouldNotRun: 'Could not run',
};

interface ModelProbeEvaluateProps {
  /** What to evaluate: a captain (through its own runtime) or a model endpoint. */
  target: 'captain' | 'modelEndpoint';
  targetId: string;
  /** The page's error display (for example its ErrorModal); errors show inline when omitted. */
  onError?: (message: string) => void;
  /** Poll interval override (tests). */
  pollMs?: number;
  evaluationRunId?: string | null;
}

function isFinished(run: ModelProbeRun | null): boolean {
  return !!run && (run.status === 'Completed' || run.status === 'Failed' || run.status === 'Cancelled');
}

/** The Full Evaluate report: completeness, evaluator, per-persona counts (the source of truth) and the narrative. */
function FullReport({ report }: { report: ModelEvaluationReport }) {
  const { t } = useLocale();
  const narrative = report.narrative;
  return (
    <div className="model-evaluation-report" data-testid="model-evaluation-report" style={{ marginTop: '0.5rem' }}>
      <p className={report.complete ? 'text-dim' : 'text-danger'}>
        {report.complete ? t('Report complete') : t('Incomplete: {{reason}}', { reason: report.incompleteReason ?? '' })}
        {report.evaluatorCaptainName && <span className="text-dim">{' '}{t('Evaluator: {{name}}', { name: report.evaluatorCaptainName })}</span>}
      </p>
      {report.warnings.map((warning) => <p key={warning} className="text-warning">{warning}</p>)}
      {report.personaOutcomes.length > 0 && (
        <ul className="model-evaluation-personas" style={{ margin: '0.25rem 0 0', paddingLeft: '1.25rem' }}>
          {report.personaOutcomes.map((outcome) => (
            <li key={outcome.persona}>
              {t('{{persona}}: {{passed}} of {{graded}} passed, {{pending}} pending, {{couldNotRun}} could not run', {
                persona: outcome.persona, passed: outcome.totals.passed, graded: outcome.totals.graded,
                pending: outcome.totals.pendingEvaluation, couldNotRun: outcome.totals.couldNotRun,
              })}
            </li>
          ))}
        </ul>
      )}
      {narrative.status === 'Accepted' && (
        <>
          {narrative.strengths.length > 0 && (
            <><strong>{t('Strengths')}</strong><ul>{narrative.strengths.map((f) => <li key={f.key + f.text}>{f.text}</li>)}</ul></>
          )}
          {narrative.weaknesses.length > 0 && (
            <><strong>{t('Weaknesses')}</strong><ul>{narrative.weaknesses.map((f) => <li key={f.key + f.text}>{f.text}</li>)}</ul></>
          )}
          {narrative.observedProblems.length > 0 && (
            <><strong>{t('Observed problems')}</strong><ul>{narrative.observedProblems.map((p) => <li key={p}>{p}</li>)}</ul></>
          )}
          {narrative.howToWork && <p><strong>{t('How to work with this model')}</strong>{' '}{narrative.howToWork}</p>}
        </>
      )}
      {narrative.status === 'Skipped' && <p className="text-dim">{t('Subjective grading skipped: {{reason}}', { reason: narrative.reason ?? '' })}</p>}
      {narrative.status === 'Rejected' && <p className="text-warning">{t('Narrative rejected: {{reason}}', { reason: narrative.reason ?? '' })}</p>}
    </div>
  );
}

/**
 * Evaluate buttons: Evaluate starts a Quick probe run; Full Evaluate adds the fixture battery and the evaluator's grading.
 * For a captain, Full Evaluate opens a dialog for the depth, evaluator, packs, personas and run count; for a model
 * endpoint it starts Full with the server defaults. Either run is polled until it finishes and can be cancelled while it
 * runs.
 */
export default function ModelProbeEvaluate({ target, targetId, onError, pollMs = MODEL_PROBE_POLL_MS, evaluationRunId }: ModelProbeEvaluateProps) {
  const { t } = useLocale();
  const [run, setRun] = useState<ModelProbeRun | null>(null);
  const [history, setHistory] = useState<ModelProbeRun[]>([]);
  const [loading, setLoading] = useState(true);
  const [now, setNow] = useState(Date.now());
  const selection = useRef(0);
  const [starting, setStarting] = useState(false);
  const [polling, setPolling] = useState(false);
  const [inlineError, setInlineError] = useState('');
  const [dialogOpen, setDialogOpen] = useState(false);
  const onErrorRef = useRef(onError);
  onErrorRef.current = onError;

  function report(message: string) {
    setInlineError(message);
    onErrorRef.current?.(message);
  }

  function remember(latest: ModelProbeRun) {
    setHistory(previous => [latest, ...previous.filter(item => item.id !== latest.id)]
      .sort((a, b) => b.createdUtc.localeCompare(a.createdUtc)).slice(0, 10));
  }

  async function selectRun(id: string) {
    const version = ++selection.current;
    setLoading(true);
    setPolling(false);
    setInlineError('');
    try {
      const latest = await getModelProbeRun(id, true);
      if (version !== selection.current) return;
      const matches = target === 'captain' ? latest.captainId === targetId : latest.modelEndpointId === targetId;
      if (!matches) throw new Error('Evaluation does not belong to this target.');
      setRun(latest);
      setPolling(!isFinished(latest));
      remember(latest);
    } catch (err) {
      if (version === selection.current) report(err instanceof Error ? err.message : t('Failed to load evaluation.'));
    } finally {
      if (version === selection.current) setLoading(false);
    }
  }

  useEffect(() => {
    let disposed = false;
    ++selection.current;
    setRun(null);
    setHistory([]);
    setPolling(false);
    setLoading(true);
    setInlineError('');
    const load = async () => {
      try {
        const page = await listModelProbeRuns({
          ...(target === 'captain' ? { captainId: targetId } : { modelEndpointId: targetId }), pageSize: 10,
        });
        if (disposed) return;
        setHistory(page.objects.slice(0, 10));
        const requested = evaluationRunId;
        let active = page.objects.find(item => !isFinished(item));
        let activeError = '';
        if (!requested && !active) {
          try {
            const filter = target === 'captain' ? { captainId: targetId } : { modelEndpointId: targetId };
            const pages = await Promise.all([
              listModelProbeRuns({ ...filter, status: 'Running', pageSize: 1 }),
              listModelProbeRuns({ ...filter, status: 'Queued', pageSize: 1 }),
            ]);
            if (disposed) return;
            active = pages.flatMap(result => result.objects).find(item => !isFinished(item));
          } catch (err) {
            activeError = err instanceof Error ? err.message : t('Failed to load evaluation.');
          }
        }
        if (disposed) return;
        const chosen = requested ?? active?.id ?? page.objects[0]?.id;
        if (chosen) await selectRun(chosen);
        if (!disposed && activeError) report(activeError);
      } catch (err) {
        if (!disposed) {
          if (evaluationRunId) await selectRun(evaluationRunId);
          report(err instanceof Error ? err.message : t('Failed to load evaluation.'));
        }
      } finally {
        if (!disposed) setLoading(false);
      }
    };
    void load();
    return () => { disposed = true; ++selection.current; };
    // Target and requested run define the lifetime of the loaded selection.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [target, targetId, evaluationRunId]);

  useEffect(() => {
    if (!polling) return;
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, [polling]);

  const runId = run?.id ?? null;
  useEffect(() => {
    if (!polling || !runId) return undefined;
    let cancelled = false;
    let timer: ReturnType<typeof setTimeout> | undefined;
    const tick = async () => {
      try {
        const latest = await getModelProbeRun(runId, true);
        if (cancelled) return;
        // A poll answered before a cancel must not replace the finished run the cancel returned.
        setRun((previous) => (previous && previous.id === latest.id && isFinished(previous) && !isFinished(latest) ? previous : latest));
        remember(latest);
        if (isFinished(latest)) { setPolling(false); return; }
        timer = setTimeout(tick, pollMs);
      } catch (err) {
        if (cancelled) return;
        setPolling(false);
        report(err instanceof Error ? err.message : t('Failed to load evaluation.'));
      }
    };
    timer = setTimeout(tick, pollMs);
    return () => {
      cancelled = true;
      if (timer) clearTimeout(timer);
    };
    // report and t are stable enough for a poll loop; restarting on their identity would re-poll immediately.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [polling, runId, pollMs]);

  async function handleEvaluate(depth: ModelProbeDepth, options?: ModelEvaluationOptions) {
    setStarting(true);
    setInlineError('');
    try {
      const started = target === 'captain'
        ? await (options ? evaluateCaptain(targetId, depth, options) : evaluateCaptain(targetId, depth))
        : await evaluateModelEndpoint(targetId, depth);
      setRun(started);
      remember(started);
      setPolling(!isFinished(started));
      setDialogOpen(false);
    } catch (err) {
      report(err instanceof Error ? err.message : t('Evaluate failed.'));
    } finally {
      setStarting(false);
    }
  }

  async function handleCancel() {
    if (!run) return;
    try {
      const latest = await cancelModelProbeRun(run.id);
      setRun(latest);
      remember(latest);
      if (isFinished(latest)) setPolling(false);
    } catch (err) {
      report(err instanceof Error ? err.message : t('Evaluate failed.'));
    }
  }

  const busy = loading || starting || polling;
  const finished = isFinished(run);
  const battery = run?.batteryProgress;
  const phase = run?.report?.narrative?.status && run.report.narrative.status !== 'Skipped'
    ? 'Writing report' : battery && ['Completed', 'Failed', 'Cancelled'].includes(battery.status)
      ? 'Evaluator grading' : run?.batteryRunId ? 'Fixture battery' : 'Quick probes';
  const elapsed = run ? Math.max(0, Math.floor(((run.completedUtc ? Date.parse(run.completedUtc) : now)
    - Date.parse(run.startedUtc ?? run.createdUtc)) / 1000)) : 0;

  return (
    <div className="model-probe-evaluate">
      <div style={{ display: 'flex', gap: '0.5rem', alignItems: 'center', flexWrap: 'wrap' }}>
        <button
          type="button"
          className="btn btn-sm"
          onClick={() => handleEvaluate('Quick')}
          disabled={busy}
          title={t('Run the Quick mechanical probes against this target as an evaluation. Results are observations only: they never change routing or settings and never count in production stage outcomes.')}
        >
          {starting || polling ? t('Evaluating...') : t('Evaluate')}
        </button>
        <button
          type="button"
          className="btn btn-sm"
          onClick={() => (target === 'captain' ? setDialogOpen(true) : handleEvaluate('Full'))}
          disabled={busy}
          title={t('Run the Quick probes plus the fixture battery, then have an evaluator captain of another model family grade the subjective tasks. Advisory only: nothing changes routing, persona captain lists or prompts.')}
        >
          {target === 'captain' ? t('Full Evaluate...') : t('Full Evaluate')}
        </button>
        {polling && run && (
          <button type="button" className="btn btn-sm" onClick={handleCancel}>{t('Cancel')}</button>
        )}
        {run && (
          <span data-testid="model-probe-counts">
            <StatusBadge status={run.status} />{' '}
            <span className="text-dim">
              {t('{{passed}} passed, {{failed}} failed, {{couldNotRun}} could not run', {
                passed: run.passedCount, failed: run.failedCount, couldNotRun: run.couldNotRunCount,
              })}
            </span>
          </span>
        )}
      </div>
      {run?.profileError && <p role="alert" className="text-danger">{run.profileError}</p>}
      {run?.batteryError && <p role="alert" className="text-danger">{run.batteryError}</p>}
      {inlineError && <p role="alert" className="text-danger">{inlineError}</p>}
      {run && <div aria-live="polite">
        <p>{run.depth} | {elapsed}s{run.depth === 'Full' && !finished && <> | {t(phase)}</>}</p>
        <label>{t('Tokens')}: {run.tokensUsed} / {run.tokenBudget}
          <progress aria-label={t('Tokens')} value={Math.min(run.tokensUsed, run.tokenBudget)} max={Math.max(1, run.tokenBudget)} />
        </label>
        {battery && <p>{t('Tasks')}: {battery.passedCount + battery.failedCount + battery.couldNotRunCount + battery.pendingEvaluationCount} / {battery.totalTasks}</p>}
        {battery?.failureReason && <p className="text-danger">{battery.failureReason}</p>}
      </div>}
      {run?.failureReason && <p className="text-danger">{run.failureReason}</p>}
      {run && (run.results ?? []).length > 0 && (
        <ul className="model-probe-results" style={{ margin: '0.5rem 0 0', paddingLeft: '1.25rem' }}>
          {(run.results ?? []).map((result) => (
            <li key={result.id || result.probe}>
              <strong>{t(PROBE_LABELS[result.probe] ?? result.probe)}</strong>{' '}
              <span className={`tag ${result.outcome.toLowerCase()}`}>{t(OUTCOME_LABELS[result.outcome] ?? result.outcome)}</span>
              {result.summary && <span>{' '}{result.summary}</span>}
              {result.reason && <span className="text-dim">{' '}({result.reason})</span>}
            </li>
          ))}
        </ul>
      )}
      {finished && run?.report && <FullReport report={run.report} />}
      {run?.profileRevision && run.report?.profileRevisionId && <p>
        <a download={`${run.report.profileRevisionId}.json`} href={`data:application/json;charset=utf-8,${encodeURIComponent(JSON.stringify(run.profileRevision, null, 2))}`}>
          {run.report.profileRevisionId}
        </a>
      </p>}
      {history.length > 0 && <table className="data-table">
        <caption>{t('History')}</caption>
        <thead><tr>{['Started', 'Depth', 'Status', 'Passed', 'Failed', 'Could not run', 'Report'].map(label => <th key={label}>{t(label)}</th>)}</tr></thead>
        <tbody>{history.map(item => <tr key={item.id} aria-selected={run?.id === item.id}>
          <td><button type="button" className="btn btn-sm" disabled={loading || starting} onClick={() => void selectRun(item.id)}>{item.startedUtc ?? item.createdUtc}</button></td>
          <td>{item.depth}</td><td>{item.status}</td><td>{item.passedCount}</td><td>{item.failedCount}</td><td>{item.couldNotRunCount}</td>
          <td>{item.report ? (item.report.complete ? t('Report complete') : t('Incomplete')) : isFinished(item) ? (item.status === 'Completed' ? t('Completed') : t('Incomplete')) : t('Pending')}</td>
        </tr>)}</tbody>
      </table>}
      {target === 'captain' && (
        <ModelEvaluateDialog
          open={dialogOpen}
          captainId={targetId}
          submitting={starting}
          onClose={() => setDialogOpen(false)}
          onSubmit={(depth, options) => handleEvaluate(depth, options)}
        />
      )}
    </div>
  );
}
