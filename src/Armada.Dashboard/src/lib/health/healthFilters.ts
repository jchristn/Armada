import type {
  VesselDivergenceFilter,
  VesselHealthEnumerateRequest,
  VesselHealthSortField,
  VesselHealthStatus,
} from '../../types/models';
import { HEALTH_STATUSES } from './healthText';

/**
 * Filter, sort and page state of the Health table, plus its URL query-string encoding. The URL is the
 * source of truth so Home KPI links, bookmarks and reloads restore the exact view.
 *
 * Query parameters (all optional; defaults are omitted from the URL):
 *   q=<name contains>          fleet=<fleet id>
 *   overall=Fail,Warn          deps=Fail            tests=Warn,Fail     (comma-separated statuses)
 *   dirty=yes|no               ci=yes|no            div=Ahead|Behind|Diverged|Even
 *   minBranches=<n>            maxBranches=<n>
 *   after=YYYY-MM-DD           before=YYYY-MM-DD    (last commit date range, inclusive days)
 *   sort=<SortBy>              dir=asc|desc         page=<n>
 * Page size is a per-browser preference (localStorage), not part of the URL.
 */

export type TriState = '' | 'yes' | 'no';

export interface HealthFilters {
  name: string;
  fleetId: string;
  overall: VesselHealthStatus[];
  deps: VesselHealthStatus[];
  tests: VesselHealthStatus[];
  dirty: TriState;
  ci: TriState;
  divergence: '' | VesselDivergenceFilter;
  minBranches: string;
  maxBranches: string;
  commitAfter: string;
  commitBefore: string;
  sortBy: VesselHealthSortField;
  sortDesc: boolean;
  page: number;
}

export const HEALTH_SORT_FIELDS: VesselHealthSortField[] = [
  'VesselName', 'FleetName', 'OverallStatus', 'Divergence', 'AheadOfDefault', 'BehindDefault', 'IsDirty',
  'BranchCount', 'StaleBranchCount', 'OutdatedCount', 'OutdatedMajorCount', 'VulnerableCount',
  'DependencyStatus', 'TestInfraStatus', 'CiStatus', 'LastCommitUtc', 'EvaluatedUtc',
];

export const DIVERGENCE_FILTERS: VesselDivergenceFilter[] = ['Ahead', 'Behind', 'Diverged', 'Even'];

export const DEFAULT_HEALTH_FILTERS: HealthFilters = {
  name: '',
  fleetId: '',
  overall: [],
  deps: [],
  tests: [],
  dirty: '',
  ci: '',
  divergence: '',
  minBranches: '',
  maxBranches: '',
  commitAfter: '',
  commitBefore: '',
  sortBy: 'OverallStatus',
  sortDesc: false,
  page: 1,
};

/** Names of every query parameter the Health table owns (anything else, such as `tab`, is preserved). */
export const HEALTH_QUERY_KEYS = [
  'q', 'fleet', 'overall', 'deps', 'tests', 'dirty', 'ci', 'div', 'minBranches', 'maxBranches',
  'after', 'before', 'sort', 'dir', 'page',
];

const DATE_RE = /^\d{4}-\d{2}-\d{2}$/;

function parseStatuses(raw: string | null): VesselHealthStatus[] {
  if (!raw) return [];
  const seen = new Set<VesselHealthStatus>();
  for (const part of raw.split(',')) {
    const match = HEALTH_STATUSES.find((s) => s.toLowerCase() === part.trim().toLowerCase());
    if (match) seen.add(match);
  }
  return HEALTH_STATUSES.filter((s) => seen.has(s));
}

function parseTri(raw: string | null): TriState {
  const v = (raw ?? '').toLowerCase();
  if (v === 'yes' || v === 'true' || v === '1') return 'yes';
  if (v === 'no' || v === 'false' || v === '0') return 'no';
  return '';
}

function parseNonNegativeInt(raw: string | null): string {
  if (!raw || !/^\d+$/.test(raw.trim())) return '';
  return String(parseInt(raw.trim(), 10));
}

function parseDate(raw: string | null): string {
  return raw && DATE_RE.test(raw) ? raw : '';
}

/** Reads filters from a query string, ignoring unknown or invalid values. */
export function filtersFromQuery(params: URLSearchParams): HealthFilters {
  const sortRaw = params.get('sort');
  const sortBy = HEALTH_SORT_FIELDS.find((f) => f.toLowerCase() === (sortRaw ?? '').toLowerCase()) ?? DEFAULT_HEALTH_FILTERS.sortBy;
  const dirRaw = (params.get('dir') ?? '').toLowerCase();
  const divRaw = params.get('div') ?? '';
  const divergence = DIVERGENCE_FILTERS.find((d) => d.toLowerCase() === divRaw.toLowerCase()) ?? '';
  const pageRaw = parseInt(params.get('page') ?? '', 10);
  return {
    name: params.get('q') ?? '',
    fleetId: params.get('fleet') ?? '',
    overall: parseStatuses(params.get('overall')),
    deps: parseStatuses(params.get('deps')),
    tests: parseStatuses(params.get('tests')),
    dirty: parseTri(params.get('dirty')),
    ci: parseTri(params.get('ci')),
    divergence,
    minBranches: parseNonNegativeInt(params.get('minBranches')),
    maxBranches: parseNonNegativeInt(params.get('maxBranches')),
    commitAfter: parseDate(params.get('after')),
    commitBefore: parseDate(params.get('before')),
    sortBy,
    sortDesc: dirRaw === 'desc' ? true : dirRaw === 'asc' ? false : (sortRaw ? false : DEFAULT_HEALTH_FILTERS.sortDesc),
    page: Number.isFinite(pageRaw) && pageRaw > 1 ? pageRaw : 1,
  };
}

