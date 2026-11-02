import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import UsageOverTimeChart, { limitSegments } from './UsageOverTimeChart';
import type { UsageSeries, UsageSeriesPoint } from '../../types/models';

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

const FROM = '2026-10-01T00:00:00.000Z';
const TO = '2026-10-01T06:00:00.000Z';

function hour(h: number): string {
  return new Date(Date.parse(FROM) + h * 3600000).toISOString();
}

function point(h: number, percent: number | null, extra: Partial<UsageSeriesPoint> = {}): UsageSeriesPoint {
  return {
    bucketStartUtc: hour(h), bucketEndUtc: hour(h + 1), lastPercent: percent, minPercent: percent, maxPercent: percent, meanPercent: percent,
    lastResetUtc: null, observedUtc: hour(h), observationAgeSeconds: 0, observationCount: percent === null ? 0 : 1, status: 'Ok',
    inputTokens: 0, outputTokens: 0, cachedTokens: 0, totalTokens: 0, reportedCost: null, costCurrency: null, estimated: false, ...extra,
  };
}

function limitSeries(accountKey: string, hostId: string, points: UsageSeriesPoint[], window = '5h'): UsageSeries {
  return { provider: 'anthropic', accountKey, hostId, runtime: 'ClaudeCode', window, model: null, costCurrency: null, points };
}

function tokenSeries(provider: string, totals: number[]): UsageSeries {
  return {
    provider, accountKey: null, hostId: null, runtime: null, window: null, model: null, costCurrency: null,
    points: totals.map((total, h) => point(h, null, { totalTokens: total, inputTokens: total })),
  };
}

