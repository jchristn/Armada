import { Link } from 'react-router-dom';
import type { AskMissionSnapshot, AskTargetSnapshot, AskTrackedWork, AskWorkSnapshot } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import StatusBadge from '../shared/StatusBadge';
import { isFailedChildStatus, isWorkActive, statusCounts, workProgress, workRoute } from '../../lib/askWork';

/** Localized label for a tracked entity type; unknown types render as-is. */
export function useEntityTypeLabel(): (entityType: string | null | undefined) => string {
  const { t } = useLocale();
  return (entityType) => {
    switch (entityType) {
      case 'Voyage': return t('Voyage');
      case 'Mission': return t('Mission');
      case 'FleetActionRun': return t('Fleet action run');
      case 'Job': return t('Background job');
      case 'VesselImportBatch': return t('Vessel import');
      default: return entityType ?? '';
    }
  };
}

interface AskWorkCardProps {
  work: AskTrackedWork | null;
  snapshot: AskWorkSnapshot | null;
  /** Visually emphasize the card briefly after the work strip scrolls to it. */
  highlighted?: boolean;
}

function MissionRow({ mission }: { mission: AskMissionSnapshot }) {
  const { t } = useLocale();
  const failed = isFailedChildStatus(mission.status);
  return (
    <li className={`ask-work-row${failed ? ' is-failed' : ''}`}>
      <div className="ask-work-row-main">
        <StatusBadge status={mission.status} />
        <Link className="ask-work-row-title" to={`/missions/${encodeURIComponent(mission.id)}`} title={mission.title || mission.id}>
          {mission.title || mission.id}
        </Link>
      </div>
      <dl className="ask-work-row-facts">
        {(mission.captainName || mission.captainId) && (
          <div>
            <dt>{t('Captain')}</dt>
            <dd>
              {mission.captainId
                ? <Link to={`/captains/${encodeURIComponent(mission.captainId)}`}>{mission.captainName || mission.captainId}</Link>
                : mission.captainName}
            </dd>
          </div>
        )}
        {(mission.pipelineStage || mission.persona) && (
          <div>
            <dt>{t('Stage')}</dt>
            <dd>{mission.pipelineStage || mission.persona}</dd>
          </div>
        )}
        {mission.checkRunStatus && (
          <div>
            <dt>{t('Checks')}</dt>
            <dd>
              {mission.checkRunId
                ? <Link to={`/checks/${encodeURIComponent(mission.checkRunId)}`}>{t(mission.checkRunStatus)}</Link>
                : t(mission.checkRunStatus)}
            </dd>
          </div>
        )}
        {mission.mergeQueueStatus && (
          <div>
            <dt>{t('Merge')}</dt>
            <dd>
              {mission.mergeEntryId
                ? <Link to={`/merge-queue/${encodeURIComponent(mission.mergeEntryId)}`}>{t(mission.mergeQueueStatus)}</Link>
                : t(mission.mergeQueueStatus)}
            </dd>
          </div>
        )}
        {mission.landingOutcome && (
          <div>
            <dt>{t('Landing')}</dt>
            <dd>{t(mission.landingOutcome)}</dd>
          </div>
        )}
        {mission.branchName && (
          <div className="ask-work-row-branch">
            <dt>{t('Branch')}</dt>
            <dd className="mono" title={mission.branchName}>{mission.branchName}</dd>
          </div>
        )}
        {mission.prUrl && (
          <div>
            <dt>{t('PR')}</dt>
            <dd><a href={mission.prUrl} target="_blank" rel="noopener noreferrer">{t('Open pull request')}</a></dd>
          </div>
        )}
      </dl>
      {mission.failureReason && <p className="ask-work-row-reason">{mission.failureReason}</p>}
    </li>
  );
}

function TargetRow({ target }: { target: AskTargetSnapshot }) {
  const { t } = useLocale();
  const failed = isFailedChildStatus(target.status);
  return (
    <li className={`ask-work-row${failed ? ' is-failed' : ''}`}>
      <div className="ask-work-row-main">
        <StatusBadge status={target.status} />
        {target.vesselId
          ? <Link className="ask-work-row-title" to={`/vessels/${encodeURIComponent(target.vesselId)}`}>{target.vesselName || target.vesselId}</Link>
          : <span className="ask-work-row-title">{target.vesselName || target.id}</span>}
      </div>
      {(target.missionId || target.voyageId) && (
        <dl className="ask-work-row-facts">
          {target.voyageId && (
            <div><dt>{t('Voyage')}</dt><dd><Link to={`/voyages/${encodeURIComponent(target.voyageId)}`} className="mono">{target.voyageId}</Link></dd></div>
          )}
          {target.missionId && (
            <div><dt>{t('Mission')}</dt><dd><Link to={`/missions/${encodeURIComponent(target.missionId)}`} className="mono">{target.missionId}</Link></dd></div>
          )}
        </dl>
      )}
      {target.reason && <p className="ask-work-row-reason">{target.reason}</p>}
    </li>
  );
}

