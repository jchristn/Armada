import { Link } from 'react-router-dom';
import { useLocale } from '../../context/LocaleContext';
import type { MissionAssignmentBlocker } from '../../types/models';
import { assignmentAdviceTarget } from '../../lib/assignmentAdvice';
import { selectionReasonKey } from '../../lib/usageSelection';
import { assignmentBlockerTitle } from '../../lib/missionActions';

interface AssignmentBlockerCardProps {
  blocker: MissionAssignmentBlocker;
}

/**
 * Explains why a Pending mission is waiting, from the server-computed assignment blocker: the reason, a summary, when it
 * clears on its own (a quarantine end), and what each captain or blocking mission is doing, with links to them.
 */
export default function AssignmentBlockerCard({ blocker }: AssignmentBlockerCardProps) {
  const { t, formatDateTime } = useLocale();
  const title = assignmentBlockerTitle(blocker.reason);

  return (
    <div className="card assignment-blocker-card" role="status" style={{ marginBottom: '1rem' }} data-reason={blocker.reason}>
      <div className="readiness-panel-header">
        <div>
          <h3 title={t('Armada\'s explanation of why no captain has picked up this Pending mission yet. It is recomputed each time the mission is loaded.')}>{t('Why This Mission Is Waiting')}</h3>
          <div className="readiness-panel-meta">{t(title)}</div>
        </div>
        <span className={`readiness-pill ${blocker.reason === 'AwaitingDispatch' ? 'ready' : 'warning'}`} title={blocker.reason === 'AwaitingDispatch' ? t('Nothing is blocking this mission; it will be assigned on the next dispatch pass.') : t('Something is holding this mission back. The reason and details are shown in this card.')}>{t('Pending')}</span>
      </div>
      <p style={{ margin: '0.5rem 0' }}>{blocker.selectionReasonCode ? t(selectionReasonKey(blocker.selectionReasonCode)) : blocker.summary}</p>
      {blocker.selectionReasonCode && <Link className="btn btn-sm" to="/activity?source=usage-overview">{t('Captain Usage')}</Link>}
      {blocker.selectionNextReconsiderUtc && <p className="text-dim">{t('usageSelection.reconsiderAt', { time: formatDateTime(blocker.selectionNextReconsiderUtc) })}</p>}
      {(blocker.advice?.length ?? 0) > 0 && (
        <section className="assignment-blocker-next-steps" aria-label={t('Next steps')}>
          <h4>{t('Next steps')}</h4>
          <ul>
            {blocker.advice!.map((advice, index) => {
              const target = assignmentAdviceTarget(advice);
              return <li key={`${advice.setting ?? advice.targetId ?? 'step'}-${index}`}>
                <span>{advice.text}</span>
                {target && <Link className="btn btn-sm" to={target.path}>{t(target.label)}</Link>}
              </li>;
            })}
          </ul>
        </section>
      )}
      {blocker.untilUtc && (
        <p className="text-dim" style={{ margin: '0.25rem 0' }}>{t('Expected to clear at {{time}}', { time: formatDateTime(blocker.untilUtc) })}</p>
      )}
      {blocker.dependsOnMissionId && (
        <p style={{ margin: '0.25rem 0' }}>
          {t('Depends on')} <Link to={`/missions/${blocker.dependsOnMissionId}`} className="mono" title={t('This mission cannot start until the linked mission completes successfully. Click to open it.')}>{blocker.dependsOnMissionId}</Link>
        </p>
      )}
      {blocker.blockingMissionIds.length > 0 && (
        <p style={{ margin: '0.25rem 0' }}>
          {t('Held by')}{' '}
          {blocker.blockingMissionIds.map((missionId, index) => (
            <span key={missionId}>
              {index > 0 && ', '}
              <Link to={`/missions/${missionId}`} className="mono" title={t('A mission that must settle before this one can be assigned. Click to open it.')}>{missionId}</Link>
            </span>
          ))}
          {(blocker.blockingMissionCount ?? 0) > blocker.blockingMissionIds.length && <span className="text-dim">{` (+${(blocker.blockingMissionCount ?? 0) - blocker.blockingMissionIds.length})`}</span>}
        </p>
      )}
      {blocker.captains.length > 0 && (
        <ul className="assignment-blocker-captains" style={{ margin: '0.5rem 0 0', paddingLeft: '1.25rem' }}>
          {blocker.captains.map((captain) => (
            <li key={captain.captainId} data-eligible={captain.eligibleForMission == null ? undefined : String(captain.eligibleForMission)}>
              <Link to={`/captains/${captain.captainId}`} title={t('A captain that could run this mission, with what it is doing now. Click to open the captain.')}>{captain.captainName || captain.captainId}</Link>
              {': '}
              {/* Idle captains that may not take this mission are dimmed; the detail says why. */}
              <span className={captain.eligibleForMission === false ? 'text-dim' : undefined}>{captain.detail}</span>
              {captain.objectiveId && (
                <>
                  {' '}
                  <Link to={`/backlog/${captain.objectiveId}`} title={t('Open the backlog item this captain is busy with, such as a planning or refinement session.')}>{t('Open backlog item')}</Link>
                </>
              )}
            </li>
          ))}
          {(blocker.captainCount ?? 0) > blocker.captains.length && <li className="text-dim">{`+${(blocker.captainCount ?? 0) - blocker.captains.length}`}</li>}
        </ul>
      )}
    </div>
  );
}
