import { afterEach, expect, it, vi } from 'vitest';
import { configureClient, getStorage, listAllStorageHosts, refreshStorage } from './client';
afterEach(() => { configureClient({ baseUrl: '', headers: null, platform: null }); vi.unstubAllGlobals(); });
it('camelizes DTO properties and preserves numeric dictionary identifiers', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
    Database: { Tables: [{ Schema: 'MixedCase', Name: 'Missions' }] },
    Hosts: [{ HostId: 'admiral', Locations: [] }], Retention: { BuildSlots: 3 },
    History: [{ Categories: { DatabaseFiles: 123 }, Hosts: { HostABC: 456 }, Tables: { 'MixedCase.Missions': 789 } }],
  }), { status: 200 })));
  const result = await getStorage();
  expect(result.database.tables[0].name).toBe('Missions');
  expect(result.retention.BuildSlots).toBe(3);
  expect(result.history[0].categories.DatabaseFiles).toBe(123);
  expect(result.history[0].tables['MixedCase.Missions']).toBe(789);
  expect(result.history[0].hosts.HostABC).toBe(456);
});
it('requests exact counts explicitly and accepts asynchronous 202 responses', async () => {
  const fetchMock = vi.fn().mockResolvedValue(new Response('{}', { status: 202 }));
  vi.stubGlobal('fetch', fetchMock);
  await refreshStorage(true);
  expect(fetchMock.mock.calls[0][0]).toBe('/api/v1/storage/refresh');
  expect(JSON.parse(fetchMock.mock.calls[0][1].body)).toEqual({ exactCounts: true });
});
it('reads storage hosts through explicit bounded pages instead of trusting the first page', async () => {
  const page = (n: number, total: number) => new Response(JSON.stringify({ PageNumber: n, PageSize: 100, TotalPages: total, TotalRecords: 150, Objects: [{ HostId: 'h' + n, Locations: [], Volumes: [], Warnings: [] }] }), { status: 200 });
  const fetchMock = vi.fn().mockResolvedValueOnce(page(1, 2)).mockResolvedValueOnce(page(2, 2));
  vi.stubGlobal('fetch', fetchMock);
  const hosts = await listAllStorageHosts();
  expect(hosts.map(h => h.hostId)).toEqual(['h1', 'h2']);
  expect(fetchMock).toHaveBeenCalledTimes(2);
  expect(fetchMock.mock.calls[0][0]).toBe('/api/v1/storage/hosts/enumerate');
  expect(JSON.parse(fetchMock.mock.calls[1][1].body)).toEqual({ PageNumber: 2, PageSize: 100 });
});
