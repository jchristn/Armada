import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import UsageMissionDrilldown from './UsageMissionDrilldown';
import { getUsageMissions } from '../../api/client';
import type { UsageMissionResult, UsageMissionRow } from '../../types/models';

vi.mock('../../api/client', () => ({ getUsageMissions: vi.fn() }));
vi.mock('../../context/LocaleContext', async () => {
  const { translateTemplate } = await import('../../i18n/runtime');
  const value = {
    locale: 'en',
    t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
    formatDateTime: (v: string | null | undefined) => v ?? '',
    formatRelativeTime: (v: string | null | undefined) => (v ? 'recently' : ''),
  };
  return { useLocale: () => value };
});

const SELECTION = { from: '2026-10-01T13:00:00.000Z', to: '2026-10-01T14:00:00.000Z', provider: 'anthropic', accountKey: 'acct_1234567890abcdef', hostId: 'hbr_laptop' };

function row(overrides: Partial<UsageMissionRow> = {}): UsageMissionRow {
  return {
    missionId: 'msn_live', title: 'Fix the parser', status: 'Complete', vesselId: 'vsl_1', vesselName: 'armada', captainId: 'cpt_1', captainName: 'Ada',
    accountKey: SELECTION.accountKey, hostId: SELECTION.hostId, missionUrl: '/missions/msn_live', completedUtc: '2026-10-01T13:59:00.000Z',
    inputTokens: 1200, outputTokens: 300, cachedTokens: 100, totalTokens: 1500, reportedCost: 0.25, costCurrency: 'USD', estimated: false, costCoverage: 1, ...overrides,
  };
}

function result(objects: UsageMissionRow[], coverage: Partial<UsageMissionResult['coverage']> = {}): UsageMissionResult {
  return {
    success: true, pageNumber: 1, pageSize: 25, totalPages: 1, totalRecords: objects.length, objects, totalMs: 1,
    coverage: { unattributedRecords: 0, excludedUnattributedRecords: 0, nonMissionRecords: 0, missionRecords: objects.length, knownCostRecords: 0, seriesTruncated: false, warning: null, ...coverage },
  };
}

function renderDrilldown(selection = SELECTION) {
  return render(<MemoryRouter><UsageMissionDrilldown selection={selection} /></MemoryRouter>);
}

describe('UsageMissionDrilldown', () => {
  beforeEach(() => {
    vi.mocked(getUsageMissions).mockReset();
  });

  it('requests exactly the selected provider, account, host and time range', async () => {
    vi.mocked(getUsageMissions).mockResolvedValue(result([row()]));
    renderDrilldown();
    await waitFor(() => expect(getUsageMissions).toHaveBeenCalledTimes(1));
    expect(vi.mocked(getUsageMissions).mock.calls[0][0]).toEqual({
      from: SELECTION.from, to: SELECTION.to, provider: 'anthropic', accountKey: 'acct_1234567890abcdef', hostId: 'hbr_laptop',
      sortBy: 'totalTokens', sortDirection: 'descending', pageNumber: 1, pageSize: 25,
    });
    expect(screen.getByRole('heading', { name: `Missions completed ${SELECTION.from} to ${SELECTION.to}` })).toBeInTheDocument();
    expect(screen.getByText('Provider anthropic, account acct_123..., host hbr_laptop')).toBeInTheDocument();
  });

  it('sends no account or host filter for a provider-wide selection', async () => {
    vi.mocked(getUsageMissions).mockResolvedValue(result([]));
    renderDrilldown({ from: SELECTION.from, to: SELECTION.to, provider: 'openai', accountKey: null, hostId: null } as never);
    await waitFor(() => expect(getUsageMissions).toHaveBeenCalled());
    const filter = vi.mocked(getUsageMissions).mock.calls[0][0];
    expect(Object.keys(filter)).not.toContain('accountKey');
    expect(Object.keys(filter)).not.toContain('hostId');
    expect(filter.provider).toBe('openai');
    expect(screen.getByText('Provider openai, account all accounts, host all hosts')).toBeInTheDocument();
  });

  it('shows Unknown for a null cost, links live missions and keeps deleted missions as ID only', async () => {
    vi.mocked(getUsageMissions).mockResolvedValue(result([
      row(),
      row({ missionId: 'msn_nocost', title: 'No cost reported', reportedCost: null, costCurrency: null, costCoverage: 0 }),
      row({ missionId: 'msn_deleted', title: null, status: null, vesselName: null, vesselId: null }),
    ], { excludedUnattributedRecords: 4, nonMissionRecords: 7, knownCostRecords: 2, missionRecords: 3 }));
    renderDrilldown();
    await waitFor(() => expect(screen.getByRole('link', { name: 'Fix the parser' })).toHaveAttribute('href', '/missions/msn_live'));
    expect(screen.getByRole('link', { name: 'No cost reported' })).toBeInTheDocument();
    expect(screen.getAllByText('Unknown')).toHaveLength(1);
    expect(screen.queryByText('$0.00')).not.toBeInTheDocument();
    expect(screen.getByText('msn_deleted')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'msn_deleted' })).not.toBeInTheDocument();
    expect(screen.getByText('4 unattributed token records are excluded from this account-specific view.')).toBeInTheDocument();
    expect(screen.getByText('7 non-mission token records (chat, planning, other work) are not listed as missions.')).toBeInTheDocument();
    expect(screen.getByText('Cost known for 2 of 3 mission token records.')).toBeInTheDocument();
  });

  it('says no completed missions for an empty bucket', async () => {
    vi.mocked(getUsageMissions).mockResolvedValue(result([]));
    renderDrilldown();
    await waitFor(() => expect(screen.getByText('No completed missions in this bucket')).toBeInTheDocument());
  });

  it('shows a retryable error and reloads the same selection on Retry', async () => {
    vi.mocked(getUsageMissions).mockRejectedValueOnce(new Error('usage service down')).mockResolvedValueOnce(result([row()]));
    renderDrilldown();
    await waitFor(() => expect(screen.getByText('usage service down')).toBeInTheDocument());
    expect(screen.queryByRole('link', { name: 'Fix the parser' })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    await waitFor(() => expect(screen.getByRole('link', { name: 'Fix the parser' })).toBeInTheDocument());
    expect(getUsageMissions).toHaveBeenCalledTimes(2);
    expect(vi.mocked(getUsageMissions).mock.calls[1][0]).toEqual(vi.mocked(getUsageMissions).mock.calls[0][0]);
  });

  it('sorts through the server with sortBy and sortDirection', async () => {
    vi.mocked(getUsageMissions).mockResolvedValue(result([row()]));
    renderDrilldown();
    await waitFor(() => expect(screen.getByRole('link', { name: 'Fix the parser' })).toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: /^Cost/ }));
    await waitFor(() => expect(getUsageMissions).toHaveBeenLastCalledWith(expect.objectContaining({ sortBy: 'cost', sortDirection: 'descending', pageNumber: 1 })));
    fireEvent.click(screen.getByRole('button', { name: /^Cost/ }));
    await waitFor(() => expect(getUsageMissions).toHaveBeenLastCalledWith(expect.objectContaining({ sortBy: 'cost', sortDirection: 'ascending' })));
  });
});
