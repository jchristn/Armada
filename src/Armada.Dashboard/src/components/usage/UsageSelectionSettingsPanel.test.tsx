import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiErrorMessage, getSettings, updateSettings } from '../../api/client';
import { translateTemplate } from '../../i18n/runtime';
import UsageSelectionSettingsPanel from './UsageSelectionSettingsPanel';
import type { SettingsData } from '../../types/models';

vi.mock('../../api/client', () => ({ getSettings: vi.fn(), updateSettings: vi.fn(),
  isApiStatus: (error: unknown, status: number) => (error as { status?: number })?.status === status, apiErrorMessage: vi.fn((error: unknown, fallback: string) => error instanceof Error ? error.message : fallback) }));
const locale = { t: (key: string) => translateTemplate('en', key, null) };
vi.mock('../../context/LocaleContext', () => ({ useLocale: () => locale }));
const stored: SettingsData = {
  usageSelectionRevision: 7,
  usageSelection: { mode: 'Observe', strategy: 'FirstAvailable', unknownUsageBehavior: 'AllowDeprioritized', reservePercent: 10,
    comfortableHeadroomPercent: 30, perAttemptReservationPercent: 2, maximumFreshnessSeconds: 900, unknownRetrySeconds: 60,
    providerCooldownSeconds: 60, settlementLagSeconds: 60, maximumUnsettledSeconds: 3600, decisionRetentionDays: 90, maximumCandidates: 1000 },
  usageSelectionCapabilities: { strategies: ['FirstAvailable', 'MostRemainingAllowance', 'MostUsedAllowance'], observeAvailable: true,
    enforcementAvailable: false, appliedLive: true, environmentOverride: false, source: 'SettingsFile' },
};

describe('configurable captain selection', () => {
  beforeEach(() => {
    vi.mocked(getSettings).mockReset().mockResolvedValue(structuredClone(stored));
    vi.mocked(updateSettings).mockReset().mockImplementation(async change => ({ ...stored, ...change, usageSelectionRevision: 8 }));
    vi.mocked(apiErrorMessage).mockClear();
  });
  it('offers every advertised strategy and carries the complete policy with the loaded revision', async () => {
    render(<UsageSelectionSettingsPanel canEdit />);
    const strategy = await screen.findByLabelText('Selection strategy');
    expect(screen.getByRole('option', { name: 'First available' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Most remaining allowance' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Most used allowance' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Enforce' })).toBeDisabled();
    fireEvent.change(strategy, { target: { value: 'MostUsedAllowance' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    await screen.findByText('Captain selection settings saved and applied.');
    expect(updateSettings).toHaveBeenCalledWith({ expectedUsageSelectionRevision: 7, usageSelection: { ...stored.usageSelection, strategy: 'MostUsedAllowance' } });
  });
  it('enables opt-in enforcement when supported and saves it with the selected strategy', async () => {
    vi.mocked(getSettings).mockResolvedValue({ ...structuredClone(stored), usageSelectionCapabilities: { ...stored.usageSelectionCapabilities!, enforcementAvailable: true } });
    render(<UsageSelectionSettingsPanel canEdit />);
    const mode = await screen.findByLabelText('Mode');
    expect(screen.getByRole('option', { name: 'Enforce' })).toBeEnabled();
    fireEvent.change(mode, { target: { value: 'Enforce' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    await screen.findByText('Captain selection settings saved and applied.');
    expect(updateSettings).toHaveBeenCalledWith({ expectedUsageSelectionRevision: 7, usageSelection: { ...stored.usageSelection, mode: 'Enforce' } });
  });
  it('keeps edits after revision conflict and supports explicit refresh', async () => {
    vi.mocked(updateSettings).mockRejectedValue(new Error('Settings changed. Reload before saving.'));
    render(<UsageSelectionSettingsPanel canEdit />);
    const strategy = await screen.findByLabelText('Selection strategy');
    fireEvent.change(strategy, { target: { value: 'MostRemainingAllowance' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    await screen.findByRole('alert');
    expect(strategy).toHaveValue('MostRemainingAllowance');
    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }));
    await waitFor(() => expect(strategy).toHaveValue('FirstAvailable'));
  });
  it('explains a stale revision as a settings conflict', async () => {
    vi.mocked(updateSettings).mockRejectedValue(Object.assign(new Error('Conflict'), { status: 409 }));
    render(<UsageSelectionSettingsPanel canEdit />);
    await screen.findByLabelText('Selection strategy');
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Another administrator changed these settings. Refresh before saving again.');
  });
  it('rejects blank numeric input and cross-field reserve errors', async () => {
    render(<UsageSelectionSettingsPanel canEdit />);
    await screen.findByLabelText('Selection strategy');
    fireEvent.click(screen.getByText('Evidence and accounting limits'));
    fireEvent.change(screen.getByLabelText('Protected reserve (%)'), { target: { value: '35' } });
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
    fireEvent.change(screen.getByLabelText('Protected reserve (%)'), { target: { value: '' } });
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
    expect(updateSettings).not.toHaveBeenCalled();
  });
  it('makes the environment policy read-only and does not load settings for non-admin users', async () => {
    vi.mocked(getSettings).mockResolvedValue({ ...stored, usageSelectionCapabilities: { ...stored.usageSelectionCapabilities!, environmentOverride: true } });
    const view = render(<UsageSelectionSettingsPanel canEdit />);
    await screen.findByText(/An environment override controls/);
    expect(screen.getByLabelText('Selection strategy')).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
    view.unmount(); vi.mocked(getSettings).mockClear();
    render(<UsageSelectionSettingsPanel canEdit={false} />);
    expect(getSettings).not.toHaveBeenCalled();
  });
});
