import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, onTestFinished, vi } from 'vitest';
import VesselHistory, { HISTORY_PAGE_SIZE } from './VesselHistory';
import { getVessel, getVesselBranches, getVesselCommitActivity, getVesselCommits } from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import { addDays, beforeForDay, browserUtcOffsetMinutes, rangeForYear, todayIsoDate } from '../lib/vesselHistory';
import type { Vessel, VesselCommit, VesselCommitActivity, VesselCommitActivityQuery, VesselCommitPage, VesselCommitQuery } from '../types/models';

vi.mock('../api/client', () => ({
  getVessel: vi.fn(),
  getVesselBranches: vi.fn(),
  getVesselCommitActivity: vi.fn(),
  getVesselCommits: vi.fn(),
}));

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDate: (v: string | null | undefined) => v ?? '',
  formatDateTime: (v: string | null | undefined) => `abs:${v ?? ''}`,
  formatRelativeTime: (v: string | null | undefined) => `rel:${v ?? ''}`,
};
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));

const today = todayIsoDate();
const busyDay = addDays(today, -3);
const olderDay = addDays(today, -10);

const vessel = { id: 'vsl_1', name: 'gateway', defaultBranch: 'main' } as unknown as Vessel;

function atLocal(date: string, hour: number): string {
  const [y, m, d] = date.split('-').map(Number);
  return new Date(y, m - 1, d, hour, 0, 0).toISOString();
}

function commit(sha: string, committedUtc: string, extra: Partial<VesselCommit> = {}): VesselCommit {
  return {
    sha, shortSha: sha.slice(0, 7), subject: `Subject ${sha}`, body: '', authorName: 'Ada', authorEmail: 'ada@example.com',
    authoredUtc: committedUtc, committerName: 'Ada', committerEmail: 'ada@example.com', committedUtc, parentShas: ['p1'],
    isMerge: false, filesChanged: 1, addedLines: 3, deletedLines: 1,
    files: [{ kind: 'Modified', path: 'src/a.ts', oldPath: null, addedLines: 3, deletedLines: 1, isBinary: false }],
    filesTruncated: false, ...extra,
  };
}

function page(commits: VesselCommit[], nextCursor: string | null, branch = 'main'): VesselCommitPage {
  return { vesselId: 'vsl_1', branch, commits, nextCursor, error: null };
}

function activityFor(params?: VesselCommitActivityQuery): VesselCommitActivity {
  const from = params?.from ?? addDays(today, -364);
  const to = params?.to ?? today;
  const days = [];
  for (let d = from; d <= to; d = addDays(d, 1)) days.push({ date: d, count: d === busyDay ? 3 : 0 });
  return {
    vesselId: 'vsl_1', branch: params?.branch ?? 'main', from, to, utcOffsetMinutes: params?.utcOffsetMinutes ?? 0, days,
    totalCommits: 3, maxDayCount: 3, firstCommitUtc: '2024-05-01T00:00:00Z', lastCommitUtc: atLocal(busyDay, 12), error: null,
  };
}

interface Deferred<T> { promise: Promise<T>; resolve: (value: T) => void; reject: (err: unknown) => void }
function deferred<T>(): Deferred<T> {
  let resolve!: (value: T) => void;
  let reject!: (err: unknown) => void;
  const promise = new Promise<T>((res, rej) => { resolve = res; reject = rej; });
  return { promise, resolve, reject };
}

