import type { EffectCallback } from 'react';
import { AppState } from 'react-native';
import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import * as client from '@dashboard/api/client';
import type { CaptainSelectionDecisionView } from '@dashboard/types/models';
import { CaptainSelectionSection } from '../screens/operations/mission/CaptainSelectionSection';
import { page } from '../test/operationsClient';
import { renderScreen } from '../test/screen';

let mockFocusEffect: EffectCallback | undefined;
let mockBlurEffect: (() => void) | undefined;
jest.mock('expo-router', () => ({
  ...require('../test/routerMock').routerMockFactory(),
  useFocusEffect(effect: EffectCallback) {
    const { useEffect } = jest.requireActual<typeof import('react')>('react');
    useEffect(() => {
      mockFocusEffect = effect;
      const cleanup = effect(); mockBlurEffect = typeof cleanup === 'function' ? cleanup : undefined;
      return cleanup;
    }, [effect]);
  },
}));
jest.mock('@dashboard/api/client', () => require('../test/operationsClient').operationsClientMockFactory());
const api = client as jest.Mocked<typeof client>;
const decision: CaptainSelectionDecisionView = { id: 'csd_one', tenantId: 'tenant', missionId: 'msn_one', trigger: 'Dispatch', outcome: 'Admitted', mode: 'Enforce', strategy: 'MostRemainingAllowance',
  selectedCaptainId: 'cpt_one', selectedCaptainName: 'Retained captain', selectedCaptainAvailable: false, intendedModel: 'configured-model', reportedModel: 'runtime-model',
  reasonCode: 'SelectedForHeadroom', algorithmVersion: 'usage-bands-v2', policyRevisionId: 'policy', evaluatedUtc: '2026-11-03T12:00:00Z' };
const rows = page([{ captainId: 'cpt_one', captainName: 'Retained captain', captainAvailable: false, model: 'configured-model', rank: 1, band: 'Comfortable' as const,
  reasonCode: 'SufficientHeadroom', availablePercent: 72, canSelect: true, actualSelection: true, hypotheticalSelection: false }], 11, 10);
