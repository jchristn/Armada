import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import BackgroundActivityIndicator, { ACTIVITY_FAST_POLL_MS, ACTIVITY_IDLE_POLL_MS, ACTIVITY_PAGE_SIZE } from './BackgroundActivityIndicator';
import { getJob, listJobs } from '../../api/client';
import { translateTemplate } from '../../i18n/runtime';
import type { Job } from '../../types/models';

vi.mock('../../api/client', () => ({
  listJobs: vi.fn(),
  getJob: vi.fn(),
}));

const pushToast = vi.fn();
const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: () => 'just now',
};
vi.mock('../../context/LocaleContext', () => ({ useLocale: () => localeValue }));
vi.mock('../../context/NotificationContext', () => ({ useNotifications: () => ({ pushToast }) }));

function job(id: string, kind: string, name: string, status = 'Running'): Job {
  return { id, tenantId: 'ten', userId: null, name, kind, status, progress: 10, resultJson: null, errorReason: null, createdUtc: '', startedUtc: '2026-10-04T00:00:00Z', completedUtc: null, lastUpdateUtc: '' };
}

function page(objects: Job[]) {
  return { success: true, pageNumber: 1, pageSize: objects.length, totalPages: 1, totalRecords: objects.length, totalMs: 1, objects };
}

function renderIndicator() {
  return render(<MemoryRouter><BackgroundActivityIndicator /></MemoryRouter>);
}

describe('BackgroundActivityIndicator', () => {
  afterEach(() => {
    vi.resetAllMocks();
    vi.useRealTimers();
  });

  it('stays hidden while nothing runs and polls slowly', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.mocked(listJobs).mockResolvedValue(page([job('job_old', 'Generic', 'Old', 'Succeeded')]));
    renderIndicator();
    await waitFor(() => expect(listJobs).toHaveBeenCalledTimes(1));
    // The poll asks the server for active jobs only instead of downloading the whole job history.
    expect(listJobs).toHaveBeenCalledWith({ status: ['Queued', 'Running'], pageSize: ACTIVITY_PAGE_SIZE });
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
    await act(async () => { await vi.advanceTimersByTimeAsync(ACTIVITY_FAST_POLL_MS); });
    expect(listJobs).toHaveBeenCalledTimes(1);
    await act(async () => { await vi.advanceTimersByTimeAsync(ACTIVITY_IDLE_POLL_MS); });
    expect(listJobs).toHaveBeenCalledTimes(2);
  });

  it('shows a count with text and lists running jobs with friendly names and links', async () => {
    vi.mocked(listJobs).mockResolvedValue(page([
      job('job_1', 'FleetCategorization', 'Fleet categorization for import vib_abc (3 vessels)'),
      job('job_2', 'Report', 'Vessel health evaluation', 'Queued'),
      job('job_3', 'Generic', 'Done', 'Succeeded'),
    ]));
    renderIndicator();
    const button = await screen.findByRole('button', { name: '2 background tasks running' });
    expect(within(button).getByText('2 running')).toBeInTheDocument();
    fireEvent.click(button);
    const popover = screen.getByRole('dialog', { name: 'Background tasks' });
    expect(within(popover).getByRole('link', { name: /Recommending fleets/ })).toHaveAttribute('href', '/vessels/import?batch=vib_abc');
    expect(within(popover).getByRole('link', { name: /Evaluating vessel health/ })).toHaveAttribute('href', '/jobs');
    expect(within(popover).getByText(/Queued/)).toBeInTheDocument();
  });

  it('announces finished work, toasts a finished categorization, and hides when idle', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.mocked(listJobs)
      .mockResolvedValueOnce(page([job('job_1', 'FleetCategorization', 'Fleet categorization for import vib_abc (3 vessels)')]))
      .mockResolvedValue(page([]));
    vi.mocked(getJob).mockResolvedValue(job('job_1', 'FleetCategorization', 'Fleet categorization for import vib_abc (3 vessels)', 'Succeeded'));
    renderIndicator();
    await screen.findByRole('button', { name: '1 background task running' });
    await act(async () => { await vi.advanceTimersByTimeAsync(ACTIVITY_FAST_POLL_MS); });
    await waitFor(() => expect(screen.queryByRole('button')).not.toBeInTheDocument());
    await waitFor(() => expect(pushToast).toHaveBeenCalledWith('success', expect.stringContaining('Fleet recommendations are ready')));
    expect(screen.getByRole('status')).toHaveTextContent('Background task finished: Recommending fleets');
  });

  it('toasts a failed categorization with its reason', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.mocked(listJobs)
      .mockResolvedValueOnce(page([job('job_1', 'FleetCategorization', 'Fleet categorization for import vib_abc (1 vessels)')]))
      .mockResolvedValue(page([]));
    vi.mocked(getJob).mockResolvedValue({ ...job('job_1', 'FleetCategorization', 'x', 'Failed'), errorReason: 'Captain is busy' });
    renderIndicator();
    await screen.findByRole('button', { name: '1 background task running' });
    await act(async () => { await vi.advanceTimersByTimeAsync(ACTIVITY_FAST_POLL_MS); });
    await waitFor(() => expect(pushToast).toHaveBeenCalledWith('error', 'Fleet categorization failed: Captain is busy'));
  });
});
