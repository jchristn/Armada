import { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { getVesselHealthSummary } from '../../../api/client';
import { useLocale } from '../../../context/LocaleContext';
import type { VesselHealthSummary } from '../../../types/models';
import { healthUrl } from '../../../lib/health/healthFilters';
import { formatCount } from '../../../lib/health/healthText';
import './vesselHealth.css';

/** Home deep links. Vulnerabilities have no status filter in the enumerate DTO, so that tile sorts instead. */
export const HEALTH_KPI_LINKS = {
  failing: healthUrl({ overall: ['Fail'] }),
  outdatedMajors: healthUrl({ deps: ['Fail'], sortBy: 'OutdatedMajorCount', sortDesc: true }),
  vulnerable: healthUrl({ sortBy: 'VulnerableCount', sortDesc: true }),
};

interface HealthKpiCardsProps {
  /** Increment to reload (for example from the Home refresh button). */
  refreshToken?: number;
}

/**
 * Home KPI tiles for vessel health: failing vessels, vessels with outdated major versions, and vessels with
 * high or critical vulnerabilities. Each tile opens the Health tab with a matching filter. Renders nothing
 * when the summary endpoint is unavailable, so Home never breaks because of it.
 */
export default function HealthKpiCards({ refreshToken = 0 }: HealthKpiCardsProps) {
  const navigate = useNavigate();
  const { t, locale } = useLocale();
  const [summary, setSummary] = useState<VesselHealthSummary | null>(null);

  const load = useCallback(async () => {
    try {
      setSummary(await getVesselHealthSummary());
    } catch {
      setSummary(null);
    }
  }, []);

  useEffect(() => { void load(); }, [load, refreshToken]);

  if (!summary || summary.totalVessels === 0) return null;

  const tiles = [
    {
      key: 'failing',
      label: t('Vessels failing health'),
      value: summary.fail,
      detail: t('{{count}} warn, {{unknown}} not evaluated', { count: formatCount(locale, summary.warn), unknown: formatCount(locale, summary.notEvaluated) }),
      title: t('Open the Health tab filtered to failing vessels'),
      to: HEALTH_KPI_LINKS.failing,
    },
    {
      key: 'outdatedMajors',
      label: t('Outdated majors'),
      value: summary.outdatedMajorVessels,
      detail: t('Vessels with a dependency a major version behind'),
      title: t('Open the Health tab filtered to failing dependencies'),
      to: HEALTH_KPI_LINKS.outdatedMajors,
    },
    {
      key: 'vulnerable',
      label: t('High/critical vulnerabilities'),
      value: summary.highOrCriticalVulnerabilityVessels,
      detail: t('Vessels with a high or critical advisory'),
      title: t('Open the Health tab sorted by vulnerable packages'),
      to: HEALTH_KPI_LINKS.vulnerable,
    },
  ];

  return (
    <div className="cards vh-kpi-cards" aria-label={t('Vessel health')}>
      {tiles.map((tile) => (
        <a
          key={tile.key}
          href={`/dashboard${tile.to}`}
          className="card clickable"
          title={tile.title}
          style={{ textDecoration: 'none', color: 'inherit' }}
          onClick={(e) => { e.preventDefault(); navigate(tile.to); }}
        >
          <div className="card-label">{tile.label}</div>
          <div className={`card-value${tile.value === 0 ? ' vh-kpi-zero' : ''}`}>{formatCount(locale, tile.value)}</div>
          <div className="card-detail text-dim" style={{ fontSize: '0.8rem' }}>{tile.detail}</div>
        </a>
      ))}
    </div>
  );
}