/**
 * The live card for one tracked item (voyage, mission, fleet action run, job, import batch): status, a progress
 * bar, counts by status, and per-mission / per-target rows linking to the normal detail pages. Updated in place
 * from `ask.work` events.
 */
export default function AskWorkCard({ work, snapshot, highlighted }: AskWorkCardProps) {
  const { t, formatRelativeTime } = useLocale();
  const entityLabel = useEntityTypeLabel();
  const entityType = snapshot?.entityType ?? work?.entityType ?? '';
  const entityId = snapshot?.entityId ?? work?.entityId ?? '';
  const title = work?.title || snapshot?.title || entityId;
  const status = snapshot?.status ?? work?.status ?? '';
  const active = isWorkActive(snapshot ?? work);
  const progress = workProgress(snapshot);
  const counts = statusCounts(snapshot);
  const missions = snapshot?.missions ?? [];
  const targets = snapshot?.targets ?? [];
  const route = entityId ? workRoute(entityType, entityId) : null;
  const updated = snapshot?.capturedUtc ?? work?.lastChangeUtc ?? null;

  return (
    <section
      className={`ask-work-card${active ? ' is-active' : ''}${highlighted ? ' is-highlighted' : ''}`}
      id={work ? `ask-work-${work.id}` : undefined}
      aria-label={t('{{type}}: {{title}}', { type: entityLabel(entityType), title })}
    >
      <header className="ask-work-card-header">
        <div className="ask-work-card-heading">
          <span className="ask-work-card-type">{entityLabel(entityType)}</span>
          {route
            ? <Link className="ask-work-card-title" to={route}>{title}</Link>
            : <span className="ask-work-card-title">{title}</span>}
        </div>
        <div className="ask-work-card-status">
          {active && <span className="ask-live-dot" aria-hidden="true" />}
          {status ? <StatusBadge status={status} /> : <span className="text-dim">{t('Waiting for status...')}</span>}
        </div>
      </header>

      {progress && (
        <div className="ask-work-progress">
          <div
            className="ask-work-progress-bar"
            role="progressbar"
            aria-valuemin={0}
            aria-valuemax={100}
            aria-valuenow={progress.percent}
            aria-label={t('Progress')}
          >
            <span className="ask-work-progress-done" style={{ width: `${progress.total > 0 ? ((progress.done - progress.failed) / progress.total) * 100 : 0}%` }} />
            <span className="ask-work-progress-failed" style={{ width: `${progress.total > 0 ? (progress.failed / progress.total) * 100 : 0}%` }} />
          </div>
          <span className="ask-work-progress-text">
            {t('{{done}} of {{total}} finished', { done: progress.done, total: progress.total })}
            {progress.failed > 0 && <> &middot; <span className="ask-work-failed-text">{t('{{count}} failed', { count: progress.failed })}</span></>}
          </span>
        </div>
      )}

      {counts.length > 1 && (
        <ul className="ask-work-counts" aria-label={t('Counts by status')}>
          {counts.map((c) => (
            <li key={c.status}><span className="text-dim">{t(c.status)}</span> <strong>{c.count}</strong></li>
          ))}
        </ul>
      )}

      {missions.length > 0 && (
        <ul className="ask-work-rows" aria-label={t('Missions')}>
          {missions.map((m) => <MissionRow key={m.id} mission={m} />)}
        </ul>
      )}
      {missions.length === 0 && targets.length > 0 && (
        <ul className="ask-work-rows" aria-label={t('Targets')}>
          {targets.map((target) => <TargetRow key={target.id} target={target} />)}
        </ul>
      )}

      {snapshot?.errorText && <p className="ask-work-error" role="note">{snapshot.errorText}</p>}
      {!snapshot && <p className="text-dim ask-work-loading">{t('Loading live status...')}</p>}

      <footer className="ask-work-card-footer text-dim">
        {updated && <span>{t('Updated {{time}}', { time: formatRelativeTime(updated) })}</span>}
        {route && <Link to={route}>{t('Open details')}</Link>}
      </footer>
    </section>
  );
}
