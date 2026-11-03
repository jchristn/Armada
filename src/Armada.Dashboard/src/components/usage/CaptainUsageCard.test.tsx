import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import CaptainUsageCard from './CaptainUsageCard';
import { ApiError, captureCaptainUsage, getUsageLatest, updateCaptain } from '../../api/client';
import type { Captain, UsageSnapshotView } from '../../types/models';
import { USAGE_UNKNOWN_COOLDOWN_MS } from '../../lib/useUsage';

vi.mock('../../api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../../api/client')>()),
  getUsageLatest: vi.fn(),
  getUsageSeries: vi.fn(() => Promise.reject(new Error('no series in this test'))),
  captureCaptainUsage: vi.fn(),
  updateCaptain: vi.fn(),
  listHarbors: vi.fn(async () => [{ id: 'hbr_office', tenantId: 'default', name: 'Office', enabled: true, connectionStatus: 'Connected' }]),
}));

vi.mock('../../context/LocaleContext', async () => {
  const { translateTemplate } = await import('../../i18n/runtime');
  return {
    useLocale: () => ({
      t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
      locale: 'en',
      formatDateTime: (v: string | null | undefined) => (v ? `at(${v})` : ''),
      formatRelativeTime: (v: string | null | undefined) => (v ? `ago(${v})` : ''),
    }),
  };
});

const revision = { value: 0 };
vi.mock('../../context/WebSocketContext', () => ({ useUsageRevision: () => revision.value }));

function page<T>(objects: T[], totalPages = 1) {
  return { success: true, pageNumber: 1, pageSize: 25, totalPages, totalRecords: objects.length, totalMs: 1, objects };
}

const baseCaptain = {
  id: 'cpt_a', tenantId: 'default', name: 'alpha', runtime: 'ClaudeCode', model: null, state: 'Idle', allowedPersonas: null,
  createdUtc: '2026-10-01T00:00:00Z', lastUpdateUtc: '2026-10-01T00:00:00Z',
  usageCaptureEnabled: true, usageCaptureMode: 'Auto', usageCaptureIntervalSeconds: 300, usageCaptureActive: true, usageCaptureCommand: null,
} as unknown as Captain;

const future = new Date(Date.now() + 7200_000).toISOString();
function view(status: UsageSnapshotView['status'], extra: Partial<UsageSnapshotView> = {}): UsageSnapshotView {
  return {
    id: 'uss_1', tenantId: 'default', hostId: 'hbr_laptop', harborId: 'hbr_laptop', runtime: 'ClaudeCode', provider: 'anthropic',
    accountKey: 'acct_abcdef123456', captainIds: ['cpt_a', 'cpt_b'], capturedUtc: '2026-11-01T10:00:00Z', status, errorCode: null,
    windows: [
      { name: 'five_hour', usedPercent: 91, used: null, limit: null, unit: 'percent', windowMinutes: 300, resetsUtc: future, model: null, scope: null, coverageNote: null },
      { name: 'seven_day', usedPercent: 40, used: null, limit: null, unit: 'percent', windowMinutes: 10080, resetsUtc: future, model: null, scope: null, coverageNote: null },
    ],
    stale: false, observedUtc: '2026-11-01T09:00:00Z', requestedIntervalSeconds: 300, effectiveIntervalSeconds: 600, capabilityReason: null, ...extra,
  };
}

function renderCard(captain: Captain = baseCaptain, canManage = true) {
  const onSaved = vi.fn();
  render(<MemoryRouter><CaptainUsageCard captain={captain} canManage={canManage} onCaptainSaved={onSaved} /></MemoryRouter>);
  return onSaved;
}

