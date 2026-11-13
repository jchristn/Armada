import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import { Slot, Stack } from 'expo-router';
import { renderRouter } from 'expo-router/testing-library';
import * as client from '@dashboard/api/client';
import type { Captain, PlanningSession, PlanningSessionView } from '@dashboard/types/models';
import PlanningIndexRoute from '../app/(app)/(work)/planning/index';
import PlanningSessionRoute from '../app/(app)/(work)/planning/[id]';
import DispatchRoute from '../app/(app)/(work)/dispatch';
import { BuildProviders, buildSockets, page } from '../test/buildFixtures';
import { serveTranscriptsFromDetails } from '../test/transcripts';

jest.mock('@dashboard/api/client', () => require('../test/buildClientMock').buildClientMockFactory());
// An iPad in landscape (1180 x 820).
jest.mock('react-native/Libraries/Utilities/useWindowDimensions', () => ({
  __esModule: true,
  default: () => ({ width: 1180, height: 820, scale: 2, fontScale: 1 }),
}));

const api = client as jest.Mocked<typeof client>;
const NOW = '2026-10-07T12:00:00Z';

function session(id: string, title: string): PlanningSession {
  return {
    id, tenantId: null, userId: null, captainId: 'cpt_1', vesselId: 'vsl_1', fleetId: null, dockId: null, branchName: null, title, status: 'Active',
    pipelineId: null, processId: null, failureReason: null, createdUtc: NOW, startedUtc: NOW, completedUtc: null, lastUpdateUtc: NOW,
  };
}

function detail(s: PlanningSession): PlanningSessionView {
  return { session: s, messages: [], messageCount: 0, captain: { id: 'cpt_1', name: 'Ada', runtime: 'ClaudeCode', state: 'Idle' } as Captain, vessel: null };
}

const ROUTES = {
  _layout: () => <BuildProviders><Slot /></BuildProviders>,
  '(work)/_layout': () => <Stack />,
  '(work)/planning/index': PlanningIndexRoute,
  '(work)/planning/[id]': PlanningSessionRoute,
  '(work)/dispatch': DispatchRoute,
};

beforeEach(() => {
  serveTranscriptsFromDetails(api as unknown as Record<string, jest.Mock>);
  api.listCaptains.mockResolvedValue(page([]));
  api.listFleets.mockResolvedValue(page([]));
  api.listVessels.mockResolvedValue(page([]));
  api.listPipelines.mockResolvedValue(page([]));
  api.listPlanningSessions.mockResolvedValue(page([session('pls_1', 'First plan'), session('pls_2', 'Second plan')]));
  api.getPlanningSession.mockImplementation(async (id: string) => detail(session(id, id === 'pls_1' ? 'First plan' : 'Second plan')));
});

describe('Planning on tablets', () => {
  it('shows the list and the selected session side by side, and a deep link opens beside the list', async () => {
    const result = renderRouter(ROUTES, { initialUrl: '/planning/pls_2' });
    await result;
    await act(async () => { buildSockets()[0]?.open(); });
    await waitFor(() => expect(screen.getByTestId('split-view')).toBeTruthy());
    await waitFor(() => expect(screen.getByTestId('planning-session')).toBeTruthy());
    expect(api.getPlanningSession).toHaveBeenCalledWith('pls_2');
    await waitFor(() => expect(screen.getByText('First plan')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('planning-open-pls_1'));
    await waitFor(() => expect(api.getPlanningSession).toHaveBeenCalledWith('pls_1'));
    // The old /planning/:id link landed on the Dispatch hub's Planning tab; selecting in place keeps that route and
    // the hub's tabs, and the list stays visible.
    expect(result.getPathname()).toBe('/dispatch');
    expect(result.getSearchParams()).toEqual(expect.objectContaining({ tab: 'planning', session: 'pls_2' }));
    expect(screen.getByTestId('planning-list')).toBeTruthy();
    expect(screen.getByTestId('dispatch-tab-planning')).toBeTruthy();
  });
});
