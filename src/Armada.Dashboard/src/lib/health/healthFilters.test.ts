import { describe, expect, it } from 'vitest';
import {
  DEFAULT_HEALTH_FILTERS,
  buildEnumerateRequest,
  filtersFromQuery,
  filtersToQuery,
  hasActiveFilters,
  healthUrl,
  toggleSort,
  type HealthFilters,
} from './healthFilters';

const full: HealthFilters = {
  name: 'api',
  fleetId: 'flt_123',
  overall: ['Warn', 'Fail'],
  deps: ['Fail'],
  tests: ['Warn', 'Unknown'],
  dirty: 'yes',
  ci: 'no',
  divergence: 'Behind',
  minBranches: '2',
  maxBranches: '10',
  commitAfter: '2026-01-01',
  commitBefore: '2026-02-15',
  sortBy: 'BehindDefault',
  sortDesc: true,
  page: 3,
};

describe('health filter URL round trip', () => {
  it('state -> query -> state preserves every field', () => {
    const query = filtersToQuery(full);
    expect(filtersFromQuery(new URLSearchParams(query.toString()))).toEqual(full);
  });

  it('omits defaults so a clean view has a clean URL', () => {
    expect(filtersToQuery(DEFAULT_HEALTH_FILTERS).toString()).toBe('');
    expect(filtersFromQuery(new URLSearchParams(''))).toEqual(DEFAULT_HEALTH_FILTERS);
  });

  it('uses readable parameter names', () => {
    const query = filtersToQuery(full);
    expect(query.get('overall')).toBe('Warn,Fail');
    expect(query.get('q')).toBe('api');
    expect(query.get('dirty')).toBe('yes');
    expect(query.get('div')).toBe('Behind');
    expect(query.get('sort')).toBe('BehindDefault');
    expect(query.get('dir')).toBe('desc');
    expect(query.get('page')).toBe('3');
  });

  it('keeps parameters it does not own and replaces the ones it does', () => {
    const base = new URLSearchParams('tab=health&overall=Pass&foo=bar');
    const query = filtersToQuery({ ...DEFAULT_HEALTH_FILTERS, overall: ['Fail'] }, base);
    expect(query.get('tab')).toBe('health');
    expect(query.get('foo')).toBe('bar');
    expect(query.getAll('overall')).toEqual(['Fail']);
  });

  it('ignores invalid values from hand-edited URLs', () => {
    const parsed = filtersFromQuery(new URLSearchParams('overall=fail,bogus,WARN&div=Sideways&sort=Nope&page=-4&minBranches=x&after=yesterday&dirty=maybe'));
    expect(parsed.overall).toEqual(['Warn', 'Fail']);
    expect(parsed.divergence).toBe('');
    expect(parsed.sortBy).toBe(DEFAULT_HEALTH_FILTERS.sortBy);
    expect(parsed.page).toBe(1);
    expect(parsed.minBranches).toBe('');
    expect(parsed.commitAfter).toBe('');
    expect(parsed.dirty).toBe('');
  });

  it('builds Home deep links the table reads back', () => {
    const url = healthUrl({ overall: ['Fail'] });
    expect(url).toBe('/vessels/health?overall=Fail');
    const parsed = filtersFromQuery(new URLSearchParams(url.split('?')[1]));
    expect(parsed.overall).toEqual(['Fail']);
    expect(healthUrl({})).toBe('/vessels/health');
  });

  it('reports whether any filter is active', () => {
    expect(hasActiveFilters(DEFAULT_HEALTH_FILTERS)).toBe(false);
    expect(hasActiveFilters({ ...DEFAULT_HEALTH_FILTERS, sortBy: 'VesselName', page: 4 })).toBe(false);
    expect(hasActiveFilters({ ...DEFAULT_HEALTH_FILTERS, ci: 'yes' })).toBe(true);
  });
});

describe('enumerate request', () => {
  it('maps every filter to the PascalCase DTO', () => {
    const req = buildEnumerateRequest(full, 50);
    expect(req).toMatchObject({
      PageNumber: 3,
      PageSize: 50,
      SortBy: 'BehindDefault',
      SortDescending: true,
      NameContains: 'api',
      FleetId: 'flt_123',
      OverallStatus: ['Warn', 'Fail'],
      DependencyStatus: ['Fail'],
      TestInfraStatus: ['Warn', 'Unknown'],
      IsDirty: true,
      HasCiConfig: false,
      Divergence: 'Behind',
      MinBranchCount: 2,
      MaxBranchCount: 10,
    });
    // Inclusive local days become exclusive UTC bounds: start of "after", start of the day after "before".
    expect(req.LastCommitAfterUtc).toBe(new Date(2026, 0, 1).toISOString());
    expect(req.LastCommitBeforeUtc).toBe(new Date(2026, 1, 16).toISOString());
  });

  it('sends only paging and sort for the default view', () => {
    expect(buildEnumerateRequest(DEFAULT_HEALTH_FILTERS, 25)).toEqual({
      PageNumber: 1,
      PageSize: 25,
      SortBy: 'OverallStatus',
      SortDescending: false,
    });
  });

  it('clamps page size to the server range and keeps a zero branch bound', () => {
    expect(buildEnumerateRequest(DEFAULT_HEALTH_FILTERS, 9999).PageSize).toBe(500);
    expect(buildEnumerateRequest({ ...DEFAULT_HEALTH_FILTERS, minBranches: '0' }, 25).MinBranchCount).toBe(0);
  });

  it('sort toggling flips direction on the same column and resets to page 1', () => {
    const onPage = { ...DEFAULT_HEALTH_FILTERS, page: 5 };
    const sameField = toggleSort(onPage, 'OverallStatus');
    expect(sameField.sortDesc).toBe(true);
    expect(sameField.page).toBe(1);
    const newField = toggleSort({ ...onPage, sortDesc: true }, 'VesselName');
    expect(newField).toMatchObject({ sortBy: 'VesselName', sortDesc: false, page: 1 });
  });
});