describe('CaptainUsageCard', () => {
  beforeEach(() => {
    revision.value = 0;
    vi.mocked(getUsageLatest).mockReset();
    vi.mocked(captureCaptainUsage).mockReset();
    vi.mocked(updateCaptain).mockReset();
  });

  it('reads only the selected captain and shows each window with thresholds, reset and shared captains', async () => {
    vi.mocked(getUsageLatest).mockResolvedValue(page([view('Ok')]) as never);
    renderCard();
    const bars = await screen.findAllByRole('progressbar');
    expect(getUsageLatest).toHaveBeenCalledWith({ captainId: 'cpt_a', pageSize: 25 });
    expect(bars[0].getAttribute('aria-valuenow')).toBe('91');
    expect(bars[0].getAttribute('aria-valuetext')).toContain('Near limit');
    expect(bars[1].getAttribute('aria-valuetext')).toContain('Normal');
    expect(screen.getAllByText(`Resets at(${future}) (ago(${future}))`).length).toBe(2);
    expect(screen.getByRole('link', { name: 'cpt_b' }).getAttribute('href')).toBe('/captains/cpt_b');
    expect(screen.getByText('Requested every 300s, effective every 600s', { exact: false })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Provider history' }).getAttribute('href')).toBe('/activity?source=usage-overview&section=account-limits&provider=anthropic&accountKey=acct_abcdef123456&hostId=hbr_laptop');
  });

  it('keeps loading, error, empty and disabled distinct', async () => {
    let reject: (e: Error) => void = () => {};
    vi.mocked(getUsageLatest).mockReturnValueOnce(new Promise((_, r) => { reject = r; }) as never);
    renderCard();
    expect(screen.getByText('Loading usage...')).toBeInTheDocument();
    await act(async () => { reject(new Error('down')); });
    expect(await screen.findByText('Usage could not be loaded.')).toBeInTheDocument();
    vi.mocked(getUsageLatest).mockResolvedValue(page([]) as never);
    await userEvent.click(screen.getByRole('button', { name: 'Retry' }));
    expect(await screen.findByText('No usage observed yet')).toBeInTheDocument();
    expect(screen.queryByRole('progressbar')).toBeNull();
  });

  it('shows Disabled for a captain with capture off and blocks Capture now with the reason', async () => {
    vi.mocked(getUsageLatest).mockResolvedValue(page([]) as never);
    renderCard({ ...baseCaptain, usageCaptureEnabled: false, usageCaptureActive: false });
    expect(await screen.findByText('Capture disabled')).toBeInTheDocument();
    const button = screen.getByRole('button', { name: 'Capture now' }) as HTMLButtonElement;
    expect(button.disabled).toBe(true);
    expect(button.title).toBe('Capture is off for this captain. Enable it in the capture settings first.');
  });

  it('names the host for AuthMissing and never asks for a credential', async () => {
    vi.mocked(getUsageLatest).mockResolvedValue(page([view('AuthMissing', { windows: [], observedUtc: null })]) as never);
    renderCard();
    expect(await screen.findByText('Sign in to the runtime on host hbr_laptop. Armada never asks you to paste a credential here.')).toBeInTheDocument();
  });

  it('explains Unsupported and Estimated', async () => {
    vi.mocked(getUsageLatest).mockResolvedValue(page([
      view('Unsupported', { id: 'u1', windows: [], capabilityReason: 'No non-prompt interface.' }),
      view('Estimated', { id: 'u2' }),
    ]) as never);
    renderCard();
    expect(await screen.findByText('This runtime has no verified non-prompt interface for account limits, so Armada cannot read them.')).toBeInTheDocument();
    expect(screen.getByText('No non-prompt interface.')).toBeInTheDocument();
    expect(screen.getByText('Estimated from the configured budget and Armada-recorded tokens, not read from the provider.')).toBeInTheDocument();
  });

  it('keeps last-good windows visible with their age when the latest capture failed, and marks stale separately', async () => {
    vi.mocked(getUsageLatest).mockResolvedValue(page([
      view('Failed', { id: 'f1', stale: true }),
      view('Ok', { id: 's1', stale: true, accountKey: 'acct_other999999' }),
    ]) as never);
    renderCard();
    expect(await screen.findByText('The last capture failed. Last good data is from ago(2026-11-01T09:00:00Z) and is shown below with a warning.')).toBeInTheDocument();
    expect(screen.getByText('The last good observation is older than two capture intervals. Values may no longer match the provider.')).toBeInTheDocument();
    expect(screen.getAllByRole('progressbar').length).toBe(4);
    expect(document.querySelectorAll('.usage-bar-stale').length).toBe(4);
  });

  it('queues a capture, reports cooldown, and refetches after the receipt', async () => {
    vi.mocked(getUsageLatest).mockResolvedValue(page([view('Ok')]) as never);
    vi.mocked(captureCaptainUsage).mockResolvedValueOnce({ jobId: 'job_1', status: 'Queued', joinedExisting: false, nextEligibleUtc: null } as never);
    renderCard();
    await screen.findAllByRole('progressbar');
    expect(getUsageLatest).toHaveBeenCalledTimes(1);
    await userEvent.click(screen.getByRole('button', { name: 'Capture now' }));
    expect(captureCaptainUsage).toHaveBeenCalledWith('cpt_a');
    expect(await screen.findByText('Capture queued. The card refreshes when the observation is stored.')).toBeInTheDocument();
    await waitFor(() => expect(getUsageLatest).toHaveBeenCalledTimes(2));
    vi.mocked(captureCaptainUsage).mockRejectedValueOnce(new ApiError('cooldown', 429, { code: 'Cooldown', nextEligibleUtc: future }));
    await userEvent.click(screen.getByRole('button', { name: 'Capture now' }));
    expect(await screen.findByText(`Captured recently. Next capture allowed at at(${future}).`)).toBeInTheDocument();
    expect((screen.getByRole('button', { name: 'Capture now' }) as HTMLButtonElement).disabled).toBe(true);
  });

  it('allows capture again after cooldown expires and an event refreshes usage', async () => {
    vi.mocked(getUsageLatest).mockResolvedValue(page([view('Ok')]) as never);
    const nextEligibleUtc = new Date(Date.now() + 60_000).toISOString();
    vi.mocked(captureCaptainUsage).mockRejectedValueOnce(new ApiError('cooldown', 429, { code: 'Cooldown', nextEligibleUtc }));
    const onSaved = vi.fn();
    const { rerender } = render(<MemoryRouter><CaptainUsageCard captain={baseCaptain} canManage onCaptainSaved={onSaved} /></MemoryRouter>);
    await screen.findAllByRole('progressbar');
    await userEvent.click(screen.getByRole('button', { name: 'Capture now' }));
    expect(await screen.findByText(`Captured recently. Next capture allowed at at(${nextEligibleUtc}).`)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Capture now' })).toBeDisabled();
    const expired = Date.parse(nextEligibleUtc) + 60_000;
    const nowSpy = vi.spyOn(Date, 'now').mockReturnValue(expired);
    try {
      revision.value = 1;
      rerender(<MemoryRouter><CaptainUsageCard captain={baseCaptain} canManage onCaptainSaved={onSaved} /></MemoryRouter>);
      await waitFor(() => expect(getUsageLatest).toHaveBeenCalledTimes(2));
      await waitFor(() => expect(screen.getByRole('button', { name: 'Capture now' })).toBeEnabled());
      expect(screen.queryByText(/Captured recently/)).not.toBeInTheDocument();
      vi.mocked(captureCaptainUsage).mockResolvedValueOnce({ jobId: 'job_2', status: 'Queued', joinedExisting: false, nextEligibleUtc: null } as never);
      await userEvent.click(screen.getByRole('button', { name: 'Capture now' }));
      expect(captureCaptainUsage).toHaveBeenCalledTimes(2);
    } finally {
      nowSpy.mockRestore();
    }
  });

  it('re-enables Capture now on a mounted card when the cooldown deadline passes', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      vi.mocked(getUsageLatest).mockResolvedValue(page([view('Ok')]) as never);
      const nextEligibleUtc = new Date(Date.now() + 30_000).toISOString();
      vi.mocked(captureCaptainUsage).mockRejectedValueOnce(new ApiError('cooldown', 429, { code: 'Cooldown', nextEligibleUtc }));
      renderCard();
      await screen.findAllByRole('progressbar');
      await userEvent.click(screen.getByRole('button', { name: 'Capture now' }));
      await waitFor(() => expect(screen.getByRole('button', { name: 'Capture now' })).toBeDisabled());
      await act(async () => { await vi.advanceTimersByTimeAsync(31_000); });
      expect(screen.getByRole('button', { name: 'Capture now' })).toBeEnabled();
      expect(getUsageLatest).toHaveBeenCalledTimes(1);
    } finally {
      vi.useRealTimers();
    }
  });

  it('recovers from a cooldown whose next eligible time is unknown', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      vi.mocked(getUsageLatest).mockResolvedValue(page([view('Ok')]) as never);
      vi.mocked(captureCaptainUsage).mockRejectedValueOnce(new ApiError('cooldown', 429, { code: 'Cooldown' }));
      renderCard();
      await screen.findAllByRole('progressbar');
      await userEvent.click(screen.getByRole('button', { name: 'Capture now' }));
      expect(await screen.findByText('Captured recently. Next capture allowed at the next eligible time.')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Capture now' })).toBeDisabled();
      await act(async () => { await vi.advanceTimersByTimeAsync(USAGE_UNKNOWN_COOLDOWN_MS + 1_000); });
      expect(screen.getByRole('button', { name: 'Capture now' })).toBeEnabled();
    } finally {
      vi.useRealTimers();
    }
  });

  it('refetches when a usage event bumps the revision', async () => {
    vi.mocked(getUsageLatest).mockResolvedValue(page([view('Ok')]) as never);
    const onSaved = vi.fn();
    const { rerender } = render(<MemoryRouter><CaptainUsageCard captain={baseCaptain} canManage onCaptainSaved={onSaved} /></MemoryRouter>);
    await screen.findAllByRole('progressbar');
    expect(getUsageLatest).toHaveBeenCalledTimes(1);
    revision.value = 1;
    rerender(<MemoryRouter><CaptainUsageCard captain={baseCaptain} canManage onCaptainSaved={onSaved} /></MemoryRouter>);
    await waitFor(() => expect(getUsageLatest).toHaveBeenCalledTimes(2));
  });

  it('hides Capture now and locks the capture settings for non-administrators', async () => {
    vi.mocked(getUsageLatest).mockResolvedValue(page([view('Ok')]) as never);
    renderCard(baseCaptain, false);
    await screen.findAllByRole('progressbar');
    expect(screen.queryByRole('button', { name: 'Capture now' })).toBeNull();
    expect(screen.getByText('Only an administrator can change usage capture for this captain.')).toBeInTheDocument();
    expect(screen.getByLabelText('Capture account usage for this captain')).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Save capture settings' })).toBeNull();
  });

  it('saves the credential Harbor independently of a dock and distinguishes unresolved capture from disabled capture', async () => {
    vi.mocked(getUsageLatest).mockResolvedValue(page([]) as never);
    vi.mocked(captureCaptainUsage).mockRejectedValueOnce(new ApiError('HostUnresolved: select a credential Harbor in capture settings.', 409, null));
    vi.mocked(updateCaptain).mockResolvedValue({ ...baseCaptain, usageCaptureHarborId: 'hbr_office' });
    renderCard(baseCaptain, true);
    await userEvent.click(await screen.findByRole('button', { name: 'Capture now' }));
    expect(await screen.findByText('Capture failed to start: HostUnresolved: select a credential Harbor in capture settings.')).toBeInTheDocument();
    await screen.findByRole('option', { name: 'Office (Connected)' });
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Credential Harbor' }), 'hbr_office');
    await userEvent.click(screen.getByRole('button', { name: 'Save capture settings' }));
    await waitFor(() => expect(updateCaptain).toHaveBeenCalledWith(baseCaptain.id, expect.objectContaining({ usageCaptureHarborId: 'hbr_office' })));
  });

  it('validates Custom input, saves only the usage fields, and keeps edits after a server rejection', async () => {
    vi.mocked(getUsageLatest).mockResolvedValue(page([]) as never);
    vi.mocked(updateCaptain).mockRejectedValueOnce(new ApiError('UsageCaptureCommandInvalid: shell syntax', 400, null));
    const onSaved = renderCard({ ...baseCaptain, usageCaptureEnabled: false, usageCaptureActive: false });
    await screen.findByText('Capture disabled');
    await userEvent.click(screen.getByLabelText('Capture account usage for this captain'));
    await userEvent.selectOptions(screen.getByLabelText('Capture mode'), 'Custom');
    expect(screen.getByText('Custom capture needs an executable path.')).toBeInTheDocument();
    const save = screen.getByRole('button', { name: 'Save capture settings' }) as HTMLButtonElement;
    expect(save.disabled).toBe(true);
    await userEvent.type(screen.getByLabelText('Executable path'), 'quota; rm');
    expect(screen.getByText('The executable must be a path, not shell syntax. Put each argument on its own line.')).toBeInTheDocument();
    await userEvent.clear(screen.getByLabelText('Executable path'));
    await userEvent.type(screen.getByLabelText('Executable path'), '/opt/usage/read-quota');
    await userEvent.type(screen.getByLabelText('Arguments (one per line)'), '--json{Enter}--fast');
    await userEvent.clear(screen.getByLabelText('Interval (seconds)'));
    await userEvent.type(screen.getByLabelText('Interval (seconds)'), '30');
    expect(screen.getByText('Interval must be a whole number of seconds from 60 to 86400.')).toBeInTheDocument();
    await userEvent.clear(screen.getByLabelText('Interval (seconds)'));
    await userEvent.type(screen.getByLabelText('Interval (seconds)'), '900');
    await userEvent.click(screen.getByRole('button', { name: 'Save capture settings' }));
    expect(await screen.findByText('UsageCaptureCommandInvalid: shell syntax')).toBeInTheDocument();
    expect(updateCaptain).toHaveBeenCalledWith('cpt_a', {
      usageCaptureEnabled: true, usageCaptureHarborId: null, usageCaptureIntervalSeconds: 900, usageCaptureMode: 'Custom',
      usageCaptureCommand: JSON.stringify({ executable: '/opt/usage/read-quota', arguments: ['--json', '--fast'] }),
    });
    expect((screen.getByLabelText('Executable path') as HTMLInputElement).value).toBe('/opt/usage/read-quota');
    expect(onSaved).not.toHaveBeenCalled();
    vi.mocked(updateCaptain).mockResolvedValueOnce({ ...baseCaptain, usageCaptureMode: 'Custom' } as never);
    await userEvent.click(screen.getByRole('button', { name: 'Save capture settings' }));
    expect(await screen.findByText('Usage capture settings saved.')).toBeInTheDocument();
    expect(onSaved).toHaveBeenCalledTimes(1);
  });
});
