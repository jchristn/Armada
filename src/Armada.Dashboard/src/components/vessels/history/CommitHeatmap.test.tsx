import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import CommitHeatmap from './CommitHeatmap';
import { translateTemplate } from '../../../i18n/runtime';
import { addDays } from '../../../lib/vesselHistory';
import type { VesselCommitActivity } from '../../../types/models';

vi.mock('../../../context/LocaleContext', () => ({
  useLocale: () => ({
    t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
    locale: 'en',
  }),
}));

function activity(): VesselCommitActivity {
  // Sunday 2026-01-04 .. Saturday 2026-03-14 (10 full weeks).
  const counts: Record<string, number> = { '2026-01-05': 1, '2026-02-02': 4, '2026-03-02': 8, '2026-03-03': 6 };
  const days = Array.from({ length: 70 }, (_, i) => {
    const date = addDays('2026-01-04', i);
    return { date, count: counts[date] ?? 0 };
  });
  return {
    vesselId: 'vsl_1', branch: 'main', from: '2026-01-04', to: '2026-03-14', utcOffsetMinutes: 0, days,
    totalCommits: 19, maxDayCount: 8, firstCommitUtc: '2025-01-01T00:00:00Z', lastCommitUtc: '2026-03-03T00:00:00Z', error: null,
  };
}

describe('CommitHeatmap', () => {
  it('renders one cell per day with scaled levels, month labels, and the total', () => {
    const { container } = render(<CommitHeatmap activity={activity()} selectedDate={null} onSelectDate={vi.fn()} rangeLabel="the last year" />);
    expect(container.querySelectorAll('[data-date]')).toHaveLength(70);
    expect(container.querySelector('[data-date="2026-01-04"]')).toHaveAttribute('data-level', '0');
    expect(container.querySelector('[data-date="2026-01-05"]')).toHaveAttribute('data-level', '1');
    expect(container.querySelector('[data-date="2026-02-02"]')).toHaveAttribute('data-level', '2');
    expect(container.querySelector('[data-date="2026-03-03"]')).toHaveAttribute('data-level', '3');
    expect(container.querySelector('[data-date="2026-03-02"]')).toHaveAttribute('data-level', '4');
    expect(screen.getByTestId('heatmap-months').textContent).toBe('JanFebMar');
    expect(screen.getByText('19 commits in the last year')).toBeInTheDocument();
    expect(screen.getByRole('gridcell', { name: '8 commits on Mon, Mar 2, 2026' })).toBeInTheDocument();
    expect(screen.getByRole('gridcell', { name: '1 commit on Mon, Jan 5, 2026' })).toHaveAttribute('title', '1 commit on Mon, Jan 5, 2026');
    expect(screen.getAllByRole('row')).toHaveLength(7);
  });

  it('selects a day on click and marks it selected', () => {
    const onSelect = vi.fn();
    const { container, rerender } = render(<CommitHeatmap activity={activity()} selectedDate={null} onSelectDate={onSelect} rangeLabel="2026" />);
    fireEvent.click(container.querySelector('[data-date="2026-02-02"]')!);
    expect(onSelect).toHaveBeenCalledWith('2026-02-02');
    rerender(<CommitHeatmap activity={activity()} selectedDate="2026-02-02" onSelectDate={onSelect} rangeLabel="2026" />);
    expect(container.querySelector('[data-date="2026-02-02"]')).toHaveAttribute('aria-selected', 'true');
    expect(container.querySelector('[data-date="2026-02-02"]')).toHaveClass('selected');
  });

  it('is one tab stop navigable with arrow keys; Enter selects', () => {
    const onSelect = vi.fn();
    const { container } = render(<CommitHeatmap activity={activity()} selectedDate="2026-02-02" onSelectDate={onSelect} rangeLabel="2026" />);
    const focusable = container.querySelectorAll('[data-date][tabindex="0"]');
    expect(focusable).toHaveLength(1);
    const start = focusable[0] as HTMLElement;
    expect(start.getAttribute('data-date')).toBe('2026-02-02');
    start.focus();
    fireEvent.keyDown(start, { key: 'ArrowRight' });
    expect(document.activeElement?.getAttribute('data-date')).toBe('2026-02-09');
    fireEvent.keyDown(document.activeElement!, { key: 'ArrowDown' });
    expect(document.activeElement?.getAttribute('data-date')).toBe('2026-02-10');
    fireEvent.keyDown(document.activeElement!, { key: 'ArrowLeft' });
    expect(document.activeElement?.getAttribute('data-date')).toBe('2026-02-03');
    fireEvent.keyDown(document.activeElement!, { key: 'End' });
    expect(document.activeElement?.getAttribute('data-date')).toBe('2026-03-14');
    fireEvent.keyDown(document.activeElement!, { key: 'ArrowRight' }); // past the range: stays put
    expect(document.activeElement?.getAttribute('data-date')).toBe('2026-03-14');
    expect(container.querySelectorAll('[data-date][tabindex="0"]')).toHaveLength(1);
    fireEvent.keyDown(document.activeElement!, { key: 'Enter' });
    expect(onSelect).toHaveBeenCalledWith('2026-03-14');
  });
});
