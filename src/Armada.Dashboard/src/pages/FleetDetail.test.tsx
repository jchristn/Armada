import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useNavigate } from 'react-router-dom';
import FleetDetail from './FleetDetail';
import { getFleet, listPipelines, listVessels } from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import type { Fleet } from '../types/models';

vi.mock('../api/client', () => ({
  getFleet: vi.fn(),
  listVessels: vi.fn(),
  listPipelines: vi.fn(),
  createFleet: vi.fn(),
  updateFleet: vi.fn(),
  deleteFleet: vi.fn(),
  apiErrorMessage: vi.fn(),
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

function fleet(id: string, name: string): Fleet {
  return { id, name, description: null, active: true, createdUtc: '2026-10-01T00:00:00Z', lastUpdateUtc: '2026-10-01T00:00:00Z' } as unknown as Fleet;
}

describe('FleetDetail route load order', () => {
  const fleets = new Map<string, Deferred<Fleet>>();

  function gate(id: string): Deferred<Fleet> {
    let entry = fleets.get(id);
    if (!entry) {
      entry = deferred<Fleet>();
      fleets.set(id, entry);
    }
    return entry;
  }

  beforeEach(() => {
    fleets.clear();
    vi.mocked(listVessels).mockResolvedValue(emptyPage as never);
    vi.mocked(listPipelines).mockResolvedValue(emptyPage as never);
    vi.mocked(getFleet).mockImplementation((id: string) => gate(id).promise);
  });

  it('keeps the newer fleet when the older request finishes last', async () => {
    function Harness() {
      const navigate = useNavigate();
      return (
        <>
          <button type="button" onClick={() => navigate('/fleets/flt_b')}>Open route B</button>
          <Routes>
            <Route path="/fleets/:id" element={<FleetDetail />} />
          </Routes>
        </>
      );
    }

    render(
      <MemoryRouter initialEntries={['/fleets/flt_a']}>
        <Harness />
      </MemoryRouter>,
    );
    fireEvent.click(screen.getByRole('button', { name: 'Open route B' }));

    await act(async () => { gate('flt_b').resolve(fleet('flt_b', 'Fleet Beta')); });
    expect(await screen.findByRole('heading', { name: 'Fleet Beta' })).toBeInTheDocument();

    await act(async () => { gate('flt_a').resolve(fleet('flt_a', 'Fleet Alpha')); });

    await waitFor(() => {
      expect(screen.getByRole('heading', { name: 'Fleet Beta' })).toBeInTheDocument();
      expect(screen.queryByRole('heading', { name: 'Fleet Alpha' })).not.toBeInTheDocument();
    });
  });
});
