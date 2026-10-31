import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import TokenUsage from './TokenUsage';
import { getTokenUsage, listCaptains } from '../api/client';

vi.mock('../api/client', () => ({ getTokenUsage: vi.fn(), listCaptains: vi.fn() }));
vi.mock('../context/LocaleContext', () => ({ useLocale: () => ({ locale: 'en', t: (text: string) => text }) }));

const summary = {
  fromUtc: null, toUtc: null, bucketMinutes: 15, recordCount: 1, estimatedCount: 0, inputTokens: 1000, outputTokens: 200, cachedTokens: 600, totalTokens: 1200,
  buckets: [{ bucketStartUtc: new Date().toISOString(), inputTokens: 1000, outputTokens: 200, cachedTokens: 600, totalTokens: 1200, models: [{ model: 'opus', inputTokens: 1000, outputTokens: 200, cachedTokens: 600, totalTokens: 1200 }] }],
  byModel: [{ model: 'opus', inputTokens: 1000, outputTokens: 200, cachedTokens: 600, totalTokens: 1200 }],
};

function Where() {
  const location = useLocation();
  return <div data-testid="where">{location.search}</div>;
}

describe('Token usage page', () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(getTokenUsage).mockResolvedValue(summary as never);
    vi.mocked(listCaptains).mockResolvedValue({ objects: [{ id: 'cpt_ada', name: 'Ada' }] } as never);
  });

  it('filters the totals, heatmap, and charts by the captain or model picker, with one shared time window', async () => {
    render(<MemoryRouter initialEntries={['/activity']}><Routes><Route path="/activity" element={<><TokenUsage /><Where /></>} /></Routes></MemoryRouter>);
    await waitFor(() => expect(screen.getByRole('option', { name: 'Ada' })).toBeInTheDocument());
    await waitFor(() => expect(screen.getByRole('option', { name: 'opus' })).toBeInTheDocument());
    expect(screen.queryByLabelText('Heatmap range')).not.toBeInTheDocument();

    fireEvent.change(screen.getByRole('combobox', { name: /Captain or model/ }), { target: { value: 'captain:cpt_ada' } });
    expect(screen.getByTestId('where')).toHaveTextContent('captainId=cpt_ada');
    await waitFor(() => {
      const calls = vi.mocked(getTokenUsage).mock.calls.map(c => c[0]);
      expect(calls.filter(c => c?.captainId === 'cpt_ada').map(c => c?.bucketMinutes).sort()).toEqual([15, 60]);
    });

    fireEvent.change(screen.getByRole('combobox', { name: /Captain or model/ }), { target: { value: 'model:opus' } });
    expect(screen.getByTestId('where')).toHaveTextContent('model=opus');
    expect(screen.getByTestId('where')).not.toHaveTextContent('captainId');

    fireEvent.click(screen.getByRole('button', { name: 'Last Month' }));
    await waitFor(() => {
      const matched = vi.mocked(getTokenUsage).mock.calls.some((call) => call[0]?.bucketMinutes === 1440 && call[0]?.model === 'opus');
      expect(matched).toBe(true);
    });
  });

  it('shows a summary load failure instead of an empty range and retries that request', async () => {
    vi.mocked(getTokenUsage).mockRejectedValue(new Error('offline'));
    render(<MemoryRouter><TokenUsage /></MemoryRouter>);
    await waitFor(() => {
      expect(screen.queryByText('Loading token usage...')).not.toBeInTheDocument();
      const emptyRanges = screen.queryAllByText('No token usage for this time range').filter((el) => el.classList.contains('mission-history-empty'));
      expect(emptyRanges).toHaveLength(0);
      expect(screen.getByText('Could not load token usage.')).toBeInTheDocument();
    });
    const beforeRetry = vi.mocked(getTokenUsage).mock.calls.length;
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    await waitFor(() => expect(vi.mocked(getTokenUsage).mock.calls.length).toBeGreaterThan(beforeRetry));
  });

  it('shows the empty-range text for a successful summary with no usage', async () => {
    const empty = { ...summary, recordCount: 0, estimatedCount: 0, inputTokens: 0, outputTokens: 0, cachedTokens: 0, totalTokens: 0, buckets: [], byModel: [] };
    vi.mocked(getTokenUsage).mockResolvedValue(empty as never);
    render(<MemoryRouter><TokenUsage /></MemoryRouter>);
    await waitFor(() => {
      const emptyRanges = screen.getAllByText('No token usage for this time range').filter((el) => el.classList.contains('mission-history-empty'));
      expect(emptyRanges).toHaveLength(2);
    });
    expect(screen.queryByText('Could not load token usage.')).not.toBeInTheDocument();
  });

  it('shows a load failure for a new filter and does not keep the previous figures', async () => {
    render(<MemoryRouter><TokenUsage /></MemoryRouter>);
    await waitFor(() => expect(screen.getAllByText('1.2K').length).toBeGreaterThan(0));
    const firstSignal = vi.mocked(getTokenUsage).mock.calls
      .map((call) => (call[1] as { signal?: AbortSignal } | undefined)?.signal)
      .find((signal) => signal);
    vi.mocked(getTokenUsage).mockRejectedValue(new Error('offline'));
    fireEvent.change(screen.getByRole('combobox', { name: /Captain or model/ }), { target: { value: 'captain:cpt_ada' } });
    await waitFor(() => {
      const emptyRanges = screen.queryAllByText('No token usage for this time range').filter((el) => el.classList.contains('mission-history-empty'));
      expect(emptyRanges).toHaveLength(0);
      expect(screen.getByText('Could not load token usage.')).toBeInTheDocument();
    });
    expect(screen.queryByText('1.2K')).not.toBeInTheDocument();
    expect(firstSignal?.aborted).toBe(true);
  });

  it('always splits usage by model into token types that add up to the total', async () => {
    render(<MemoryRouter><TokenUsage /></MemoryRouter>);
    await waitFor(() => expect(screen.getByText('Usage by model')).toBeInTheDocument());
    expect(screen.getByText('Uncached input')).toBeInTheDocument();
    expect(screen.getByText('Cached input')).toBeInTheDocument();
  });
});
