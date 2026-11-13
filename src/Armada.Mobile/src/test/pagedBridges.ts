/**
 * Screens read Harbors, model endpoints, and CLI rules one bounded page at a time through the enumerate* helpers. Tests
 * keep configuring the older array helpers (listHarbors, listModelEndpoints, listCliPermissionRules); these bridges make
 * each enumerate* mock answer with the matching array mock's value as a one-page result, unless the test configures
 * the enumerate* mock itself. No app imports: the client mock factories call this while the client is still loading.
 */
const BRIDGES: [string, string][] = [
  ['enumerateHarbors', 'listHarbors'],
  ['enumerateModelEndpoints', 'listModelEndpoints'],
  ['enumerateCliPermissionRules', 'listCliPermissionRules'],
];

function asPage(value: unknown): unknown {
  if (value && !Array.isArray(value) && typeof value === 'object' && 'objects' in value) return value;
  const objects = Array.isArray(value) ? value : [];
  return { success: true, pageNumber: 1, pageSize: 1000, totalPages: objects.length > 0 ? 1 : 0, totalRecords: objects.length, totalMs: 0, objects };
}

export function addPagedBridges(mocks: Record<string, unknown>): void {
  for (const [paged, array] of BRIDGES) {
    const source = mocks[array] as ((...args: unknown[]) => Promise<unknown>) | undefined;
    if (typeof source !== 'function') continue;
    mocks[paged] = jest.fn(async (...args: unknown[]) => asPage(await source(...args.slice(0, 1))));
  }
}
