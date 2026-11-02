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
  beforeEach(() => { vi.mocked(getUsageLatest).mockReset(); });

  it('lists one row per account with member captains linking to their usage section', async () => {
    vi.mocked(getUsageLatest).mockResolvedValue(page([view()]));
    renderTable({ provider: 'anthropic', hostId: 'hbr_laptop' });
    await waitFor(() => expect(screen.getByRole('link', { name: 'cpt_a' })).toHaveAttribute('href', '/captains/cpt_a?section=usage'));
    expect(screen.getByRole('link', { name: 'cpt_b' })).toHaveAttribute('href', '/captains/cpt_b?section=usage');
    expect(getUsageLatest).toHaveBeenCalledWith({ provider: 'anthropic', accountKey: undefined, hostId: 'hbr_laptop', pageNumber: 1, pageSize: 25 });
    expect(screen.getByText('acct_123...')).toBeInTheDocument();
    expect(screen.getByRole('progressbar', { name: '5-hour limit usage' })).toHaveAttribute('aria-valuenow', '82');
    expect(screen.getByText('600s')).toBeInTheDocument();
    expect(screen.getByText('600s').closest('td')).toHaveAttribute('title', 'Every 600s (requested 300s)');
    expect(screen.getByText('acct_123...')).toHaveAttribute('title', 'acct_1234567890abcdef');
    expect(screen.queryByText('acct_1234567890abcdef')).not.toBeInTheDocument();
  });

  it('copies the full account key and host ID from their cells', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } });
    vi.mocked(getUsageLatest).mockResolvedValue(page([view()]));
    renderTable();
    const accountCell = (await screen.findByText('acct_123...')).closest('td')!;
    fireEvent.click(within(accountCell).getByRole('button', { name: 'Copy to clipboard' }));
    await waitFor(() => expect(writeText).toHaveBeenCalledWith('acct_1234567890abcdef'));
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
    await waitFor(() => expect(screen.getByText('Unsupported')).toBeInTheDocument());
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
});
