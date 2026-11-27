import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import Reliability from './Reliability';
import BatteryRuns from './BatteryRuns';
import { cancelBatteryRun, getGateReport, getReliabilitySummary, listBatteryRuns } from '../api/client';
import { translateTemplate } from '../i18n/runtime';
import type { BatteryRun, ReliabilitySummary } from '../types/reliability';

vi.mock('../api/client', () => ({
  getReliabilitySummary: vi.fn(),
  getGateReport: vi.fn(),
  listBatteryRuns: vi.fn(),
  cancelBatteryRun: vi.fn(),
  apiErrorMessage: (err: unknown, fallback: string) => (err instanceof Error ? err.message : fallback),
}));

const auth = { isAdmin: true, isTenantAdmin: false, user: null };
vi.mock('../context/AuthContext', () => ({ useAuth: () => auth }));
const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};
vi.mock('../context/LocaleContext', () => ({ useLocale: () => localeValue }));
const pushToast = vi.fn();
vi.mock('../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast }) }));

function row(model: string, persona: string, completionRate: number) {
  return {
    model, persona, attempts: 40, completed: Math.round(40 * completionRate), failed: 4, relaunched: 2, repairs: 3, escalations: 2,
    unclassified: 1, completionRate, unclassifiedRate: 0.025, failureKinds: { ToolCallMalformed: 3, NoOp: 1 }, runtimeFailureKinds: {}, medianDurationMs: 60000,
  };
}

function summary(): ReliabilitySummary {
  return {
    fromUtc: '2026-11-12T00:00:00Z', toUtc: '2026-11-26T00:00:00Z', outcomesRead: 80, truncated: false,
    totals: { ...row('', '', 0.8), attempts: 80, repairs: 6, escalations: 4 },
    pageNumber: 1, pageSize: 50, totalRecords: 2, totalPages: 1,
    rows: [row('qwen3.8', 'Worker', 0.75), row('opus-5.5', 'Judge', 0.95)],
  };
}

describe('Reliability', () => {
  beforeEach(() => {
    vi.mocked(getReliabilitySummary).mockReset().mockResolvedValue(summary());
    vi.mocked(getGateReport).mockReset().mockResolvedValue({
      id: 'gate-1', startedUtc: '2026-11-25T00:00:00Z', finishedUtc: '2026-11-25T02:00:00Z', armadaVersion: '1.5.0', packs: ['core'],
      pipelines: [{ pipeline: 'Mixed', status: 'Passed', passed: 10, failed: 0, couldNotRun: 0, pendingEvaluation: 0 }],
      measures: [{ id: 'm1', name: 'Completion rate', value: '92%', threshold: '>= 90%', status: 'Pass', detail: '' }],
      passed: true,
    });
  });

  it('shows rows per model and persona, totals, and the latest gate report', async () => {
    render(<MemoryRouter><Reliability /></MemoryRouter>);
    expect(await screen.findByText('qwen3.8')).toBeInTheDocument();
    expect(screen.getByText('75%')).toBeInTheDocument();
    expect(screen.getAllByText('ToolCallMalformed 3, NoOp 1').length).toBe(2);
    expect(screen.getByTestId('reliability-totals')).toHaveTextContent('80 attempts, 80% completed, 6 repairs, 4 escalations, 3% unclassified');
    expect(await screen.findByTestId('gate-report')).toHaveTextContent('Completion rate');
    expect(getGateReport).toHaveBeenCalledWith('latest');
    expect(getReliabilitySummary).toHaveBeenCalledWith(expect.objectContaining({ sort: 'Attempts', descending: true, includeEvaluation: false, pageNumber: 1 }));
  });

  it('sorts, filters and widens the window on the server', async () => {
    render(<MemoryRouter><Reliability /></MemoryRouter>);
    await screen.findByText('qwen3.8');
    fireEvent.click(screen.getByRole('button', { name: /Completion/ }));
    await waitFor(() => expect(getReliabilitySummary).toHaveBeenLastCalledWith(expect.objectContaining({ sort: 'CompletionRate', descending: true })));
    fireEvent.change(screen.getAllByLabelText('Model').find(e => e.tagName === 'INPUT')!, { target: { value: 'qwen' } });
    await waitFor(() => expect(getReliabilitySummary).toHaveBeenLastCalledWith(expect.objectContaining({ model: 'qwen' })));
    fireEvent.change(screen.getByLabelText('Window'), { target: { value: '90' } });
    fireEvent.click(screen.getByLabelText(/Include evaluations/));
    await waitFor(() => expect(getReliabilitySummary).toHaveBeenLastCalledWith(expect.objectContaining({ includeEvaluation: true })));
    const from = Date.parse(vi.mocked(getReliabilitySummary).mock.lastCall![0]!.fromUtc!);
    expect(Math.round((Date.now() - from) / 86400000)).toBe(90);
  });

  it('says when no gate report is stored and hides the panel when not allowed', async () => {
    vi.mocked(getGateReport).mockRejectedValue(Object.assign(new Error('none'), { status: 404 }));
    const { unmount } = render(<MemoryRouter><Reliability /></MemoryRouter>);
    expect(await screen.findByTestId('gate-report-none')).toBeInTheDocument();
    unmount();
    vi.mocked(getGateReport).mockRejectedValue(Object.assign(new Error('no'), { status: 403 }));
    render(<MemoryRouter><Reliability /></MemoryRouter>);
    await screen.findByText('qwen3.8');
    expect(screen.queryByTestId('gate-report-none')).toBeNull();
    expect(screen.queryByTestId('gate-report')).toBeNull();
  });
});

function run(id: string, status: string): BatteryRun {
  return {
    id, status, captainId: 'cpt_1', captainName: 'qwen-worker', runCount: 1, isEvaluation: true, model: 'qwen3.8', totalTasks: 10,
    passedCount: 6, failedCount: 1, couldNotRunCount: 0, pendingEvaluationCount: 0, createdUtc: '2026-11-26T00:00:00Z', lastUpdateUtc: '2026-11-26T00:00:00Z',
  };
}

describe('BatteryRuns', () => {
  beforeEach(() => {
    auth.isAdmin = true;
    auth.isTenantAdmin = false;
    pushToast.mockReset();
    vi.mocked(listBatteryRuns).mockReset().mockResolvedValue({ objects: [run('bat_1', 'Running'), run('bat_2', 'Completed')], totalPages: 1, totalRecords: 2, pageNumber: 1, pageSize: 25 } as never);
    vi.mocked(cancelBatteryRun).mockReset().mockResolvedValue(run('bat_1', 'Cancelled'));
  });

  it('lists runs and cancels an open one after confirmation', async () => {
    render(<MemoryRouter><BatteryRuns /></MemoryRouter>);
    expect(await screen.findByText('bat_1')).toBeInTheDocument();
    const cancels = screen.getAllByRole('button', { name: 'Cancel' });
    expect(cancels).toHaveLength(1);
    fireEvent.click(cancels[0]);
    fireEvent.click(await screen.findByRole('button', { name: 'Cancel run' }));
    await waitFor(() => expect(cancelBatteryRun).toHaveBeenCalledWith('bat_1', 'Cancelled from the dashboard'));
    expect(pushToast).toHaveBeenCalledWith('success', expect.any(String));
    expect(listBatteryRuns).toHaveBeenCalledTimes(2);
  });

  it('filters by status on the server', async () => {
    render(<MemoryRouter><BatteryRuns /></MemoryRouter>);
    await screen.findByText('bat_1');
    fireEvent.change(screen.getByLabelText('Status'), { target: { value: 'Running' } });
    await waitFor(() => expect(listBatteryRuns).toHaveBeenLastCalledWith(expect.objectContaining({ status: 'Running', pageNumber: 1 })));
  });

  it('offers no cancel to a user who is not an administrator', async () => {
    auth.isAdmin = false;
    render(<MemoryRouter><BatteryRuns /></MemoryRouter>);
    await screen.findByText('bat_1');
    expect(screen.queryByRole('button', { name: 'Cancel' })).toBeNull();
  });
});
