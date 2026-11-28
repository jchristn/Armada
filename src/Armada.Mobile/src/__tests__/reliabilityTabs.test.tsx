import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import * as client from '@dashboard/api/client';
import type { ConcurrencyMetrics } from '@dashboard/types/models';
import type { BatteryRun, GateReport, ReliabilitySummary, ReliabilitySummaryRow } from '@dashboard/types/reliability';
import catalog from '../../../Armada.Server/wwwroot/i18n/armada.json';
import { ConcurrencyTab, concurrencyWindow, groupTotals } from '../screens/activity/ConcurrencyTab';
import { BatteryRunsTab, batteryRunTone, isBatteryRunOpen } from '../screens/captains/BatteryRunsTab';
import { completionTone, ReliabilityTab, reliabilityRowKey, summaryPage } from '../screens/captains/ReliabilityTab';
import { mockRouter } from '../test/routerMock';
import { page, renderW4, resetW4 } from '../test/w4';

jest.mock('@dashboard/api/client', () => require('../test/w4Client').autoMockClient());
jest.mock('expo-router', () => require('../test/routerMock').routerMockFactory());

const api = client as unknown as Record<string, jest.Mock>;

function row(over: Partial<ReliabilitySummaryRow> = {}): ReliabilitySummaryRow {
  return {
    model: 'qwen3.8', persona: 'Worker', attempts: 20, completed: 18, failed: 2, unclassified: 1, repairs: 3, escalations: 1,
    completionRate: 0.9, unclassifiedRate: 0.05, failureKinds: { Timeout: 2 }, runtimeFailureKinds: {}, medianDurationMs: 60000, ...over,
  } as ReliabilitySummaryRow;
}

function summary(rows: ReliabilitySummaryRow[]): ReliabilitySummary {
  return {
    fromUtc: '2026-11-12T00:00:00Z', toUtc: '2026-11-26T00:00:00Z', outcomesRead: 20, truncated: false,
    totals: row({ model: '', persona: '' }), pageNumber: 1, pageSize: 50, totalRecords: rows.length, totalPages: 1, rows,
  };
}

function run(over: Partial<BatteryRun> = {}): BatteryRun {
  return {
    id: 'bat_1', captainId: 'cpt_1', captainName: 'Qwen', runCount: 1, status: 'Running', isEvaluation: true, model: 'qwen3.8',
    totalTasks: 10, passedCount: 4, failedCount: 1, couldNotRunCount: 0, pendingEvaluationCount: 0,
    createdUtc: '2026-11-26T00:00:00Z', lastUpdateUtc: '2026-11-26T00:00:00Z', ...over,
  } as BatteryRun;
}

const gateReport: GateReport = {
  id: 'gate_1', startedUtc: '2026-11-25T00:00:00Z', finishedUtc: '2026-11-25T01:00:00Z', armadaVersion: '1.5.0', packs: [],
  pipelines: [], passed: true,
  measures: [{ id: 'G1', name: 'Completion', value: '92%', threshold: '>= 90%', status: 'Pass', detail: '' }],
};

beforeEach(async () => {
  await resetW4();
  jest.clearAllMocks();
});

describe('reliability helpers', () => {
  it('maps the summary to a page and keys rows', () => {
    const s = summary([row()]);
    expect(summaryPage(s)).toEqual({ objects: s.rows, totalRecords: 1, totalPages: 1, pageNumber: 1 });
    expect(reliabilityRowKey({ model: 'm', persona: 'p' })).toBe('m|p');
  });

  it('tones completion rates', () => {
    expect(completionTone(0.95, 10)).toBe('success');
    expect(completionTone(0.7, 10)).toBe('warning');
    expect(completionTone(0.2, 10)).toBe('error');
    expect(completionTone(null, 0)).toBe('pending');
  });

  it('battery run status helpers', () => {
    expect(isBatteryRunOpen({ status: 'Queued' })).toBe(true);
    expect(isBatteryRunOpen({ status: 'Completed' })).toBe(false);
    expect(batteryRunTone('Failed')).toBe('failed');
    expect(batteryRunTone('Queued')).toBe('pending');
  });

  it('concurrency window and group totals', () => {
    const now = new Date('2026-11-26T12:00:00Z');
    expect(concurrencyWindow('6h', now)).toEqual({ from: '2026-11-26T06:00:00.000Z', to: '2026-11-26T12:00:00.000Z' });
    const metrics = {
      buckets: [{ startUtc: 'a', missionsWaiting: 1, byGate: [{ gate: 'CaptainSlot', seconds: 120, missions: 1 }, { gate: 'Dependency', seconds: 60, missions: 1 }] }],
    } as unknown as ConcurrencyMetrics;
    expect(groupTotals(metrics)).toEqual({ captain: 2, vessel: 0, capacity: 0, readiness: 1 });
  });
});

