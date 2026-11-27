import { useCallback, useEffect, useState } from 'react';
import { listMissionSubmissions } from '../../api/client';
import type { StageSubmission } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import { useMissionAttemptRevision } from '../../context/WebSocketContext';
import { isBlockingFinding, submissionFindings, submissionHeadline } from '../../lib/reliability';
import Markdown from '../shared/Markdown';

interface MissionSubmissionsCardProps {
  missionId: string;
  /** Changes when the mission changes, so the submissions reload. */
  revision?: string;
}

/**
 * Structured results and verdicts the mission's captains submitted (the submit tools), newest first: status or
 * verdict, summary, report sections, and findings with severity (blocking first). Hidden when nothing was submitted.
 */
export default function MissionSubmissionsCard({ missionId, revision }: MissionSubmissionsCardProps) {
  const { t, formatDateTime } = useLocale();
  const liveRevision = useMissionAttemptRevision(missionId);
  const [submissions, setSubmissions] = useState<StageSubmission[]>([]);

  const load = useCallback(async () => {
    try {
      const list = await listMissionSubmissions(missionId);
      setSubmissions(list ?? []);
    } catch {
      setSubmissions([]);
    }
  }, [missionId]);

  useEffect(() => { void load(); }, [load, revision, liveRevision]);

  if (submissions.length === 0) return null;

  return (
    <div className="card mission-submissions-card" style={{ marginBottom: '1rem' }} data-testid="mission-submissions">
      <h3 style={{ marginTop: 0 }} title={t('Structured results and verdicts the captains submitted for this mission, newest first. Blocking findings stop the stage until fixed.')}>
        {t('Submitted Results ({{count}})', { count: submissions.length })}
      </h3>
      {submissions.map((s, index) => {
        const headline = submissionHeadline(s);
        const findings = submissionFindings(s);
        const sections = s.submission.result?.sections ?? [];
        return (
          <details key={s.id ?? `${s.attemptId}-${index}`} open={index === 0} className="mission-submission" data-testid="mission-submission">
            <summary>
              <strong>{s.persona}</strong> <span className="tag info">{headline.label}</span>{' '}
              <span className="text-dim">{formatDateTime(s.createdUtc)}</span>
              {findings.length > 0 && (
                <span className="text-dim"> · {t('{{count}} findings, {{blocking}} blocking', { count: findings.length, blocking: findings.filter(isBlockingFinding).length })}</span>
              )}
            </summary>
            {headline.summary && <Markdown>{headline.summary}</Markdown>}
            {sections.map((section, i) => (
              <div key={i} style={{ marginTop: '0.5rem' }}>
                <h4 style={{ margin: '0.25rem 0' }}>{section.heading}</h4>
                <Markdown>{section.body}</Markdown>
              </div>
            ))}
            {s.submission.verdict && (
              <p className="text-dim" style={{ margin: '0.25rem 0' }}>
                {[
                  s.submission.verdict.correctness && t('Correctness: {{value}}', { value: s.submission.verdict.correctness }),
                  s.submission.verdict.blastRadius && t('Blast radius: {{value}}', { value: s.submission.verdict.blastRadius }),
                  s.submission.verdict.sourceFidelity && t('Source fidelity: {{value}}', { value: s.submission.verdict.sourceFidelity }),
                ].filter(Boolean).join(' · ')}
              </p>
            )}
            {findings.length > 0 && (
              <div className="table-wrap" style={{ marginTop: '0.5rem' }}>
                <table className="table">
                  <thead>
                    <tr><th>{t('Severity')}</th><th>{t('Location')}</th><th>{t('Finding')}</th><th>{t('Required fix')}</th></tr>
                  </thead>
                  <tbody>
                    {findings.map((f, i) => (
                      <tr key={i} data-testid="mission-finding">
                        <td className="cell-nowrap"><span className={`tag ${isBlockingFinding(f) ? 'failed' : 'warning'}`}>{isBlockingFinding(f) ? t('Blocking') : t('Non-blocking')}</span></td>
                        <td className="cell-nowrap mono">{f.file ? `${f.file}${f.line ? `:${f.line}` : ''}` : '-'}</td>
                        <td>{f.scenario}</td>
                        <td>{f.requiredFix || '-'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </details>
        );
      })}
    </div>
  );
}
