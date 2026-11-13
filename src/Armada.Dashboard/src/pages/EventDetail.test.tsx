import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useNavigate } from 'react-router-dom';
import EventDetail from './EventDetail';
import { getEvent, listCaptains, listVessels } from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import type { ArmadaEvent } from '../types/models';

vi.mock('../api/client', () => ({
  getEvent: vi.fn(),
  deleteEventsBatch: vi.fn(),
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

function eventRecord(id: string, message: string): ArmadaEvent {
  return {
    id, message, eventType: 'mission.completed', payload: null, tenantId: 'default', createdUtc: '2026-10-01T00:00:00Z',
  } as unknown as ArmadaEvent;
}

describe('EventDetail route load order', () => {
  const events = new Map<string, Deferred<ArmadaEvent>>();

  function gate(id: string): Deferred<ArmadaEvent> {
    let entry = events.get(id);
    if (!entry) {
      entry = deferred<ArmadaEvent>();
      events.set(id, entry);
    }
    return entry;
  }

  beforeEach(() => {
    events.clear();
    vi.mocked(listCaptains).mockResolvedValue(emptyPage as never);
    vi.mocked(listVessels).mockResolvedValue(emptyPage as never);
    vi.mocked(getEvent).mockImplementation((id: string) => gate(id).promise);
  });

  it('keeps the newer event when the older request finishes last', async () => {
    function Harness() {
      const navigate = useNavigate();
      return (
        <>
          <button type="button" onClick={() => navigate('/events/evt_b')}>Open route B</button>
          <Routes>
            <Route path="/events/:id" element={<EventDetail />} />
          </Routes>
        </>
      );
    }

    render(
      <MemoryRouter initialEntries={['/events/evt_a']}>
        <Harness />
      </MemoryRouter>,
    );
    fireEvent.click(screen.getByRole('button', { name: 'Open route B' }));

    await act(async () => { gate('evt_b').resolve(eventRecord('evt_b', 'Event Beta happened')); });
    expect(await screen.findByText('Event Beta happened')).toBeInTheDocument();

    await act(async () => { gate('evt_a').resolve(eventRecord('evt_a', 'Event Alpha happened')); });

    await waitFor(() => {
      expect(screen.getByText('Event Beta happened')).toBeInTheDocument();
      expect(screen.queryByText('Event Alpha happened')).not.toBeInTheDocument();
    });
  });
});
