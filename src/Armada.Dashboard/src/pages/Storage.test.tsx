import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import Storage from './Storage';
import { getStorage, listAllStorageHosts, refreshStorage } from '../api/client';
import { storageMessages } from '../i18n/storageMessages';
import type { StorageHostReport, StorageSnapshot } from '../types/storage';

const auth = { isAdmin: true };
const translate = (key: string, args?: Record<string, unknown>) => Object.entries(args ?? {}).reduce((v, [k, value]) => v.split(`{{${k}}}`).join(String(value)), storageMessages[key] ?? key);
vi.mock('../context/AuthContext', () => ({ useAuth: () => auth }));
vi.mock('../context/LocaleContext', () => ({ useLocale: () => ({ t: translate, locale: 'en', formatDateTime: (v: string) => v }) }));
vi.mock('../api/client', () => ({ getStorage: vi.fn(), refreshStorage: vi.fn(), listAllStorageHosts: vi.fn() }));
const fixture = (): StorageSnapshot => ({
  measuredUtc: '2026-10-16T12:00:00Z', collecting: false, lastError: null, intervalSeconds: 300, historyCapacity: 288,
  database: { provider: 'Sqlite', status: 'Complete', measuredUtc: '2026-10-16T12:00:00Z', allocatedBytes: 4096, unusedBytes: 0, warnings: [], tables: [
    { schema: 'main', name: 'missions', rows: 500, countKind: 'Exact', dataBytes: 2048, indexBytes: 1024, warning: null },
  ] },
  hostCount: 1, freshHostCount: 1, observedFileBytes: 1024,
  history: [], retention: { JobDays: 30, BuildSlots: 3 },
});
const hostsFixture = (): StorageHostReport[] => [{ hostId: 'admiral', name: 'Test host', measuredUtc: '2026-10-16T12:00:00Z', stale: false, status: 'Complete', warnings: [],
    volumes: [{ path: '/', totalBytes: 1000, availableBytes: 250, status: 'Complete' }],
    locations: [{ category: 'Logs/missions', path: '/logs/missions', entityId: 'msn_1', activity: null, bytes: 1024, fileCount: 1, oldestWriteUtc: null, newestWriteUtc: null,
      status: 'Partial', warnings: ['BudgetExceeded'], largestFiles: [{ path: '/logs/missions/msn_1.log', bytes: 1024, lastWriteUtc: '2026-10-16T12:00:00Z' }] }],
  }];
const mount = () => render(<MemoryRouter><Storage /></MemoryRouter>);
beforeEach(() => { vi.clearAllMocks(); localStorage.clear(); auth.isAdmin = true; vi.mocked(getStorage).mockResolvedValue(fixture()); vi.mocked(refreshStorage).mockResolvedValue(fixture()); vi.mocked(listAllStorageHosts).mockResolvedValue(hostsFixture()); });

it('denies non-admin access without requesting inventory', () => {
  auth.isAdmin = false; mount();
  expect(screen.getByRole('alert')).toHaveTextContent('system administrators');
  expect(getStorage).not.toHaveBeenCalled();
});
it('shows measured categories, partial warnings and explicit exact counts', async () => {
  mount();
  expect(await screen.findByText('/logs/missions')).toBeInTheDocument();
  expect(screen.getByText('The scan reached its entry or time limit. Reported bytes are partial.')).toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Count records exactly' }));
  await waitFor(() => expect(refreshStorage).toHaveBeenCalledWith(true));
  fireEvent.click(screen.getByRole('tab', { name: 'Database' }));
  expect(await screen.findByText('missions')).toBeInTheDocument();
  expect(screen.getByText('Exact')).toBeInTheDocument();
  fireEvent.click(screen.getByRole('tab', { name: 'Volumes' }));
  expect(screen.getByText('75%')).toBeInTheDocument();
});
it('filters the complete cached rows and displays retention', async () => {
  mount(); await screen.findByText('/logs/missions');
  fireEvent.change(screen.getByRole('textbox', { name: 'Filter cached rows' }), { target: { value: 'no-match' } });
  expect(screen.getByText('No measurements match this view.')).toBeInTheDocument();
  fireEvent.click(screen.getByRole('tab', { name: 'Retention' }));
  expect(screen.getByText('Finished background jobs')).toBeInTheDocument();
  expect(screen.getByText('30 days')).toBeInTheDocument();
});
it('keeps the previous snapshot visible on an API error', async () => {
  mount(); await screen.findByText('/logs/missions');
  vi.mocked(getStorage).mockRejectedValue(new Error('offline'));
  fireEvent.click(screen.getByRole('button', { name: 'Refresh view' }));
  expect(await screen.findByRole('alert')).toHaveTextContent('Previous measurements');
  expect(screen.getByText('/logs/missions')).toBeInTheDocument();
});
it('explains a first measurement pending', async () => {
  const pending = fixture(); pending.measuredUtc = null; pending.hostCount = 0; pending.collecting = true; vi.mocked(listAllStorageHosts).mockResolvedValue([]);
  vi.mocked(getStorage).mockResolvedValue(pending);
  mount(); expect(await screen.findByText(/The first background measurement/)).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Collecting...' })).toBeDisabled();
});

it('shows SQLite logical bytes and exact rows when native table allocation is unavailable', async () => {
  const data = fixture();
  data.database.status = 'Partial';
  data.database.warnings = ['TableAllocationUnavailable'];
  data.database.tables[0] = { schema: 'main', name: 'missions', rows: 500, countKind: 'Exact', logicalBytes: 3072, dataBytes: null, indexBytes: null, warning: null };
  vi.mocked(getStorage).mockResolvedValue(data);
  mount(); await screen.findByText('/logs/missions');
  fireEvent.click(screen.getByRole('tab', { name: 'Database' }));
  expect(screen.getByText('Logical data bytes')).toBeInTheDocument();
  expect(screen.getByText('3 KiB')).toBeInTheDocument();
  expect(screen.getByText('500')).toBeInTheDocument();
  expect(screen.getByText(/This SQLite runtime cannot report/)).toBeInTheDocument();
  expect(refreshStorage).not.toHaveBeenCalled();
});
