import { useCallback, useEffect, useState } from 'react';
import { getMission, getMissionLog, listIncidents } from '../../api/client';
import type { Mission } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import LogViewer from './LogViewer';

/** Mission statuses this panel explains. */
export const FAILED_MISSION_STATUSES: ReadonlySet<string> = new Set(['Failed', 'LandingFailed']);

const RESCUE_POLL_MS = 10000;
const SETTLED_STATUSES = new Set(['Complete', 'Failed', 'Cancelled', 'LandingFailed']);

interface MissionFailureDetailsProps {
  mission: Mission;
  /** Opens a mission (the failed one or a rescue). */
  onOpenMission: (missionId: string) => void;
}

/**
 * Explains a failed mission where the user meets it outside the mission page (the setup wizard handoff): the
 * failure reason, the mission log, and the rescue missions Armada's recovery started for it.
 */
export default function MissionFailureDetails({ mission, onOpenMission }: MissionFailureDetailsProps) {
  const { t } = useLocale();
  const [rescues, setRescues] = useState<Mission[]>([]);
  const [logOpen, setLogOpen] = useState(false);
  const [logContent, setLogContent] = useState('');
  const [logTotal, setLogTotal] = useState<number | undefined>(undefined);
  const [logLoading, setLogLoading] = useState(false);
  const [logLines, setLogLines] = useState(200);

  const loadRescues = useCallback(async () => {
    try {
      const incidents = await listIncidents({ missionId: mission.id, pageSize: 50 });
      const ids = Array.from(new Set((incidents.objects || []).flatMap((incident) => incident.rescueMissionIds || [])));
      const missions = await Promise.all(ids.map((id) => getMission(id).catch(() => null)));
      setRescues(missions.filter((m): m is Mission => m !== null));
    } catch {
      // Incidents may be unavailable to this user; the failure reason and log still help.
    }
  }, [mission.id]);

  // Recovery opens the incident and dispatches rescues shortly after the failure, so keep checking until a rescue
  // has settled.
  const allRescuesSettled = rescues.length > 0 && rescues.every((rescue) => SETTLED_STATUSES.has(String(rescue.status)));
  useEffect(() => {
    void loadRescues();
    if (allRescuesSettled) return;
    const timer = window.setInterval(() => { void loadRescues(); }, RESCUE_POLL_MS);
    return () => window.clearInterval(timer);
  }, [loadRescues, allRescuesSettled]);

  const loadLog = useCallback(async (lines: number) => {
    try {
      setLogLoading(true);
      const result = await getMissionLog(mission.id, lines);
      setLogContent(result.log || '');
      setLogTotal(result.totalLines);
    } catch {
      setLogContent(t('The mission log could not be loaded.'));
    } finally {
      setLogLoading(false);
    }
  }, [mission.id, t]);

  function openLog() {
    setLogOpen(true);
    void loadLog(logLines);
  }

  return (
    <div className="wizard-result wizard-result-error" role="alert" style={{ marginTop: '1rem' }}>
      <strong>{mission.status === 'LandingFailed' ? t('The mission ran, but its work could not land.') : t('The mission failed.')}</strong>
      <div style={{ marginTop: '0.35rem' }}>
        <span className="text-dim">{t('Reason')}: </span>
        {mission.failureReason?.trim() ? mission.failureReason : t('No reason was recorded. The mission log usually shows what went wrong.')}
      </div>

      {rescues.length > 0 ? (
        <div style={{ marginTop: '0.6rem' }}>
          <div>
            {rescues.length === 1
              ? t('Armada started a rescue mission to retry this work:')
              : t('Armada started {{count}} rescue missions to retry this work:', { count: rescues.length })}
          </div>
          <ul style={{ margin: '0.35rem 0 0', paddingLeft: '1.25rem' }}>
            {rescues.map((rescue) => (
              <li key={rescue.id}>
                <button type="button" className="btn-link" onClick={() => onOpenMission(rescue.id)}>{rescue.title}</button>
                {' '}<span className="text-dim">({t(String(rescue.status))})</span>
              </li>
            ))}
          </ul>
        </div>
      ) : (
        <div className="text-dim" style={{ marginTop: '0.6rem' }}>
          {t('Armada may start a rescue mission for this failure automatically; it will appear here.')}
        </div>
      )}

      <div className="wizard-inline-actions" style={{ marginTop: '0.6rem' }}>
        <button type="button" className="btn btn-sm" onClick={openLog}>{t('View Mission Log')}</button>
        <button type="button" className="btn btn-sm" onClick={() => onOpenMission(mission.id)}>{t('Open Mission')}</button>
      </div>

      <LogViewer
        open={logOpen}
        title={t('Mission Log: {{title}}', { title: mission.title })}
        content={logContent}
        totalLines={logTotal}
        loading={logLoading}
        markdown
        completed
        defaultLineCount={logLines}
        onLineCountChange={(lines) => { setLogLines(lines); void loadLog(lines); }}
        onClose={() => setLogOpen(false)}
      />
    </div>
  );
}
