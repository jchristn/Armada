import { afterEach, describe, expect, it, vi } from 'vitest';
import { applyModelAlertAction, ignoreModelAlert, setAuthToken } from './client';
import { onlyCallArgs } from '../test/mockCalls';

function stub(status: number, body: unknown) {
  const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }));
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

const RESULT = {
  Record: { Action: 'MoveDown', AppliedUtc: '2026-10-30T00:00:00Z', Changed: true, Result: 'Moved captain cpt_1 down.' },
  Alert: { Id: 'mal_1', Rule: 'FirstPassSuccess', Status: 'Open', SuggestedActions: [] },
};

describe('model alert client', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    setAuthToken(null);
  });

  it('applies a suggested action with a PascalCase body and the session token', async () => {
    setAuthToken('tok_1');
    const fetchMock = stub(200, RESULT);
    const result = await applyModelAlertAction('mal 1', 'MoveDown');
    const [url, init] = onlyCallArgs(fetchMock);
    expect(new URL(url, 'http://localhost').pathname).toBe('/api/v1/model-alerts/mal%201/apply');
    expect(init.method).toBe('POST');
    expect(init.headers['X-Token']).toBe('tok_1');
    expect(JSON.parse(init.body)).toEqual({ Action: 'MoveDown', Days: null });
    expect(result).toMatchObject({ record: { action: 'MoveDown', changed: true }, alert: { id: 'mal_1' } });
  });

  it('ignores an alert with the server default or an explicit number of days', async () => {
    let fetchMock = stub(200, RESULT);
    await ignoreModelAlert('mal_1');
    let [url, init] = onlyCallArgs(fetchMock);
    expect(new URL(url, 'http://localhost').pathname).toBe('/api/v1/model-alerts/mal_1/ignore');
    expect(JSON.parse(init.body)).toEqual({ Days: null });

    fetchMock = stub(200, RESULT);
    await ignoreModelAlert('mal_1', 14);
    [url, init] = onlyCallArgs(fetchMock);
    expect(JSON.parse(init.body)).toEqual({ Days: 14 });
  });

  it('rejects on an HTTP error', async () => {
    stub(404, { Error: 'NotFound', Message: 'Model alert not found.' });
    await expect(applyModelAlertAction('mal_x', 'Reevaluate')).rejects.toThrow();
  });
});
