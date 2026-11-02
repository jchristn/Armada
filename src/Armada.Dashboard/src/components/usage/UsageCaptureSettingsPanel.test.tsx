import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import UsageCaptureSettingsPanel from './UsageCaptureSettingsPanel';
import { ApiError, getHealth, getSettings, getUsageLatest, updateSettings } from '../../api/client';
import { translateTemplate } from '../../i18n/runtime';
import { onlyCallArgs } from '../../test/mockCalls';

vi.mock('../../api/client', () => ({
  getHealth: vi.fn(),
  getSettings: vi.fn(),
  updateSettings: vi.fn(),
  getUsageLatest: vi.fn(() => Promise.resolve({ objects: [], totalRecords: 0, totalPages: 0, pageNumber: 1, pageSize: 1, success: true, totalMs: 0 })),
  ApiError: class ApiError extends Error { status: number; data: unknown; constructor(m: string, s: number, d: unknown) { super(m); this.status = s; this.data = d; } },
  apiErrorMessage: (err: unknown, fallback: string) => (err instanceof Error && err.message ? err.message : fallback),
  getProxySessionContext: vi.fn(() => Promise.resolve(null)),
}));

const localeValue = {
  t: (text: string, params?: Record<string, string | number | null | undefined>) => translateTemplate('en', text, null, params),
  locale: 'en',
  formatDateTime: (v: string | null | undefined) => v ?? '',
  formatRelativeTime: (v: string | null | undefined) => v ?? '',
};
vi.mock('../../context/LocaleContext', () => ({ useLocale: () => localeValue }));
const authValue = { isAdmin: true };
vi.mock('../../context/AuthContext', () => ({ useAuth: () => authValue }));

const baseSettings = {
  admiralPort: 7890, mcpPort: 7891, maxCaptains: 4, heartbeatIntervalSeconds: 30, stallThresholdMinutes: 10,
  idleCaptainTimeoutSeconds: 0, dataDirectory: '/d', databasePath: '/d/db', logDirectory: '/d/logs', docksDirectory: '/d/docks', reposDirectory: '/d/repos',
};

