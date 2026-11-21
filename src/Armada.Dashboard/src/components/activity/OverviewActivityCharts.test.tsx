import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import OverviewActivityCharts from './OverviewActivityCharts';
import { getRequestHistorySummary, getTokenUsage } from '../../api/client';

vi.mock('../../api/client', () => ({ getRequestHistorySummary: vi.fn(), getTokenUsage: vi.fn() }));
vi.mock('../../context/WebSocketContext', () => ({ useUsageRevision: () => 0 }));
vi.mock('../../context/LocaleContext', () => ({
  useLocale: () => ({
    locale: 'en',
    formatDateTime: (v: string | null | undefined) => v ?? '',
    t: (text: string, args?: Record<string, unknown>) => text.replace(/\{\{(\w+)\}\}/g, (_m, k: string) => String(args?.[k] ?? '')),
  }),
}));

function summary(total: number, failures: number) {
  return { totalCount: total, successCount: total - failures, failureCount: failures, averageDurationMs: 3, buckets: [] };
}

describe('OverviewActivityCharts', () => {
  beforeEach(() => {
    vi.mocked(getRequestHistorySummary).mockReset();
    vi.mocked(getTokenUsage).mockResolvedValue({ recordCount: 0, syntheticCount: 0, buckets: [] } as never);
  });

  it('shows the API request chart and the token heatmap, with links to their Activity tabs', async () => {
    vi.mocked(getRequestHistorySummary).mockResolvedValue(summary(42, 2) as never);
    render(<MemoryRouter><OverviewActivityCharts /></MemoryRouter>);
    expect(await screen.findByText('42 requests, 2 failed')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Open API Requests' })).toHaveAttribute('href', '/activity?source=requests');
    expect(screen.getByRole('link', { name: 'Open Token Usage' })).toHaveAttribute('href', '/activity?source=tokens');
    await waitFor(() => expect(getTokenUsage).toHaveBeenCalled());
  });

  it('reloads the request chart for another range', async () => {
    vi.mocked(getRequestHistorySummary).mockResolvedValue(summary(1, 0) as never);
    render(<MemoryRouter><OverviewActivityCharts /></MemoryRouter>);
    await screen.findByText('1 requests, 0 failed');
    fireEvent.click(screen.getByRole('button', { name: 'Last Week' }));
    await waitFor(() => expect(getRequestHistorySummary).toHaveBeenCalledTimes(2));
  });

  it('hides the request chart when request history cannot be read', async () => {
    vi.mocked(getRequestHistorySummary).mockRejectedValue(new Error('Forbidden'));
    render(<MemoryRouter><OverviewActivityCharts /></MemoryRouter>);
    await waitFor(() => expect(screen.queryByRole('link', { name: 'Open API Requests' })).not.toBeInTheDocument());
    expect(screen.getByRole('link', { name: 'Open Token Usage' })).toBeInTheDocument();
  });
});