describe('Reliability tab', () => {
  it('shows rows, totals and the latest gate report, and refetches on a window change', async () => {
    api.getReliabilitySummary.mockResolvedValue(summary([row()]));
    api.getGateReport.mockResolvedValue(gateReport);
    await renderW4(<ReliabilityTab />);
    expect(await screen.findByTestId('reliability-row-qwen3.8|Worker')).toBeTruthy();
    expect(screen.getByTestId('reliability-totals')).toBeTruthy();
    expect(await screen.findByTestId('gate-report')).toBeTruthy();
    expect(screen.getByTestId('gate-measure-G1')).toBeTruthy();
    expect(api.getGateReport).toHaveBeenCalledWith('latest');
    await act(async () => { fireEvent.press(screen.getByTestId('reliability-window-30')); });
    await waitFor(() => expect(api.getReliabilitySummary).toHaveBeenCalledTimes(2));
    expect(api.getReliabilitySummary.mock.calls[1][0]).toMatchObject({ sort: 'Attempts', descending: true, includeEvaluation: false });
  });

  it('hides the gate report when none is readable', async () => {
    api.getReliabilitySummary.mockResolvedValue(summary([]));
    api.getGateReport.mockRejectedValue(Object.assign(new Error('Forbidden'), { status: 403 }));
    await renderW4(<ReliabilityTab />, 'user');
    expect(await screen.findByText('No stage outcomes match the current filters.')).toBeTruthy();
    expect(screen.queryByTestId('gate-report')).toBeNull();
  });
});

describe('Battery Runs tab', () => {
  it('lists runs, opens the captain, and cancels an open run as an administrator', async () => {
    api.listBatteryRuns.mockResolvedValue(page([run(), run({ id: 'bat_2', status: 'Completed' })]));
    api.cancelBatteryRun.mockResolvedValue(run({ status: 'Cancelled' }));
    await renderW4(<BatteryRunsTab />, 'tenantAdmin');
    expect(await screen.findByTestId('battery-run-bat_1')).toBeTruthy();
    expect(screen.queryByTestId('battery-run-cancel-bat_2')).toBeNull();
    await act(async () => { fireEvent.press(screen.getByTestId('battery-run-bat_1')); });
    expect(mockRouter.push).toHaveBeenCalledWith('/captains/cpt_1');
    await act(async () => { fireEvent.press(screen.getByTestId('battery-run-cancel-bat_1')); });
    await act(async () => { fireEvent.press(screen.getByTestId('battery-run-cancel-dialog-confirm')); });
    await waitFor(() => expect(api.cancelBatteryRun).toHaveBeenCalledWith('bat_1', 'Cancelled from the mobile app'));
    await waitFor(() => expect(api.listBatteryRuns).toHaveBeenCalledTimes(2));
  });

  it('offers no cancel to a regular user', async () => {
    api.listBatteryRuns.mockResolvedValue(page([run()]));
    await renderW4(<BatteryRunsTab />, 'user');
    expect(await screen.findByTestId('battery-run-bat_1')).toBeTruthy();
    expect(screen.queryByTestId('battery-run-cancel-bat_1')).toBeNull();
  });
});

describe('Concurrency tab', () => {
  it('shows the slot wait tiles and per-gate totals', async () => {
    api.listVessels.mockResolvedValue(page([]));
    api.getConcurrencyMetrics.mockResolvedValue({
      fromUtc: 'a', toUtc: 'b', vesselId: null, captainId: null, bucketSeconds: 3600, truncated: false,
      slotWait: { missions: 2, p50Seconds: 30, p95Seconds: 90, maxSeconds: 120 },
      byGate: [{ gate: 'CaptainSlot', seconds: 180, missions: 2 }],
      buckets: [{ startUtc: '2026-11-26T10:00:00Z', missionsWaiting: 2, byGate: [{ gate: 'CaptainSlot', seconds: 180, missions: 2 }] }],
    } as unknown as ConcurrencyMetrics);
    await renderW4(<ConcurrencyTab />);
    expect(await screen.findByTestId('concurrency-p50')).toBeTruthy();
    expect(screen.getByTestId('concurrency-gate-CaptainSlot')).toBeTruthy();
    expect(screen.getByTestId('concurrency-group-captain')).toBeTruthy();
  });
});

describe('translations', () => {
  it('every new mobile string is in every locale', () => {
    const keys = [
      'Concurrency metrics unavailable.', 'Failed to load the gate report.', 'Longest slot wait', 'Missions that waited', 'Missions waiting',
      'No waits recorded in this window.', 'Opens the captain', 'Slot wait p50', 'Slot wait p95', 'Sort', 'Wait by gate', 'Wait time by cause',
      '{{count}} attempts', '{{count}} missions', '{{count}} missions waiting', 'Reliability', 'Battery Runs', 'Latest gate report',
      'Cancel battery run', 'Cancel run', 'Keep running', 'Include evaluations',
    ];
    const locales = (catalog as { locales: Record<string, { terms?: Record<string, string>; phrases?: Record<string, string>; sections?: Record<string, string> }> }).locales;
    const missing: string[] = [];
    for (const [code, pack] of Object.entries(locales)) {
      for (const key of keys) {
        if (!pack.terms?.[key] && !pack.phrases?.[key] && !pack.sections?.[key]) missing.push(`${code}: ${key}`);
      }
    }
    expect(missing).toEqual([]);
  });
});
