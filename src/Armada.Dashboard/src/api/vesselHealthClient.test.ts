import { afterEach, describe, expect, it, vi } from 'vitest';
import { enumerateVesselHealth, evaluateVesselHealth, setVesselHealthOverride } from './client';
import { onlyCallArgs } from '../test/mockCalls';

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

describe('vessel health client', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('evaluate: 202 resolves with the camelized start payload', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(202, { JobId: 'job_1', AlreadyRunning: false, VesselCount: 3 }));
    vi.stubGlobal('fetch', fetchMock);
    await expect(evaluateVesselHealth({ Force: true })).resolves.toEqual({ jobId: 'job_1', alreadyRunning: false, vesselCount: 3 });
    const [url, init] = onlyCallArgs(fetchMock);
    expect(new URL(url, 'http://localhost').pathname).toBe('/api/v1/vessel-health/evaluate');
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body)).toEqual({ Force: true });
  });

  it('evaluate: 409 is not an error and returns the running job id', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(409, { JobId: 'job_running', AlreadyRunning: true, VesselCount: 0 })));
    await expect(evaluateVesselHealth()).resolves.toEqual({ jobId: 'job_running', alreadyRunning: true, vesselCount: 0 });
  });

  it('evaluate: other failures still throw', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(403, { Message: 'Forbidden' })));
    await expect(evaluateVesselHealth()).rejects.toThrow('Forbidden');
  });

  it('enumerate posts the PascalCase DTO', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(200, { Success: true, PageNumber: 1, PageSize: 25, TotalPages: 0, TotalRecords: 0, Objects: [] }));
    vi.stubGlobal('fetch', fetchMock);
    const result = await enumerateVesselHealth({ PageNumber: 1, PageSize: 25, OverallStatus: ['Fail'] });
    expect(result.totalRecords).toBe(0);
    expect(JSON.parse(onlyCallArgs(fetchMock)[1].body)).toEqual({ PageNumber: 1, PageSize: 25, OverallStatus: ['Fail'] });
  });

  it('override PUT targets the criterion route with Status and Note', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(200, { Health: { VesselId: 'vsl_1' }, Findings: [], Dependencies: [], Overrides: [] }));
    vi.stubGlobal('fetch', fetchMock);
    await setVesselHealthOverride('vsl_1', 'Dependencies', 'Pass', 'pinned');
    const [url, init] = onlyCallArgs(fetchMock);
    expect(new URL(url, 'http://localhost').pathname).toBe('/api/v1/vessels/vsl_1/health/overrides/Dependencies');
    expect(init.method).toBe('PUT');
    expect(JSON.parse(init.body)).toEqual({ Status: 'Pass', Note: 'pinned' });
  });
});
