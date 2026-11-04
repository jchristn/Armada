import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import AccountLimitsTable from './AccountLimitsTable';
import { getUsageLatest } from '../../api/client';
import type { EnumerationResult, UsageSnapshotView } from '../../types/models';

vi.mock('../../api/client', () => ({ getUsageLatest: vi.fn() }));
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

function view(overrides: Partial<UsageSnapshotView> = {}): UsageSnapshotView {
  return {
    id: 'usn_1', tenantId: 'default', hostId: 'hbr_laptop', harborId: 'hbr_laptop', runtime: 'ClaudeCode', provider: 'anthropic',
    accountKey: 'acct_1234567890abcdef', captainIds: ['cpt_a', 'cpt_b'], capturedUtc: '2026-10-01T13:00:00Z', status: 'Ok', errorCode: null,
    windows: [{ name: '5h', usedPercent: 82, used: null, limit: null, unit: null, windowMinutes: 300, resetsUtc: null, model: null, scope: null, coverageNote: null }],
    stale: false, observedUtc: '2026-10-01T13:00:00Z', requestedIntervalSeconds: 300, effectiveIntervalSeconds: 600, capabilityReason: null, ...overrides,
  };
}

function page(objects: UsageSnapshotView[]): EnumerationResult<UsageSnapshotView> {
  return { success: true, pageNumber: 1, pageSize: 25, totalPages: 1, totalRecords: objects.length, objects, totalMs: 1 };
}

function renderTable(props: { provider?: string; accountKey?: string; hostId?: string } = {}) {
  return render(<MemoryRouter><AccountLimitsTable {...props} /></MemoryRouter>);
}

