import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import * as client from '@dashboard/api/client';
import type { Harbor, HarborMetrics } from '@dashboard/types/models';
import MoreLayout from '../app/(app)/(more)/_layout';
import ConfigurationRoute from '../app/(app)/(more)/configuration';
import { jobSeries, linkStateDurations, tokenSeries } from '../screens/configuration/HarborMetricsSection';
import { renderW4Routes, resetW4 } from '../test/w4';

jest.mock('@dashboard/api/client', () => require('../test/w4Client').autoMockClient());

const api = client as jest.Mocked<typeof client>;
const ROUTES = {
  '(more)/_layout': MoreLayout,
  '(more)/more': () => null,
  '(more)/configuration': ConfigurationRoute,
};
const t = (text: string) => text;

const HARBOR: Harbor = {
  id: 'hbr_1', tenantId: 'ten_1', userId: null, name: 'Laptop', capabilities: [], connectionStatus: 'Connected', maxConcurrentJobs: 4,
  enabled: true, protocolVersion: '1.0', osPlatform: 'macOS', architecture: 'arm64', lastSeenUtc: null, lastConnectedUtc: null,
  createdUtc: '2026-10-01T00:00:00Z', lastUpdateUtc: '2026-10-02T00:00:00Z',
};

function tokenRow(runtime: string, model: string, total: number) {
  return { runtime, model, inputTokens: total, outputTokens: 0, cachedTokens: 0, totalTokens: total };
}

function metrics(range: '1h' | '24h' | '7d' = '24h'): HarborMetrics {
  return {
    harborId: 'hbr_1', harborName: 'Laptop', range, fromUtc: '2026-10-08T10:00:00Z', toUtc: '2026-10-08T11:00:00Z',
    bucketMinutes: 30, bucketCount: 2, generatedUtc: '2026-10-08T10:45:00Z', connectionStatus: 'Connected',
    jobs: {
      buckets: [
        { bucketStartUtc: '2026-10-08T10:00:00Z', missionsFinished: 2, missionsFailed: 1, interactiveFinished: 3, interactiveFailed: 0 },
        { bucketStartUtc: '2026-10-08T10:30:00Z', missionsFinished: 0, missionsFailed: 0, interactiveFinished: 1, interactiveFailed: 1 },
      ],
      missionsFinished: 2, missionsFailed: 1, interactiveFinished: 4, interactiveFailed: 1, running: 1,
    },
    slots: {
      maxConcurrentJobs: 4, peak: 3, average: 1.25,
      buckets: [{ bucketStartUtc: '2026-10-08T10:00:00Z', peak: 3, average: 1.5 }, { bucketStartUtc: '2026-10-08T10:30:00Z', peak: 1, average: 1 }],
    },
    launchSpeed: [{
      runtime: 'ClaudeCode', jobCount: 5, firstOutputCount: 5, firstOutputMedianMs: 1200, firstOutputP95Ms: 4000,
      durationCount: 5, durationMedianMs: 60000, durationP95Ms: 120000, firstOutputMedianMsByBucket: [1200, null],
    }],
    link: {
      segments: [
        { state: 'Connected', startUtc: '2026-10-08T10:00:00Z', endUtc: '2026-10-08T10:30:00Z' },
        { state: 'Reconnecting', startUtc: '2026-10-08T10:30:00Z', endUtc: '2026-10-08T10:30:30Z' },
        { state: 'Connected', startUtc: '2026-10-08T10:30:30Z', endUtc: '2026-10-08T10:45:00Z' },
      ],
      roundTrip: [
        { bucketStartUtc: '2026-10-08T10:00:00Z', heartbeatCount: 120, sampleCount: 119, averageMs: 18.5, maxMs: 40 },
        { bucketStartUtc: '2026-10-08T10:30:00Z', heartbeatCount: 58, sampleCount: 58, averageMs: 21, maxMs: 33 },
      ],
      connectedPercent: 99.9, disconnects: 1, reconnectCount: 1, lastReconnectUtc: '2026-10-08T10:30:30Z', roundTripMedianMs: 20,
    },
    tokens: {
      buckets: [
        { bucketStartUtc: '2026-10-08T10:00:00Z', inputTokens: 700, outputTokens: 0, cachedTokens: 0, totalTokens: 700, series: [tokenRow('ClaudeCode', 'opus', 500), tokenRow('Codex', 'gpt', 200)] },
        { bucketStartUtc: '2026-10-08T10:30:00Z', inputTokens: 0, outputTokens: 0, cachedTokens: 0, totalTokens: 0, series: [] },
      ],
      series: [tokenRow('ClaudeCode', 'opus', 500), tokenRow('Codex', 'gpt', 200)],
      inputTokens: 700, outputTokens: 0, cachedTokens: 0, totalTokens: 700, recordCount: 3, estimatedCount: 1,
    },
  };
}

