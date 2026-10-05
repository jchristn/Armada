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

    // The export is the query with the export page size; select it by that, not by call position.
    const exportCalls = callsMatching(vi.mocked(enumerateHistoryTimeline), ([query]) => query?.pageSize === 5000);
    expect(exportCalls).toHaveLength(1);
    expect(exportCalls[0][0]).toMatchObject({
      pageNumber: 1,
      pageSize: 5000,
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
      pageSize: 250,
      postmortemOnly: true,
    });
  });
});
