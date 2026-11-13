import { Link } from 'react-router-dom';
import type { AskMissionSnapshot, AskTargetSnapshot, AskTrackedWork, AskWorkSnapshot } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import StatusBadge from '../shared/StatusBadge';
import { entityTypeLabel, isFailedChildStatus, isWorkActive, statusCounts, workProgress, workRoute } from '../../lib/askWork';
import AskBlackBoxCard from './AskBlackBoxCard';

/** Localized label for a tracked entity type; unknown types render as-is. */
export function useEntityTypeLabel(): (entityType: string | null | undefined) => string {
  const { t } = useLocale();
  return (entityType) => entityTypeLabel(t, entityType);
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
            <dt title={t('The captain working on this mission.')}>{t('Captain')}</dt>
            <dd>
              {mission.captainId
                ? <Link to={`/captains/${encodeURIComponent(mission.captainId)}`}>{mission.captainName || mission.captainId}</Link>
                : mission.captainName}
            </dd>
          </div>
        )}
        {(mission.pipelineStage || mission.persona) && (
          <div>
            <dt title={t('Current pipeline stage or persona of the mission, for example Architect, Worker, or Judge.')}>{t('Stage')}</dt>
            <dd>{mission.pipelineStage || mission.persona}</dd>
          </div>
        )}
        {mission.checkRunStatus && (
          <div>
            <dt title={t('Status of the build and test check run for this mission. Click it for the output.')}>{t('Checks')}</dt>
            <dd>
              {mission.checkRunId
                ? <Link to={`/checks/${encodeURIComponent(mission.checkRunId)}`}>{t(mission.checkRunStatus)}</Link>
                : t(mission.checkRunStatus)}
            </dd>
          </div>
        )}
        {mission.mergeQueueStatus && (
          <div>
            <dt title={t('Status of this mission in the merge queue, where it is tested and merged in order.')}>{t('Merge')}</dt>
            <dd>
              {mission.mergeEntryId
                ? <Link to={`/merge-queue/${encodeURIComponent(mission.mergeEntryId)}`}>{t(mission.mergeQueueStatus)}</Link>
                : t(mission.mergeQueueStatus)}
            </dd>
          </div>
        )}
        {mission.landingOutcome && (
          <div>
            <dt title={t('How the finished work was delivered: merged locally, pushed, opened as a pull request, or left on its branch.')}>{t('Landing')}</dt>
            <dd>{t(mission.landingOutcome)}</dd>
          </div>
        )}
        {mission.branchName && (
          <div className="ask-work-row-branch">
            <dt title={t('Git branch the captain committed its work to.')}>{t('Branch')}</dt>
            <dd className="mono" title={mission.branchName}>{mission.branchName}</dd>
          </div>
        )}
        {mission.prUrl && (
          <div>
            <dt title={t('Pull request opened for this mission.')}>{t('PR')}</dt>
            <dd><a href={mission.prUrl} target="_blank" rel="noopener noreferrer" title={t('Open the pull request on the git host in a new tab.')}>{t('Open pull request')}</a></dd>
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
            <div><dt title={t('The voyage dispatched for this vessel.')}>{t('Voyage')}</dt><dd><Link to={`/voyages/${encodeURIComponent(target.voyageId)}`} className="mono" title={t('Open the voyage.')}>{target.voyageId}</Link></dd></div>
          )}
          {target.missionId && (
            <div><dt title={t('The mission working on this vessel.')}>{t('Mission')}</dt><dd><Link to={`/missions/${encodeURIComponent(target.missionId)}`} className="mono" title={t('Open the mission.')}>{target.missionId}</Link></dd></div>
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
 * from `ask.work` events. A black box entry shows its summary, conflicts, recovery paths and follow-ups instead.
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
  const blackBox = snapshot?.blackBox ?? null;

  return (
    <section
      className={`ask-work-card${active ? ' is-active' : ''}${highlighted ? ' is-highlighted' : ''}`}
      id={work ? `ask-work-${work.id}` : undefined}
      aria-label={t('{{type}}: {{title}}', { type: entityLabel(entityType), title })}
    >
      <header className="ask-work-card-header">
        <div className="ask-work-card-heading">
          <span className="ask-work-card-type" title={t('Kind of work this card tracks. It updates live while the work runs.')}>{entityLabel(entityType)}</span>
          {route
            ? <Link className="ask-work-card-title" to={route}>{title}</Link>
            : <span className="ask-work-card-title">{title}</span>}
        </div>
        <div className="ask-work-card-status">
          {active && <span className="ask-live-dot" aria-hidden="true" />}
          {status ? <StatusBadge status={status} /> : <span className="text-dim">{t('Waiting for status...')}</span>}
        </div>
      </header>

      {blackBox && <AskBlackBoxCard card={blackBox} />}

      {!blackBox && progress && (
        <div className="ask-work-progress">
          <div
            title={t('Share of child items finished: green succeeded, red failed.')}
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
        <ul className="ask-work-counts" aria-label={t('Counts by status')} title={t('How many missions or vessels are in each status.')}>
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

      {snapshot?.rowTotal != null && snapshot.rowTotal > Math.max(missions.length, targets.length) && (
        <p className="text-dim ask-work-more" role="note">
          {t('Showing {{shown}} of {{total}} rows.', { shown: Math.max(missions.length, targets.length), total: snapshot.rowTotal })}
          {route && <> <Link to={route}>{t('Open details')}</Link></>}
        </p>
      )}
      {snapshot?.errorText && <p className="ask-work-error" role="note">{snapshot.errorText}</p>}
      {!snapshot && <p className="text-dim ask-work-loading">{t('Loading live status...')}</p>}

      <footer className="ask-work-card-footer text-dim">
        {updated && <span title={t('When this card last received a status change.')}>{t('Updated {{time}}', { time: formatRelativeTime(updated) })}</span>}
        {route && <Link to={route} title={t('Open the full detail page for this work.')}>{t('Open details')}</Link>}
      </footer>
    </section>
  );
}