beforeEach(async () => {
  await resetW4();
});

describe('harbor metrics series', () => {
  it('splits jobs into missions and interactive launches per bucket', () => {
    const { series, buckets } = jobSeries(t, metrics());
    expect(series.map((s) => s.key)).toEqual(['missionsFinished', 'missionsFailed', 'interactiveFinished', 'interactiveFailed']);
    expect(buckets[0].values).toEqual([2, 1, 3, 0]);
    expect(buckets[1].values).toEqual([0, 0, 1, 1]);
  });

  it('stacks tokens by runtime and model and folds the rest into Other', () => {
    const m = metrics();
    const many = ['a', 'b', 'c', 'd', 'e', 'f'].map((model, i) => tokenRow('Codex', model, 60 - i * 10));
    m.tokens.series = many;
    m.tokens.buckets = [{ bucketStartUtc: '2026-10-08T10:00:00Z', inputTokens: 0, outputTokens: 0, cachedTokens: 0, totalTokens: 225, series: many }];
    const { series, buckets } = tokenSeries(t, m);
    expect(series.map((s) => s.label)).toEqual(['Codex / a', 'Codex / b', 'Codex / c', 'Codex / d', 'Codex / e', 'Other']);
    expect(buckets[0].values).toEqual([60, 50, 40, 30, 20, 25]);
  });

  it('measures the link timeline by state', () => {
    const d = linkStateDurations(metrics());
    expect(d.Connected).toBe(30 * 60000 + 14.5 * 60000);
    expect(d.Reconnecting).toBe(30000);
    expect(d.Down).toBe(0);
  });
});

describe('harbor detail metrics', () => {
  it('shows every chart for the selected range and reloads on a range change', async () => {
    api.listHarbors.mockResolvedValue([HARBOR]);
    api.getHarbor.mockResolvedValue(HARBOR);
    api.getHarborMetrics.mockImplementation(async (_id: string, range?: '1h' | '24h' | '7d') => metrics(range ?? '24h'));
    await renderW4Routes(ROUTES, '/configuration?tab=harbors');
    await waitFor(() => expect(screen.getByTestId('harbor-row-hbr_1')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('harbor-row-hbr_1')); });
    await waitFor(() => expect(screen.getByTestId('harbor-jobs-chart')).toBeTruthy());
    expect(api.getHarborMetrics).toHaveBeenCalledWith('hbr_1', '24h');
    expect(screen.getByTestId('harbor-slots-chart')).toBeTruthy();
    expect(screen.getByTestId('harbor-link-strip').props.accessibilityLabel).toContain('1 disconnects');
    expect(screen.getByTestId('harbor-rtt-chart')).toBeTruthy();
    expect(screen.getByTestId('harbor-speed-ClaudeCode').props.accessibilityLabel).toContain('5 jobs');
    expect(screen.getByTestId('harbor-tokens-chart')).toBeTruthy();

    // Screen readers step through the buckets of the jobs chart.
    await act(async () => { await fireEvent(screen.getByTestId('harbor-jobs-chart'), 'accessibilityAction', { nativeEvent: { actionName: 'increment' } }); });
    expect(screen.getByTestId('harbor-jobs-chart-tooltip')).toBeTruthy();
    expect(screen.getByText('Missions finished: 2')).toBeTruthy();

    await act(async () => { await fireEvent.press(screen.getByTestId('harbor-metrics-range-7d')); });
    await waitFor(() => expect(api.getHarborMetrics).toHaveBeenCalledWith('hbr_1', '7d'));
  });

  it('says why when the metrics cannot be loaded', async () => {
    api.listHarbors.mockResolvedValue([HARBOR]);
    api.getHarbor.mockResolvedValue(HARBOR);
    api.getHarborMetrics.mockRejectedValue(new client.ApiError('Harbor not found', 404, null));
    await renderW4Routes(ROUTES, '/configuration?tab=harbors');
    await waitFor(() => expect(screen.getByTestId('harbor-row-hbr_1')).toBeTruthy());
    await act(async () => { await fireEvent.press(screen.getByTestId('harbor-row-hbr_1')); });
    await waitFor(() => expect(screen.getByTestId('harbor-metrics-pending')).toBeTruthy());
    expect(screen.queryByTestId('harbor-jobs-chart')).toBeNull();
  });
});
