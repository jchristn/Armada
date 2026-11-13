import { fireEvent, screen, waitFor } from '@testing-library/react-native';
import * as client from '@dashboard/api/client';
import type { StorageHostReport, StorageSnapshot } from '@dashboard/types/storage';
import { StorageTab } from '../screens/system/StorageTab';
import { renderW4, resetW4 } from '../test/w4';

jest.mock('@dashboard/api/client', () => require('../test/w4Client').autoMockClient());
const api = client as jest.Mocked<typeof client>;
const snapshot: StorageSnapshot = {
  measuredUtc: '2026-10-16T12:00:00Z', collecting: false, lastError: null, intervalSeconds: 300, historyCapacity: 288,
  database: { provider: 'Sqlite', measuredUtc: '2026-10-16T12:00:00Z', allocatedBytes: 4096, unusedBytes: 0, status: 'Complete', tables: [], warnings: [] },
  hostCount: 1, freshHostCount: 1, observedFileBytes: 42,
  history: [], retention: {},
};
const hosts: StorageHostReport[] = [{ hostId: 'admiral', name: 'Test host', measuredUtc: '2026-10-16T12:00:00Z', stale: false, status: 'Complete', volumes: [], warnings: [], locations: [
    { category: 'Logs/missions', path: '/data/missions', entityId: null, activity: null, bytes: 42, fileCount: 1, oldestWriteUtc: null, newestWriteUtc: null, status: 'Complete', warnings: [], largestFiles: [] },
  ] }];
beforeEach(async () => { await resetW4(); jest.clearAllMocks(); api.getStorage.mockResolvedValue(snapshot); api.refreshStorage.mockResolvedValue(snapshot); api.listAllStorageHosts.mockResolvedValue(hosts); });
it('shows cached inventory and only requests exact counts on explicit action', async () => {
  await renderW4(<StorageTab />);
  expect(await screen.findByText('Test host: /data/missions')).toBeOnTheScreen();
  expect(api.refreshStorage).not.toHaveBeenCalled();
  expect(api.listAllStorageHosts).toHaveBeenCalled();
  await fireEvent.press(screen.getByText('Count records exactly'));
  await waitFor(() => expect(api.refreshStorage).toHaveBeenCalledWith(true));
});
it('denies a tenant administrator without requesting storage', async () => {
  await renderW4(<StorageTab />, 'tenantAdmin');
  expect(await screen.findByText('Storage is available to system administrators only.')).toBeOnTheScreen();
  expect(api.getStorage).not.toHaveBeenCalled();
});
