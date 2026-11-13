import { act, fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import * as client from '@dashboard/api/client';
import type { Captain, UsageSnapshotView } from '@dashboard/types/models';
import { UsageView } from '../screens/usage/UsageView';
import { BuildProviders, page } from '../test/buildFixtures';

jest.mock('@dashboard/api/client', () => require('../test/buildClientMock').buildClientMockFactory());
jest.mock('../auth/AuthContext', () => {
  const actual = jest.requireActual('../auth/AuthContext');
  return { ...actual, useAuth: () => ({ ...actual.useAuth(), isAdmin: true, isTenantAdmin: true }) };
});
const api = client as jest.Mocked<typeof client>;
const captain = { id: 'cpt_usage', usageCaptureEnabled: null, usageCaptureMode: 'Auto' } as Captain;
function view(status: UsageSnapshotView['status']): UsageSnapshotView {
  return { id: 'usage', tenantId: 'ten', hostId: 'quota-host', harborId: null, runtime: 'ClaudeCode', provider: 'anthropic', accountKey: 'opaque', captainIds: [captain.id], capturedUtc: '2026-11-01T00:00:00Z', status, errorCode: null, windows: [], stale: false, observedUtc: null, requestedIntervalSeconds: 300, effectiveIntervalSeconds: 600, capabilityReason: null };
}
beforeEach(() => jest.clearAllMocks());
it.each([['Ok', 'Fresh'], ['AuthMissing', 'Sign-in needed'], ['Failed', 'Capture failed'], ['Unsupported', 'Unsupported'], ['Estimated', 'Estimated']] as const)('shows the %s state and host', async (status, label) => {
  api.getUsageLatest.mockResolvedValue(page([view(status)]));
  await render(<BuildProviders><UsageView captain={captain} /></BuildProviders>);
  await waitFor(() => expect(screen.getAllByText(label).length).toBeGreaterThan(0));
  expect(screen.getAllByText(/quota-host/).length).toBeGreaterThan(0);
});
it('re-enables capture after the exact 429 cooldown deadline', async () => {
  jest.useFakeTimers();
  try {
    api.getUsageLatest.mockResolvedValue(page([]));
    api.captureCaptainUsage.mockRejectedValueOnce(new client.ApiError('cooldown', 429, { data: { nextEligibleUtc: new Date(Date.now() + 1000).toISOString() } }));
    await render(<BuildProviders><UsageView captain={captain} /></BuildProviders>);
    await act(async () => { fireEvent.press(screen.getByRole('button', { name: 'Capture now' })); });
    await waitFor(() => expect(screen.getByRole('button', { name: 'Capture cooldown' })).toBeDisabled());
    await act(async () => { jest.advanceTimersByTime(1100); });
    expect(screen.getByRole('button', { name: 'Capture now' })).toBeEnabled();
    expect(api.captureCaptainUsage).toHaveBeenCalledTimes(1);
  } finally { jest.useRealTimers(); }
});
it('keeps disabled and empty observations distinct and never fabricates percentages', async () => {
  api.getUsageLatest.mockResolvedValue(page([]));
  await render(<BuildProviders><UsageView captain={{ ...captain, usageCaptureEnabled: false }} /></BuildProviders>);
  expect(screen.getAllByText('Capture disabled').length).toBeGreaterThan(0);
  await waitFor(() => expect(screen.getByText('No usage observed yet')).toBeTruthy());
  expect(screen.getByRole('button', { name: 'Capture now' })).toBeDisabled();
});

it('passive capture refreshes the original observation without claiming a queued job', async () => {
  api.getUsageLatest.mockResolvedValue(page([view('Ok')]));
  api.captureCaptainUsage.mockResolvedValue(view('Ok'));
  await render(<BuildProviders><UsageView captain={captain} /></BuildProviders>);
  await act(async () => { fireEvent.press(screen.getByRole('button', { name: 'Capture now' })); });
  await waitFor(() => expect(api.captureCaptainUsage).toHaveBeenCalledTimes(1));
  expect(screen.queryByText('Capture queued')).toBeNull();
  expect(screen.getByRole('button', { name: 'Capture now' })).toBeEnabled();
});
