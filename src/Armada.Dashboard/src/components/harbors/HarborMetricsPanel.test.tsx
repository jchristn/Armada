import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import catalogSource from '../../../../Armada.Server/wwwroot/i18n/armada.json?raw';
import panelSource from './HarborMetricsPanel.tsx?raw';
import chartSource from './HarborBucketChart.tsx?raw';
import libSource from '../../lib/harborMetrics.ts?raw';
import harborsSource from '../../pages/Harbors.tsx?raw';
import tokenUsageSource from '../../pages/TokenUsage.tsx?raw';
import HarborMetricsPanel from './HarborMetricsPanel';
import { getHarborMetrics } from '../../api/client';
import { translateTemplate } from '../../i18n/runtime';
import type { I18nCatalog } from '../../i18n/runtime';
import type { HarborMetrics } from '../../types/models';
import { LINK_STATE_LABELS, formatDurationMs, linkStrip, sparklineRuns, tokenUsageLink, bucketTimes, hasAnyData } from '../../lib/harborMetrics';

vi.mock('../../api/client', () => ({
  getHarborMetrics: vi.fn(),
}));

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};

vi.mock('../../context/LocaleContext', () => ({
  useLocale: () => localeValue,
}));

function emptyMetrics(): HarborMetrics {
  const from = '2026-10-08T10:00:00Z';
  const buckets = [0, 1, 2].map((i) => new Date(Date.parse(from) + i * 30 * 60000).toISOString());
  return {
    harborId: 'hbr_test',
    harborName: 'Rig',
    range: '24h',
    fromUtc: from,
    toUtc: '2026-10-08T11:30:00Z',
    bucketMinutes: 30,
    bucketCount: 3,
    generatedUtc: '2026-10-08T11:20:00Z',
    connectionStatus: 'Connected',
    jobs: {
      buckets: buckets.map((b) => ({ bucketStartUtc: b, missionsFinished: 0, missionsFailed: 0, interactiveFinished: 0, interactiveFailed: 0 })),
      missionsFinished: 0, missionsFailed: 0, interactiveFinished: 0, interactiveFailed: 0, running: 0,
    },
    slots: { maxConcurrentJobs: 4, buckets: buckets.map((b) => ({ bucketStartUtc: b, peak: 0, average: 0 })), peak: 0, average: 0 },
    launchSpeed: [],
    link: {
      segments: [{ state: 'Unknown', startUtc: from, endUtc: '2026-10-08T11:20:00Z' }],
      roundTrip: buckets.map((b) => ({ bucketStartUtc: b, heartbeatCount: 0, sampleCount: 0, averageMs: null, maxMs: null })),
      connectedPercent: null, disconnects: 0, reconnectCount: null, lastReconnectUtc: null, roundTripMedianMs: null,
    },
    tokens: { buckets: buckets.map((b) => ({ bucketStartUtc: b, inputTokens: 0, outputTokens: 0, cachedTokens: 0, totalTokens: 0, series: [] })), series: [], inputTokens: 0, outputTokens: 0, cachedTokens: 0, totalTokens: 0, recordCount: 0, estimatedCount: 0 },
  };
}

function busyMetrics(): HarborMetrics {
  const m = emptyMetrics();
  m.jobs.buckets[0] = { ...m.jobs.buckets[0], missionsFinished: 2, missionsFailed: 1 };
  m.jobs.buckets[2] = { ...m.jobs.buckets[2], interactiveFinished: 3, interactiveFailed: 1 };
  m.jobs = { ...m.jobs, missionsFinished: 2, missionsFailed: 1, interactiveFinished: 3, interactiveFailed: 1, running: 1 };
  m.slots = { ...m.slots, buckets: m.slots.buckets.map((b, i) => ({ ...b, peak: i + 1, average: i + 0.5 })), peak: 3, average: 1.5 };
  m.launchSpeed = [{
    runtime: 'ClaudeCode', jobCount: 5, firstOutputCount: 5, firstOutputMedianMs: 1200, firstOutputP95Ms: 4800,
    durationCount: 5, durationMedianMs: 95000, durationP95Ms: 3_900_000, firstOutputMedianMsByBucket: [1000, null, 1400],
  }];
  m.link = {
    segments: [
      { state: 'Connected', startUtc: '2026-10-08T10:00:00Z', endUtc: '2026-10-08T10:40:00Z' },
      { state: 'Reconnecting', startUtc: '2026-10-08T10:40:00Z', endUtc: '2026-10-08T10:41:00Z' },
      { state: 'Down', startUtc: '2026-10-08T10:41:00Z', endUtc: '2026-10-08T10:50:00Z' },
      { state: 'Connected', startUtc: '2026-10-08T10:50:00Z', endUtc: '2026-10-08T11:20:00Z' },
    ],
    roundTrip: m.link.roundTrip.map((b, i) => ({ ...b, heartbeatCount: 8, sampleCount: 8, averageMs: 20 + i, maxMs: 40 + i })),
    connectedPercent: 87.5, disconnects: 1, reconnectCount: 2, lastReconnectUtc: '2026-10-08T10:50:00Z', roundTripMedianMs: 21,
  };
  const series = { runtime: 'ClaudeCode', model: 'claude-sonnet', inputTokens: 900, outputTokens: 100, cachedTokens: 0, totalTokens: 1000 };
  m.tokens = {
    ...m.tokens,
    buckets: m.tokens.buckets.map((b, i) => (i === 1 ? { ...b, inputTokens: 900, outputTokens: 100, totalTokens: 1000, series: [series] } : b)),
    series: [series], inputTokens: 900, outputTokens: 100, totalTokens: 1000, recordCount: 2, estimatedCount: 1,
  };
  return m;
}