function commitCalls(): VesselCommitQuery[] {
  return vi.mocked(getVesselCommits).mock.calls.map((call) => call[1] ?? {});
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={['/vessels/vsl_1/history']}>
      <Routes>
        <Route path="/vessels/:id/history" element={<VesselHistory />} />
        <Route path="/vessels/:id" element={<div>vessel page</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('VesselHistory', () => {
  beforeEach(() => {
    vi.mocked(getVessel).mockResolvedValue(vessel);
    vi.mocked(getVesselBranches).mockResolvedValue({
      vesselId: 'vsl_1', defaultBranch: 'main', branchCount: 2,
      branches: [
        { name: 'main', isCurrent: true, isDefault: true, commitHash: null, commitSubject: null, commitDate: null, ahead: 0, behind: 0 },
        { name: 'dev', isCurrent: false, isDefault: false, commitHash: null, commitSubject: null, commitDate: null, ahead: 0, behind: 0 },
      ],
    });
    vi.mocked(getVesselCommitActivity).mockImplementation(async (_id, params) => activityFor(params));
    vi.mocked(getVesselCommits).mockResolvedValue(page([
      commit('aaaaaaa1', atLocal(busyDay, 15)),
      commit('aaaaaaa2', atLocal(busyDay, 9)),
      commit('aaaaaaa3', atLocal(olderDay, 12)),
    ], null));
  });

  afterEach(() => {
    vi.clearAllMocks();
    vi.unstubAllGlobals();
  });

  it('loads the default branch heatmap in the browser offset and groups commits by day', async () => {
    renderPage();
    expect(await screen.findByText('Subject aaaaaaa1')).toBeInTheDocument();
    const breadcrumb = document.querySelector('.breadcrumb')!;
    expect(breadcrumb.textContent).toContain('Vessels');
    expect(breadcrumb.textContent).toContain('gateway');
    expect(breadcrumb.textContent).toContain('History');

    const range = rangeForYear(null, today);
    expect(getVesselCommitActivity).toHaveBeenCalledWith('vsl_1', { branch: 'main', from: range.from, to: range.to, utcOffsetMinutes: browserUtcOffsetMinutes() });
    expect(browserUtcOffsetMinutes()).toBe(-new Date().getTimezoneOffset() || 0);
    expect(commitCalls()).toEqual([{ branch: 'main', before: null, limit: HISTORY_PAGE_SIZE }]);

    const days = screen.getAllByTestId('vhist-day');
    expect(days).toHaveLength(2);
    expect(within(days[0]).getAllByTestId('vhist-commit')).toHaveLength(2);
    expect(within(days[1]).getAllByTestId('vhist-commit')).toHaveLength(1);
    expect(within(days[0]).getAllByText('aaaaaaa')).toHaveLength(2); // short SHA
    expect(within(days[0]).getAllByText(`rel:${atLocal(busyDay, 15)}`)).toHaveLength(1);
    expect(screen.getByText('3 commits in the last year')).toBeInTheDocument();
    expect(screen.getByText('End of history')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Load more' })).not.toBeInTheDocument();
  });

  it('clicking a heatmap day requests the page before the next local day and highlights it', async () => {
    const scrollSpy = vi.fn();
    const original = Element.prototype.scrollIntoView;
    Element.prototype.scrollIntoView = scrollSpy;
    onTestFinished(() => { Element.prototype.scrollIntoView = original; });
    const { container } = renderPage();
    await screen.findByText('Subject aaaaaaa1');

    vi.mocked(getVesselCommits).mockResolvedValueOnce(page([commit('bbbbbbb1', atLocal(busyDay, 15))], null));
    fireEvent.click(container.querySelector(`[data-date="${busyDay}"]`)!);

    await waitFor(() => expect(commitCalls()).toHaveLength(2));
    expect(commitCalls()[1]).toEqual({ branch: 'main', before: beforeForDay(busyDay), limit: HISTORY_PAGE_SIZE });
    expect(await screen.findByText('Subject bbbbbbb1')).toBeInTheDocument();
    expect(container.querySelector(`[data-date="${busyDay}"]`)).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByTestId('vhist-day')).toHaveClass('selected');
    expect(screen.getByText(/Showing commits on or before/)).toBeInTheDocument();
    expect(scrollSpy).toHaveBeenCalled();
  });

  it('expands a commit to show the body and the changed files', async () => {
    vi.mocked(getVesselCommits).mockResolvedValue(page([
      commit('ccccccc1', atLocal(busyDay, 10), {
        isMerge: true, body: 'Line one\n\nLine two', filesChanged: 250, filesTruncated: true,
        files: [
          { kind: 'Renamed', path: 'src/new.ts', oldPath: 'src/old.ts', addedLines: 2, deletedLines: 0, isBinary: false },
          { kind: 'Added', path: 'img/logo.png', oldPath: null, addedLines: null, deletedLines: null, isBinary: true },
        ],
      }),
    ], null));
    renderPage();
    const toggle = await screen.findByRole('button', { name: /Subject ccccccc1/ });
    expect(screen.getByText('Merge')).toBeInTheDocument();
    expect(screen.getByText('250 files')).toBeInTheDocument();
    expect(screen.queryByText(/Line one/)).not.toBeInTheDocument();

    fireEvent.click(toggle);
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByText(/Line one/).tagName).toBe('PRE');
    const files = screen.getByRole('list', { name: 'Changed files' });
    expect(within(files).getByText('Renamed')).toBeInTheDocument();
    expect(within(files).getByText(/src\/old\.ts/).textContent).toMatch(/src\/old\.ts\s*->\s*renamed to\s*src\/new\.ts/);
    expect(within(files).getByText('binary')).toBeInTheDocument();
    expect(screen.getByText('Showing 2 of 250 changed files.')).toBeInTheDocument();

    fireEvent.click(toggle);
    expect(screen.queryByRole('list', { name: 'Changed files' })).not.toBeInTheDocument();
  });

  it('pages with nextCursor via Load more and stops at the end of history', async () => {
    vi.mocked(getVesselCommits)
      .mockResolvedValueOnce(page([commit('ddddddd1', atLocal(busyDay, 10))], 'cur_1'))
      .mockResolvedValueOnce(page([commit('ddddddd2', atLocal(olderDay, 10))], 'cur_2'))
      .mockResolvedValueOnce(page([commit('ddddddd3', atLocal(olderDay, 8))], null));
    renderPage();
    await screen.findByText('Subject ddddddd1');
    expect(screen.queryByText('End of history')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Load more' }));
    await screen.findByText('Subject ddddddd2');
    fireEvent.click(screen.getByRole('button', { name: 'Load more' }));
    await screen.findByText('Subject ddddddd3');

    expect(commitCalls()).toEqual([
      { branch: 'main', before: null, limit: HISTORY_PAGE_SIZE },
      { cursor: 'cur_1', limit: HISTORY_PAGE_SIZE },
      { cursor: 'cur_2', limit: HISTORY_PAGE_SIZE },
    ]);
    expect(screen.getByText('End of history')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Load more' })).not.toBeInTheDocument();
    expect(screen.getAllByTestId('vhist-commit')).toHaveLength(3);
  });

  it('loads the next page when the sentinel intersects, once per cursor', async () => {
    const callbacks: IntersectionObserverCallback[] = [];
    class FakeObserver {
      constructor(cb: IntersectionObserverCallback) { callbacks.push(cb); }
      observe() {}
      disconnect() {}
      unobserve() {}
      takeRecords() { return []; }
    }
    vi.stubGlobal('IntersectionObserver', FakeObserver);
    const second = deferred<VesselCommitPage>();
    vi.mocked(getVesselCommits)
      .mockResolvedValueOnce(page([commit('eeeeeee1', atLocal(busyDay, 10))], 'cur_1'))
      .mockReturnValueOnce(second.promise);
    renderPage();
    await screen.findByText('Subject eeeeeee1');
    await waitFor(() => expect(callbacks.length).toBeGreaterThan(0));

    const fire = () => callbacks[callbacks.length - 1]([{ isIntersecting: true } as IntersectionObserverEntry], {} as IntersectionObserver);
    act(() => { fire(); fire(); });
    expect(commitCalls()).toEqual([
      { branch: 'main', before: null, limit: HISTORY_PAGE_SIZE },
      { cursor: 'cur_1', limit: HISTORY_PAGE_SIZE },
    ]);
    await act(async () => { second.resolve(page([commit('eeeeeee2', atLocal(olderDay, 10))], null)); });
    expect(await screen.findByText('Subject eeeeeee2')).toBeInTheDocument();
    expect(screen.getByText('End of history')).toBeInTheDocument();
  });

  it('ignores a stale first page after the branch changes', async () => {
    const mainPage = deferred<VesselCommitPage>();
    vi.mocked(getVesselCommits).mockImplementation((_id, params) => {
      if (params?.branch === 'dev') return Promise.resolve(page([commit('fffffff2', atLocal(busyDay, 10))], null, 'dev'));
      return mainPage.promise;
    });
    renderPage();
    const branchSelect = await screen.findByRole('combobox', { name: 'Branch' });
    await waitFor(() => expect(commitCalls()).toHaveLength(1));
    fireEvent.change(branchSelect, { target: { value: 'dev' } });

    expect(await screen.findByText('Subject fffffff2')).toBeInTheDocument();
    await act(async () => { mainPage.resolve(page([commit('fffffff1', atLocal(busyDay, 11))], 'cur_main')); });
    expect(screen.queryByText('Subject fffffff1')).not.toBeInTheDocument();
    expect(screen.getByText('Subject fffffff2')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Load more' })).not.toBeInTheDocument();
    await waitFor(() => expect(getVesselCommitActivity).toHaveBeenLastCalledWith('vsl_1', expect.objectContaining({ branch: 'dev' })));
  });

  it('ignores a stale Load more page after the branch changes', async () => {
    const more = deferred<VesselCommitPage>();
    vi.mocked(getVesselCommits).mockImplementation((_id, params) => {
      if (params?.cursor) return more.promise;
      if (params?.branch === 'dev') return Promise.resolve(page([commit('ggggggg9', atLocal(busyDay, 10))], null, 'dev'));
      return Promise.resolve(page([commit('ggggggg1', atLocal(busyDay, 10))], 'cur_main'));
    });
    renderPage();
    await screen.findByText('Subject ggggggg1');
    fireEvent.click(screen.getByRole('button', { name: 'Load more' }));
    fireEvent.change(screen.getByRole('combobox', { name: 'Branch' }), { target: { value: 'dev' } });
    await screen.findByText('Subject ggggggg9');
    await act(async () => { more.resolve(page([commit('ggggggg2', atLocal(olderDay, 10))], null)); });
    expect(screen.queryByText('Subject ggggggg2')).not.toBeInTheDocument();
    expect(screen.getAllByTestId('vhist-commit')).toHaveLength(1);
  });

  it('jumps to a date and returns to the latest commits', async () => {
    renderPage();
    await screen.findByText('Subject aaaaaaa1');
    const input = screen.getByLabelText('Jump to date');
    expect(input).toHaveAttribute('type', 'date');
    expect(input).toHaveAttribute('max', today);

    const target = addDays(today, -500); // always before the last-year range
    const targetYear = target.slice(0, 4);
    fireEvent.change(input, { target: { value: target } });
    fireEvent.click(screen.getByRole('button', { name: 'Go' }));
    await waitFor(() => expect(commitCalls()).toHaveLength(2));
    expect(commitCalls()[1]).toEqual({ branch: 'main', before: beforeForDay(target), limit: HISTORY_PAGE_SIZE });
    // A date outside the shown range moves the heatmap to that year.
    await waitFor(() => expect(getVesselCommitActivity).toHaveBeenLastCalledWith('vsl_1', expect.objectContaining({ from: `${targetYear}-01-01`, to: `${targetYear}-12-31` })));
    expect(screen.getByRole('combobox', { name: 'Year' })).toHaveValue(targetYear);

    fireEvent.click(screen.getAllByRole('button', { name: 'Latest' })[0]);
    await waitFor(() => expect(commitCalls()).toHaveLength(3));
    expect(commitCalls()[2]).toEqual({ branch: 'main', before: null, limit: HISTORY_PAGE_SIZE });
  });

  it('navigates years back to the first commit', async () => {
    renderPage();
    await screen.findByText('Subject aaaaaaa1');
    const yearSelect = screen.getByRole('combobox', { name: 'Year' });
    const options = within(yearSelect).getAllByRole('option').map((o) => o.textContent);
    const thisYear = Number(today.slice(0, 4));
    const expectedYears: string[] = [];
    for (let y = thisYear; y >= 2024; y -= 1) expectedYears.push(String(y));
    expect(options).toEqual(['Last year', ...expectedYears]);
    expect(screen.getByRole('button', { name: 'Next year' })).toBeDisabled();

    fireEvent.click(screen.getByRole('button', { name: 'Previous year' }));
    expect(yearSelect).toHaveValue(String(thisYear));
    await waitFor(() => expect(getVesselCommitActivity).toHaveBeenLastCalledWith('vsl_1', expect.objectContaining({ from: `${thisYear}-01-01`, to: today })));
    expect(await screen.findByText(`3 commits in ${thisYear}`)).toBeInTheDocument();

    fireEvent.change(yearSelect, { target: { value: '2024' } });
    expect(screen.getByRole('button', { name: 'Previous year' })).toBeDisabled();
  });

  it('shows a friendly state when the repository cannot be read', async () => {
    vi.mocked(getVesselCommitActivity).mockResolvedValue({ ...activityFor(), days: [], error: 'Repository not cloned yet.' });
    vi.mocked(getVesselCommits).mockResolvedValue({ vesselId: 'vsl_1', branch: 'main', commits: [], nextCursor: null, error: 'Repository not cloned yet.' });
    renderPage();
    expect(await screen.findByText('History is not available')).toBeInTheDocument();
    expect(screen.getByText('Repository not cloned yet.')).toBeInTheDocument();
    expect(screen.queryByRole('grid')).not.toBeInTheDocument();
  });

  it('shows a 400 message with retry', async () => {
    vi.mocked(getVesselCommits).mockRejectedValueOnce(new Error('Limit must be between 1 and 200.'));
    renderPage();
    expect(await screen.findByText('Limit must be between 1 and 200.')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByText('Subject aaaaaaa1')).toBeInTheDocument();
  });
});
