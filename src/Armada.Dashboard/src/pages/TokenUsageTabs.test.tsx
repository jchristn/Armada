import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import Activity from './Activity';
vi.mock('./History', () => ({ default: () => <div>History</div> }));
vi.mock('./RequestHistory', () => ({ default: () => null }));
vi.mock('./Events', () => ({ default: () => null }));
vi.mock('./Signals', () => ({ default: () => null }));
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
  return render(<MemoryRouter initialEntries={[url]}><Routes><Route path="/activity" element={<><Activity /><Where /></>} /></Routes></MemoryRouter>);
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

  it('keeps guidance collapsed and omits the legacy attribution notice without applying filters', async () => {
    const warning = 'Legacy records without captured membership are correlation only; account-specific views exclude them.';
    vi.mocked(getUsageSeries).mockImplementation(async filter => ({ ...series(filter.metric, filter.from), coverage: { ...coverage, warning } }));
    renderAt('/activity?source=usage-overview&provider=anthropic');
    await screen.findByRole('button', { name: 'Show chart data table' });
    expect(screen.queryByText('Coverage')).not.toBeInTheDocument();
    const summary = screen.getByText('About usage');
    const help = summary.closest('details')!;
    const note = within(help).getByText(/Account usage reported by captains/);
    expect(note).not.toBeVisible();
    expect(screen.queryByText(warning)).not.toBeInTheDocument();
    const query = screen.getByTestId('where').textContent;
    fireEvent.click(summary);
    expect(help).toHaveAttribute('open');
    expect(note).toBeVisible();
    expect(within(help).getByText(/Select a point, a bar/)).toBeVisible();
    expect(screen.getByTestId('where').textContent).toBe(query);
    expect(getUsageSeries).toHaveBeenCalledTimes(2);
  });

  it('refreshes both charts, filter choices and the selected bucket without applying a draft or changing the URL', async () => {
    renderAt('/activity?source=usage-overview&range=7d&provider=anthropic&from=2026-10-01T00:00:00Z&to=2026-10-01T01:00:00Z&bucketProvider=anthropic');
    await screen.findByRole('button', { name: 'Show chart data table' });
    await screen.findByText('No completed missions in this bucket');
    const query = screen.getByTestId('where').textContent;
    const optionsBefore = vi.mocked(getUsageLatest).mock.calls.length;
    const missionsBefore = vi.mocked(getUsageMissions).mock.calls.length;
    fireEvent.change(screen.getByRole('combobox', { name: 'Provider' }), { target: { value: '' } });
    const releases: Array<() => void> = [];
    vi.mocked(getUsageSeries).mockImplementation(filter => new Promise(resolve => { releases.push(() => resolve(series(filter.metric, filter.from))); }));
    const refresh = screen.getByRole('button', { name: 'Refresh captain usage' });
    expect(refresh).toHaveClass('refresh-btn');
    expect(refresh).toHaveAttribute('type', 'button');
    fireEvent.click(refresh);
    expect(refresh).toHaveClass('refreshing');
    await waitFor(() => expect(releases).toHaveLength(2));
    await waitFor(() => expect(getUsageLatest).toHaveBeenCalledTimes(optionsBefore + 2));
    await waitFor(() => expect(getUsageMissions).toHaveBeenCalledTimes(missionsBefore + 1));
    await act(async () => { releases[0](); });
    expect(refresh).toHaveClass('refreshing');
    await act(async () => { releases[1](); });
    await waitFor(() => expect(refresh).toHaveClass('refresh-success'));
    expect(screen.getByTestId('where').textContent).toBe(query);
    expect(screen.getByRole('combobox', { name: 'Provider' })).toHaveValue('');
    expect(getUsageSeries).toHaveBeenLastCalledWith(expect.objectContaining({ provider: 'anthropic', bucketMinutes: 60 }));
  });

  it('keeps filters and makes refresh available after a failed overview request', async () => {
    renderAt('/activity?source=usage-overview&provider=anthropic');
    await screen.findByRole('button', { name: 'Show chart data table' });
    vi.mocked(getUsageSeries).mockRejectedValueOnce(new Error('offline'));
    const refresh = screen.getByRole('button', { name: 'Refresh captain usage' });
    fireEvent.click(refresh);
    await screen.findByText('offline');
    await waitFor(() => expect(refresh).not.toHaveClass('refreshing'));
    expect(refresh).not.toHaveClass('refresh-success');
    expect(screen.getByTestId('where')).toHaveTextContent('provider=anthropic');
    fireEvent.click(refresh);
    await waitFor(() => expect(refresh).toHaveClass('refresh-success'));
  });

  it('uses one Activity tab row for all usage views', async () => {
    renderAt('/activity?source=tokens');
    await screen.findByText('Usage by model');
    expect(screen.getAllByRole('tablist')).toHaveLength(1);
    expect(getUsageSeries).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('tab', { name: 'Captain Usage' }));
    await screen.findByRole('combobox', { name: 'Range' });
    expect(screen.getByTestId('where')).toHaveTextContent('source=usage-overview');
    expect(screen.getAllByRole('tablist')).toHaveLength(1);
    expect(screen.getByRole('combobox', { name: 'Range' }).closest('form')).toBe(screen.getByRole('combobox', { name: 'Window' }).closest('form'));
    expect(screen.queryByRole('tab', { name: 'Account Limits' })).not.toBeInTheDocument();
    const accountTable = await screen.findByRole('table', { name: 'Limits grouped by account and host' });
    const charts = screen.getByRole('heading', { name: 'Limit utilization (%)' }).closest('.usage-chart')!;
    expect(charts.compareDocumentPosition(accountTable) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('preserves legacy nested-tab links and their filters', async () => {
    renderAt('/activity?source=tokens&tab=overview&provider=anthropic');
    await screen.findByRole('combobox', { name: 'Window' });
    expect(screen.getByTestId('where')).toHaveTextContent('source=usage-overview');
    expect(screen.getByTestId('where')).toHaveTextContent('provider=anthropic');
    expect(screen.getByTestId('where')).not.toHaveTextContent('tab=');
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
    const row = within(screen.getByRole('table', { name: 'Chart data: limits are grouped by observation time, tokens by completion time.' })).getAllByRole('row').find((r) => r.textContent?.includes('acct_sel')) as HTMLElement;
    fireEvent.click(within(row).getByRole('button', { name: /Show missions for/ }));
    await waitFor(() => expect(getUsageMissions).toHaveBeenCalledTimes(1));
    const limitsCall = vi.mocked(getUsageSeries).mock.calls.filter((c) => c[0].metric === 'limits' && c[0].bucketMinutes === 60).pop()![0];
    expect(vi.mocked(getUsageMissions).mock.calls[0][0]).toMatchObject({
      from: limitsCall.from, to: new Date(Date.parse(limitsCall.from) + 300000).toISOString(), provider: 'anthropic', accountKey: 'acct_sel', hostId: 'hbr_sel',
    });
    expect(screen.getByTestId('where')).toHaveTextContent('bucketAccountKey=acct_sel');
    expect(await screen.findByText('No completed missions in this bucket')).toBeInTheDocument();
  });

  it('offers captured identities and readable windows as dropdowns and applies and clears selections', async () => {
    vi.mocked(getUsageLatest).mockImplementation(async (filter) => ({
      success: true, pageNumber: filter?.pageNumber ?? 1, pageSize: 100, totalPages: 2, totalRecords: 2, totalMs: 1,
      objects: [{ provider: filter?.pageNumber === 2 ? 'openai' : 'anthropic', accountKey: 'acct_choice', hostId: 'hbr_choice', windows: [{ name: 'seven_day', model: null, windowMinutes: 10080 }] }],
    }) as never);
    renderAt('/activity?tab=overview');
    const provider = screen.getByRole('combobox', { name: 'Provider' });
    await waitFor(() => expect(within(provider).getByRole('option', { name: 'openai' })).toBeInTheDocument());
    expect(getUsageLatest).toHaveBeenCalledWith({ pageNumber: 2, pageSize: 100 });
    const window = screen.getByRole('combobox', { name: 'Window' });
    expect(within(window).getByRole('option', { name: 'Weekly' })).toHaveValue('seven_day');
    for (const [name, value] of [['Provider', 'openai'], ['Account', 'acct_choice'], ['Host', 'hbr_choice'], ['Window', 'seven_day']]) {
      fireEvent.change(screen.getByRole('combobox', { name }), { target: { value } });
    }
    fireEvent.click(screen.getByRole('button', { name: 'Apply' }));
    await waitFor(() => expect(getUsageSeries).toHaveBeenCalledWith(expect.objectContaining({ metric: 'limits', provider: 'openai', accountKey: 'acct_choice', hostId: 'hbr_choice', window: 'seven_day' })));
    expect(within(provider).getByRole('option', { name: 'anthropic' })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Clear filters' }));
    expect(provider).toHaveValue('');
    expect(window).toHaveValue('');
    expect(screen.getByTestId('where')).not.toHaveTextContent('accountKey=');
  });

  it('retains filter values after a failed request and offers a retry', async () => {
    vi.mocked(getUsageLatest).mockRejectedValue(new Error('latest unavailable'));
    renderAt('/activity?tab=limits&provider=anthropic&hostId=hbr_laptop');
    await waitFor(() => expect(getUsageLatest).toHaveBeenCalledWith(expect.objectContaining({ provider: 'anthropic', hostId: 'hbr_laptop' })));
    expect((await screen.findAllByText('latest unavailable')).length).toBeGreaterThan(0);
    expect(screen.getByRole('combobox', { name: 'Provider' })).toHaveValue('anthropic');
    expect(screen.getByRole('combobox', { name: 'Host' })).toHaveValue('hbr_laptop');
    expect(screen.getByTestId('where')).toHaveTextContent('provider=anthropic');
    const calls = vi.mocked(getUsageLatest).mock.calls.length;
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    await waitFor(() => expect(vi.mocked(getUsageLatest).mock.calls.length).toBe(calls + 1));
    expect(vi.mocked(getUsageLatest).mock.calls[calls][0]).toMatchObject({ provider: 'anthropic', hostId: 'hbr_laptop' });
    expect(screen.getByRole('combobox', { name: 'Provider' })).toHaveValue('anthropic');
  });

  it('opens the account table with preserved filters from a legacy limits link', async () => {
    vi.mocked(getUsageLatest).mockResolvedValue({ success: true, pageNumber: 1, pageSize: 25, totalPages: 1, totalRecords: 0, objects: [], totalMs: 1 });
    renderAt('/activity?source=usage-limits&provider=anthropic&accountKey=acct_x&hostId=hbr_y');
    await waitFor(() => expect(screen.getByTestId('where')).toHaveTextContent('source=usage-overview'));
    expect(screen.getByTestId('where')).toHaveTextContent('section=account-limits');
    expect(screen.getByRole('tab', { name: 'Captain Usage' })).toHaveAttribute('aria-selected', 'true');
    await waitFor(() => expect(getUsageLatest).toHaveBeenCalledWith({ provider: 'anthropic', accountKey: 'acct_x', hostId: 'hbr_y', pageNumber: 1, pageSize: 25, sortBy: 'updated', sortDirection: 'descending' }));
    expect(screen.getByRole('combobox', { name: 'Account' })).toHaveValue('acct_x');
  });
});
