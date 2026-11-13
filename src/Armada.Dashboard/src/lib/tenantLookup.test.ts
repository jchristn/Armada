import { lookupAllTenants, TENANT_LOOKUP_PAGE_SIZE } from './tenantLookup';
import type { TenantLookupResult } from '../types/models';

describe('lookupAllTenants', () => {
  it('reads every page until totalPages instead of trusting the first page', async () => {
    const pages: TenantLookupResult[] = [
      { tenants: [{ id: 'ten_a', name: 'Alpha' }], pageNumber: 1, pageSize: 1, totalPages: 3, totalRecords: 3 },
      { tenants: [{ id: 'ten_b', name: 'Bravo' }], pageNumber: 2, pageSize: 1, totalPages: 3, totalRecords: 3 },
      { tenants: [{ id: 'ten_c', name: 'Charlie' }], pageNumber: 3, pageSize: 1, totalPages: 3, totalRecords: 3 },
    ];
    const lookup = vi.fn(async (_email: string, page: { pageNumber: number; pageSize: number }) => pages[page.pageNumber - 1]);
    const tenants = await lookupAllTenants('me@x.com', lookup);
    expect(tenants.map(t => t.id)).toEqual(['ten_a', 'ten_b', 'ten_c']);
    expect(lookup).toHaveBeenCalledTimes(3);
    expect(lookup).toHaveBeenNthCalledWith(1, 'me@x.com', { pageNumber: 1, pageSize: TENANT_LOOKUP_PAGE_SIZE });
  });

  it('stops on an empty page', async () => {
    const lookup = vi.fn(async () => ({ tenants: [], pageNumber: 1, pageSize: 100, totalPages: 0, totalRecords: 0 }));
    expect(await lookupAllTenants('nobody@x.com', lookup)).toEqual([]);
    expect(lookup).toHaveBeenCalledTimes(1);
  });
});