const open = jest.fn();
beforeEach(() => {
  jest.clearAllMocks();
  api.getCaptainSelection.mockResolvedValue({ missionId: 'msn_one', currentMode: 'Enforce', currentStrategy: 'MostRemainingAllowance', historyAvailable: true, currentAttempt: decision, latestDecision: decision });
  api.getCaptainSelectionDecision.mockResolvedValue({ decision, policy: { mode: 'Enforce', strategy: 'MostRemainingAllowance', reservePercent: 10, perAttemptReservationPercent: 2 } as never,
    candidates: rows, executionEvents: page([]) });
  api.getCaptainSelectionCandidates.mockResolvedValue({ ...rows, pageNumber: 2 });
  api.getCaptainSelectionHistory.mockResolvedValue(page([{ ...decision, id: 'csd_older', outcome: 'Waiting', reasonCode: 'PlacementWaiting' }], 1, 10));
  api.previewCaptainSelection.mockResolvedValue({ missionId: 'msn_one', isPreview: true, mode: 'Enforce', strategy: 'MostRemainingAllowance', evaluatedUtc: decision.evaluatedUtc,
    reasonCode: 'VesselMissing', candidates: page([]) });
});
it('polls only the focused screen and reloads when it regains focus', async () => {
  jest.useFakeTimers();
  const previousState = AppState.currentState; AppState.currentState = 'active';
  let closeRefocused: ReturnType<EffectCallback> = undefined;
  try {
    await renderScreen(<CaptainSelectionSection missionId="msn_one" status="Pending" onOpen={open} />);
    await screen.findByText('runtime-model');
    const initial = api.getCaptainSelection.mock.calls.length;
    await act(async () => { jest.advanceTimersByTime(30000); });
    expect(api.getCaptainSelection.mock.calls.length).toBeGreaterThan(initial);
    await act(async () => { mockBlurEffect?.(); });
    const paused = api.getCaptainSelection.mock.calls.length;
    await act(async () => { jest.advanceTimersByTime(30000); });
    expect(api.getCaptainSelection).toHaveBeenCalledTimes(paused);
    await act(async () => { closeRefocused = mockFocusEffect?.(); });
    expect(api.getCaptainSelection).toHaveBeenCalledTimes(paused + 1);
  } finally {
    await act(async () => { if (typeof closeRefocused === 'function') closeRefocused(); });
    AppState.currentState = previousState; jest.useRealTimers();
  }
});
it('separates intended/reported models, keeps deleted captains unlinked and pages comparisons', async () => {
  await renderScreen(<CaptainSelectionSection missionId="msn_one" status="InProgress" onOpen={open} />);
  expect(await screen.findByText('runtime-model')).toBeOnTheScreen();
  expect(screen.getByText('configured-model')).toBeOnTheScreen();
  expect(screen.queryByRole('link', { name: /Retained captain/ })).toBeNull();
  await fireEvent.press(screen.getByRole('button', { name: 'Compare captains' }));
  expect(await screen.findByText('Enough allowance remains after estimated reservations.')).toBeOnTheScreen();
  await fireEvent.press(screen.getByRole('button', { name: 'Next' }));
  await waitFor(() => expect(api.getCaptainSelectionCandidates).toHaveBeenCalledWith('csd_one', 'tenant', 2, 10, expect.any(AbortSignal)));
  expect(open).not.toHaveBeenCalled();
});
it('filters history on the server and opens a retained decision', async () => {
  await renderScreen(<CaptainSelectionSection missionId="msn_one" status="Pending" onOpen={open} />);
  await screen.findByText('runtime-model');
  await fireEvent.press(screen.getByRole('button', { name: 'Compare captains' }));
  await screen.findByRole('tab', { name: 'History' });
  await fireEvent.press(screen.getByRole('tab', { name: 'History' }));
  await fireEvent.press(await screen.findByRole('tab', { name: 'Waiting' }));
  await waitFor(() => expect(api.getCaptainSelectionHistory).toHaveBeenLastCalledWith('msn_one', { outcome: 'Waiting', pageNumber: 1, pageSize: 10 }, expect.any(AbortSignal)));
  await fireEvent.press(await screen.findByRole('button', { name: 'View' }));
  await waitFor(() => expect(api.getCaptainSelectionDecision).toHaveBeenLastCalledWith('csd_older', 'tenant', 1, 10, expect.any(AbortSignal)));
});
it('shows a blocked read-only preview and clears evidence after access is revoked', async () => {
  await renderScreen(<CaptainSelectionSection missionId="msn_one" status="Pending" onOpen={open} />);
  await screen.findByText('runtime-model');
  await fireEvent.press(screen.getByRole('button', { name: 'Preview current selection' }));
  expect(await screen.findByText("The mission's vessel is unavailable.")).toBeOnTheScreen();
  expect(api.previewCaptainSelection).toHaveBeenCalledWith('msn_one', 1, 10, expect.any(AbortSignal));
  api.getCaptainSelection.mockRejectedValueOnce(new client.ApiError('Denied', 403, null));
  await fireEvent.press(screen.getByRole('button', { name: 'Refresh captain selection' }));
  await screen.findByRole('alert');
  expect(screen.queryByText('runtime-model')).toBeNull();
});
it('reloads on a matching captain-selection event only', async () => {
  const { socket } = await renderScreen(<CaptainSelectionSection missionId="msn_one" status="Pending" onOpen={open} />);
  await screen.findByText('runtime-model');
  await act(async () => { socket.open(); });
  const before = api.getCaptainSelection.mock.calls.length;
  await act(async () => { socket.message({ type: 'captain-selection.changed', data: { missionId: 'msn_other', outcome: 'Waiting' } }); });
  expect(api.getCaptainSelection).toHaveBeenCalledTimes(before);
  await act(async () => { socket.message({ type: 'captain-selection.changed', data: { missionId: 'msn_one', decisionId: 'csd_two', outcome: 'Admitted' } }); });
  await waitFor(() => expect(api.getCaptainSelection.mock.calls.length).toBeGreaterThan(before));
});
it('lets an administrator save audited recovery permissions and keeps the draft on conflict', async () => {
  const recovery = { id: 'ura_one', missionId: 'msn_one', actorUserId: 'admin', reason: 'Earlier approval', revision: 2, allowFallback: true, reserveException: false, createdUtc: decision.evaluatedUtc };
  api.getCaptainSelection.mockResolvedValue({ missionId: 'msn_one', currentMode: 'Enforce', currentStrategy: 'MostRemainingAllowance', historyAvailable: true,
    currentAttempt: decision, latestDecision: decision, canManageRecovery: true, recovery });
  api.authorizeCaptainSelectionRecovery.mockRejectedValueOnce(new client.ApiError('Conflict', 409, null))
    .mockResolvedValueOnce({ ...recovery, revision: 3, reserveException: true, reason: 'Operator approved' });
  await renderScreen(<CaptainSelectionSection missionId="msn_one" status="Pending" onOpen={open} />);
  expect(await screen.findByText('Fallback allowed')).toBeOnTheScreen();
  await fireEvent.press(screen.getByTestId('selection-recovery-open'));
  expect(screen.getByTestId('selection-recovery-save')).toBeDisabled();
  await fireEvent(screen.getByTestId('selection-recovery-reserve'), 'valueChange', true);
  await fireEvent.changeText(screen.getByTestId('selection-recovery-reason'), 'Operator approved');
  await fireEvent.press(screen.getByTestId('selection-recovery-save'));
  expect(await screen.findByText('Permissions changed or this mission is no longer Pending. Refresh before retrying.')).toBeOnTheScreen();
  expect(screen.getByTestId('selection-recovery-reason').props.value).toBe('Operator approved');
  await fireEvent.press(screen.getByTestId('selection-recovery-refresh'));
  await fireEvent.press(await screen.findByTestId('selection-recovery-save'));
  await waitFor(() => expect(api.authorizeCaptainSelectionRecovery).toHaveBeenLastCalledWith('msn_one', { expectedRevision: 2, allowFallback: true, reserveException: false, reason: 'Operator approved' }));
});
it('hides recovery permissions from viewers', async () => {
  await renderScreen(<CaptainSelectionSection missionId="msn_one" status="Pending" onOpen={open} />);
  await screen.findByText('runtime-model');
  expect(screen.queryByTestId('selection-recovery-open')).toBeNull();
});
