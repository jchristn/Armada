import { act, fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import * as client from '@dashboard/api/client';
import type { Fleet, Pipeline, Vessel } from '@dashboard/types/models';
import { FleetDetailView } from '../screens/fleets/FleetDetailView';
import { pipelineLabel } from '../screens/fleets/FleetFormSheet';
import { FleetsTab } from '../screens/fleets/FleetsTab';
import { filterFleets, vesselCounts, vesselsOfFleet } from '../screens/fleets/fleetData';
import { BuildProviders, page } from '../test/buildFixtures';
import { rowActionTarget } from '../test/a11y';

jest.mock('@dashboard/api/client', () => require('../test/buildClientMock').buildClientMockFactory());

// The swipe container has no gesture runtime under Jest; render its rows directly (SwipeRow keeps the actions as
// accessibility actions, which the tests use).
jest.mock('react-native-gesture-handler/ReanimatedSwipeable', () => {
  const { forwardRef } = jest.requireActual('react');
  return { __esModule: true, default: forwardRef(({ children }: { children: unknown }, _ref: unknown) => children) };
});

const mockRouter = { push: jest.fn(), replace: jest.fn(), back: jest.fn(), setParams: jest.fn(), canGoBack: jest.fn(() => true) };
jest.mock('expo-router', () => ({
  ...jest.requireActual('expo-router'),
  useRouter: () => mockRouter,
  useLocalSearchParams: () => ({}),
  // Rendered outside a navigator: focus effects (hardware back in selection mode) do not run.
  useFocusEffect: () => undefined,
  Stack: { Screen: () => null },
}));

const api = client as jest.Mocked<typeof client>;
const NOW = '2026-10-07T12:00:00Z';

function fleet(over: Partial<Fleet> = {}): Fleet {
  return { id: 'flt_1', name: 'core', tenantId: 'ten_1', description: 'Core services', defaultPipelineId: null, active: true, createdUtc: NOW, lastUpdateUtc: NOW, ...over };
}

function vessel(over: Partial<Vessel> = {}): Vessel {
  return { id: 'vsl_1', name: 'api', fleetId: 'flt_1', repoUrl: 'https://git/api', defaultBranch: 'main', ...over } as Vessel;
}

const CORE = fleet();
const EDGE = fleet({ id: 'flt_2', name: 'edge', description: null });
const PIPELINE = { id: 'ppl_1', name: 'Reviewed', stages: [{ personaName: 'Worker' }, { personaName: 'Judge' }] } as Pipeline;
const VESSELS = [vessel(), vessel({ id: 'vsl_2', name: 'web' }), vessel({ id: 'vsl_3', name: 'tools', fleetId: 'flt_2' })];

beforeEach(() => {
  jest.clearAllMocks();
  api.listFleets.mockResolvedValue(page([EDGE, CORE]));
  api.listVessels.mockResolvedValue(page(VESSELS));
  api.listPipelines.mockResolvedValue(page([PIPELINE]));
});

describe('fleet logic', () => {
  it('counts vessels per fleet, lists a fleet\'s vessels, and filters and sorts fleets by name', () => {
    expect(vesselCounts(VESSELS).get('flt_1')).toBe(2);
    expect(vesselsOfFleet(VESSELS, 'flt_2').map((v) => v.name)).toEqual(['tools']);
    expect(filterFleets([EDGE, CORE], '').map((f) => f.name)).toEqual(['core', 'edge']);
    expect(filterFleets([EDGE, CORE], 'services').map((f) => f.name)).toEqual(['core']);
    expect(pipelineLabel(PIPELINE)).toBe('Reviewed (Worker -> Judge)');
  });
});

describe('Fleets tab', () => {
  it('lists fleets with vessel counts and creates one through the form sheet', async () => {
    api.createFleet.mockResolvedValue(fleet({ id: 'flt_3', name: 'new' }));
    await render(<BuildProviders><FleetsTab /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('fleet-row-core')).toBeTruthy());
    expect(screen.getByTestId('fleet-row-core').props.accessibilityLabel).toContain('2 vessels');
    expect(screen.getByTestId('fleet-row-edge')).toBeTruthy();

    await fireEvent.changeText(screen.getByTestId('fleets-list-search'), 'edg');
    expect(screen.queryByTestId('fleet-row-core')).toBeNull();
    await fireEvent.changeText(screen.getByTestId('fleets-list-search'), '');

    await fireEvent.press(screen.getByTestId('fleets-list-new'));
    await fireEvent.press(screen.getByTestId('fleet-form-save'));
    expect(screen.getByText('Name is required.')).toBeTruthy();
    expect(api.createFleet).not.toHaveBeenCalled();
    await fireEvent.changeText(screen.getByTestId('fleet-form-name'), 'new');
    await fireEvent.press(screen.getByTestId('fleet-form-pipeline'));
    await fireEvent.press(screen.getByTestId('fleet-form-pipeline-option-ppl_1'));
    await fireEvent.press(screen.getByTestId('fleet-form-save'));
    await waitFor(() => expect(api.createFleet).toHaveBeenCalledWith({ name: 'new', description: '', defaultPipelineId: 'ppl_1' }));
    await waitFor(() => expect(api.listFleets).toHaveBeenCalledTimes(2));
  });

  it('deletes one fleet after the confirmation, and several with long-press selection', async () => {
    api.deleteFleetsBatch.mockResolvedValue({ deleted: 1, skipped: [{ id: 'flt_2', reason: 'has vessels' }] });
    await render(<BuildProviders><FleetsTab /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('fleet-swipe-core')).toBeTruthy());

    await act(async () => { fireEvent(rowActionTarget(screen.getByTestId('fleet-swipe-core'), 'delete'), 'accessibilityAction', { nativeEvent: { actionName: 'delete' } }); });
    expect(screen.getByText('Delete fleet "core"? This cannot be undone.')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('fleet-delete-confirm-confirm'));
    await waitFor(() => expect(api.deleteFleet).toHaveBeenCalledWith('flt_1'));

    await fireEvent(screen.getByTestId('fleet-row-core'), 'longPress');
    await fireEvent.press(screen.getByTestId('fleet-row-edge'));
    expect(screen.getByTestId('fleets-bulk-bar')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('fleets-delete-selected'));
    expect(screen.getByText('Delete 2 selected fleet(s)? This cannot be undone.')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('fleet-delete-confirm-confirm'));
    await waitFor(() => expect(api.deleteFleetsBatch).toHaveBeenCalledWith(['flt_1', 'flt_2']));
    expect(screen.queryByTestId('fleets-bulk-bar')).toBeNull();
  });

  it('duplicates a fleet and opens the copy', async () => {
    api.createFleet.mockResolvedValue(fleet({ id: 'flt_9', name: 'core (Copy)' }));
    await render(<BuildProviders><FleetsTab /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('fleet-swipe-core')).toBeTruthy());
    await act(async () => { fireEvent(rowActionTarget(screen.getByTestId('fleet-swipe-core'), 'duplicate'), 'accessibilityAction', { nativeEvent: { actionName: 'duplicate' } }); });
    await waitFor(() => expect(mockRouter.push).toHaveBeenCalledWith('/fleets/flt_9'));
    expect(api.createFleet.mock.calls[0][0]).toMatchObject({ name: 'core (Copy)' });
  });
});