function renderPanel() {
  return render(<MemoryRouter><HarborMetricsPanel harborId="hbr_test" /></MemoryRouter>);
}

describe('HarborMetricsPanel', () => {
  beforeEach(() => {
    vi.mocked(getHarborMetrics).mockReset();
  });

  it('renders every chart from the Admiral metrics', async () => {
    vi.mocked(getHarborMetrics).mockResolvedValue(busyMetrics());
    const { container } = renderPanel();

    await screen.findByText('Jobs over time');
    expect(getHarborMetrics).toHaveBeenCalledWith('hbr_test', '24h');
    expect(screen.getByText('Slot usage')).toBeInTheDocument();
    expect(screen.getByText('Heartbeat round trip')).toBeInTheDocument();
    expect(screen.getByText('Tokens by runtime and model')).toBeInTheDocument();

    // Stacked job bars: one rect per non-zero series value.
    expect(container.querySelectorAll('rect[data-series="missionsFinished"]').length).toBe(1);
    expect(container.querySelectorAll('rect[data-series="interactiveFailed"]').length).toBe(1);
    // Slot usage draws the capacity line.
    expect(container.querySelector('g[data-series="reference"]')).not.toBeNull();
    expect(screen.getByText('Max slots (4)', { selector: 'text' })).toBeInTheDocument();
    // Token chart stacks by runtime and model and links to the filtered Token Usage page.
    expect(container.querySelectorAll('rect[data-series="ClaudeCode|claude-sonnet"]').length).toBe(1);
    expect(screen.getByRole('link', { name: 'Open in Token Usage' })).toHaveAttribute('href', '/activity?source=tokens&harborId=hbr_test');
  });

  it('draws the link strip with a segment per state', async () => {
    vi.mocked(getHarborMetrics).mockResolvedValue(busyMetrics());
    const { container } = renderPanel();
    await screen.findByText('Link health');

    const states = Array.from(container.querySelectorAll('.harbor-metrics-strip rect[data-state]')).map((r) => r.getAttribute('data-state'));
    expect(states).toEqual(['Connected', 'Reconnecting', 'Down', 'Connected']);
    const down = container.querySelector('.harbor-metrics-strip rect[data-state="Down"]')!;
    expect(down.getAttribute('fill')).toBe('var(--red)');
    expect(screen.getByText('87.5%')).toBeInTheDocument();
  });

  it('lists launch speed per runtime with a sparkline', async () => {
    vi.mocked(getHarborMetrics).mockResolvedValue(busyMetrics());
    renderPanel();
    const table = await screen.findByRole('table', { name: 'Launch speed per runtime' });
    const row = within(table).getByText('ClaudeCode').closest('tr')!;
    expect(within(row).getByText('1.2s')).toBeInTheDocument();
    expect(within(row).getByText('4.8s')).toBeInTheDocument();
    expect(within(row).getByText('1m 35s')).toBeInTheDocument();
    expect(within(row).getByText('1h 5m')).toBeInTheDocument();
    expect(within(row).getByRole('img', { name: 'First output trend for ClaudeCode' })).toBeInTheDocument();
  });

  it('asks for the chosen range', async () => {
    vi.mocked(getHarborMetrics).mockResolvedValue(busyMetrics());
    renderPanel();
    await screen.findByText('Jobs over time');

    await userEvent.click(screen.getByRole('button', { name: 'Last Hour' }));
    await waitFor(() => expect(getHarborMetrics).toHaveBeenLastCalledWith('hbr_test', '1h'));
    await userEvent.click(screen.getByRole('button', { name: 'Last Week' }));
    await waitFor(() => expect(getHarborMetrics).toHaveBeenLastCalledWith('hbr_test', '7d'));
    expect(screen.getByRole('button', { name: 'Last Week' })).toHaveAttribute('aria-pressed', 'true');
  });

  it('says so when the Harbor has no activity', async () => {
    vi.mocked(getHarborMetrics).mockResolvedValue(emptyMetrics());
    renderPanel();
    expect(await screen.findByText('No activity on this Harbor in this time range.')).toBeInTheDocument();
    expect(screen.getByText('No jobs ended on this Harbor in this time range.')).toBeInTheDocument();
    expect(screen.getByText('No token usage for this time range')).toBeInTheDocument();
  });

  it('shows the error when the metrics cannot load', async () => {
    vi.mocked(getHarborMetrics).mockRejectedValue(new Error('Harbor not found'));
    renderPanel();
    expect(await screen.findByRole('alert')).toHaveTextContent('Harbor not found');
  });
});

