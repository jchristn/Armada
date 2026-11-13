import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { getVesselConcurrency } from '../../api/client';
import type { SlotWaitStatistics, VesselConcurrency } from '../../types/models';
import { useLocale } from '../../context/LocaleContext';
import RefreshButton from '../shared/RefreshButton';
import { formatWaitDuration, waitDetailText, waitGateLabel } from '../../lib/missionWaits';

interface VesselConcurrencyCardProps {
  vesselId: string;
}

function statsLine(stats: SlotWaitStatistics, t: (text: string, vars?: Record<string, string | number | null | undefined>) => string): string {
  if (stats.missions === 0 || stats.p50Seconds === null) return t('No waits recorded');
  const vars = { p50: formatWaitDuration(stats.p50Seconds), p95: formatWaitDuration(stats.p95Seconds), max: formatWaitDuration(stats.maxSeconds), count: stats.missions };
  return stats.missions === 1
    ? t('p50 {{p50}}, p95 {{p95}}, max {{max}} (1 mission)', vars)
    : t('p50 {{p50}}, p95 {{p95}}, max {{max}} ({{count}} missions)', vars);
}

/**
 * The vessel's capacity in use: voyage slots against its limit, the voyages holding them, the missions waiting and on
 * what, and recent slot wait.
 */
export default function VesselConcurrencyCard({ vesselId }: VesselConcurrencyCardProps) {
  const { t } = useLocale();
  const [report, setReport] = useState<VesselConcurrency | null>(null);

  const load = useCallback(async () => {
    try { setReport(await getVesselConcurrency(vesselId)); } catch { setReport(null); }
  }, [vesselId]);

  useEffect(() => { void load(); }, [load]);

  if (!report) return null;
  const voyageLimit = report.maxParallelVoyages === 0
    ? t('{{used}} in use (no limit)', { used: report.voyageSlotsInUse })
    : t('{{used}} / {{limit}} in use', { used: report.voyageSlotsInUse, limit: report.maxParallelVoyages });
  const full = report.maxParallelVoyages > 0 && report.voyageSlotsInUse >= report.maxParallelVoyages;

  return (
    <div className="card vessel-concurrency-card" style={{ marginTop: '1rem' }} data-testid="vessel-concurrency">
      <div className="readiness-panel-header">
        <div>
          <h3 style={{ marginTop: 0 }} title={t('How much of this vessel\'s capacity is in use now and how long missions waited for a slot. Each running mission also needs a captain slot.')}>{t('Concurrency')}</h3>
          <div className="readiness-panel-meta">
            {t('{{running}} missions running; up to {{perVoyage}} per voyage', {
              running: report.runningMissions,
              perVoyage: report.maxParallelMissionsPerVoyage === 0 ? t('unlimited') : report.maxParallelMissionsPerVoyage,
            })}
          </div>
        </div>
        <div style={{ display: 'flex', gap: '0.5rem', alignItems: 'center' }}>
          <span className={`readiness-pill ${full ? 'warning' : 'ready'}`} title={t('Voyages holding one of this vessel\'s voyage slots, against Max parallel voyages.')}>{voyageLimit}</span>
          <RefreshButton onRefresh={load} title="Refresh concurrency" />
        </div>
      </div>
      <div className="detail-grid" style={{ marginTop: '0.5rem' }}>
        <div className="detail-field">
          <span className="detail-label" title={t('Per mission, the time spent waiting for capacity (voyage, mission, captain, endpoint, runtime and global slots) in the window.')}>{t('Slot wait, 24 hours')}</span>
          <span>{statsLine(report.slotWait24h, t)}</span>
        </div>
        <div className="detail-field">
          <span className="detail-label" title={t('Per mission, the time spent waiting for capacity in the last 7 days.')}>{t('Slot wait, 7 days')}</span>
          <span>{statsLine(report.slotWait7d, t)}</span>
        </div>
      </div>
      {report.holdingVoyages.length > 0 && (
        <>
          <h4 style={{ margin: '0.75rem 0 0.35rem' }}>{t('Voyages holding a slot')}</h4>
          <ul className="mission-hierarchy-children">
            {report.holdingVoyages.map(v => (
              <li key={v.voyageId ?? v.missionId ?? v.title}>
                {v.voyageId
                  ? <Link to={`/voyages/${encodeURIComponent(v.voyageId)}`}>{v.title}</Link>
                  : v.missionId ? <Link to={`/missions/${encodeURIComponent(v.missionId)}`}>{v.title}</Link> : <span>{v.title}</span>}
                <span className="text-dim">
                  {v.runningMissions === 0 && v.pendingMissions === 0
                    ? t('work landing or in review')
                    : t('{{running}} running, {{pending}} pending', { running: v.runningMissions, pending: v.pendingMissions })}
                </span>
              </li>
            ))}
          </ul>
        </>
      )}
      {report.waiting.length > 0 && (
        <>
          <h4 style={{ margin: '0.75rem 0 0.35rem' }}>
            {report.waitingCount > report.waiting.length
              ? t('Waiting ({{shown}} of {{count}})', { shown: report.waiting.length, count: report.waitingCount })
              : t('Waiting ({{count}})', { count: report.waitingCount })}
          </h4>
          <div className="table-wrap">
            <table className="table">
              <thead><tr><th>{t('Mission')}</th><th>{t('Waiting on')}</th><th>{t('For')}</th></tr></thead>
              <tbody>
                {report.waiting.map(w => (
                  <tr key={w.missionId}>
                    <td><Link to={`/missions/${encodeURIComponent(w.missionId)}`} title={w.title}>{w.title}</Link></td>
                    <td title={w.detail ? t(waitDetailText(w.detail)!) : undefined}>{t(waitGateLabel(w.gate))}</td>
                    <td>{formatWaitDuration(w.waitedSeconds)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
}
