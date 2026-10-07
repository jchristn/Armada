import { act, fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { useState } from 'react';
import { Text } from 'react-native';
import * as client from '@dashboard/api/client';
import type { FleetAction, FleetActionRun, FleetActionRunTarget, FleetActionRunTargetSummary, Vessel } from '@dashboard/types/models';
import { FleetActionRunDetailView } from '../screens/fleetActions/FleetActionRunDetailView';
import { FleetActionRunsTab, runStatusParam } from '../screens/fleetActions/FleetActionRunsTab';
import { FleetActionsTab, parseVesselIds } from '../screens/fleetActions/FleetActionsTab';
import { RunActionSheet } from '../screens/fleetActions/RunActionSheet';
import { tailLines } from '../screens/fleetActions/TargetDetailSheet';
import { usePolling } from '../screens/fleetActions/usePolling';
import { BuildProviders, page } from '../test/buildFixtures';

jest.mock('@dashboard/api/client', () => require('../test/buildClientMock').buildClientMockFactory());

jest.mock('react-native-gesture-handler/ReanimatedSwipeable', () => {
  const { forwardRef } = jest.requireActual('react');
  return { __esModule: true, default: forwardRef(({ children }: { children: unknown }, _ref: unknown) => children) };
});

const mockAuth = { admin: true };
jest.mock('../auth/AuthContext', () => {
  const actual = jest.requireActual('../auth/AuthContext');
  return { ...actual, useAuth: () => ({ ...actual.useAuth(), isAdmin: mockAuth.admin, isTenantAdmin: mockAuth.admin }) };
});

const mockRouter = { push: jest.fn(), replace: jest.fn(), back: jest.fn(), setParams: jest.fn(), canGoBack: jest.fn(() => true) };
let mockParams: Record<string, string> = {};
jest.mock('expo-router', () => ({
  ...jest.requireActual('expo-router'),
  useRouter: () => mockRouter,
  useLocalSearchParams: () => mockParams,
  Stack: { Screen: () => null },
}));

const api = client as jest.Mocked<typeof client>;
const NOW = '2026-10-07T12:00:00Z';

function action(over: Partial<FleetAction> = {}): FleetAction {
  return {
    id: 'fac_1', tenantId: 'ten', userId: 'usr', name: 'Fast-forward', description: 'Pull the default branch', kind: 'Command',
    commandText: 'git -C {{vessel.workingDirectory}} pull --ff-only', promptTemplate: null, pipelineId: null, persona: null,
    timeoutSeconds: 300, defaultConcurrency: 4, requiresCleanWorkingTree: true, isBuiltIn: true, builtInKey: 'ff', active: true,
    createdUtc: NOW, lastUpdateUtc: NOW, ...over,
  };
}

function run(over: Partial<FleetActionRun> = {}): FleetActionRun {
  return {
    id: 'far_1', tenantId: 'ten', userId: 'usr', actionId: 'fac_1', actionName: 'Fast-forward', kind: 'Command', commandText: 'git pull',
    promptTemplate: null, pipelineId: null, persona: null, timeoutSeconds: 300, requiresCleanWorkingTree: true, concurrency: 4,
    status: 'Running', targetCount: 4, succeededCount: 1, failedCount: 1, skippedCount: 0, cancelledCount: 0,
    startedUtc: NOW, completedUtc: null, createdUtc: NOW, lastUpdateUtc: NOW, ...over,
  };
}

function target(over: Partial<FleetActionRunTargetSummary> = {}): FleetActionRunTargetSummary {
  return {
    id: 'fat_1', runId: 'far_1', vesselId: 'vsl_1', vesselName: 'api', status: 'Failed', skipReason: null, failureReason: 'NonZeroExit',
    exitCode: 2, outputTruncated: false, outputLength: 10, errorLength: 5, renderedLength: 8, voyageId: null,
    startedUtc: NOW, completedUtc: NOW, durationMs: 1500, createdUtc: NOW, lastUpdateUtc: NOW, ...over,
  };
}

const VESSEL = { id: 'vsl_1', name: 'api', defaultBranch: 'main', workingDirectory: '/src/api', fleetId: null } as Vessel;

afterEach(() => jest.useRealTimers());

beforeEach(() => {
  jest.clearAllMocks();
  mockAuth.admin = true;
  mockParams = {};
  api.enumerateFleetActions.mockResolvedValue(page([action(), action({ id: 'fac_2', name: 'Fix lint', kind: 'Mission', commandText: null, promptTemplate: 'Fix lint in {{vessel.name}}', isBuiltIn: false, defaultConcurrency: 2 })]));
  api.getSettings.mockResolvedValue({ fleetActions: { defaultTimeoutSeconds: 120 } } as never);
  api.listPipelines.mockResolvedValue(page([]));
  api.listPersonas.mockResolvedValue(page([]));
  api.listVessels.mockResolvedValue(page([VESSEL, { ...VESSEL, id: 'vsl_2', name: 'web', workingDirectory: '/src/web' }]));
  api.listFleets.mockResolvedValue(page([]));
  api.getVessel.mockResolvedValue(VESSEL);
});

describe('fleet action helpers', () => {
  it('parses the vessels and status parameters and tails output', () => {
    expect(parseVesselIds('vsl_1, vsl_2,,vsl_1')).toEqual(['vsl_1', 'vsl_2']);
    expect(parseVesselIds(undefined)).toEqual([]);
    expect(runStatusParam('Running')).toBe('Running');
    expect(runStatusParam('bogus')).toBe('');
    expect(tailLines('a\nb\nc', 2)).toBe('b\nc');
    expect(tailLines('a', 2)).toBe('a');
  });

  it('polls only while enabled', async () => {
    jest.useFakeTimers();
    const tick = jest.fn();
    function Probe() {
      const [on, setOn] = useState(true);
      usePolling(on, tick, 1000);
      return <Text testID="stop" onPress={() => setOn(false)}>p</Text>;
    }
    await render(<Probe />);
    await act(async () => { jest.advanceTimersByTime(2100); });
    expect(tick).toHaveBeenCalledTimes(2);
    await fireEvent.press(screen.getByTestId('stop'));
    await act(async () => { jest.advanceTimersByTime(5000); });
    expect(tick).toHaveBeenCalledTimes(2);
  });
});

describe('Actions tab', () => {
  it('lists actions and hides a built-in after the confirmation', async () => {
    await render(<BuildProviders><FleetActionsTab /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('fleet-action-row-Fast-forward')).toBeTruthy());
    expect(screen.getByTestId('fleet-action-row-Fix lint')).toBeTruthy();
    await act(async () => { fireEvent(screen.getByTestId('fleet-action-swipe-Fast-forward'), 'accessibilityAction', { nativeEvent: { actionName: 'delete' } }); });
    expect(screen.getByText('Hide built-in action')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('fleet-action-delete-confirm-confirm'));
    await waitFor(() => expect(api.deleteFleetAction).toHaveBeenCalledWith('fac_1'));
  });

  it('creates an action, refusing unknown template variables first', async () => {
    api.createFleetAction.mockResolvedValue(action({ id: 'fac_3', name: 'Status' }));
    await render(<BuildProviders><FleetActionsTab /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('fleet-actions-list-new')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('fleet-actions-list-new'));
    expect(screen.getByTestId('fleet-action-form-timeout').props.value).toBe('120');
    await fireEvent.changeText(screen.getByTestId('fleet-action-form-name'), 'Status');
    await fireEvent.changeText(screen.getByTestId('fleet-action-form-body'), 'git status {{vessel.nope}}');
    await fireEvent.press(screen.getByTestId('fleet-action-form-save'));
    expect(screen.getByText('Unknown template variable: vessel.nope')).toBeTruthy();
    expect(api.createFleetAction).not.toHaveBeenCalled();
    await fireEvent.changeText(screen.getByTestId('fleet-action-form-body'), 'git status ');
    await fireEvent.press(screen.getByTestId('template-variables'));
    await fireEvent.press(screen.getByTestId('template-insert-vessel.name'));
    await fireEvent.press(screen.getByTestId('fleet-action-form-save'));
    await waitFor(() => expect(api.createFleetAction).toHaveBeenCalledWith({
      Name: 'Status', Description: null, Kind: 'Command', CommandText: 'git status {{vessel.name}}', PromptTemplate: null, PipelineId: null,
      Persona: null, TimeoutSeconds: 120, DefaultConcurrency: 4, RequiresCleanWorkingTree: true,
    }));
  });

  it('opens the run sheet with preselected vessels from ?run=new&vessels=', async () => {
    mockParams = { run: 'new', vessels: 'vsl_1,vsl_2' };
    await render(<BuildProviders><FleetActionsTab /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('run-action-configure')).toBeTruthy());
    expect(screen.getByText('2 vessels selected')).toBeTruthy();
    expect(mockRouter.setParams).toHaveBeenCalledWith({ run: undefined, vessels: undefined });
  });

  it('starts with the vessel picker for ?run=new and continues to the run sheet', async () => {
    mockParams = { run: 'new' };
    await render(<BuildProviders><FleetActionsTab /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('vessel-picker-row-web')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('vessel-picker-row-web'));
    await fireEvent.press(screen.getByTestId('vessel-picker-continue'));
    await waitFor(() => expect(screen.getByTestId('run-action-configure')).toBeTruthy());
    expect(screen.getByText('1 vessel selected')).toBeTruthy();
  });

  it('read-only users only view JSON', async () => {
    mockAuth.admin = false;
    await render(<BuildProviders><FleetActionsTab /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('fleet-action-row-Fast-forward')).toBeTruthy());
    expect(screen.queryByTestId('fleet-actions-list-new')).toBeNull();
    expect(screen.queryByTestId('fleet-actions-run')).toBeNull();
    await fireEvent.press(screen.getByTestId('fleet-action-row-Fast-forward'));
    expect(screen.getByTestId('json-sheet')).toBeTruthy();
  });
});

