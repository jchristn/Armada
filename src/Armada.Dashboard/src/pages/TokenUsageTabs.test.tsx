import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import TokenUsage from './TokenUsage';
import { getTokenUsage, getUsageLatest, getUsageMissions, getUsageSeries, listCaptains } from '../api/client';
import type { UsageSeriesPoint, UsageSeriesResult } from '../types/models';

vi.mock('../api/client', () => ({
  getTokenUsage: vi.fn(), listCaptains: vi.fn(), getUsageSeries: vi.fn(), getUsageMissions: vi.fn(), getUsageLatest: vi.fn(),
}));
vi.mock('../context/LocaleContext', async () => {
  const { translateTemplate } = await import('../i18n/runtime');
  const value = {
    locale: 'en',
    t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
    formatDateTime: (v: string | null | undefined) => v ?? '',
    formatRelativeTime: (v: string | null | undefined) => (v ? 'recently' : ''),
  };
  return { useLocale: () => value };
});

const summary = {
  fromUtc: null, toUtc: null, bucketMinutes: 15, recordCount: 1, estimatedCount: 0, inputTokens: 1000, outputTokens: 200, cachedTokens: 600, totalTokens: 1200,
  buckets: [{ bucketStartUtc: new Date().toISOString(), inputTokens: 1000, outputTokens: 200, cachedTokens: 600, totalTokens: 1200, models: [{ model: 'opus', inputTokens: 1000, outputTokens: 200, cachedTokens: 600, totalTokens: 1200 }] }],
  byModel: [{ model: 'opus', inputTokens: 1000, outputTokens: 200, cachedTokens: 600, totalTokens: 1200 }],
};

const coverage = { unattributedRecords: 0, excludedUnattributedRecords: 0, nonMissionRecords: 0, missionRecords: 0, knownCostRecords: 0, seriesTruncated: false, warning: null };

function series(metric: 'limits' | 'tokens', from: string): UsageSeriesResult {
  const start = Date.parse(from);
  const p: UsageSeriesPoint = {
    bucketStartUtc: new Date(start).toISOString(), bucketEndUtc: new Date(start + 300000).toISOString(), lastPercent: metric === 'limits' ? 42 : null,
    minPercent: null, maxPercent: null, meanPercent: null, lastResetUtc: null, observedUtc: null, observationAgeSeconds: null, observationCount: 1, status: 'Ok',
    inputTokens: 10, outputTokens: 5, cachedTokens: 0, totalTokens: 15, reportedCost: null, costCurrency: null, estimated: false,
  };
  return {
    fromUtc: from, toUtc: from, bucketMinutes: 5, resolution: 'raw', groupBy: metric === 'limits' ? 'account' : 'provider', coverage,
    series: [metric === 'limits'
      ? { provider: 'anthropic', accountKey: 'acct_sel', hostId: 'hbr_sel', runtime: 'ClaudeCode', window: '5h', model: null, costCurrency: null, points: [p] }
      : { provider: 'anthropic', accountKey: null, hostId: null, runtime: null, window: null, model: null, costCurrency: null, points: [p] }],
  };
}

function Where() {
  const location = useLocation();
  return <div data-testid="where">{location.search}</div>;
}

function renderAt(url: string) {
  return render(<MemoryRouter initialEntries={[url]}><Routes><Route path="/activity" element={<><TokenUsage /><Where /></>} /></Routes></MemoryRouter>);
}

