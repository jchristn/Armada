import { act, fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { BackHandler, Text } from 'react-native';
import * as client from '@dashboard/api/client';
import type { MissionSummary, Signal } from '@dashboard/types/models';
import { useHardwareBack } from '../navigation/useHardwareBack';
import { MissionsHubScreen } from '../screens/operations/MissionsHubScreen';
import { SignalsList } from '../screens/operations/SignalsList';
import { VoyagesList } from '../screens/operations/VoyagesList';
import { page } from '../test/operationsClient';
import { mockRouter, setMockParams } from '../test/routerMock';
import { renderScreen } from '../test/screen';

jest.mock('@dashboard/api/client', () => ({
  ...require('../test/operationsClient').operationsClientMockFactory(),
}));
// Focus effects run like effects here (the screens are rendered focused, outside a navigator).
jest.mock('expo-router', () => ({
  ...require('../test/routerMock').routerMockFactory(),
  useFocusEffect: (effect: () => (() => void) | undefined) => {
    const { useEffect: useMockEffect } = jest.requireActual('react') as typeof import('react');
    useMockEffect(effect, [effect]);
  },
}));

const api = client as jest.Mocked<typeof client>;
// Swipe rows use gesture-handler, which warns under Jest about mixed worklet callbacks (as in signals.test.tsx).
const realError = console.error;
beforeAll(() => {
  jest.spyOn(console, 'error').mockImplementation((...args: unknown[]) => {
    if (typeof args[0] === 'string' && args[0].includes('[react-native-gesture-handler]')) return;
    realError(...args);
  });
});
afterAll(() => { (console.error as jest.Mock).mockRestore(); });

/** Android hardware back: the newest listener runs first; the press is consumed when one returns true. */
type BackListener = Parameters<typeof BackHandler.addEventListener>[1];
const listeners: BackListener[] = [];
function pressBack(): boolean {
  for (let i = listeners.length - 1; i >= 0; i -= 1) {
    if (listeners[i]({} as Parameters<BackListener>[0])) return true;
  }
  return false;
}

beforeEach(() => {
  jest.clearAllMocks();
  listeners.length = 0;
  setMockParams({});
  jest.spyOn(BackHandler, 'addEventListener').mockImplementation((_event, handler) => {
    listeners.push(handler);
    return { remove: () => { const i = listeners.indexOf(handler); if (i >= 0) listeners.splice(i, 1); } };
  });
});

function summary(id: string): MissionSummary {
  return {
    id, tenantId: null, userId: null, voyageId: null, vesselId: 'vsl_1', captainId: null, title: `Mission ${id}`, status: 'Pending',
    priority: 100, parentMissionId: null, persona: null, dependsOnMissionId: null, branchName: null, dockId: null,
    processId: null, prUrl: null, commitHash: null, failureReason: null, requiresReview: false, reviewDenyAction: 'RetryStage',
    reviewComment: null, reviewedByUserId: null, reviewRequestedUtc: null, reviewedUtc: null, descriptionLength: 0,
    diffSnapshotLength: 0, agentOutputLength: 0, createdUtc: '2026-10-07T10:00:00Z', startedUtc: null, completedUtc: null,
    totalRuntimeMs: null, lastUpdateUtc: '2026-10-07T10:00:00Z',
  };
}

describe('useHardwareBack', () => {
  function Probe({ active, onBack }: { active: boolean; onBack: () => void }) {
    useHardwareBack(active, onBack);
    return <Text>probe</Text>;
  }

  it('consumes back only while active, and calls the latest handler', async () => {
    const first = jest.fn();
    const second = jest.fn();
    const view = await render(<Probe active={false} onBack={first} />);
    expect(pressBack()).toBe(false);
    await view.rerender(<Probe active onBack={first} />);
    await view.rerender(<Probe active onBack={second} />);
    expect(pressBack()).toBe(true);
    expect(first).not.toHaveBeenCalled();
    expect(second).toHaveBeenCalledTimes(1);
    await view.rerender(<Probe active={false} onBack={second} />);
    expect(pressBack()).toBe(false);
    await view.unmount();
    expect(listeners).toHaveLength(0);
  });
});

describe('hardware back in list selection modes', () => {
  it('leaves Missions bulk selection instead of leaving the screen', async () => {
    api.listVessels.mockResolvedValue(page([{ id: 'vsl_1', name: 'api' }] as never));
    api.listCaptains.mockResolvedValue(page([]));
    api.listUsers.mockResolvedValue(page([]) as never);
    api.listMissionSummaries.mockResolvedValue(page([summary('msn_1'), summary('msn_2')]));
    await renderScreen(<MissionsHubScreen />);
    expect(pressBack()).toBe(false);
    await fireEvent(await screen.findByTestId('mission-row-msn_1'), 'onLongPress');
    expect(screen.getByTestId('missions-select-cancel')).toBeTruthy();
    let handled = false;
    await act(async () => { handled = pressBack(); });
    expect(handled).toBe(true);
    await waitFor(() => expect(screen.queryByTestId('missions-select-cancel')).toBeNull());
    expect(screen.getByTestId('missions-create')).toBeTruthy();
    expect(mockRouter.back).not.toHaveBeenCalled();
    // Out of selection mode, back is the navigator's again.
    expect(pressBack()).toBe(false);
  });

  it('leaves Voyages bulk selection on back', async () => {
    const voyage = {
      id: 'vyg_1', tenantId: 'default', title: 'Gateway hardening', description: null, status: 'InProgress',
      createdUtc: '2026-10-04T00:00:00Z', completedUtc: null, lastUpdateUtc: '2026-10-04T00:00:00Z',
      autoPush: null, autoCreatePullRequests: null, autoMergePullRequests: null, landingMode: null,
    };
    api.listVoyages.mockResolvedValue(page([voyage]) as never);
    api.listVessels.mockResolvedValue(page([]) as never);
    api.listUsers.mockResolvedValue(page([]) as never);
    await renderScreen(<VoyagesList onSelect={jest.fn()} />);
    await fireEvent(await screen.findByTestId('voyage-row-vyg_1'), 'longPress');
    expect(screen.getByTestId('voyages-bulk-cancel')).toBeTruthy();
    let handled = false;
    await act(async () => { handled = pressBack(); });
    expect(handled).toBe(true);
    await waitFor(() => expect(screen.queryByTestId('voyages-bulk-cancel')).toBeNull());
    expect(pressBack()).toBe(false);
  });

  it('leaves Signals bulk selection on back (the shared selection mode)', async () => {
    const signal = (id: string): Signal => ({ id, tenantId: null, fromCaptainId: null, toCaptainId: null, type: 'Progress', payload: `payload ${id}`, read: false, createdUtc: '2026-10-07T10:00:00Z' });
    api.listSignals.mockResolvedValue(page([signal('sig_1'), signal('sig_2')]) as never);
    api.listCaptains.mockResolvedValue(page([]) as never);
    await renderScreen(<SignalsList onSelect={jest.fn()} />);
    await fireEvent(await screen.findByTestId('signal-row-sig_1'), 'longPress');
    expect(screen.getByTestId('signal-selection-delete')).toBeTruthy();
    let handled = false;
    await act(async () => { handled = pressBack(); });
    expect(handled).toBe(true);
    await waitFor(() => expect(screen.queryByTestId('signal-selection-delete')).toBeNull());
    expect(pressBack()).toBe(false);
  });
});
