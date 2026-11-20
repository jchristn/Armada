import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useNavigate } from 'react-router-dom';
import DockDetail from './DockDetail';
import { getDock, listCaptains, listVessels } from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import type { Dock } from '../types/models';

vi.mock('../api/client', () => ({
  getDock: vi.fn(),
  deleteDock: vi.fn(),
  listCaptains: vi.fn(),
  listVessels: vi.fn(),
}));

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast: vi.fn() }) }));

const emptyPage = { success: true, pageNumber: 1, pageSize: 1000, totalPages: 1, totalRecords: 0, totalMs: 1, objects: [] };

interface Deferred<T> { promise: Promise<T>; resolve: (value: T) => void }

function deferred<T>(): Deferred<T> {
  let resolve: (value: T) => void = () => {};
  const promise = new Promise<T>((res) => { resolve = res; });
  return { promise, resolve };
}

function dock(id: string, branchName: string): Dock {
  return {
    id, branchName, tenantId: 'default', active: true, vesselId: null, captainId: null,
    worktreePath: '/work/dock', createdUtc: '2026-10-01T00:00:00Z', lastUpdateUtc: '2026-10-01T00:00:00Z', gitAnchorsJson: null,
  } as unknown as Dock;
}

describe('DockDetail route load order', () => {
  const docks = new Map<string, Deferred<Dock>>();

  function gate(id: string): Deferred<Dock> {
    let entry = docks.get(id);
    if (!entry) {
      entry = deferred<Dock>();
      docks.set(id, entry);
    }
    return entry;
  }

  beforeEach(() => {
    docks.clear();
    vi.mocked(listCaptains).mockResolvedValue(emptyPage as never);
    vi.mocked(listVessels).mockResolvedValue(emptyPage as never);
    vi.mocked(getDock).mockImplementation((id: string) => gate(id).promise);
  });

  it('keeps the newer dock when the older request finishes last', async () => {
    function Harness() {
      const navigate = useNavigate();
      return (
        <>
          <button type="button" onClick={() => navigate('/docks/dck_b')}>Open route B</button>
          <Routes>
            <Route path="/docks/:id" element={<DockDetail />} />
          </Routes>
        </>
      );
    }

    render(
      <MemoryRouter initialEntries={['/docks/dck_a']}>
        <Harness />
      </MemoryRouter>,
    );
    fireEvent.click(screen.getByRole('button', { name: 'Open route B' }));

    await act(async () => { gate('dck_b').resolve(dock('dck_b', 'branch-b')); });
    expect(await screen.findByText('branch-b')).toBeInTheDocument();

    await act(async () => { gate('dck_a').resolve(dock('dck_a', 'branch-a')); });

    await waitFor(() => {
      expect(screen.getByText('branch-b')).toBeInTheDocument();
      expect(screen.queryByText('branch-a')).not.toBeInTheDocument();
    });
  });
});
