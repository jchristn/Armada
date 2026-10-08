import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import * as client from '@dashboard/api/client';
import { buildExplorerOperations, type OpenApiSpec } from '@dashboard/lib/apiExplorer';
import MoreLayout from '../app/(app)/(more)/_layout';
import ExplorerIndex from '../app/(app)/(more)/api-explorer/index';
import ExplorerOperation from '../app/(app)/(more)/api-explorer/[operationId]';
import { buildExplorerRequest, explorerAuthHeaders } from '../screens/system/ApiExplorerLogic';
import { renderW4Routes, resetW4 } from '../test/w4';

jest.mock('@dashboard/api/client', () => require('../test/w4Client').autoMockClient());

const SPEC: OpenApiSpec = {
  paths: {
    '/api/v1/fleets/{id}': {
      parameters: [{ name: 'id', in: 'path', required: true }],
      get: { operationId: 'GetFleet', summary: 'Get fleet', tags: ['Fleets'], parameters: [{ name: 'verbose', in: 'query', schema: { type: 'boolean', default: false } }] },
    },
    '/api/v1/fleets': {
      post: { operationId: 'CreateFleet', summary: 'Create fleet', tags: ['Fleets'], requestBody: { content: { 'application/json': { schema: { type: 'object', properties: { name: { type: 'string', example: 'Main' } } } } } } },
    },
    '/api/v1/status': { get: { operationId: 'GetStatus', summary: 'Status', tags: ['Status'] } },
  },
};

function jsonResponse(body: unknown, status = 200) {
  const text = JSON.stringify(body);
  return {
    ok: status < 400, status, statusText: status < 400 ? 'OK' : 'Error',
    headers: { get: (k: string) => (k.toLowerCase() === 'content-type' ? 'application/json' : null), forEach: (cb: (v: string, k: string) => void) => cb('application/json', 'content-type') },
    json: async () => body, text: async () => text,
  };
}

const api = client as jest.Mocked<typeof client>;
const fetchMock = jest.fn();
const ROUTES = { '(more)/_layout': MoreLayout, '(more)/more': () => null, '(more)/api-explorer/index': ExplorerIndex, '(more)/api-explorer/[operationId]': ExplorerOperation };

beforeEach(async () => {
  await resetW4();
  fetchMock.mockReset();
  fetchMock.mockImplementation(async (url: string) => (url.endsWith('/openapi.json') ? jsonResponse(SPEC) : jsonResponse({ id: 'flt_1', name: 'Main' })));
  global.fetch = fetchMock as unknown as typeof fetch;
});

describe('API explorer logic', () => {
  it('builds requests without URL.searchParams: path values, non-empty query, token, content type', () => {
    const ops = buildExplorerOperations(SPEC);
    const get = ops.find((o) => o.id === 'GetFleet')!;
    expect(get.parameters.map((p) => p.name)).toEqual(['id', 'verbose']);
    const preview = buildExplorerRequest(get, { path: { id: 'flt 1' }, query: { verbose: 'true', empty: '' }, header: { 'X-Token': 'x', 'X-Other': 'y' }, body: '' }, 'http://h:1', 'tok');
    expect(preview.url).toBe('http://h:1/api/v1/fleets/flt%201?verbose=true');
    expect(preview.headers).toEqual({ 'X-Token': 'tok', 'X-Other': 'y' });
    const post = ops.find((o) => o.id === 'CreateFleet')!;
    expect(buildExplorerRequest(post, { path: {}, query: {}, header: {}, body: '{"name":"A"}' }, '', null).headers).toEqual({ 'Content-Type': 'application/json' });
    expect(explorerAuthHeaders('tok', { 'X-Armada-Proxy-Session': 'p' })).toEqual({ 'X-Armada-Proxy-Session': 'p', 'X-Token': 'tok' });
  });
});

describe('API Explorer screens', () => {
  it('lists operations from the live OpenAPI document, filters them, and opens one', async () => {
    const h = await renderW4Routes(ROUTES, '/api-explorer');
    await waitFor(() => expect(screen.getByTestId('api-operation-GetFleet')).toBeTruthy());
    expect(fetchMock).toHaveBeenCalledWith('/openapi.json', { headers: { 'X-Token': 'tok' } });
    await fireEvent.changeText(screen.getByTestId('api-explorer-search'), 'status');
    await waitFor(() => expect(screen.queryByTestId('api-operation-GetFleet')).toBeNull());
    await fireEvent.press(screen.getByTestId('api-operation-GetStatus'));
    await waitFor(() => expect(h.getPathname()).toBe('/api-explorer/GetStatus'));
  });

  it('builds and sends a request and shows the response', async () => {
    await renderW4Routes(ROUTES, '/api-explorer/GetFleet');
    await waitFor(() => expect(screen.getByTestId('api-path-id')).toBeTruthy());
    expect(screen.getByTestId('api-request-url')).toHaveTextContent('/api/v1/fleets/%7Bid%7D?verbose=false');
    await fireEvent.changeText(screen.getByTestId('api-path-id'), 'flt_1');
    await act(async () => { await fireEvent.press(screen.getByTestId('api-send')); });
    await waitFor(() => expect(screen.getByTestId('api-response-text')).toHaveTextContent(/"name": "Main"/));
    expect(fetchMock).toHaveBeenLastCalledWith('/api/v1/fleets/flt_1?verbose=false', expect.objectContaining({ method: 'GET', headers: { 'X-Token': 'tok' } }));
    await fireEvent.press(screen.getByTestId('api-response-tab-code'));
    await waitFor(() => expect(screen.getByTestId('api-response-text')).toHaveTextContent(/curl -X GET/));
  });

  it('prefills the request body from the schema example', async () => {
    await renderW4Routes(ROUTES, '/api-explorer/CreateFleet');
    await waitFor(() => expect(screen.getByTestId('api-body')).toBeTruthy());
    expect(screen.getByTestId('api-body').props.value).toContain('"name": "Main"');
  });

  it('replays a captured request: ?replay= opens the matching operation prefilled', async () => {
    api.getRequestHistoryEntry.mockResolvedValue({
      entry: { id: 'req_1', method: 'GET', route: '/api/v1/fleets/flt_7', routeTemplate: '/api/v1/fleets/{id}' },
      detail: { queryParamsJson: '{"verbose":"true"}', requestHeadersJson: '{"X-Token":"old","X-Trace":"t1"}', pathParamsJson: '{}', requestBodyText: '' },
    } as never);
    const h = await renderW4Routes(ROUTES, '/api-explorer?replay=req_1');
    await waitFor(() => expect(h.getPathname()).toBe('/api-explorer/GetFleet'));
    expect(api.getRequestHistoryEntry).toHaveBeenCalledWith('req_1');
    await waitFor(() => expect(screen.getByTestId('api-request-url')).toHaveTextContent('/api/v1/fleets/flt_7?verbose=true'));
    expect(screen.getByTestId('api-path-id').props.value).toBe('flt_7');
  });

  it('a replay with no matching operation says so', async () => {
    api.getRequestHistoryEntry.mockResolvedValue({
      entry: { id: 'req_2', method: 'DELETE', route: '/api/v1/nothing', routeTemplate: null },
      detail: null,
    } as never);
    await renderW4Routes(ROUTES, '/api-explorer?replay=req_2');
    await waitFor(() => expect(screen.getByTestId('api-replay-error')).toHaveTextContent('No matching OpenAPI operation was found for the replay request.'));
  });
});
