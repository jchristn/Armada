import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import Events from './Events';
import { listCaptains, listEvents, listVessels } from '../api/client';
import type { ArmadaEvent, EnumerationResult } from '../types/models';

const translate = (text: string, params?: Record<string, string | number | null | undefined>) => {
  if (!params) return text;
  return Object.entries(params).reduce(
    (current, [key, value]) => current.split(`{{${key}}}`).join(value == null ? '' : String(value)),
    text,
  );
};
const localeValue = { t: translate, formatDateTime: (v: string | null | undefined) => v ?? '', formatRelativeTime: (v: string | null | undefined) => v ?? '' };
const notifications = { pushToast: vi.fn() };

vi.mock('../api/client', () => ({
  listEvents: vi.fn(),
  deleteEventsBatch: vi.fn(),
  listCaptains: vi.fn(),
  listVessels: vi.fn(),
}));
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../context/NotificationContext', () => ({ useNotifications: () => notifications }));
vi.mock('../lib/useAutoRefresh', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../lib/useAutoRefresh')>()),
  useAutoRefresh: () => ({ seconds: 0, setSeconds: vi.fn() }),
}));
vi.mock('../components/shared/UserScopeFilter', () => ({ default: () => null }));

function event(id: string, eventType: string): ArmadaEvent {
  return {
    id,
    tenantId: null,
    eventType,
    entityType: null,
    entityId: null,
    captainId: null,
    missionId: null,
    vesselId: null,
    voyageId: null,
    message: '',
    payload: null,
    createdUtc: '2026-10-01T00:00:00Z',
  };
}

function page(objects: ArmadaEvent[], totalPages: number, totalRecords: number, pageNumber: number): EnumerationResult<ArmadaEvent> {
  return { success: true, pageNumber, pageSize: 50, totalPages, totalRecords, totalMs: 1, objects };
}

function deferred<T>() {
  let resolvePromise: (value: T) => void = () => {};
  const promise = new Promise<T>((resolve) => { resolvePromise = resolve; });
  return { promise, resolve: resolvePromise };
}

function emptyList<T>(): EnumerationResult<T> {
  return { success: true, pageNumber: 1, pageSize: 50, totalPages: 1, totalRecords: 0, totalMs: 0, objects: [] };
}

async function openOnFirstPage() {
  render(<MemoryRouter><Events /></MemoryRouter>);
  await screen.findByText('alpha.kept');
}

async function jumpToPage(pageNumber: number) {
  const input = screen.getByLabelText('Page number');
  fireEvent.change(input, { target: { value: String(pageNumber) } });
  fireEvent.keyDown(input, { key: 'Enter' });
  await waitFor(() => {
    expect(vi.mocked(listEvents).mock.calls.some((call) => call[0]?.pageNumber === pageNumber)).toBe(true);
  });
}

describe('Events server paging', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.clearAllMocks();
    vi.mocked(listCaptains).mockResolvedValue(emptyList());
    vi.mocked(listVessels).mockResolvedValue(emptyList());
  });

  it('shows the last page of rows when a later page is past the new end', async () => {
    const pastEnd = deferred<EnumerationResult<ArmadaEvent>>();
    const lastPage = deferred<EnumerationResult<ArmadaEvent>>();
    vi.mocked(listEvents).mockImplementation(async (params) => {
      const pageNumber = params?.pageNumber ?? 1;
      if (pageNumber === 4) return pastEnd.promise;
      if (pageNumber === 2) return lastPage.promise;
      return page([event('evt_alpha', 'alpha.kept')], 4, 80, 1);
    });

    await openOnFirstPage();
    await jumpToPage(4);
    pastEnd.resolve(page([], 2, 50, 4));

    await waitFor(() => expect(screen.getByLabelText('Page number')).toHaveValue(2));
    expect(screen.getByText('alpha.kept')).toBeInTheDocument();
    expect(screen.queryByText('No events found.')).not.toBeInTheDocument();

    lastPage.resolve(page([event('evt_second', 'second.kept')], 2, 50, 2));
    await screen.findByText('second.kept');
    expect(screen.queryByText('alpha.kept')).not.toBeInTheDocument();
    expect(screen.getByText('of 2')).toBeInTheDocument();
    expect(screen.getByLabelText('Page number')).toHaveValue(2);
  });

  it('returns an empty list to page 1', async () => {
    const pastEnd = deferred<EnumerationResult<ArmadaEvent>>();
    const emptyPage = deferred<EnumerationResult<ArmadaEvent>>();
    let opened = false;
    vi.mocked(listEvents).mockImplementation(async (params) => {
      const pageNumber = params?.pageNumber ?? 1;
      if (pageNumber === 4) return pastEnd.promise;
      if (opened) return emptyPage.promise;
      opened = true;
      return page([event('evt_alpha', 'alpha.kept')], 4, 80, 1);
    });

    await openOnFirstPage();
    await jumpToPage(4);
    pastEnd.resolve(page([], 0, 0, 4));

    await waitFor(() => expect(screen.getByLabelText('Page number')).toHaveValue(1));
    expect(screen.getByText('alpha.kept')).toBeInTheDocument();

    emptyPage.resolve(page([], 0, 0, 1));
    await screen.findByText('No events found.');
    expect(screen.getByLabelText('Page number')).toHaveValue(1);
    expect(screen.getByText('of 1')).toBeInTheDocument();
  });

  it('keeps the newer page when an older request finishes later', async () => {
    const older = deferred<EnumerationResult<ArmadaEvent>>();
    const newer = deferred<EnumerationResult<ArmadaEvent>>();
    vi.mocked(listEvents).mockImplementation(async (params) => {
      const pageNumber = params?.pageNumber ?? 1;
      if (pageNumber === 4) return older.promise;
      if (pageNumber === 2) return newer.promise;
      return page([event('evt_alpha', 'alpha.kept')], 4, 80, 1);
    });

    await openOnFirstPage();
    await jumpToPage(4);
    await jumpToPage(2);
    newer.resolve(page([event('evt_bravo', 'bravo.kept')], 4, 80, 2));
    await screen.findByText('bravo.kept');

    await act(async () => {
      older.resolve(page([event('evt_charlie', 'charlie.kept')], 4, 80, 4));
    });
    expect(screen.getByText('bravo.kept')).toBeInTheDocument();
    expect(screen.queryByText('charlie.kept')).not.toBeInTheDocument();
    expect(screen.getByLabelText('Page number')).toHaveValue(2);
  });
});
