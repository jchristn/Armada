import { healthUrl } from './healthFilters';

/**
 * Home deep links from the vessel health KPI tiles (dashboard and mobile). Vulnerabilities have no status filter in
 * the enumerate DTO, so that tile sorts instead.
 */
export const HEALTH_KPI_LINKS = {
  failing: healthUrl({ overall: ['Fail'] }),
  outdatedMajors: healthUrl({ deps: ['Fail'], sortBy: 'OutdatedMajorCount', sortDesc: true }),
  vulnerable: healthUrl({ sortBy: 'VulnerableCount', sortDesc: true }),
};