describe('fleet detail', () => {
  it('shows the fleet, its vessels, and saves an edit that clears the default pipeline', async () => {
    api.listFleets.mockResolvedValue(page([fleet({ defaultPipelineId: 'ppl_1' })]));
    api.updateFleet.mockResolvedValue(fleet());
    await render(<BuildProviders><FleetDetailView id="flt_1" /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('fleet-detail-pipeline')).toBeTruthy());
    expect(screen.getByTestId('fleet-detail-pipeline').props.accessibilityLabel).toBe('Default Pipeline: Reviewed');
    expect(screen.getByTestId('fleet-vessel-api')).toBeTruthy();
    expect(screen.queryByTestId('fleet-vessel-tools')).toBeNull();
    await fireEvent.press(screen.getByTestId('fleet-vessel-web'));
    expect(mockRouter.push).toHaveBeenCalledWith('/vessels/vsl_2');

    await fireEvent.press(screen.getByTestId('fleet-detail-edit'));
    await fireEvent.press(screen.getByTestId('fleet-form-pipeline'));
    await fireEvent.press(screen.getByTestId('fleet-form-pipeline-option-none'));
    await fireEvent.press(screen.getByTestId('fleet-form-save'));
    await waitFor(() => expect(api.updateFleet).toHaveBeenCalledWith('flt_1', { name: 'core', description: 'Core services', defaultPipelineId: '' }));
  });

  it('deletes the fleet after the confirmation and reports it', async () => {
    const onDeleted = jest.fn();
    await render(<BuildProviders><FleetDetailView id="flt_1" embedded onDeleted={onDeleted} /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('fleet-detail-delete')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('fleet-detail-delete'));
    await fireEvent.press(screen.getByTestId('fleet-delete-confirm-confirm'));
    await waitFor(() => expect(onDeleted).toHaveBeenCalled());
    expect(api.deleteFleet).toHaveBeenCalledWith('flt_1');
  });

  it('says when the fleet does not exist', async () => {
    await render(<BuildProviders><FleetDetailView id="flt_x" /></BuildProviders>);
    await waitFor(() => expect(screen.getByText('Fleet not found.')).toBeTruthy());
  });
});
