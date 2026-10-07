import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import RequestHistory from './RequestHistory';
import {
  deleteRequestHistoryByFilter,
  deleteRequestHistoryEntries,
  deleteRequestHistoryEntry,
  getRequestHistoryEntry,
  getRequestHistorySummary,
  listRequestHistory,
} from '../api/client';

const mockNavigate = vi.fn();

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return {
    ...actual,
    useNavigate: () => mockNavigate,
  };
});

const translate = (text: string, params?: Record<string, string | number | null | undefined>) => {
  if (!params) return text;
  return Object.entries(params).reduce(
    (current, [key, value]) => current.split(`{{${key}}}`).join(value == null ? '' : String(value)),
    text,
  );
};

vi.mock('../api/client', () => ({
  listRequestHistory: vi.fn(),
  getRequestHistorySummary: vi.fn(),
  getRequestHistoryEntry: vi.fn(),
  deleteRequestHistoryEntry: vi.fn(),
  deleteRequestHistoryEntries: vi.fn(),
  deleteRequestHistoryByFilter: vi.fn(),
}));

vi.mock('../context/LocaleContext', () => ({
  useLocale: () => ({
    t: translate,
    formatDateTime: (value: string | null | undefined) => value ?? '',
    formatRelativeTime: (value: string | null | undefined) => value ?? '',
  }),
}));

vi.mock('../context/NotificationContext', () => ({
  useNotifications: () => ({
    pushToast: vi.fn(),
  }),
}));

vi.mock('../context/AuthContext', () => ({
  useAuth: () => ({
    isAdmin: false,
    isTenantAdmin: true,
  }),
}));

