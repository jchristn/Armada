import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { getMissionAttempts } from '../../api/client';
import type { MissionAttemptHistory } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import { useMissionAttemptRevision } from '../../context/WebSocketContext';
import { attemptNextStep, dispositionClass, formatDurationMs } from '../../lib/reliability';

interface MissionAttemptsCardProps {
  missionId: string;
  /** Changes when the mission changes, so the history reloads. */
  revision?: string;
}

/**
 * Attempts timeline: each recorded attempt of the mission (captain, model, outcome, failure kind, repair, escalation,
 * duration), oldest first, with the mission's escalation and recovery counters. Reloads on mission, escalation, repair,
 * and attempt events. Hidden until the mission has an attempt.
 */
export default function MissionAttemptsCard({ missionId, revision }: MissionAttemptsCardProps) {
  const { t, formatDateTime } = useLocale();
  const liveRevision = useMissionAttemptRevision(missionId);
  const [history, setHistory] = useState<MissionAttemptHistory | null>(null);

  const load = useCallback(async () => {
    try {
      setHistory(await getMissionAttempts(missionId));
    } catch {
      setHistory(null);
    }
  }, [missionId]);

  useEffect(() => { void load(); }, [load, revision, liveRevision]);

  if (!history || history.attempts.length === 0) return null;

  const counters: string[] = [];
  if (history.escalationCount > 0) counters.push(t('Escalations: {{count}}', { count: history.escalationCount }));
  if (history.redispatchAttempts > 0) counters.push(t('Resumes and reruns: {{count}}', { count: history.redispatchAttempts }));
  if (history.outputRepairAttempts > 0) counters.push(t('Output repairs: {{count}}', { count: history.outputRepairAttempts }));
  if (history.noOpRedispatchAttempts > 0) counters.push(t('No-op reruns: {{count}}', { count: history.noOpRedispatchAttempts }));
  if (history.stallRecoveryAttempts > 0) counters.push(t('Stall recoveries: {{count}}', { count: history.stallRecoveryAttempts }));

  return (
    <div className="card mission-attempts-card" style={{ marginBottom: '1rem' }} data-testid="mission-attempts">
      <h3 style={{ marginTop: 0 }} title={t('Every attempt Armada recorded for this mission, oldest first: who ran it, how it ended, and what Armada did next. Updates live.')}>
        {t('Attempts ({{count}})', { count: history.attempts.length })}
      </h3>
      {(counters.length > 0 || history.escalatedFromModel || history.tier) && (
        <p className="text-dim" style={{ margin: '0.25rem 0 0.5rem' }}>
          {history.tier && <span title={t('Tier the mission was last routed at.')}>{t('Tier {{tier}}', { tier: history.tier })}</span>}
          {history.tier && counters.length > 0 && ' · '}
          {counters.join(' · ')}
          {history.escalatedFromModel && (
            <span> · {t('Escalated from {{model}}', { model: history.escalatedFromModel })}</span>
          )}
        </p>
      )}
      <div className="table-wrap">
        <table className="table">
          <thead>
            <tr>
              <th title={t('Attempt number, with the Judge rework round when there was one.')}>#</th>
              <th>{t('Captain')}</th>
              <th>{t('Model')}</th>
              <th title={t('How the attempt ended.')}>{t('Outcome')}</th>
              <th title={t('Why the attempt failed, as classified by Armada.')}>{t('Failure kind')}</th>
              <th title={t('What Armada did after the attempt: a repair rerun, a contract repair, or an escalation to another captain.')}>{t('Then')}</th>
              <th>{t('Duration')}</th>
            </tr>
          </thead>
          <tbody>
            {history.attempts.map(a => (
              <tr key={a.outcomeId} data-testid="mission-attempt-row">
                <td className="cell-nowrap">{a.attempt}{a.reworkRound > 0 ? ` (${t('rework {{round}}', { round: a.reworkRound })})` : ''}</td>
                <td className="cell-nowrap">
                  {a.captainId ? <Link to={`/captains/${encodeURIComponent(a.captainId)}`}>{a.captainName || a.captainId}</Link> : '-'}
                </td>
                <td className="cell-nowrap" title={[a.runtime, a.modelProvider].filter(Boolean).join(' / ') || undefined}>{a.model || '-'}</td>
                <td className="cell-nowrap">
                  <span className={`tag ${dispositionClass(a.disposition)}`}>{t(a.disposition)}</span>
                  {a.judgeVerdict && <span className="text-dim" style={{ marginLeft: '0.35rem' }} title={t('Judge verdict for this attempt.')}>{a.judgeVerdict}</span>}
                  {a.isEvaluation && <span className="tag info" style={{ marginLeft: '0.35rem' }}>{t('Evaluation')}</span>}
                </td>
                <td title={[a.failureReason, a.suspectedCause].filter(Boolean).join(' ') || undefined}>
                  {a.failureKind || a.runtimeFailureKind || '-'}
                  {a.failureKind && a.runtimeFailureKind && <span className="text-dim"> / {a.runtimeFailureKind}</span>}
                </td>
                <td className="cell-nowrap">{attemptNextStep(a, t)}</td>
                <td className="cell-nowrap" title={a.startedUtc ? formatDateTime(a.startedUtc) : undefined}>{formatDurationMs(a.durationMs)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