describe('Settings usage capture', () => {
  const storedGroup = {
    enabled: true, minimumIntervalSeconds: 120, allowPtyFallback: false, probeTimeoutSeconds: 20, maximumConcurrentProbes: 4,
    maximumConcurrentProbesPerHost: 1, maximumBackoffSeconds: 3600, maximumExcerptBytes: 4096, rawSnapshotRetentionDays: 14, rollupRetentionDays: 180,
  };

  beforeEach(() => {
    authValue.isAdmin = true;
    vi.mocked(getHealth).mockResolvedValue({ status: 'Healthy', uptime: '1m', version: '1.0.0' } as never);
    vi.mocked(updateSettings).mockReset();
    vi.mocked(getUsageLatest).mockClear();
    vi.mocked(getUsageLatest).mockResolvedValue({ objects: [], totalRecords: 3, totalPages: 3, pageNumber: 1, pageSize: 1, success: true, totalMs: 0 } as never);
  });

  it('shows inherited enabled values when the server sends no usage group', async () => {
    vi.mocked(getSettings).mockResolvedValue({ ...baseSettings } as never);
    render(<UsageCaptureSettingsPanel canEdit={authValue.isAdmin} />);
    const toggle = await screen.findByLabelText('Enable account usage capture on this server') as HTMLInputElement;
    expect(toggle.checked).toBe(true);
    expect((screen.getByLabelText('Minimum interval (seconds)') as HTMLInputElement).value).toBe('60');
    expect((screen.getByLabelText('Concurrent captures per host') as HTMLInputElement).value).toBe('1');
    fireEvent.click(toggle);
    expect(await screen.findByText('Saving stops scheduled capture for 3 observed accounts and cancels captures in progress. Stored history is kept.')).toBeInTheDocument();
  });

  it('saves the full group, carrying retention values the form does not edit, and confirms success', async () => {
    vi.mocked(getSettings).mockResolvedValue({ ...baseSettings, usageCapture: storedGroup } as never);
    vi.mocked(updateSettings).mockImplementation((data) => Promise.resolve({ ...baseSettings, ...data } as never));
    render(<UsageCaptureSettingsPanel canEdit={authValue.isAdmin} />);
    const interval = await screen.findByLabelText('Minimum interval (seconds)') as HTMLInputElement;
    expect(interval.value).toBe('120');
    fireEvent.change(interval, { target: { value: '600' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save usage capture settings' }));
    await waitFor(() => expect(updateSettings).toHaveBeenCalledTimes(1));
    const body = onlyCallArgs(vi.mocked(updateSettings))[0] as unknown as { usageCapture: Record<string, unknown>; landingMode?: unknown };
    expect(body.usageCapture.minimumIntervalSeconds).toBe(600);
    expect(body.usageCapture.enabled).toBe(true);
    expect(body.usageCapture.rawSnapshotRetentionDays).toBe(14);
    expect(body.usageCapture.rollupRetentionDays).toBe(180);
    expect(body.usageCapture.maximumExcerptBytes).toBe(4096);
    expect(body).not.toHaveProperty('landingMode');
    expect(await screen.findByText('Usage capture settings saved.')).toBeTruthy();
  });

  it('blocks out-of-range input before any request', async () => {
    vi.mocked(getSettings).mockResolvedValue({ ...baseSettings, usageCapture: storedGroup } as never);
    render(<UsageCaptureSettingsPanel canEdit={authValue.isAdmin} />);
    const timeout = await screen.findByLabelText('Probe timeout (seconds)') as HTMLInputElement;
    fireEvent.change(timeout, { target: { value: '301' } });
    expect(timeout.getAttribute('aria-invalid')).toBe('true');
    const save = screen.getByRole('button', { name: 'Save usage capture settings' }) as HTMLButtonElement;
    expect(save.disabled).toBe(true);
    fireEvent.click(save);
    expect(updateSettings).not.toHaveBeenCalled();
    expect(screen.getByText('Fix the highlighted values: each must be a whole number inside its range.')).toBeTruthy();
  });

  it('names the scheduled work that stops when disabling, and keeps edits after a server rejection', async () => {
    vi.mocked(getSettings).mockResolvedValue({ ...baseSettings, usageCapture: storedGroup } as never);
    vi.mocked(updateSettings).mockRejectedValue(new ApiError('MinimumIntervalSeconds rejected', 400, null));
    render(<UsageCaptureSettingsPanel canEdit={authValue.isAdmin} />);
    const toggle = await screen.findByLabelText('Enable account usage capture on this server') as HTMLInputElement;
    fireEvent.click(toggle);
    expect(await screen.findByText('Saving stops scheduled capture for 3 observed accounts and cancels captures in progress. Stored history is kept.')).toBeTruthy();
    fireEvent.change(screen.getByLabelText('Maximum backoff (seconds)'), { target: { value: '7200' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save usage capture settings' }));
    expect(await screen.findByText('MinimumIntervalSeconds rejected')).toBeTruthy();
    expect((screen.getByLabelText('Enable account usage capture on this server') as HTMLInputElement).checked).toBe(false);
    expect((screen.getByLabelText('Maximum backoff (seconds)') as HTMLInputElement).value).toBe('7200');
  });

  it('shows ordinary users a read-only explanation without reading or writing the group', async () => {
    authValue.isAdmin = false;
    vi.mocked(getSettings).mockResolvedValue({ ...baseSettings } as never);
    render(<UsageCaptureSettingsPanel canEdit={authValue.isAdmin} />);
    expect(await screen.findByText(/Only a global administrator can change server-wide usage capture settings/)).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Save usage capture settings' })).toBeNull();
    expect(getUsageLatest).not.toHaveBeenCalled();
  });
});