describe('run sheet', () => {
  it('runs a saved action with the rendered preview, a confirmation, and the clean-tree override', async () => {
    api.runFleetAction.mockResolvedValue({ runId: 'far_9', actionId: 'fac_1', kind: 'Command', status: 'Pending', targetCount: 2, concurrency: 4 });
    const onStarted = jest.fn();
    await render(<BuildProviders><RunActionSheet open vesselIds={['vsl_1', 'vsl_2']} onClose={jest.fn()} onStarted={onStarted} /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('run-action-preview')).toBeTruthy());
    expect(screen.getByText('git -C /src/api pull --ff-only')).toBeTruthy();
    await fireEvent(screen.getByTestId('run-action-clean'), 'valueChange', false);
    await fireEvent.press(screen.getByTestId('run-action-review'));
    expect(screen.getByTestId('run-action-confirm')).toBeTruthy();
    expect(screen.getByText('The clean-tree check is off: the command also runs in vessels with uncommitted changes.')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('run-action-start'));
    await waitFor(() => expect(api.runFleetAction).toHaveBeenCalledWith('fac_1', { VesselIds: ['vsl_1', 'vsl_2'], Concurrency: 4, Overrides: { RequiresCleanWorkingTree: false } }));
    expect(onStarted).toHaveBeenCalledWith(expect.objectContaining({ runId: 'far_9' }));
  });

  it('runs an ad hoc command and opens the run when no handler is given', async () => {
    api.runAdHocFleetAction.mockResolvedValue({ runId: 'far_7', actionId: null, kind: 'Command', status: 'Pending', targetCount: 1, concurrency: 2 });
    await render(<BuildProviders><RunActionSheet open vesselIds={['vsl_1']} onClose={jest.fn()} /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('run-action-mode-adhoc')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('run-action-mode-adhoc'));
    await fireEvent.press(screen.getByTestId('run-action-review'));
    expect(screen.getByText('Name is required.')).toBeTruthy();
    await fireEvent.changeText(screen.getByTestId('run-action-name'), 'Status');
    await fireEvent.changeText(screen.getByTestId('run-action-body'), 'git status -sb');
    await fireEvent.changeText(screen.getByTestId('run-action-concurrency'), '2');
    await fireEvent.press(screen.getByTestId('run-action-review'));
    await fireEvent.press(screen.getByTestId('run-action-start'));
    await waitFor(() => expect(api.runAdHocFleetAction).toHaveBeenCalledWith({
      VesselIds: ['vsl_1'], Concurrency: 2,
      Definition: { Name: 'Status', Kind: 'Command', CommandText: 'git status -sb', PromptTemplate: null, PipelineId: null, TimeoutSeconds: 300, RequiresCleanWorkingTree: true },
    }));
    expect(mockRouter.push).toHaveBeenCalledWith('/fleet-actions/runs/far_7');
  });
});

