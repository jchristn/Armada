import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import ApiExplorer from './ApiExplorer';
import { getRequestHistoryEntry, listRequestHistory } from '../api/client';

const translate = (text: string, params?: Record<string, string | number | null | undefined>) => {
  if (!params) return text;
  return Object.entries(params).reduce(
    (current, [key, value]) => current.split(`{{${key}}}`).join(value == null ? '' : String(value)),
    text,
  );
};

vi.mock('../api/client', () => ({
  getClientBaseUrl: () => '',
  listRequestHistory: vi.fn(),
  getRequestHistoryEntry: vi.fn(),
}));

vi.mock('../context/AuthContext', () => ({
  useAuth: () => ({
    sessionToken: 'test-session-token',
  }),
}));

vi.mock('../context/LocaleContext', () => ({
  useLocale: () => ({
    t: translate,
  }),
}));

vi.mock('../context/NotificationContext', () => ({
  useNotifications: () => ({
    pushToast: vi.fn(),
  }),
}));

describe('ApiExplorer', () => {
  beforeEach(() => {
    vi.mocked(listRequestHistory).mockResolvedValue({
      success: true,
      pageNumber: 1,
      pageSize: 8,
      totalPages: 1,
      totalRecords: 0,
      totalMs: 1,
      objects: [],
    });
    vi.mocked(getRequestHistoryEntry).mockResolvedValue({
      entry: {
        id: 'req_test',
        tenantId: null,
        userId: null,
        credentialId: null,
        principalDisplay: null,
        authMethod: null,
        method: 'GET',
        route: '/api/v1/fleets',
        routeTemplate: '/api/v1/fleets',
        queryString: null,
        statusCode: 200,
        durationMs: 5,
        requestSizeBytes: 0,
        responseSizeBytes: 0,
        requestContentType: null,
        responseContentType: 'application/json',
        isSuccess: true,
        clientIp: null,
        correlationId: null,
        createdUtc: '2026-05-01T00:00:00Z',
      },
      detail: null,
    });

    vi.stubGlobal('fetch', vi.fn(async (input: string | URL | Request) => {
      const url = typeof input === 'string'
        ? input
        : input instanceof URL
          ? input.toString()
          : input.url;

      if (url.endsWith('/openapi.json') || url === '/openapi.json') {
        return {
          ok: true,
          json: async () => ({
            paths: {
              '/api/v1/fleets': {
                get: {
                  operationId: 'listFleets',
                  summary: 'List fleets',
                  description: 'Enumerate fleets',
                  tags: ['Fleets'],
                  parameters: [],
                  responses: {
                    200: {},
                  },
                },
              },
              '/api/v1/backlog/{id}/refinement-sessions': {
                post: {
                  operationId: 'createBacklogRefinementSession',
                  summary: 'Create backlog refinement session',
                  description: 'Create a captain-backed backlog refinement session.',
                  tags: ['Objectives'],
                  parameters: [
                    {
                      name: 'id',
                      in: 'path',
                      required: true,
                      schema: { type: 'string' },
                    },
                    {
                      name: 'trace',
                      in: 'query',
                      required: false,
                      schema: { type: 'string' },
                    },
                    {
                      name: 'x-correlation-id',
                      in: 'header',
                      required: false,
                      schema: { type: 'string' },
                    },
                  ],
                  requestBody: {
                    content: {
                      'application/json': {
                        schema: {
                          type: 'object',
                          properties: {
                            captainId: { type: 'string' },
                            title: { type: 'string' },
                          },
                        },
                      },
                    },
                  },
                  responses: {
                    201: {},
                  },
                },
              },
            },
          }),
        } as Response;
      }

      return {
        ok: true,
        status: 200,
        statusText: 'OK',
        headers: new Headers({ 'content-type': 'application/json' }),
        text: async () => '{"ok":true}',
        blob: async () => new Blob(['{"ok":true}'], { type: 'application/json' }),
      } as unknown as Response;
    }));
  });

  afterEach(() => {
    vi.clearAllMocks();
    vi.unstubAllGlobals();
  });

  it('loads the live OpenAPI document and renders operations', async () => {
    render(
      <MemoryRouter initialEntries={['/api-explorer']}>
        <Routes>
          <Route path="/api-explorer" element={<ApiExplorer />} />
          <Route path="/api-explorer/:operationId" element={<ApiExplorer />} />
          <Route path="/requests" element={<div>Requests Route</div>} />
        </Routes>
      </MemoryRouter>,
    );

    expect(screen.getByRole('heading', { name: 'API Explorer' })).toBeInTheDocument();
    expect((await screen.findAllByText('/api/v1/fleets')).length).toBeGreaterThan(0);
    expect(screen.getByText('List fleets')).toBeInTheDocument();
    // Operations are chosen from a dropdown; the fleets operation is one of the options.
    expect(screen.getByRole('heading', { name: 'Operations' })).toBeInTheDocument();
    expect(await screen.findByRole('option', { name: /GET \/api\/v1\/fleets/ })).toBeInTheDocument();
  });

  it('hydrates replay state for parameterized backlog refinement routes', async () => {
    render(
      <MemoryRouter initialEntries={[
        {
          pathname: '/api-explorer',
          state: {
            replayRequest: {
              method: 'POST',
              route: '/api/v1/backlog/obj_123/refinement-sessions',
              routeTemplate: '/api/v1/backlog/{id}/refinement-sessions',
              pathValues: {},
              queryValues: { trace: 'replay-trace' },
              headerValues: { 'x-correlation-id': 'corr_123' },
              bodyValue: '{"captainId":"cpt_123","title":"Replay refinement"}',
            },
          },
        } as never,
      ]}>
        <Routes>
          <Route path="/api-explorer" element={<ApiExplorer />} />
          <Route path="/api-explorer/:operationId" element={<ApiExplorer />} />
          <Route path="/requests" element={<div>Requests Route</div>} />
        </Routes>
      </MemoryRouter>,
    );

    expect(await screen.findByText('Create backlog refinement session')).toBeInTheDocument();
    expect(await screen.findByDisplayValue('obj_123')).toBeInTheDocument();
    expect(await screen.findByDisplayValue('replay-trace')).toBeInTheDocument();
    expect(await screen.findByDisplayValue('corr_123')).toBeInTheDocument();
    expect(await screen.findByDisplayValue('{"captainId":"cpt_123","title":"Replay refinement"}')).toBeInTheDocument();
  });
});
