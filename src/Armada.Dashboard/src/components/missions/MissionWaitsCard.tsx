import { useCallback, useEffect, useState } from 'react';
import { getMissionWaits } from '../../api/client';
import type { MissionWaitInterval } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import { formatWaitDuration, slotWaitSeconds, summarizeWaits, waitBreakdown, waitGateLabel } from '../../lib/missionWaits';

interface MissionWaitsCardProps {
  missionId: string;
  /** Changes when the mission changes, so the history reloads. */
  revision?: string;
  readyUtc?: string | null;
  assignedUtc?: string | null;
}

/**
 * How long the mission waited and on what: a one-line summary ("Waited 12m before starting: Captain slot 9m, ...") and
 * the recorded wait intervals. Hidden when nothing was recorded.
 */
export default function MissionWaitsCard({ missionId, revision, readyUtc, assignedUtc }: MissionWaitsCardProps) {
  const { t, formatDateTime } = useLocale();
  const [waits, setWaits] = useState<MissionWaitInterval[]>([]);
  const [total, setTotal] = useState(0);

  const load = useCallback(async () => {
    try {
      const page = await getMissionWaits(missionId, 1, 100);
      setWaits(page.objects || []);
      setTotal(page.totalRecords || 0);
    } catch {
      setWaits([]);
    }
  }, [missionId]);

  useEffect(() => { void load(); }, [load, revision]);

  if (waits.length === 0) return null;
  const summary = summarizeWaits(waits);
  const breakdown = waitBreakdown(summary, t);
  const slot = slotWaitSeconds(readyUtc, assignedUtc);

  return (
    <div className="card mission-waits-card" style={{ marginBottom: '1rem' }} data-testid="mission-waits">
      <h3 style={{ marginTop: 0 }} title={t('Recorded each time Armada tried to start this Pending mission: what held it back and for how long. Accurate to one scheduler tick.')}>{t('Wait Time')}</h3>
      <p style={{ margin: '0.25rem 0' }}>
        {summary.current
          ? t('Waiting {{duration}} so far, now on {{gate}} ({{breakdown}})', { duration: formatWaitDuration(summary.totalSeconds), gate: t(waitGateLabel(summary.current)), breakdown })
          : t('Waited {{duration}} before starting ({{breakdown}})', { duration: formatWaitDuration(summary.totalSeconds), breakdown })}
      </p>
      {slot !== null && (
        <p className="text-dim" style={{ margin: '0.25rem 0' }} title={t('From the first time only capacity held it back to its assignment.')}>
          {t('Slot wait {{duration}}', { duration: formatWaitDuration(slot) })}
        </p>
      )}
      <details>
        <summary>{t('Wait history ({{count}})', { count: total })}</summary>
        <div className="table-wrap" style={{ marginTop: '0.5rem' }}>
          <table className="table">
            <thead>
              <tr><th>{t('Waiting on')}</th><th>{t('Started')}</th><th>{t('Duration')}</th><th>{t('Detail')}</th></tr>
            </thead>
            <tbody>
              {waits.map(w => (
                <tr key={w.id}>
                  <td>{t(waitGateLabel(w.gate))}</td>
                  <td>{formatDateTime(w.startedUtc)}</td>
                  <td>{w.endedUtc
                    ? formatWaitDuration((Date.parse(w.endedUtc) - Date.parse(w.startedUtc)) / 1000)
                    : <span title={t('Still waiting on this gate.')}>{t('{{duration}} (ongoing)', { duration: formatWaitDuration((Date.now() - Date.parse(w.startedUtc)) / 1000) })}</span>}</td>
                  <td className="text-dim">{w.detail || '-'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {total > waits.length && <p className="text-dim">{t('Showing the first {{shown}} of {{count}} waits.', { shown: waits.length, count: total })}</p>}
      </details>
    </div>
  );
}
