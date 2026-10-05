import { Link } from 'react-router-dom';
import { useLocale } from '../../context/LocaleContext';
import type { MissionAssignmentBlocker } from '../../types/models';

interface AssignmentBlockerCardProps {
  blocker: MissionAssignmentBlocker;
}

const REASON_TITLES: Record<string, string> = {
  AwaitingDispatch: 'About to start',
  VesselMissing: 'No vessel',
  VesselMisconfigured: 'Vessel needs attention',
  DependencyNotFinished: 'Waiting for an earlier mission',
  DependencyHandoffPending: 'Preparing the handoff',
  WaitingForVoyageWorkers: 'Waiting for the other missions in this voyage',
  VesselBroadScopeMissionActive: 'Vessel is held by a broad-scope mission',
  BroadScopeWaitingForVessel: 'Waiting for the vessel to be free',
  VesselConcurrencyLimit: 'Vessel runs one mission at a time',
  NoCaptains: 'No captains',
  NoIdleCaptain: 'Waiting for a captain',
  NoEligibleCaptain: 'No captain can take this mission',
};

/**
 * Explains why a Pending mission is waiting, from the server-computed assignment blocker: the reason, a summary, when it
 * clears on its own (a quarantine end), and what each captain or blocking mission is doing, with links to them.
 */
export default function AssignmentBlockerCard({ blocker }: AssignmentBlockerCardProps) {
  const { t, formatDateTime } = useLocale();
  const title = REASON_TITLES[blocker.reason] ?? 'Waiting';

  return (
    <div className="card assignment-blocker-card" role="status" style={{ marginBottom: '1rem' }} data-reason={blocker.reason}>
      <div className="readiness-panel-header">
        <div>
          <h3>{t('Why This Mission Is Waiting')}</h3>
          <div className="readiness-panel-meta">{t(title)}</div>
        </div>
        <span className={`readiness-pill ${blocker.reason === 'AwaitingDispatch' ? 'ready' : 'warning'}`}>{t('Pending')}</span>
      </div>
      <p style={{ margin: '0.5rem 0' }}>{blocker.summary}</p>
      {blocker.untilUtc && (
        <p className="text-dim" style={{ margin: '0.25rem 0' }}>{t('Expected to clear at {{time}}', { time: formatDateTime(blocker.untilUtc) })}</p>
      )}
      {blocker.dependsOnMissionId && (
        <p style={{ margin: '0.25rem 0' }}>
          {t('Depends on')} <Link to={`/missions/${blocker.dependsOnMissionId}`} className="mono">{blocker.dependsOnMissionId}</Link>
        </p>
      )}
      {blocker.blockingMissionIds.length > 0 && (
        <p style={{ margin: '0.25rem 0' }}>
          {t('Held by')}{' '}
          {blocker.blockingMissionIds.map((missionId, index) => (
            <span key={missionId}>
              {index > 0 && ', '}
              <Link to={`/missions/${missionId}`} className="mono">{missionId}</Link>
            </span>
          ))}
        </p>
      )}
      {blocker.captains.length > 0 && (
        <ul className="assignment-blocker-captains" style={{ margin: '0.5rem 0 0', paddingLeft: '1.25rem' }}>
          {blocker.captains.map((captain) => (
            <li key={captain.captainId}>
              <Link to={`/captains/${captain.captainId}`}>{captain.captainName || captain.captainId}</Link>
              {': '}
              <span>{captain.detail}</span>
              {captain.objectiveId && (
                <>
                  {' '}
                  <Link to={`/backlog/${captain.objectiveId}`}>{t('Open backlog item')}</Link>
                </>
              )}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