describe('Token usage tabs', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(getTokenUsage).mockResolvedValue(summary as never);
    vi.mocked(listCaptains).mockResolvedValue({ objects: [] } as never);
    vi.mocked(getUsageSeries).mockImplementation(async (filter) => series(filter.metric, filter.from));
    vi.mocked(getUsageMissions).mockResolvedValue({ success: true, pageNumber: 1, pageSize: 25, totalPages: 1, totalRecords: 0, objects: [], totalMs: 1, coverage });
    vi.mocked(getUsageLatest).mockResolvedValue({ success: true, pageNumber: 1, pageSize: 25, totalPages: 1, totalRecords: 0, objects: [], totalMs: 1 });
  });

  it('defaults to the existing Tokens charts and keeps the tab in the URL', async () => {
    renderAt('/activity');
    expect(screen.getByRole('tab', { name: 'Tokens' })).toHaveAttribute('aria-selected', 'true');
    await waitFor(() => expect(screen.getByText('Usage by model')).toBeInTheDocument());
    expect(getUsageSeries).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('tab', { name: 'Overview' }));
    expect(screen.getByTestId('where')).toHaveTextContent('tab=overview');
    expect(screen.getByRole('tab', { name: 'Overview' })).toHaveAttribute('aria-selected', 'true');
    fireEvent.keyDown(screen.getByRole('tab', { name: 'Overview' }), { key: 'ArrowRight' });
    expect(screen.getByTestId('where')).toHaveTextContent('tab=limits');
  });

  it('keeps the range in the URL and drills into exactly the selected bucket', async () => {
    renderAt('/activity?tab=overview');
    await waitFor(() => expect(getUsageSeries).toHaveBeenCalledTimes(2));
    const metrics = vi.mocked(getUsageSeries).mock.calls.map((c) => `${c[0].metric}:${c[0].groupBy}:${c[0].bucketMinutes}`).sort();
    expect(metrics).toEqual(['limits:account:5', 'tokens:provider:5']);
    fireEvent.change(screen.getByRole('combobox', { name: 'Range' }), { target: { value: '7d' } });
    expect(screen.getByTestId('where')).toHaveTextContent('range=7d');
    await waitFor(() => expect(getUsageSeries).toHaveBeenCalledWith(expect.objectContaining({ metric: 'limits', bucketMinutes: 60 })));

    fireEvent.click(await screen.findByRole('button', { name: 'Show chart data table' }));
    const row = within(screen.getByRole('table')).getAllByRole('row').find((r) => r.textContent?.includes('acct_sel')) as HTMLElement;
    fireEvent.click(within(row).getByRole('button', { name: /Show missions for/ }));
    await waitFor(() => expect(getUsageMissions).toHaveBeenCalledTimes(1));
    const limitsCall = vi.mocked(getUsageSeries).mock.calls.filter((c) => c[0].metric === 'limits' && c[0].bucketMinutes === 60).pop()![0];
    expect(vi.mocked(getUsageMissions).mock.calls[0][0]).toMatchObject({
      from: limitsCall.from, to: new Date(Date.parse(limitsCall.from) + 300000).toISOString(), provider: 'anthropic', accountKey: 'acct_sel', hostId: 'hbr_sel',
    });
    expect(screen.getByTestId('where')).toHaveTextContent('bucketAccountKey=acct_sel');
    expect(await screen.findByText('No completed missions in this bucket')).toBeInTheDocument();
  });

  it('retains filter values after a failed request and offers a retry', async () => {
    vi.mocked(getUsageLatest).mockRejectedValue(new Error('latest unavailable'));
    renderAt('/activity?tab=limits');
    await waitFor(() => expect(getUsageLatest).toHaveBeenCalledTimes(1));
    const provider = screen.getByRole('textbox', { name: 'Provider' });
    fireEvent.change(provider, { target: { value: 'anthropic' } });
    fireEvent.change(screen.getByRole('textbox', { name: 'Host' }), { target: { value: 'hbr_laptop' } });
    fireEvent.click(screen.getByRole('button', { name: 'Apply' }));
    await waitFor(() => expect(getUsageLatest).toHaveBeenCalledWith(expect.objectContaining({ provider: 'anthropic', hostId: 'hbr_laptop' })));
    expect(await screen.findByText('latest unavailable')).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Provider' })).toHaveValue('anthropic');
    expect(screen.getByRole('textbox', { name: 'Host' })).toHaveValue('hbr_laptop');
    expect(screen.getByTestId('where')).toHaveTextContent('provider=anthropic');
    const calls = vi.mocked(getUsageLatest).mock.calls.length;
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    await waitFor(() => expect(vi.mocked(getUsageLatest).mock.calls.length).toBe(calls + 1));
    expect(vi.mocked(getUsageLatest).mock.calls[calls][0]).toMatchObject({ provider: 'anthropic', hostId: 'hbr_laptop' });
    expect(screen.getByRole('textbox', { name: 'Provider' })).toHaveValue('anthropic');
  });

  it('opens the Limits tab with the provider history filters from a captain usage link', async () => {
    vi.mocked(getUsageLatest).mockResolvedValue({ success: true, pageNumber: 1, pageSize: 25, totalPages: 1, totalRecords: 0, objects: [], totalMs: 1 });
    renderAt('/activity?tab=limits&provider=anthropic&accountKey=acct_x&hostId=hbr_y');
    await waitFor(() => expect(getUsageLatest).toHaveBeenCalledWith({ provider: 'anthropic', accountKey: 'acct_x', hostId: 'hbr_y', pageNumber: 1, pageSize: 25 }));
    expect(screen.getByRole('textbox', { name: 'Account' })).toHaveValue('acct_x');
  });
});