describe('UsageOverTimeChart', () => {
  it('draws one independent line per account and never an averaged percentage', () => {
    const limits = [
      limitSeries('acct_aaaa', 'host_a', [point(0, 40), point(1, 40)]),
      limitSeries('acct_bbbb', 'host_b', [point(0, 80), point(1, 80)]),
    ];
    const { container } = render(<UsageOverTimeChart limits={limits} tokens={[]} fromUtc={FROM} toUtc={TO} onSelectBucket={vi.fn()} />);
    expect(container.querySelectorAll('polyline[data-series="0"]')).toHaveLength(1);
    expect(container.querySelectorAll('polyline[data-series="1"]')).toHaveLength(1);
    const labels = Array.from(container.querySelectorAll('[data-mark^="limits-"]')).map((el) => el.getAttribute('aria-label') ?? '');
    expect(labels.filter((l) => l.includes('acct_aaaa') && l.includes('40%'))).toHaveLength(2);
    expect(labels.filter((l) => l.includes('acct_bbbb') && l.includes('80%'))).toHaveLength(2);
    expect(labels.some((l) => l.includes('60%'))).toBe(false);
    const legend = container.querySelector('.usage-chart-legend') as HTMLElement;
    expect(within(legend).getByText(/acct_aaaa on host_a/)).toBeInTheDocument();
    expect(within(legend).getByText(/acct_bbbb on host_b/)).toBeInTheDocument();
  });

  it('breaks the line at a missing sample and starts a new segment with a marker at a reset', () => {
    const points = [
      point(0, 10), point(1, 20), point(2, null), point(3, 30), point(4, 60),
      point(5, 5, { lastResetUtc: '2026-10-01T05:10:00.000Z' }),
    ];
    expect(limitSegments(points)).toEqual({ segments: [[0, 1], [3, 4], [5]], resets: [5] });
    // A drop after a changed reset time is a reset even when the reset fell between buckets.
    const between = [point(0, 70, { lastResetUtc: '2026-09-30T20:00:00.000Z' }), point(1, 3, { lastResetUtc: '2026-10-01T00:59:00.000Z' })];
    expect(limitSegments(between).resets).toEqual([1]);
    // A drop without a reset is not a reset.
    expect(limitSegments([point(0, 50), point(1, 30)]).segments).toEqual([[0, 1]]);

    const { container } = render(<UsageOverTimeChart limits={[limitSeries('acct_a', 'host_a', points)]} tokens={[]} fromUtc={FROM} toUtc={TO} onSelectBucket={vi.fn()} />);
    expect(container.querySelectorAll('polyline.usage-line-segment')).toHaveLength(3);
    expect(container.querySelectorAll('.usage-chart-reset')).toHaveLength(1);
    const gap = container.querySelector('[data-mark="limits-0-2"]') as Element;
    expect(gap.getAttribute('aria-label')).toContain('No sample');
    expect(container.querySelector('[data-mark="limits-0-5"]')?.getAttribute('aria-label')).toContain('Reset');
  });

  it('selects the focused bucket with Enter after moving with the arrow keys', () => {
    const onSelect = vi.fn();
    const limits = [
      limitSeries('acct_a', 'host_a', [point(0, 10), point(1, 20)]),
      limitSeries('acct_b', 'host_b', [point(0, 30), point(1, 40), point(2, 50)]),
    ];
    const { container } = render(<UsageOverTimeChart limits={limits} tokens={[]} fromUtc={FROM} toUtc={TO} onSelectBucket={onSelect} />);
    const first = container.querySelector('[data-mark="limits-0-0"]') as SVGElement;
    expect(first.getAttribute('tabindex')).toBe('0');
    expect(container.querySelector('[data-mark="limits-1-1"]')?.getAttribute('tabindex')).toBe('-1');
    fireEvent.keyDown(first, { key: 'ArrowDown' });
    fireEvent.keyDown(container.querySelector('[data-mark="limits-1-0"]') as Element, { key: 'ArrowRight' });
    const target = container.querySelector('[data-mark="limits-1-1"]') as SVGElement;
    expect(target.getAttribute('tabindex')).toBe('0');
    expect(first.getAttribute('tabindex')).toBe('-1');
    fireEvent.keyDown(target, { key: 'Enter' });
    expect(onSelect).toHaveBeenCalledTimes(1);
    expect(onSelect).toHaveBeenCalledWith({ bucketStartUtc: hour(1), bucketEndUtc: hour(2), provider: 'anthropic', accountKey: 'acct_b', hostId: 'host_b' });
    fireEvent.keyDown(target, { key: ' ' });
    expect(onSelect).toHaveBeenCalledTimes(2);
  });

  it('selects from a click on a token bar and from the accessible data table', () => {
    const onSelect = vi.fn();
    const { container } = render(
      <UsageOverTimeChart limits={[limitSeries('acct_a', 'host_a', [point(0, 10)])]} tokens={[tokenSeries('openai', [100, 250])]} fromUtc={FROM} toUtc={TO} onSelectBucket={onSelect} />,
    );
    expect(screen.getByText('Tokens recorded at completion')).toBeInTheDocument();
    fireEvent.click(container.querySelector('[data-mark="tokens-0-1"]') as Element);
    expect(onSelect).toHaveBeenLastCalledWith({ bucketStartUtc: hour(1), bucketEndUtc: hour(2), provider: 'openai', accountKey: null, hostId: null });

    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Show chart data table' }));
    const table = screen.getByRole('table');
    const row = within(table).getAllByRole('row').find((r) => r.textContent?.includes('acct_a')) as HTMLElement;
    expect(row).toBeDefined();
    fireEvent.click(within(row).getByRole('button', { name: /Show missions for/ }));
    expect(onSelect).toHaveBeenLastCalledWith({ bucketStartUtc: hour(0), bucketEndUtc: hour(1), provider: 'anthropic', accountKey: 'acct_a', hostId: 'host_a' });
    expect(within(table).getAllByText('250 tokens')).toHaveLength(1);
  });

  it('marks estimated series and points visibly and in the data table', () => {
    const estimated = limitSeries('acct_est', 'host_a', [point(0, 25, { estimated: true })]);
    const { container } = render(<UsageOverTimeChart limits={[estimated]} tokens={[]} fromUtc={FROM} toUtc={TO} onSelectBucket={vi.fn()} />);
    expect(container.querySelector('.usage-chart-series-estimated')).not.toBeNull();
    expect(container.querySelector('[data-mark="limits-0-0"]')?.getAttribute('aria-label')).toContain('Estimated');
    expect(within(container.querySelector('.usage-chart-legend') as HTMLElement).getByText('Estimated')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Show chart data table' }));
    expect(within(screen.getByRole('table')).getByText('Estimated')).toBeInTheDocument();
  });
  it('shows sample details on hover and keyboard focus and dismisses the tooltip', () => {
    const { container, rerender } = render(<UsageOverTimeChart limits={[limitSeries('acct_hover', 'host_a', [point(0, 42)])]} tokens={[]} fromUtc={FROM} toUtc={TO} onSelectBucket={vi.fn()} />);
    const mark = container.querySelector('[data-mark="limits-0-0"]')!;
    fireEvent.mouseMove(mark, { clientX: 200, clientY: 120 });
    expect(screen.getByRole('tooltip')).toHaveTextContent('acct_hover on host_a');
    expect(screen.getByRole('tooltip')).toHaveTextContent('42%');
    expect(screen.getByRole('tooltip')).toHaveTextContent(hour(0));
    expect(mark).toHaveAttribute('aria-describedby', screen.getByRole('tooltip').id);
    fireEvent.mouseLeave(mark);
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
    fireEvent.focus(mark);
    expect(screen.getByRole('tooltip')).toHaveTextContent('42%');
    fireEvent.keyDown(mark, { key: 'Escape' });
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
    fireEvent.mouseMove(mark, { clientX: 200, clientY: 120 });
    fireEvent.scroll(window);
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
    fireEvent.mouseMove(mark, { clientX: 200, clientY: 120 });
    rerender(<UsageOverTimeChart limits={[]} tokens={[]} fromUtc={FROM} toUtc={TO} onSelectBucket={vi.fn()} />);
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
  });

  it('uses the nearest bucket when hovering or clicking the line', () => {
    const onSelect = vi.fn();
    const { container } = render(<UsageOverTimeChart limits={[limitSeries('acct_line', 'host_a', [point(0, 10), point(1, 20), point(2, 30)])]} tokens={[]} fromUtc={FROM} toUtc={TO} onSelectBucket={onSelect} />);
    const svg = container.querySelector('svg')!;
    vi.spyOn(svg, 'getBoundingClientRect').mockReturnValue({ left: 0, top: 0, width: 800, height: 190, right: 800, bottom: 190, x: 0, y: 0, toJSON: () => ({}) });
    const line = container.querySelector('polyline.usage-line-segment')!;
    fireEvent.mouseMove(line, { clientX: 115, clientY: 100 });
    expect(screen.getByRole('tooltip')).toHaveTextContent('10%');
    fireEvent.click(line, { clientX: 115, clientY: 100 });
    expect(onSelect).toHaveBeenLastCalledWith(expect.objectContaining({ bucketStartUtc: hour(0), bucketEndUtc: hour(1) }));
    fireEvent.mouseMove(line, { clientX: 235, clientY: 100 });
    expect(screen.getByRole('tooltip')).toHaveTextContent('20%');
    fireEvent.mouseLeave(line);
    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument();
  });

});