describe('RequestHistory', () => {
  beforeEach(() => {
    mockNavigate.mockReset();
    vi.mocked(listRequestHistory).mockResolvedValue({
      success: true,
      pageNumber: 1,
      pageSize: 25,
      totalPages: 1,
      totalRecords: 1,
      totalMs: 1,
      objects: [
        {
          id: 'req_123',
          tenantId: 'ten_123',
          userId: 'usr_123',
          credentialId: 'crd_123',
          principalDisplay: 'captain@armada',
          authMethod: 'Bearer',
          method: 'POST',
          route: '/api/v1/missions',
          routeTemplate: '/api/v1/missions',
          queryString: null,
          statusCode: 202,
          durationMs: 18.25,
          requestSizeBytes: 128,
          responseSizeBytes: 512,
          requestContentType: 'application/json',
          responseContentType: 'application/json',
          isSuccess: true,
          clientIp: '127.0.0.1',
          correlationId: 'corr_123',
          createdUtc: '2026-05-01T12:00:00Z',
        },
      ],
    });

    vi.mocked(getRequestHistorySummary).mockResolvedValue({
      totalCount: 1,
      successCount: 1,
      failureCount: 0,
      successRate: 100,
      averageDurationMs: 18.25,
      fromUtc: '2026-05-01T11:00:00Z',
      toUtc: '2026-05-01T12:00:00Z',
      bucketMinutes: 15,
      buckets: [
        {
          bucketStartUtc: '2026-05-01T11:45:00Z',
          bucketEndUtc: '2026-05-01T12:00:00Z',
          totalCount: 1,
          successCount: 1,
          failureCount: 0,
          averageDurationMs: 18.25,
        },
      ],
    });

    vi.mocked(getRequestHistoryEntry).mockResolvedValue({
      entry: {
        id: 'req_123',
        tenantId: 'ten_123',
        userId: 'usr_123',
        credentialId: 'crd_123',
        principalDisplay: 'captain@armada',
        authMethod: 'Bearer',
        method: 'POST',
        route: '/api/v1/missions',
        routeTemplate: '/api/v1/missions',
        queryString: null,
        statusCode: 202,
        durationMs: 18.25,
        requestSizeBytes: 128,
        responseSizeBytes: 512,
        requestContentType: 'application/json',
        responseContentType: 'application/json',
        isSuccess: true,
        clientIp: '127.0.0.1',
        correlationId: 'corr_123',
        createdUtc: '2026-05-01T12:00:00Z',
      },
      detail: null,
    });

    vi.mocked(deleteRequestHistoryEntry).mockResolvedValue();
    vi.mocked(deleteRequestHistoryEntries).mockResolvedValue({ deleted: 0, skipped: [] });
    vi.mocked(deleteRequestHistoryByFilter).mockResolvedValue({ deleted: 0, skipped: [] });
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  it('renders summary cards and paginated request rows', async () => {
    render(
      <MemoryRouter initialEntries={['/requests']}>
        <Routes>
          <Route path="/requests" element={<RequestHistory />} />
          <Route path="/requests/:id" element={<RequestHistory />} />
          <Route path="/api-explorer" element={<div>API Explorer Route</div>} />
        </Routes>
      </MemoryRouter>,
    );

    expect(screen.getByRole('heading', { name: 'Requests' })).toBeInTheDocument();
    expect(await screen.findByText('/api/v1/missions')).toBeInTheDocument();
    expect(screen.getByText('captain@armada')).toBeInTheDocument();
    expect(screen.getByText('100.0%')).toBeInTheDocument();
  });

  it('renders collapsible request detail sections for headers and bodies', async () => {
    vi.mocked(getRequestHistoryEntry).mockResolvedValueOnce({
      entry: {
        id: 'req_123',
        tenantId: 'ten_123',
        userId: 'usr_123',
        credentialId: 'crd_123',
        principalDisplay: 'captain@armada',
        authMethod: 'Bearer',
        method: 'POST',
        route: '/api/v1/missions',
        routeTemplate: '/api/v1/missions',
        queryString: null,
        statusCode: 202,
        durationMs: 18.25,
        requestSizeBytes: 128,
        responseSizeBytes: 512,
        requestContentType: 'application/json',
        responseContentType: 'application/json',
        isSuccess: true,
        clientIp: '127.0.0.1',
        correlationId: 'corr_123',
        createdUtc: '2026-05-01T12:00:00Z',
      },
      detail: {
        requestHistoryId: 'req_123',
        pathParamsJson: '{}',
        queryParamsJson: '{}',
        requestHeadersJson: '{"x-request-header":"header-value"}',
        responseHeadersJson: '{"x-response-header":"response-value"}',
        requestBodyText: 'request-body-value',
        responseBodyText: 'response-body-value',
        requestBodyTruncated: false,
        responseBodyTruncated: false,
      },
    });

    render(
      <MemoryRouter initialEntries={['/requests/req_123']}>
        <Routes>
          <Route path="/requests" element={<RequestHistory />} />
          <Route path="/requests/:id" element={<RequestHistory />} />
          <Route path="/api-explorer" element={<div>API Explorer Route</div>} />
        </Routes>
      </MemoryRouter>,
    );

    expect(await screen.findByText('request-body-value')).toBeInTheDocument();
    expect(screen.getByText(/x-request-header/i)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /^Request Headers/i }));
    expect(screen.queryByText(/x-request-header/i)).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /^Request Body/i }));
    expect(screen.queryByText('request-body-value')).not.toBeInTheDocument();
  });

  it('replays backlog refinement request details into API Explorer route state', async () => {
    vi.mocked(getRequestHistoryEntry).mockResolvedValue({
      entry: {
        id: 'req_123',
        tenantId: 'ten_123',
        userId: 'usr_123',
        credentialId: 'crd_123',
        principalDisplay: 'captain@armada',
        authMethod: 'Bearer',
        method: 'POST',
        route: '/api/v1/backlog/obj_123/refinement-sessions',
        routeTemplate: '/api/v1/backlog/{id}/refinement-sessions',
        queryString: null,
        statusCode: 202,
        durationMs: 18.25,
        requestSizeBytes: 128,
        responseSizeBytes: 512,
        requestContentType: 'application/json',
        responseContentType: 'application/json',
        isSuccess: true,
        clientIp: '127.0.0.1',
        correlationId: 'corr_123',
        createdUtc: '2026-05-01T12:00:00Z',
      },
      detail: {
        requestHistoryId: 'req_123',
        pathParamsJson: '{"id":"obj_123"}',
        queryParamsJson: '{"trace":"replay-trace"}',
        requestHeadersJson: '{"x-correlation-id":"corr_123"}',
        responseHeadersJson: '{}',
        requestBodyText: '{"captainId":"cpt_123","title":"Replay refinement"}',
        responseBodyText: '{}',
        requestBodyTruncated: false,
        responseBodyTruncated: false,
      },
    });

    render(
      <MemoryRouter initialEntries={['/requests/req_123']}>
        <Routes>
          <Route path="/requests" element={<RequestHistory />} />
          <Route path="/requests/:id" element={<RequestHistory />} />
          <Route path="/api-explorer" element={<div>API Explorer Route</div>} />
        </Routes>
      </MemoryRouter>,
    );

    expect(await screen.findByText('{"captainId":"cpt_123","title":"Replay refinement"}')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Replay' }));

    await waitFor(() => {
      expect(mockNavigate).toHaveBeenCalledWith('/api-explorer', {
        state: {
          replayRequest: {
            method: 'POST',
            route: '/api/v1/backlog/obj_123/refinement-sessions',
            routeTemplate: '/api/v1/backlog/{id}/refinement-sessions',
            pathValues: { id: 'obj_123' },
            queryValues: { trace: 'replay-trace' },
            headerValues: { 'x-correlation-id': 'corr_123' },
            bodyValue: '{"captainId":"cpt_123","title":"Replay refinement"}',
          },
        },
      });
    });
  });

  it('renders the table toolbar with refresh controls, a column chooser with locked identity columns, and one-line cells', async () => {
    const { container } = render(
      <MemoryRouter initialEntries={['/requests']}>
        <Routes>
          <Route path="/requests" element={<RequestHistory />} />
        </Routes>
      </MemoryRouter>,
    );
    const route = await screen.findByText('/api/v1/missions');
    // Refresh and auto-refresh live in the table toolbar row, not the page header.
    const bar = container.querySelector('.data-table .pagination-bar') as HTMLElement;
    expect(within(bar).getByLabelText('Auto-refresh interval')).toBeInTheDocument();
    expect(within(bar).getByTitle('Refresh request data')).toBeInTheDocument();
    expect(container.querySelector('.page-header .auto-refresh-select')).toBeNull();
    // Regression: duration and payload sizes stay on one line; the route truncates with its full value in the title.
    const table = screen.getByRole('table');
    expect(within(table).getByText('18.25 ms').closest('td')).toHaveClass('cell-nowrap');
    expect(within(table).getByText(/ \/ /).closest('td')).toHaveClass('cell-nowrap');
    expect(route.closest('td')).toHaveAttribute('title', '/api/v1/missions');

    await act(async () => { fireEvent.click(within(bar).getByRole('button', { name: /^Columns/ })); });
    const menu = screen.getByRole('menu', { name: 'Choose visible columns' });
    expect(within(menu).getByRole('menuitemcheckbox', { name: /^When/ })).toHaveAttribute('aria-disabled', 'true');
    expect(within(menu).getByRole('menuitemcheckbox', { name: /^Route/ })).toHaveAttribute('aria-disabled', 'true');
    expect(within(menu).getByRole('menuitemcheckbox', { name: /^Principal/ })).not.toHaveAttribute('aria-disabled');
  });
});
