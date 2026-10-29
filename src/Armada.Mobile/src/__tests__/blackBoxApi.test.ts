import { ApiError, apiErrorCode, getBlackBoxBrief, getMissionBlackBox, getVoyageBlackBox, resolveBlackBox, runBlackBoxRecovery, setAuthToken } from '@dashboard/api/client';

// The app reads black box entries and briefs, resolves them and runs recovery through the shared client
// (MISSION_BLACKBOX_PLAN.md 8.4, Mobile row). These checks run the real client under the app's Jest environment.

function response(body: unknown, status = 200, contentType = 'application/json') {
  const text = typeof body === 'string' ? body : JSON.stringify(body);
  return {
    ok: status < 400, status, statusText: status < 400 ? 'OK' : 'Error',
    headers: { get: (k: string) => (k.toLowerCase() === 'content-type' ? contentType : null) },
    json: async () => JSON.parse(text), text: async () => text,
  };
}

const fetchMock = jest.fn();

beforeEach(() => {
  fetchMock.mockReset();
  global.fetch = fetchMock as unknown as typeof fetch;
  setAuthToken('tok');
});

afterEach(() => setAuthToken(null));

describe('black box through the shared client', () => {
  it('reads a mission and a voyage entry, camelized', async () => {
    fetchMock.mockResolvedValueOnce(response({ Id: 'bbx_1', MissionId: 'msn_1', Resolution: 'Open', Triage: 'UserConfig' }));
    const entry = await getMissionBlackBox('msn_1');
    expect(fetchMock).toHaveBeenLastCalledWith('/api/v1/missions/msn_1/blackbox', expect.objectContaining({ method: 'GET', headers: expect.objectContaining({ 'X-Token': 'tok' }) }));
    expect(entry).toMatchObject({ id: 'bbx_1', missionId: 'msn_1', triage: 'UserConfig' });
    fetchMock.mockResolvedValueOnce(response({ Id: 'bbx_2', VoyageId: 'vyg_1' }));
    await getVoyageBlackBox('vyg_1', false);
    expect(fetchMock.mock.calls[1][0]).toBe('/api/v1/voyages/vyg_1/blackbox?includeRecord=false');
  });

  it('reads the brief for the user audience as Markdown', async () => {
    fetchMock.mockResolvedValueOnce(response('# What happened', 200, 'text/markdown'));
    await expect(getBlackBoxBrief('bbx_1', { audience: 'user' })).resolves.toBe('# What happened');
    expect(fetchMock.mock.calls[0][0]).toBe('/api/v1/blackbox/bbx_1/brief?audience=user');
  });

  it('resolves an entry and dry runs a recovery path', async () => {
    fetchMock.mockResolvedValueOnce(response({ Id: 'bbx_1', Resolution: 'Resolved' }));
    await resolveBlackBox('bbx_1', { resolution: 'Resolved', link: 'abc123' });
    expect(JSON.parse(fetchMock.mock.calls[0][1].body)).toEqual({ Resolution: 'Resolved', Note: null, Link: 'abc123' });
    fetchMock.mockResolvedValueOnce(response({ EntryId: 'bbx_1', PathId: 'rp1', DryRun: true, ConfirmToken: 'ct', Succeeded: true }));
    const dry = await runBlackBoxRecovery('bbx_1', 'rp1');
    expect(fetchMock.mock.calls[1][0]).toBe('/api/v1/blackbox/bbx_1/recovery/rp1');
    expect(JSON.parse(fetchMock.mock.calls[1][1].body)).toEqual({ DryRun: true });
    expect(dry.confirmToken).toBe('ct');
  });

  it('surfaces BlackBoxNotFound for a mission without an entry', async () => {
    fetchMock.mockResolvedValueOnce(response({ Error: 'NotFound', Message: 'No black box entry for mission msn_9.', Data: { Code: 'BlackBoxNotFound' } }, 404));
    const err = await getMissionBlackBox('msn_9').catch((e: unknown) => e);
    expect(err).toBeInstanceOf(ApiError);
    expect(apiErrorCode(err)).toBe('BlackBoxNotFound');
  });
});
