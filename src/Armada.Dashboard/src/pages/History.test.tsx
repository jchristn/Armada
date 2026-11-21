import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import History from './History';
import { enumerateHistoryTimeline, listObjectives, listVessels } from '../api/client';
import { callsMatching } from '../test/mockCalls';

const translate = (text: string, params?: Record<string, string | number | null | undefined>) => {
  if (!params) return text;
  return Object.entries(params).reduce(
    (current, [key, value]) => current.split(`{{${key}}}`).join(value == null ? '' : String(value)),
    text,
  );
};

vi.mock('../api/client', () => ({
  enumerateHistoryTimeline: vi.fn(),
  listObjectives: vi.fn(),
  listVessels: vi.fn(),
  deleteRequestHistoryEntry: vi.fn(),
}));

vi.mock('../context/LocaleContext', () => ({
  useLocale: () => ({
    t: translate,
    formatDateTime: (value: string | null | undefined) => value ?? '',
    formatRelativeTime: (value: string | null | undefined) => value ?? '',
  }),
}));

vi.mock('../context/NotificationContext', () => ({
  useNotifications: () => ({ pushToast: vi.fn() }),
}));

describe('History', () => {
  const createObjectUrlMock = vi.fn(() => 'blob:history');
  const revokeObjectUrlMock = vi.fn();
  const anchorClickSpy = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});

  beforeAll(() => {
    Object.defineProperty(URL, 'createObjectURL', {
      configurable: true,
      writable: true,
      value: createObjectUrlMock,
    });
    Object.defineProperty(URL, 'revokeObjectURL', {
      configurable: true,
      writable: true,
      value: revokeObjectUrlMock,
    });
  });

  beforeEach(() => {
    localStorage.clear();

    vi.mocked(enumerateHistoryTimeline).mockResolvedValue({
      success: true,
      pageNumber: 1,
      pageSize: 250,
      totalPages: 1,
      totalRecords: 1,
      totalMs: 1,
      objects: [
        {
          id: 'his_123',
          sourceType: 'CheckRun',
          sourceId: 'chk_123',
          entityType: 'CheckRun',
          entityId: 'chk_123',
          objectiveId: null,
          vesselId: 'vsl_123',
          environmentId: null,
          deploymentId: null,
          incidentId: null,
          missionId: 'msn_123',
          voyageId: 'voy_123',
          actorId: 'usr_123',
          actorDisplay: 'captain@armada',
          title: 'Deploy finished',
          description: 'Deployment verification passed.',
          status: 'Passed',
          severity: 'Success',
          route: '/checks/chk_123',
          occurredUtc: '2026-05-03T12:00:00Z',
          metadataJson: '{"summary":"ok"}',
        },
      ],
    });

    vi.mocked(listVessels).mockResolvedValue({
      success: true,
      pageNumber: 1,
      pageSize: 9999,
      totalPages: 1,
      totalRecords: 1,
      totalMs: 1,
      objects: [
        {
          id: 'vsl_123',
          name: 'History Vessel',
        } as never,
      ],
    });

    vi.mocked(listObjectives).mockResolvedValue({
      success: true,
      pageNumber: 1,
      pageSize: 9999,
      totalPages: 1,
      totalRecords: 1,
      totalMs: 1,
      objects: [
        {
          id: 'obj_123',
          title: 'History Objective',
        } as never,
      ],
    });
  });

  afterEach(() => {
    localStorage.clear();
    vi.clearAllMocks();
    createObjectUrlMock.mockClear();
    revokeObjectUrlMock.mockClear();
    anchorClickSpy.mockClear();
  });

  it('saves views and exports the current filtered history', async () => {
    render(
      <MemoryRouter initialEntries={['/history']}>
        <Routes>
          <Route path="/history" element={<History />} />
        </Routes>
      </MemoryRouter>,
    );

    expect(await screen.findByText('Deploy finished')).toBeInTheDocument();

    const searchInput = screen.getByPlaceholderText('Search title, status, route, or metadata...');
    fireEvent.change(searchInput, { target: { value: 'deploy' } });

    fireEvent.click(screen.getByRole('button', { name: 'Save View' }));
    fireEvent.change(screen.getByPlaceholderText('Staging failures'), { target: { value: 'Deploy view' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByRole('button', { name: 'Deploy view' })).toBeInTheDocument();

    fireEvent.change(searchInput, { target: { value: '' } });
    fireEvent.click(screen.getByRole('button', { name: 'Deploy view' }));
    expect(screen.getByDisplayValue('deploy')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Export JSON' }));

    await waitFor(() => {
      expect(enumerateHistoryTimeline).toHaveBeenCalledTimes(2);
    });

    // The export pages through the view at the route maximum (1000); select it by that, not by call position.
    const exportCalls = callsMatching(vi.mocked(enumerateHistoryTimeline), ([query]) => query?.pageSize === 1000);
    expect(exportCalls).toHaveLength(1);
    expect(exportCalls[0][0]).toMatchObject({
      pageNumber: 1,
      pageSize: 1000,
      text: 'deploy',
    });
    expect(createObjectUrlMock).toHaveBeenCalled();
    expect(anchorClickSpy).toHaveBeenCalled();
    const savedViews = JSON.parse(localStorage.getItem('armada_history_saved_views') ?? '[]') as Array<{ name: string; query: { text?: string | null } }>;
    const savedView = savedViews.find((view) => view.name === 'Deploy view');
    expect(savedView).toBeDefined();
    expect(savedView?.query.text).toBe('deploy');
  });

  it('applies the postmortem-only filter to history queries', async () => {
    render(
      <MemoryRouter initialEntries={['/history']}>
        <Routes>
          <Route path="/history" element={<History />} />
        </Routes>
      </MemoryRouter>,
    );

    expect(await screen.findByText('Deploy finished')).toBeInTheDocument();

    fireEvent.click(screen.getByLabelText('Postmortem context only'));
    fireEvent.click(screen.getByRole('button', { name: 'Apply' }));

    await waitFor(() => {
      expect(enumerateHistoryTimeline).toHaveBeenCalledTimes(2);
    });

    // Exactly one query carries the postmortem filter (the one Apply sent), and it is the standard page.
    const postmortemCalls = callsMatching(vi.mocked(enumerateHistoryTimeline), ([query]) => query?.postmortemOnly === true);
    expect(postmortemCalls).toHaveLength(1);
    expect(postmortemCalls[0][0]).toMatchObject({
      pageNumber: 1,
      pageSize: 100,
      postmortemOnly: true,
    });
  });
  it('hides GET requests by default and shows them when the toggle is on (F17)', async () => {
    render(
      <MemoryRouter initialEntries={['/history']}>
        <Routes>
          <Route path="/history" element={<History />} />
        </Routes>
      </MemoryRouter>,
    );

    expect(await screen.findByText('Deploy finished')).toBeInTheDocument();
    expect(enumerateHistoryTimeline).toHaveBeenCalledTimes(1);
    expect(vi.mocked(enumerateHistoryTimeline).mock.calls[0][0]).toMatchObject({ excludeReadRequests: true });

    const toggle = screen.getByLabelText('Show GET requests');
    expect(toggle).not.toBeChecked();
    fireEvent.click(toggle);

    await waitFor(() => {
      expect(enumerateHistoryTimeline).toHaveBeenCalledTimes(2);
    });
    expect(vi.mocked(enumerateHistoryTimeline).mock.calls[1][0]).toMatchObject({ excludeReadRequests: false });
  });
  it('shows the true total and pages on the server instead of stopping at one page', async () => {
    const entry = (n: number) => ({
      id: `his_${n}`, sourceType: 'Mission', sourceId: `msn_${n}`, entityType: 'Mission', entityId: `msn_${n}`,
      objectiveId: null, vesselId: null, environmentId: null, deploymentId: null, incidentId: null, missionId: null,
      voyageId: null, actorId: null, actorDisplay: null, title: `Entry ${n}`, description: null, status: null,
      severity: null, route: null, occurredUtc: '2026-05-03T12:00:00Z', metadataJson: null,
    });
    vi.mocked(enumerateHistoryTimeline).mockImplementation(async (query) => ({
      success: true, pageNumber: query?.pageNumber ?? 1, pageSize: 100, totalPages: 13, totalRecords: 1234, totalMs: 1,
      objects: [entry(query?.pageNumber ?? 1)],
    }) as never);

    render(
      <MemoryRouter initialEntries={['/history']}>
        <Routes>
          <Route path="/history" element={<History />} />
        </Routes>
      </MemoryRouter>,
    );

    expect(await screen.findByText('Entry 1')).toBeInTheDocument();
    expect(screen.getByText('Total Entries')).toBeInTheDocument();
    expect(screen.getAllByText('1,234').length).toBeGreaterThan(0);
    expect(screen.getByText('Showing 1 of 1,234')).toBeInTheDocument();
    expect(screen.getByText(/1,234 records/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Next/ }));
    expect(await screen.findByText('Entry 2')).toBeInTheDocument();
    expect(callsMatching(vi.mocked(enumerateHistoryTimeline), ([query]) => query?.pageNumber === 2)).toHaveLength(1);
  });

  const pagedEntry = (n: number) => ({
    id: `his_${n}`, sourceType: 'Mission', sourceId: `msn_${n}`, entityType: 'Mission', entityId: `msn_${n}`,
    objectiveId: null, vesselId: null, environmentId: null, deploymentId: null, incidentId: null, missionId: null,
    voyageId: null, actorId: null, actorDisplay: null, title: `Entry ${n}`, description: null, status: null,
    severity: null, route: null, occurredUtc: '2026-05-03T12:00:00Z', metadataJson: null,
  });

  function renderHistory() {
    render(
      <MemoryRouter initialEntries={['/history']}>
        <Routes>
          <Route path="/history" element={<History />} />
        </Routes>
      </MemoryRouter>,
    );
  }

  it('orders on the server from the When header and returns to page 1', async () => {
    vi.mocked(enumerateHistoryTimeline).mockImplementation(async (query) => ({
      success: true, pageNumber: query?.pageNumber ?? 1, pageSize: 100, totalPages: 3, totalRecords: 300, totalMs: 1,
      objects: [pagedEntry((query?.pageNumber ?? 1) + (query?.order === 'CreatedAscending' ? 100 : 0))],
    }) as never);
    renderHistory();

    expect(await screen.findByText('Entry 1')).toBeInTheDocument();
    expect(vi.mocked(enumerateHistoryTimeline).mock.calls[0][0]).toMatchObject({ order: 'CreatedDescending', pageNumber: 1 });
    fireEvent.click(screen.getByRole('button', { name: /Next/ }));
    expect(await screen.findByText('Entry 2')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /^When/ }));
    expect(await screen.findByText('Entry 101')).toBeInTheDocument();
    const calls = vi.mocked(enumerateHistoryTimeline).mock.calls;
    expect(calls[calls.length - 1][0]).toMatchObject({ order: 'CreatedAscending', pageNumber: 1 });
  });

  it('returns to page 1 when filters are applied', async () => {
    vi.mocked(enumerateHistoryTimeline).mockImplementation(async (query) => ({
      success: true, pageNumber: query?.pageNumber ?? 1, pageSize: 100, totalPages: 3, totalRecords: 300, totalMs: 1,
      objects: [pagedEntry(query?.pageNumber ?? 1)],
    }) as never);
    renderHistory();

    expect(await screen.findByText('Entry 1')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Next/ }));
    expect(await screen.findByText('Entry 2')).toBeInTheDocument();

    fireEvent.change(screen.getByPlaceholderText('Search title, status, route, or metadata...'), { target: { value: 'deploy' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply' }));
    expect(await screen.findByText('Entry 1')).toBeInTheDocument();
    const calls = vi.mocked(enumerateHistoryTimeline).mock.calls;
    expect(calls[calls.length - 1][0]).toMatchObject({ text: 'deploy', pageNumber: 1 });
  });

  it('shows the last page when the requested page is past the end, and the empty state for no results', async () => {
    let total = 300;
    vi.mocked(enumerateHistoryTimeline).mockImplementation(async (query) => {
      const page = query?.pageNumber ?? 1;
      const pages = Math.max(1, Math.ceil(total / 100));
      return {
        success: true, pageNumber: page, pageSize: 100, totalPages: total === 0 ? 0 : pages, totalRecords: total, totalMs: 1,
        objects: total === 0 || page > pages ? [] : [pagedEntry(page)],
      } as never;
    });
    renderHistory();

    expect(await screen.findByText('Entry 1')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Next/ }));
    expect(await screen.findByText('Entry 2')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Next/ }));
    expect(await screen.findByText('Entry 3')).toBeInTheDocument();

    total = 150; // entries went away: page 3 no longer exists
    fireEvent.click(screen.getByRole('button', { name: 'Refresh history' }));
    expect(await screen.findByText('Entry 2')).toBeInTheDocument();
    expect(callsMatching(vi.mocked(enumerateHistoryTimeline), ([query]) => query?.pageNumber === 3).length).toBeGreaterThanOrEqual(2);

    total = 0;
    fireEvent.click(screen.getByRole('button', { name: 'Apply' }));
    expect(await screen.findByText('No history entries match the current filters.')).toBeInTheDocument();
  });

  it('reads one bounded page for each filter picker instead of looping over every page', async () => {
    // Full first pages of a larger set: a fetch-all loop would keep requesting pages 2..5.
    vi.mocked(listVessels).mockResolvedValue({
      success: true, pageNumber: 1, pageSize: 1000, totalPages: 5, totalRecords: 5000, totalMs: 1,
      objects: Array.from({ length: 1000 }, (_, i) => ({ id: `vsl_${i}`, name: `Vessel ${i}` })),
    } as never);
    vi.mocked(listObjectives).mockResolvedValue({
      success: true, pageNumber: 1, pageSize: 1000, totalPages: 5, totalRecords: 5000, totalMs: 1,
      objects: Array.from({ length: 1000 }, (_, i) => ({ id: `obj_${i}`, title: `Objective ${i}` })),
    } as never);
    renderHistory();
    expect(await screen.findByText('Deploy finished')).toBeInTheDocument();
    await waitFor(() => expect(listVessels).toHaveBeenCalledTimes(1));
    expect(listObjectives).toHaveBeenCalledTimes(1);
    expect(vi.mocked(listVessels).mock.calls[0][0]).toMatchObject({ pageNumber: 1, pageSize: 1000 });
    expect(vi.mocked(listObjectives).mock.calls[0][0]).toMatchObject({ pageNumber: 1, pageSize: 1000 });
  });

  it('bounds an export to ten pages and reports what was left out', async () => {
    vi.mocked(enumerateHistoryTimeline).mockImplementation(async (query) => ({
      success: true, pageNumber: query?.pageNumber ?? 1, pageSize: query?.pageSize ?? 100, totalPages: 50, totalRecords: 50000, totalMs: 1,
      objects: Array.from({ length: query?.pageSize ?? 100 }, (_, i) => pagedEntry((query?.pageNumber ?? 1) * 100000 + i)),
    }) as never);
    renderHistory();
    expect(await screen.findByText('Entry 100000')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Export CSV' }));
    await waitFor(() => expect(createObjectUrlMock).toHaveBeenCalled());
    const exportCalls = callsMatching(vi.mocked(enumerateHistoryTimeline), ([query]) => query?.pageSize === 1000);
    expect(exportCalls).toHaveLength(10);
    expect(exportCalls.map(([query]) => query?.pageNumber)).toEqual([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
  });

  it('loads the last day by default and another range from its tab', async () => {
    render(
      <MemoryRouter initialEntries={['/history']}>
        <Routes>
          <Route path="/history" element={<History />} />
        </Routes>
      </MemoryRouter>,
    );
    expect(await screen.findByText('Deploy finished')).toBeInTheDocument();
    const first = vi.mocked(enumerateHistoryTimeline).mock.calls[0][0];
    expect(Date.now() - Date.parse(String(first?.fromUtc))).toBeGreaterThan(23 * 3600 * 1000);
    expect(Date.now() - Date.parse(String(first?.fromUtc))).toBeLessThan(25 * 3600 * 1000);
    fireEvent.click(screen.getByRole('button', { name: 'All Time' }));
    await waitFor(() => expect(enumerateHistoryTimeline).toHaveBeenCalledTimes(2));
    expect(vi.mocked(enumerateHistoryTimeline).mock.calls[1][0]?.fromUtc).toBeUndefined();
  });
});
