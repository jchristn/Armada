import { act, renderHook, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { evaluateVesselHealth, getJob, listJobs } from '../../api/client';
import type { Job } from '../../types/models';
import { describeEvaluationStart, HEALTH_JOB_NAME, useHealthEvaluation } from './useHealthEvaluation';

vi.mock('../../api/client', () => ({
  evaluateVesselHealth: vi.fn(),
  getJob: vi.fn(),
  listJobs: vi.fn(),
}));

function job(status: string, progress = 0, id = 'job_1'): Job {
  return {
    id,
    tenantId: 'ten_1',
    userId: null,
    name: HEALTH_JOB_NAME,
    kind: 'Report',
    status,
    progress,
    resultJson: status === 'Succeeded' ? '{"requested":2,"evaluated":2,"failed":0}' : null,
    errorReason: null,
    createdUtc: '2026-10-01T00:00:00Z',
    startedUtc: null,
    completedUtc: status === 'Succeeded' ? '2026-10-01T00:01:00Z' : null,
    lastUpdateUtc: '2026-10-01T00:01:00Z',
  };
}

describe('describeEvaluationStart', () => {
  it('202: success toast with the vessel count (plural aware)', () => {
    expect(describeEvaluationStart({ jobId: 'job_1', alreadyRunning: false, vesselCount: 12 })).toEqual({
      severity: 'success',
      key: 'Evaluation started for {{count}} vessels.',
      params: { count: 12 },
    });
    expect(describeEvaluationStart({ jobId: 'job_1', alreadyRunning: false, vesselCount: 1 }).key).toBe('Evaluation started for {{count}} vessel.');
  });

  it('409: warns that an evaluation is already running', () => {
    const message = describeEvaluationStart({ jobId: 'job_9', alreadyRunning: true, vesselCount: 0 });
    expect(message.severity).toBe('warning');
    expect(message.key).toMatch(/already running/);
  });
});

describe('useHealthEvaluation', () => {
  beforeEach(() => {
    vi.mocked(listJobs).mockResolvedValue({ success: true, pageNumber: 1, pageSize: 100, totalPages: 1, totalRecords: 0, totalMs: 0, objects: [] });
  });
  afterEach(() => vi.clearAllMocks());

  it('tracks a started (202) job until it succeeds, then calls onFinished once', async () => {
    vi.mocked(evaluateVesselHealth).mockResolvedValue({ jobId: 'job_1', alreadyRunning: false, vesselCount: 2 });
    vi.mocked(getJob)
      .mockResolvedValueOnce(job('Running', 50))
      .mockResolvedValueOnce(job('Succeeded', 100));
    const onFinished = vi.fn();
    const { result } = renderHook(() => useHealthEvaluation({ onFinished, pollMs: 5 }));

    await act(async () => {
      const start = await result.current.start({ VesselIds: ['vsl_1'], Force: true });
      expect(start.alreadyRunning).toBe(false);
    });
    expect(evaluateVesselHealth).toHaveBeenCalledWith({ VesselIds: ['vsl_1'], Force: true });
    expect(result.current.running).toBe(true);

    await waitFor(() => expect(onFinished).toHaveBeenCalledTimes(1));
    expect(onFinished.mock.calls[0][0].status).toBe('Succeeded');
    expect(result.current.running).toBe(false);
    expect(result.current.activeJob).toBeNull();
    expect(result.current.lastJob?.status).toBe('Succeeded');
  });

  it('on 409 still tracks the already-running job', async () => {
    vi.mocked(evaluateVesselHealth).mockResolvedValue({ jobId: 'job_running', alreadyRunning: true, vesselCount: 0 });
    vi.mocked(getJob).mockResolvedValue(job('Running', 30, 'job_running'));
    const { result } = renderHook(() => useHealthEvaluation({ pollMs: 1000 }));

    await act(async () => {
      const start = await result.current.start();
      expect(start.alreadyRunning).toBe(true);
    });
    await waitFor(() => expect(getJob).toHaveBeenCalledWith('job_running'));
    await waitFor(() => expect(result.current.activeJob?.progress).toBe(30));
    expect(result.current.running).toBe(true);
  });

  it('discover() picks up a scheduled evaluation already in progress', async () => {
    vi.mocked(listJobs).mockResolvedValue({
      success: true, pageNumber: 1, pageSize: 100, totalPages: 1, totalRecords: 2, totalMs: 0,
      objects: [{ ...job('Running', 10, 'job_sched') }, { ...job('Succeeded', 100, 'job_old') }],
    });
    vi.mocked(getJob).mockResolvedValue(job('Running', 10, 'job_sched'));
    const { result } = renderHook(() => useHealthEvaluation({ pollMs: 1000 }));
    await act(async () => { await result.current.discover(); });
    expect(result.current.running).toBe(true);
    await waitFor(() => expect(getJob).toHaveBeenCalledWith('job_sched'));
  });

  it('discover() records the last finished evaluation when none is running', async () => {
    vi.mocked(listJobs).mockResolvedValue({
      success: true, pageNumber: 1, pageSize: 100, totalPages: 1, totalRecords: 1, totalMs: 0,
      objects: [{ ...job('Succeeded', 100, 'job_old') }],
    });
    const { result } = renderHook(() => useHealthEvaluation());
    await act(async () => { await result.current.discover(); });
    expect(result.current.running).toBe(false);
    expect(result.current.lastJob?.id).toBe('job_old');
  });

  it('propagates a failed start so the caller can show an error', async () => {
    vi.mocked(evaluateVesselHealth).mockRejectedValue(new Error('403: forbidden'));
    const { result } = renderHook(() => useHealthEvaluation());
    await act(async () => {
      await expect(result.current.start()).rejects.toThrow('403');
    });
    expect(result.current.running).toBe(false);
    expect(result.current.starting).toBe(false);
  });
});
