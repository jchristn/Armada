import type { TenantListEntry, TenantLookupResult } from '../types/models';

/** Page size the login tenant pickers read with (POST /api/v1/tenants/lookup allows 1 to 1000). */
export const TENANT_LOOKUP_PAGE_SIZE = 100;

export type TenantLookupPage = (email: string, page: { pageNumber: number; pageSize: number }) => Promise<TenantLookupResult>;

/**
 * Every tenant where the email exists, read one bounded page at a time. The login pickers need the whole list, so
 * they iterate the pages explicitly until totalPages instead of trusting the first page.
 */
export async function lookupAllTenants(email: string, lookup: TenantLookupPage): Promise<TenantListEntry[]> {
  const tenants: TenantListEntry[] = [];
  for (let pageNumber = 1; ; pageNumber++) {
    const page = await lookup(email, { pageNumber, pageSize: TENANT_LOOKUP_PAGE_SIZE });
    const objects = page?.tenants ?? [];
    tenants.push(...objects);
    if (objects.length === 0 || pageNumber >= (page?.totalPages ?? 1)) break;
  }
  return tenants;
}
