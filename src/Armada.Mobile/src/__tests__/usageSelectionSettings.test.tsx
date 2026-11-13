import { fireEvent, screen, waitFor } from '@testing-library/react-native';
import * as client from '@dashboard/api/client';
import type { SettingsData, UsageSelectionSettings as Policy } from '@dashboard/types/models';
import { UsageSelectionSettings } from '../screens/usage/UsageSelectionSettings';
import { renderScreen } from '../test/screen';

jest.mock('@dashboard/api/client', () => require('../test/w4Client').autoMockClient());
const api = client as jest.Mocked<typeof client>;

const POLICY: Policy = { mode: 'Observe', strategy: 'MostRemainingAllowance', unknownUsageBehavior: 'AllowDeprioritized', reservePercent: 10, comfortableHeadroomPercent: 30,
  perAttemptReservationPercent: 2, maximumFreshnessSeconds: 900, unknownRetrySeconds: 60, providerCooldownSeconds: 60, settlementLagSeconds: 60,
  maximumUnsettledSeconds: 3600, decisionRetentionDays: 90, maximumCandidates: 1000 };

function settings(overrides: Partial<SettingsData> = {}): SettingsData {
  return { usageSelection: { ...POLICY }, usageSelectionRevision: 7,
    usageSelectionCapabilities: { strategies: ['FirstAvailable', 'MostRemainingAllowance', 'MostUsedAllowance'], observeAvailable: true, enforcementAvailable: true, appliedLive: true, environmentOverride: false, source: 'SettingsFile' },
    ...overrides } as SettingsData;
}

beforeEach(() => {
  jest.clearAllMocks();
  api.getSettings.mockResolvedValue(settings());
  api.updateSettings.mockImplementation(async (body) => ({ ...settings(), ...body, usageSelectionRevision: 8 }) as SettingsData);
});

it('saves the edited policy with the optimistic revision', async () => {
  await renderScreen(<UsageSelectionSettings />);
  await screen.findByTestId('usage-selection-save');
  await fireEvent.press(screen.getByText('Evidence and accounting limits'));
  await fireEvent.changeText(screen.getByTestId('usage-selection-reservePercent'), '12.5');
  await fireEvent.press(screen.getByTestId('usage-selection-save'));
  await waitFor(() => expect(api.updateSettings).toHaveBeenCalledWith({ usageSelection: { ...POLICY, reservePercent: 12.5 }, expectedUsageSelectionRevision: 7 }));
  expect(await screen.findByText('Captain selection settings saved and applied.')).toBeOnTheScreen();
});

it('blocks saving when comfortable headroom does not exceed the reserve', async () => {
  await renderScreen(<UsageSelectionSettings />);
  await screen.findByTestId('usage-selection-save');
  await fireEvent.press(screen.getByText('Evidence and accounting limits'));
  await fireEvent.changeText(screen.getByTestId('usage-selection-reservePercent'), '40');
  expect(screen.getByTestId('usage-selection-save')).toBeDisabled();
  expect(screen.getAllByText('Enter valid values. Comfortable headroom must exceed the reserve.').length).toBeGreaterThan(0);
});

it('reports a stale revision as a conflict', async () => {
  api.updateSettings.mockRejectedValueOnce(new client.ApiError('Stale', 409, null));
  await renderScreen(<UsageSelectionSettings />);
  await screen.findByTestId('usage-selection-save');
  await fireEvent.press(screen.getByTestId('usage-selection-save'));
  expect(await screen.findByText('Another administrator changed these settings. Refresh before saving again.')).toBeOnTheScreen();
});

it('shows an environment override read-only', async () => {
  api.getSettings.mockResolvedValue(settings({ usageSelectionCapabilities: { strategies: ['FirstAvailable'], observeAvailable: true, enforcementAvailable: false, appliedLive: true, environmentOverride: true, source: 'Environment' } }));
  await renderScreen(<UsageSelectionSettings />);
  expect(await screen.findByText('An environment override controls this policy. Change ARMADA_USAGE_SELECTION to edit it.')).toBeOnTheScreen();
  expect(screen.getByTestId('usage-selection-save')).toBeDisabled();
});