describe('harborMetrics helpers', () => {
  it('places link segments on the window and clips them', () => {
    const parts = linkStrip([
      { state: 'Down', startUtc: '2026-10-08T09:00:00Z', endUtc: '2026-10-08T10:30:00Z' },
      { state: 'Connected', startUtc: '2026-10-08T10:30:00Z', endUtc: '2026-10-08T11:00:00Z' },
    ], '2026-10-08T10:00:00Z', '2026-10-08T11:00:00Z');
    expect(parts.map((p) => [p.state, p.start, p.width])).toEqual([['Down', 0, 0.5], ['Connected', 0.5, 0.5]]);
  });

  it('formats durations compactly', () => {
    expect(formatDurationMs(null)).toBe('-');
    expect(formatDurationMs(850)).toBe('850ms');
    expect(formatDurationMs(4200)).toBe('4.2s');
    expect(formatDurationMs(192000)).toBe('3m 12s');
    expect(formatDurationMs(3_900_000)).toBe('1h 5m');
  });

  it('splits sparklines at gaps', () => {
    expect(sparklineRuns([null, null], 10, 10)).toEqual([]);
    expect(sparklineRuns([1, 2, null, 3], 30, 10)).toHaveLength(2);
  });

  it('derives bucket times and the token usage link', () => {
    const m = emptyMetrics();
    expect(bucketTimes(m)).toEqual([0, 1, 2].map((i) => Date.parse('2026-10-08T10:00:00Z') + i * 1800000));
    expect(hasAnyData(m)).toBe(false);
    expect(hasAnyData(busyMetrics())).toBe(true);
    expect(tokenUsageLink('hbr a')).toBe('/activity?source=tokens&harborId=hbr%20a');
  });
});

describe('Harbor metrics i18n catalog', () => {
  const S = "'((?:[^'\\\\]|\\\\.)*)'";
  const call = new RegExp(`\\bt\\(\\s*${S}`, 'g');
  const label = new RegExp(`\\blabel:\\s*${S}`, 'g');
  const keys = new Set<string>();
  for (const src of [panelSource, chartSource]) for (const m of src.matchAll(call)) keys.add(m[1].replace(/\\'/g, "'"));
  for (const m of libSource.matchAll(label)) keys.add(m[1]);
  for (const value of Object.values(LINK_STATE_LABELS)) keys.add(value);
  // Strings this feature added to shared pages.
  const pageKeys: Array<{ source: string; keys: string[] }> = [
    { source: harborsSource, keys: ['Charts for', 'Harbor to chart'] },
    { source: tokenUsageSource, keys: ['Showing captains run on Harbor {{id}}', 'Show all'] },
  ];
  for (const page of pageKeys) for (const key of page.keys) keys.add(key);
  const catalog = JSON.parse(catalogSource) as I18nCatalog;

  it('finds the strings', () => {
    expect(keys.has('Jobs over time')).toBe(true);
    expect(keys.has('Reconnecting')).toBe(true);
    for (const page of pageKeys) for (const key of page.keys) expect(page.source).toContain(`'${key}'`);
  });

  it('has a translation for every string in every non-English locale', () => {
    const missing: string[] = [];
    for (const meta of catalog.supportedLocales) {
      if (meta.code === catalog.defaultLocale) continue;
      const pack = catalog.locales[meta.code] ?? {};
      for (const key of keys) {
        if (!pack.phrases?.[key] && !pack.terms?.[key] && !pack.sections?.[key]) missing.push(`${meta.code}: ${key}`);
      }
    }
    expect(missing).toEqual([]);
  });

  it('keeps placeholders intact', () => {
    const bad: string[] = [];
    for (const [code, pack] of Object.entries(catalog.locales)) {
      for (const key of keys) {
        const value = pack.phrases?.[key];
        if (!value) continue;
        const ph = (text: string) => (text.match(/\{\{[\w.]+\}\}/g) ?? []).sort().join(',');
        if (ph(key) !== ph(value)) bad.push(`${code}: ${key}`);
      }
    }
    expect(bad).toEqual([]);
  });
});