/**
 * Writes filters into a copy of `base`, removing parameters that are at their defaults and keeping
 * parameters the table does not own.
 */
export function filtersToQuery(filters: HealthFilters, base?: URLSearchParams): URLSearchParams {
  const next = new URLSearchParams(base ?? undefined);
  for (const key of HEALTH_QUERY_KEYS) next.delete(key);
  const name = filters.name.trim();
  if (name) next.set('q', name);
  if (filters.fleetId) next.set('fleet', filters.fleetId);
  if (filters.overall.length) next.set('overall', filters.overall.join(','));
  if (filters.deps.length) next.set('deps', filters.deps.join(','));
  if (filters.tests.length) next.set('tests', filters.tests.join(','));
  if (filters.dirty) next.set('dirty', filters.dirty);
  if (filters.ci) next.set('ci', filters.ci);
  if (filters.divergence) next.set('div', filters.divergence);
  if (filters.minBranches) next.set('minBranches', filters.minBranches);
  if (filters.maxBranches) next.set('maxBranches', filters.maxBranches);
  if (filters.commitAfter) next.set('after', filters.commitAfter);
  if (filters.commitBefore) next.set('before', filters.commitBefore);
  if (filters.sortBy !== DEFAULT_HEALTH_FILTERS.sortBy || filters.sortDesc !== DEFAULT_HEALTH_FILTERS.sortDesc) {
    next.set('sort', filters.sortBy);
    next.set('dir', filters.sortDesc ? 'desc' : 'asc');
  }
  if (filters.page > 1) next.set('page', String(filters.page));
  return next;
}

/** True when any filter (not sort or page) differs from the defaults. */
export function hasActiveFilters(filters: HealthFilters): boolean {
  return Boolean(
    filters.name.trim() || filters.fleetId || filters.overall.length || filters.deps.length || filters.tests.length
    || filters.dirty || filters.ci || filters.divergence || filters.minBranches || filters.maxBranches
    || filters.commitAfter || filters.commitBefore,
  );
}

/** Start of a local calendar day as an ISO UTC string. */
function localDayStartIso(day: string, addDays = 0): string {
  const [y, m, d] = day.split('-').map((part) => parseInt(part, 10));
  return new Date(y, m - 1, d + addDays, 0, 0, 0, 0).toISOString();
}

function triToBool(value: TriState): boolean | undefined {
  if (value === 'yes') return true;
  if (value === 'no') return false;
  return undefined;
}

/**
 * Builds the server-side enumerate body. Date bounds are inclusive local days: "after" becomes the
 * start of that day and "before" the start of the following day (the server bounds are exclusive).
 */
export function buildEnumerateRequest(filters: HealthFilters, pageSize: number): VesselHealthEnumerateRequest {
  const req: VesselHealthEnumerateRequest = {
    PageNumber: Math.max(1, filters.page),
    PageSize: Math.min(500, Math.max(1, pageSize)),
    SortBy: filters.sortBy,
    SortDescending: filters.sortDesc,
  };
  const name = filters.name.trim();
  if (name) req.NameContains = name;
  if (filters.fleetId) req.FleetId = filters.fleetId;
  if (filters.overall.length) req.OverallStatus = [...filters.overall];
  if (filters.deps.length) req.DependencyStatus = [...filters.deps];
  if (filters.tests.length) req.TestInfraStatus = [...filters.tests];
  const dirty = triToBool(filters.dirty);
  if (dirty !== undefined) req.IsDirty = dirty;
  const ci = triToBool(filters.ci);
  if (ci !== undefined) req.HasCiConfig = ci;
  if (filters.divergence) req.Divergence = filters.divergence;
  if (filters.minBranches !== '') req.MinBranchCount = parseInt(filters.minBranches, 10);
  if (filters.maxBranches !== '') req.MaxBranchCount = parseInt(filters.maxBranches, 10);
  if (filters.commitAfter) req.LastCommitAfterUtc = localDayStartIso(filters.commitAfter);
  if (filters.commitBefore) req.LastCommitBeforeUtc = localDayStartIso(filters.commitBefore, 1);
  return req;
}

/** Toggles a sort header: same field flips direction, a new field starts ascending. Resets to page 1. */
export function toggleSort(filters: HealthFilters, field: VesselHealthSortField): HealthFilters {
  if (filters.sortBy === field) return { ...filters, sortDesc: !filters.sortDesc, page: 1 };
  return { ...filters, sortBy: field, sortDesc: false, page: 1 };
}

/** Deep-link helpers used by Home KPI tiles and the summary strip. */
export function healthUrl(partial: Partial<HealthFilters>): string {
  const query = filtersToQuery({ ...DEFAULT_HEALTH_FILTERS, ...partial }).toString();
  return query ? `/vessels/health?${query}` : '/vessels/health';
}
