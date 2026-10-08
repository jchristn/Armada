import AsyncStorage from '@react-native-async-storage/async-storage';
import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import { Slot } from 'expo-router';
import { renderRouter } from 'expo-router/testing-library';
import * as client from '@dashboard/api/client';
import type { Objective } from '@dashboard/types/models';
import * as auth from '../auth/AuthContext';
import ObjectivesRoute from '../app/(app)/(work)/objectives/index';
import BacklogItemRoute from '../app/(app)/(work)/backlog/[id]';
import { BuildProviders, buildSockets, page } from '../test/buildFixtures';

jest.mock('@dashboard/api/client', () => require('../test/buildClientMock').buildClientMockFactory());
jest.mock('../navigation/useLayout', () => ({
  TABLET_MIN_WIDTH: 768,
  layoutFor: (width: number, height: number) => ({ width, height, isTablet: true, landscape: true }),
  useLayout: () => ({ width: 1180, height: 820, isTablet: true, landscape: true }),
}));

const api = client as jest.Mocked<typeof client>;
const realUseAuth = auth.useAuth;

function objective(over: Partial<Objective>): Objective {
  return {
    id: 'obj_a', tenantId: null, userId: null, title: 'Alpha', description: null, status: 'Draft', kind: 'Feature', category: null,
    priority: 'P2', rank: 1, backlogState: 'Inbox', effort: 'M', owner: null, targetVersion: null, dueUtc: null, parentObjectiveId: null,
    blockedByObjectiveIds: [], refinementSummary: null, suggestedPipelineId: null, suggestedPlaybooks: [], refinementSessionIds: [],
    sourceProvider: null, sourceType: null, sourceId: null, sourceUrl: null, sourceUpdatedUtc: null, tags: [], acceptanceCriteria: [],
    nonGoals: [], rolloutConstraints: [], evidenceLinks: [], fleetIds: [], vesselIds: [], planningSessionIds: [], voyageIds: [],
    missionIds: [], checkRunIds: [], releaseIds: [], deploymentIds: [], incidentIds: [], createdUtc: '2026-10-07T12:00:00Z',
    lastUpdateUtc: '2026-10-07T12:00:00Z', completedUtc: null, ...over,
  };
}

describe('backlog on tablets', () => {
  beforeEach(async () => {
    await AsyncStorage.clear();
    jest.spyOn(auth, 'useAuth').mockImplementation(() => ({ ...realUseAuth(), isTenantAdmin: true, isAdmin: true }));
    api.listBacklog.mockResolvedValue(page([objective({})]));
    api.listFleets.mockResolvedValue(page([]));
    api.listVessels.mockResolvedValue(page([]));
    api.listCaptains.mockResolvedValue(page([]));
    api.listPipelines.mockResolvedValue(page([]));
    api.listBacklogRefinementSessions.mockResolvedValue([]);
    api.getBacklogItem.mockResolvedValue(objective({}));
  });
  afterEach(() => jest.restoreAllMocks());

  it('shows the selected item and the new-item form beside the list without navigating', async () => {
    api.createBacklogItem.mockResolvedValue(objective({ id: 'obj_n', title: 'Fresh' }));
    const result = renderRouter({
      _layout: () => <BuildProviders><Slot /></BuildProviders>,
      '(work)/objectives/index': ObjectivesRoute,
      '(work)/backlog/[id]': BacklogItemRoute,
    }, { initialUrl: '/objectives' });
    await result;
    await act(async () => { buildSockets()[0]?.open(); });
    await waitFor(() => expect(screen.getByTestId('objective-row-Alpha')).toBeTruthy());
    expect(screen.getByText('Select a backlog item')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('objective-row-Alpha'));
    await waitFor(() => expect(screen.getByTestId('objective-form-title').props.value).toBe('Alpha'));
    expect(result.getPathname()).toBe('/objectives');

    await fireEvent.press(screen.getByTestId('objectives-list-new'));
    await waitFor(() => expect(screen.getByTestId('objective-form-title').props.value).toBe(''));
    await fireEvent.changeText(screen.getByTestId('objective-form-title'), 'Fresh');
    api.getBacklogItem.mockResolvedValue(objective({ id: 'obj_n', title: 'Fresh' }));
    await fireEvent.press(screen.getByTestId('objective-form-save'));
    await waitFor(() => expect(api.createBacklogItem).toHaveBeenCalledWith(expect.objectContaining({ title: 'Fresh' })));
    await waitFor(() => expect(api.getBacklogItem).toHaveBeenCalledWith('obj_n'));
    expect(result.getPathname()).toBe('/objectives');
  });
});
