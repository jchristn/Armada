import type { ModelEvaluationReport, ModelProbeKind, ModelProbeOutcome, ModelProbeResult } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';

/**
 * Rendering shared by the inline Evaluation section (ModelProbeEvaluate) and the Evaluations detail page: the
 * per-probe results and the Full Evaluate report.
 */

export const PROBE_LABELS: Record<ModelProbeKind, string> = {
  ServedContext: 'Served context',
  OutputContract: 'Output contract',
  AsciiFormat: 'ASCII and format',
  ToolCalling: 'Tool calling',
  Speed: 'Speed',
  Determinism: 'Determinism',
};

export const OUTCOME_LABELS: Record<ModelProbeOutcome, string> = {
  Passed: 'Passed',
  Failed: 'Failed',
  CouldNotRun: 'Could not run',
};

/** The Full Evaluate report: completeness, evaluator, per-persona counts (the source of truth) and the narrative. */
export function FullReport({ report }: { report: ModelEvaluationReport }) {
  const { t } = useLocale();
  const narrative = report.narrative;
  return (
    <div className="model-evaluation-report" data-testid="model-evaluation-report">
      <p className={report.complete ? 'text-dim' : 'text-danger'}>
        {report.complete ? t('Report complete') : t('Incomplete: {{reason}}', { reason: report.incompleteReason ?? '' })}
        {report.evaluatorCaptainName && <span className="text-dim">{' '}{t('Evaluator: {{name}}', { name: report.evaluatorCaptainName })}</span>}
      </p>
      {report.warnings.map((warning) => <p key={warning} className="text-warning">{warning}</p>)}
      {report.personaOutcomes.length > 0 && (
        <ul className="model-evaluation-personas">
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

/** The per-probe results of a run: probe, outcome tag, summary and the could-not-run or failure reason. */
export function ModelProbeResultsList({ results }: { results: ModelProbeResult[] }) {
  const { t } = useLocale();
  if (results.length === 0) return null;
  return (
    <ul className="model-probe-results">
      {results.map((result) => (
        <li key={result.id || result.probe}>
          <strong>{t(PROBE_LABELS[result.probe] ?? result.probe)}</strong>{' '}
          <span className={`tag ${result.outcome.toLowerCase()}`}>{t(OUTCOME_LABELS[result.outcome] ?? result.outcome)}</span>
          {result.summary && <span>{' '}{result.summary}</span>}
          {result.reason && <span className="text-dim">{' '}({result.reason})</span>}
        </li>
      ))}
    </ul>
  );
}
