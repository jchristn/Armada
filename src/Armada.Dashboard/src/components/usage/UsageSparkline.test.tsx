import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import UsageSparkline from './UsageSparkline';
import { getUsageSeries } from '../../api/client';
import type { UsageSeriesPoint, UsageSnapshotView } from '../../types/models';

vi.mock('../../api/client', () => ({ getUsageSeries: vi.fn() }));
vi.mock('../../context/LocaleContext', async () => {
  const { translateTemplate } = await import('../../i18n/runtime');
  const value = { locale: 'en', t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params) };
  return { useLocale: () => value };
});

const VIEW = {
  id: 'usn_1', tenantId: 'default', hostId: 'hbr_laptop', harborId: 'hbr_laptop', runtime: 'ClaudeCode', provider: 'anthropic', accountKey: 'acct_1234567890abcdef',
  captainIds: ['cpt_a'], capturedUtc: '2026-10-01T13:00:00Z', status: 'Ok', errorCode: null, windows: [], stale: false, observedUtc: null,
  requestedIntervalSeconds: 300, effectiveIntervalSeconds: 300, capabilityReason: null,
} as UsageSnapshotView;

function point(offsetHours: number, percent: number | null): UsageSeriesPoint {
  const start = Date.now() - offsetHours * 3600000;
  return {
    bucketStartUtc: new Date(start).toISOString(), bucketEndUtc: new Date(start + 3600000).toISOString(), lastPercent: percent, minPercent: null, maxPercent: null,
    meanPercent: null, lastResetUtc: null, observedUtc: null, observationAgeSeconds: null, observationCount: 1, status: 'Ok', inputTokens: 0, outputTokens: 0,
    cachedTokens: 0, totalTokens: 0, reportedCost: null, costCurrency: null, estimated: false,
  };
}

const PATH = '/activity?tab=limits&provider=anthropic&accountKey=acct_1234567890abcdef&hostId=hbr_laptop';

describe('UsageSparkline', () => {
  beforeEach(() => { vi.mocked(getUsageSeries).mockReset(); });

  it('requests the 7-day hourly limit series for the account and links to its history with gaps preserved', async () => {
    vi.mocked(getUsageSeries).mockResolvedValue({
      fromUtc: '', toUtc: '', bucketMinutes: 60, resolution: 'hour', groupBy: 'account',
      series: [{ provider: 'anthropic', accountKey: VIEW.accountKey, hostId: VIEW.hostId, runtime: 'ClaudeCode', window: '5h', model: null, costCurrency: null, points: [point(5, 10), point(4, 20), point(3, null), point(2, 40)] }],
      coverage: { unattributedRecords: 0, excludedUnattributedRecords: 0, nonMissionRecords: 0, missionRecords: 0, knownCostRecords: 0, seriesTruncated: false, warning: null },
    });
    const { container } = render(<MemoryRouter><UsageSparkline view={VIEW} historyPath={PATH} /></MemoryRouter>);
    await waitFor(() => expect(container.querySelectorAll('polyline.usage-sparkline-line')).toHaveLength(2));
    const link = screen.getByRole('link', { name: '7-day limit history for account acct_123... on host hbr_laptop' });
    expect(link).toHaveAttribute('href', PATH);
    const filter = vi.mocked(getUsageSeries).mock.calls[0][0];
    expect(filter).toMatchObject({ metric: 'limits', groupBy: 'account', bucketMinutes: 60, provider: 'anthropic', accountKey: 'acct_1234567890abcdef', hostId: 'hbr_laptop' });
    expect(Date.parse(filter.to) - Date.parse(filter.from)).toBe(7 * 24 * 3600000);
  });

  it('degrades to a short notice when history cannot be read', async () => {
    vi.mocked(getUsageSeries).mockRejectedValue(new Error('boom'));
    render(<MemoryRouter><UsageSparkline view={VIEW} historyPath={PATH} /></MemoryRouter>);
    await waitFor(() => expect(screen.getByText('History unavailable')).toBeInTheDocument());
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
  });
});
