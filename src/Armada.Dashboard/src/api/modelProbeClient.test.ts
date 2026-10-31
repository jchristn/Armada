import { afterEach, describe, expect, it, vi } from 'vitest';
import { cancelModelProbeRun, evaluateCaptain, evaluateModelEndpoint, getModelProbeRun, listModelProbeRuns, setAuthToken } from './client';
import { onlyCallArgs } from '../test/mockCalls';

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
}

function stub(status: number, body: unknown) {
  const fetchMock = vi.fn().mockResolvedValue(jsonResponse(status, body));
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

const RUN = {
  Id: 'mpr_1', TargetKind: 'Captain', CaptainId: 'cpt_1', Depth: 'Quick', Status: 'Queued', IsEvaluation: true,
  ProbeSetVersion: 'quick-1', TokenBudget: 200000, TokensUsed: 0, PassedCount: 0, FailedCount: 0, CouldNotRunCount: 0,
  Results: [],
};

describe('model probe client', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    setAuthToken(null);
  });

  it('evaluates a captain with a PascalCase Quick depth and the session token', async () => {
    setAuthToken('tok_1');
    const fetchMock = stub(201, RUN);
    const run = await evaluateCaptain('cpt 1');
    const [url, init] = onlyCallArgs(fetchMock);
    expect(new URL(url, 'http://localhost').pathname).toBe('/api/v1/captains/cpt%201/evaluate');
    expect(init.method).toBe('POST');
    expect(init.headers['X-Token']).toBe('tok_1');
    expect(JSON.parse(init.body)).toEqual({ Depth: 'Quick' });
    expect(run).toMatchObject({ id: 'mpr_1', status: 'Queued', isEvaluation: true, couldNotRunCount: 0 });
  });

  it('evaluates a model endpoint', async () => {
    const fetchMock = stub(201, { ...RUN, TargetKind: 'ModelEndpoint', CaptainId: null, ModelEndpointId: 'mep_1' });
    const run = await evaluateModelEndpoint('mep_1', 'Quick');
    const [url, init] = onlyCallArgs(fetchMock);
    expect(new URL(url, 'http://localhost').pathname).toBe('/api/v1/model-endpoints/mep_1/evaluate');
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body)).toEqual({ Depth: 'Quick' });
    expect(run.targetKind).toBe('ModelEndpoint');
  });

  it('sends depth Full with only the Full options that were given', async () => {
    const fetchMock = stub(201, { ...RUN, Depth: 'Full' });
    const run = await evaluateCaptain('cpt_1', 'Full', { evaluatorCaptainId: 'cpt_eval', packs: ['csharp'], personas: [], runCount: 1 });
    const [url, init] = onlyCallArgs(fetchMock);
    expect(new URL(url, 'http://localhost').pathname).toBe('/api/v1/captains/cpt_1/evaluate');
    expect(JSON.parse(init.body)).toEqual({ Depth: 'Full', EvaluatorCaptainId: 'cpt_eval', Packs: ['csharp'], RunCount: 1 });
    expect(run.depth).toBe('Full');
  });

  it('surfaces the 403 for a Full Evaluate by a non-admin', async () => {
    // Attach the fetch recorder before the action and verify the rejected request was actually sent.
    const fetchMock = stub(403, { Error: 'Forbidden', Message: 'Only an admin can run a Full Evaluate.' });
    await expect(evaluateModelEndpoint('mep_1', 'Full')).rejects.toThrow('Only an admin can run a Full Evaluate.');
    const [url, init] = onlyCallArgs(fetchMock);
    expect(new URL(url, 'http://localhost').pathname).toBe('/api/v1/model-endpoints/mep_1/evaluate');
    expect(JSON.parse(init.body)).toEqual({ Depth: 'Full' });
  });

  it('cancels a run and reads back the partial Full report', async () => {
    const fetchMock = stub(200, {
      ...RUN, Depth: 'Full', Status: 'Cancelled',
      Report: { RunId: 'mpr_1', Complete: false, IncompleteReason: 'Cancelled by an administrator.', Narrative: { Status: 'Skipped' } },
    });
    const run = await cancelModelProbeRun('mpr 1');
    const [url, init] = onlyCallArgs(fetchMock);
    expect(new URL(url, 'http://localhost').pathname).toBe('/api/v1/model-probe-runs/mpr%201/cancel');
    expect(init.method).toBe('POST');
    expect(run.status).toBe('Cancelled');
    expect(run.report?.complete).toBe(false);
    expect(run.report?.narrative.status).toBe('Skipped');
  });

  it('surfaces a 404 when cancelling an unknown run', async () => {
    const fetchMock = stub(404, { Error: 'NotFound', Message: 'Model probe run not found.' });
    await expect(cancelModelProbeRun('mpr_missing')).rejects.toThrow('Model probe run not found.');
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('gets one run with camelized results and metrics, and surfaces a 404', async () => {
    const fetchMock = stub(200, {
      ...RUN, Status: 'Completed', PassedCount: 1, CouldNotRunCount: 1,
      Results: [
        { Id: 'mpres_1', RunId: 'mpr_1', Probe: 'AsciiFormat', Outcome: 'Passed', Summary: '0 violations', Metrics: { Violations: 0, EmDashes: 0 } },
        { Id: 'mpres_2', RunId: 'mpr_1', Probe: 'ServedContext', Outcome: 'CouldNotRun', Reason: 'vendor CLI does not expose served context', Metrics: {} },
      ],
    });
    const run = await getModelProbeRun('mpr_1');
    const [url, init] = onlyCallArgs(fetchMock);
    expect(new URL(url, 'http://localhost').pathname).toBe('/api/v1/model-probe-runs/mpr_1');
    expect(init.method).toBe('GET');
    expect(run.results[0]).toMatchObject({ probe: 'AsciiFormat', outcome: 'Passed', metrics: { violations: 0, emDashes: 0 } });
    expect(run.results[1]).toMatchObject({ outcome: 'CouldNotRun', reason: 'vendor CLI does not expose served context' });

    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(jsonResponse(404, { Error: 'NotFound', Message: 'Model probe run not found.' })));
    await expect(getModelProbeRun('mpr_x')).rejects.toThrow('Model probe run not found.');
  });

  it('lists runs with filters on the querystring and drops empty ones', async () => {
    const fetchMock = stub(200, { Success: true, PageNumber: 2, PageSize: 10, TotalPages: 2, TotalRecords: 11, Objects: [RUN] });
    const page = await listModelProbeRuns({ captainId: 'cpt_1', modelEndpointId: null, status: 'Completed', pageNumber: 2, pageSize: 10 });
    const [url, init] = onlyCallArgs(fetchMock);
    const parsed = new URL(url, 'http://localhost');
    expect(parsed.pathname).toBe('/api/v1/model-probe-runs');
    expect(Object.fromEntries(parsed.searchParams)).toEqual({ captainId: 'cpt_1', status: 'Completed', pageNumber: '2', pageSize: '10' });
    expect(init.method).toBe('GET');
    expect(page.totalRecords).toBe(11);
    expect(page.objects[0]).toMatchObject({ id: 'mpr_1', results: [] });
  });

  it('lists everything with no query, and an empty page stays empty', async () => {
    const fetchMock = stub(200, { Success: true, PageNumber: 1, PageSize: 100, TotalPages: 0, TotalRecords: 0, Objects: [] });
    const page = await listModelProbeRuns();
    expect(onlyCallArgs(fetchMock)[0]).toMatch(/\/api\/v1\/model-probe-runs$/);
    expect(page.objects).toEqual([]);
  });

  it('rejects a failed list response instead of treating it as an empty page', async () => {
    const fetchMock = stub(500, { Error: 'InternalError', Message: 'Probe storage unavailable.' });
    await expect(listModelProbeRuns()).rejects.toThrow('Probe storage unavailable.');
    expect(new URL(onlyCallArgs(fetchMock)[0], 'http://localhost').pathname).toBe('/api/v1/model-probe-runs');
  });
});