describe('Runs tab and run detail', () => {
  it('lists runs with progress, filters by status, and cancels an active run', async () => {
    api.enumerateFleetActionRuns.mockResolvedValue(page([run(), run({ id: 'far_2', status: 'Completed', actionId: null, actionName: 'Status' })]));
    await render(<BuildProviders><FleetActionRunsTab /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('fleet-action-run-row-far_1')).toBeTruthy());
    expect(screen.getAllByText('1 succeeded, 1 failed, 0 skipped of 4').length).toBeGreaterThan(0);

    await fireEvent.press(screen.getByTestId('fleet-action-runs-list-filter'));
    await fireEvent.press(screen.getByTestId('fleet-action-runs-status-Failed'));
    await waitFor(() => expect(api.enumerateFleetActionRuns).toHaveBeenLastCalledWith({ pageNumber: 1, pageSize: 25, status: 'Failed', order: 'CreatedDescending' }));

    await act(async () => { fireEvent(screen.getByTestId('fleet-action-run-swipe-far_1'), 'accessibilityAction', { nativeEvent: { actionName: 'cancel' } }); });
    await fireEvent.press(screen.getByTestId('fleet-action-run-cancel-confirm-confirm'));
    await waitFor(() => expect(api.cancelFleetActionRun).toHaveBeenCalledWith('far_1'));

    await fireEvent.press(screen.getByTestId('fleet-action-run-row-far_2'));
    expect(mockRouter.push).toHaveBeenCalledWith('/fleet-actions/runs/far_2');
  });

  it('shows a run, its targets and output, and cancels it', async () => {
    api.getFleetActionRun.mockResolvedValue({ run: run(), targets: [] });
    api.enumerateFleetActionRunTargets.mockResolvedValue(page([target(), target({ id: 'fat_2', vesselName: 'web', status: 'Skipped', skipReason: 'DirtyTree', failureReason: null, exitCode: null })]));
    api.getFleetActionRunTarget.mockResolvedValue({ ...target(), tenantId: null, renderedText: 'git pull', outputText: 'pulled', errorText: 'boom' } as FleetActionRunTarget);
    api.cancelFleetActionRun.mockResolvedValue(run({ status: 'Cancelled' }));
    await render(<BuildProviders><FleetActionRunDetailView id="far_1" /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('fleet-action-target-row-api')).toBeTruthy());
    expect(screen.getByText('Live: refreshing every 5 s.')).toBeTruthy();
    expect(screen.getByText('Working tree has uncommitted changes')).toBeTruthy();

    await fireEvent.press(screen.getByTestId('fleet-action-target-row-api'));
    await waitFor(() => expect(screen.getByTestId('fleet-action-target-body')).toBeTruthy());
    expect(api.getFleetActionRunTarget).toHaveBeenCalledWith('far_1', 'fat_1');
    expect(screen.getByText('pulled')).toBeTruthy();
    expect(screen.getByText('Auto-refresh paused while a panel is open.')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('fleet-action-target-close'));

    await fireEvent.press(screen.getByTestId('fleet-action-run-cancel'));
    await fireEvent.press(screen.getByTestId('fleet-action-run-cancel-confirm-confirm'));
    await waitFor(() => expect(api.cancelFleetActionRun).toHaveBeenCalledWith('far_1'));
  });

  it('re-runs failed targets with the saved action, or its snapshot when the action is gone', async () => {
    api.getFleetActionRun.mockResolvedValue({ run: run({ status: 'CompletedWithFailures', completedUtc: NOW }), targets: [] });
    api.enumerateFleetActionRunTargets.mockImplementation(async (_id, q) => page(q?.status === 'Failed' ? [target()] : q?.status === 'TimedOut' ? [target({ id: 'fat_3', vesselId: 'vsl_3' })] : [target()]));
    api.getFleetAction.mockRejectedValue(new Error('gone'));
    await render(<BuildProviders><FleetActionRunDetailView id="far_1" /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('fleet-action-run-rerun')).toBeTruthy());
    expect(screen.getByText('Run finished; auto-refresh stopped.')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('fleet-action-run-rerun'));
    await waitFor(() => expect(screen.getByTestId('run-action-configure')).toBeTruthy());
    expect(screen.getByText('2 vessels selected')).toBeTruthy();
    expect(screen.getByTestId('run-action-name').props.value).toBe('Fast-forward');
  });

  it('opens the voyage of a Mission target', async () => {
    api.getFleetActionRun.mockResolvedValue({ run: run({ kind: 'Mission' }), targets: [] });
    api.enumerateFleetActionRunTargets.mockResolvedValue(page([target({ status: 'Running', voyageId: 'vyg_1', failureReason: null })]));
    await render(<BuildProviders><FleetActionRunDetailView id="far_1" /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('fleet-action-target-row-api')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('fleet-action-target-row-api'));
    expect(mockRouter.push).toHaveBeenCalledWith('/voyages/vyg_1');
  });
});