describe('AccountLimitsTable', () => {
  beforeEach(() => { vi.mocked(getUsageLatest).mockReset(); localStorage.clear(); });

  it('lists one row per account with member captains linking to their usage section', async () => {
    vi.mocked(getUsageLatest).mockResolvedValue(page([view()]));
    renderTable({ provider: 'anthropic', hostId: 'hbr_laptop' });
    await waitFor(() => expect(screen.getByRole('link', { name: 'cpt_a' })).toHaveAttribute('href', '/captains/cpt_a?section=usage'));
    expect(screen.getByRole('link', { name: 'cpt_b' })).toHaveAttribute('href', '/captains/cpt_b?section=usage');
    expect(getUsageLatest).toHaveBeenCalledWith({ provider: 'anthropic', accountKey: undefined, hostId: 'hbr_laptop', pageNumber: 1, pageSize: 25, sortBy: 'updated', sortDirection: 'descending' });
    const table = screen.getByRole('table', { name: 'Limits grouped by account and host' });
    expect(within(table).getAllByRole('columnheader').map((header) => header.querySelector('.th-sort-label')?.textContent ?? header.textContent?.replace(/[⇅↕↑↓▲▼]/g, '').trim())).toEqual(['Captain ID', 'Host ID', 'Runtime', 'Limits', 'Updated', 'Interval']);
    expect(screen.getByRole('progressbar', { name: '5-hour limit usage' })).toHaveAttribute('aria-valuenow', '82');
    expect(screen.getByText('600s')).toBeInTheDocument();
    expect(screen.getByText('600s').closest('td')).toHaveAttribute('title', 'Every 600s (requested 300s)');
    expect(screen.queryByText('acct_1234567890abcdef')).not.toBeInTheDocument();
  });

  it('shows Armada reservation estimates beside, never inside, observed usage', async () => {
    vi.mocked(getUsageLatest).mockResolvedValue(page([view({
      reservationEstimates: [{ windowName: '5h', windowMinutes: 300, model: null, activePercent: 4, unsettledPercent: 2 }], reservationUncertain: true,
    })]));
    renderTable();
    expect(await screen.findByText('Estimated Armada reservations: 4% running, 2% awaiting settlement (not provider usage)')).toBeInTheDocument();
    expect(screen.getByRole('progressbar', { name: '5-hour limit usage' })).toHaveAttribute('aria-valuenow', '82');
    expect(screen.getByText(/Captain selection treats it as uncertain until a fresh capture/)).toBeInTheDocument();
  });

  it('omits the estimate line when nothing is reserved', async () => {
    vi.mocked(getUsageLatest).mockResolvedValue(page([view({ reservationEstimates: [] })]));
    renderTable();
    await screen.findByRole('progressbar', { name: '5-hour limit usage' });
    expect(screen.queryByText(/Estimated Armada reservations/)).not.toBeInTheDocument();
  });

  it('copies the captain and host IDs from their cells', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } });
    vi.mocked(getUsageLatest).mockResolvedValue(page([view()]));
    renderTable();
    const captainCell = (await screen.findByRole('link', { name: 'cpt_a' })).closest('li')!;
    fireEvent.click(within(captainCell).getByRole('button', { name: 'Copy to clipboard' }));
    await waitFor(() => expect(writeText).toHaveBeenCalledWith('cpt_a'));
    const hostCell = screen.getByText('hbr_laptop').closest('td')!;
    fireEvent.click(within(hostCell).getByRole('button', { name: 'Copy to clipboard' }));
    await waitFor(() => expect(writeText).toHaveBeenCalledWith('hbr_laptop'));
  });

  it('keeps unsupported and auth-missing accounts distinct from fresh ones', async () => {
    vi.mocked(getUsageLatest).mockResolvedValue(page([
      view({ id: 'usn_u', status: 'Unsupported', windows: [], capabilityReason: 'No verified interface for this runtime.' }),
      view({ id: 'usn_m', status: 'AuthMissing', windows: [] }),
    ]));
    renderTable();
    await screen.findByText('No verified interface for this runtime.');
    expect(screen.getByText('Sign-in needed')).toBeInTheDocument();
    expect(screen.getByText('No verified interface for this runtime.')).toBeInTheDocument();
    expect(screen.queryByText('Fresh')).not.toBeInTheDocument();
  });

  it('explains how to enable capture when empty and never shows zero usage', async () => {
    vi.mocked(getUsageLatest).mockResolvedValue(page([]));
    const { container } = renderTable();
    await waitFor(() => expect(screen.getByText('No account usage has been captured yet')).toBeInTheDocument());
    expect(screen.getByText(/Turn on usage capture in Settings/)).toBeInTheDocument();
    expect(container.textContent).not.toMatch(/\b0%/);
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();
  });

  it('shows a retryable error and reloads on Retry', async () => {
    vi.mocked(getUsageLatest).mockRejectedValueOnce(new Error('latest failed')).mockResolvedValueOnce(page([view()]));
    renderTable();
    await waitFor(() => expect(screen.getByText('latest failed')).toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    const table = await screen.findByRole('table', { name: 'Limits grouped by account and host' });
    expect(within(table).getByRole('link', { name: 'cpt_a' })).toBeInTheDocument();
    expect(getUsageLatest).toHaveBeenCalledTimes(2);
  });
  it('uses shared refresh, auto-refresh, sorting and table layout controls', async () => {
    vi.mocked(getUsageLatest).mockResolvedValue(page([view()]));
    renderTable();
    const table = await screen.findByRole('table');
    expect(table).toHaveClass('request-history-table');
    expect(table.closest('.request-history-table-wrap')).not.toBeNull();
    expect(screen.getByRole('combobox', { name: /Auto.refresh/i })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Runtime' }));
    await waitFor(() => expect(getUsageLatest).toHaveBeenLastCalledWith(expect.objectContaining({ sortBy: 'runtime', sortDirection: 'ascending', pageNumber: 1 })));
    const calls = vi.mocked(getUsageLatest).mock.calls.length;
    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }));
    await waitFor(() => expect(getUsageLatest).toHaveBeenCalledTimes(calls + 1));
  });

});
