import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import { Slot, Stack } from 'expo-router';
import { renderRouter } from 'expo-router/testing-library';
import * as client from '@dashboard/api/client';
import type { Captain, PlanningSession, PlanningSessionDetail } from '@dashboard/types/models';
import PlanningIndexRoute from '../app/(app)/(work)/planning/index';
import PlanningSessionRoute from '../app/(app)/(work)/planning/[id]';
import { BuildProviders, buildSockets, page } from '../test/buildFixtures';

jest.mock('@dashboard/api/client', () => require('../test/buildClientMock').buildClientMockFactory());
jest.mock('../navigation/useLayout', () => ({
  TABLET_MIN_WIDTH: 768,
  layoutFor: (width: number, height: number) => ({ width, height, isTablet: true, landscape: true }),
  useLayout: () => ({ width: 1180, height: 820, isTablet: true, landscape: true }),
}));

const api = client as jest.Mocked<typeof client>;
const NOW = '2026-10-07T12:00:00Z';

function session(id: string, title: string): PlanningSession {
  return {
    id, tenantId: null, userId: null, captainId: 'cpt_1', vesselId: 'vsl_1', fleetId: null, dockId: null, branchName: null, title, status: 'Active',
    pipelineId: null, processId: null, failureReason: null, createdUtc: NOW, startedUtc: NOW, completedUtc: null, lastUpdateUtc: NOW,
  };
}

function detail(s: PlanningSession): PlanningSessionDetail {
  return { session: s, messages: [], captain: { id: 'cpt_1', name: 'Ada', runtime: 'ClaudeCode', state: 'Idle' } as Captain, vessel: null };
}

const ROUTES = {
  _layout: () => <BuildProviders><Slot /></BuildProviders>,
  '(work)/_layout': () => <Stack />,
  '(work)/planning/index': PlanningIndexRoute,
  '(work)/planning/[id]': PlanningSessionRoute,
};

beforeEach(() => {
  api.listCaptains.mockResolvedValue(page([]));
  api.listFleets.mockResolvedValue(page([]));
  api.listVessels.mockResolvedValue(page([]));
  api.listPipelines.mockResolvedValue(page([]));
  api.listPlanningSessions.mockResolvedValue([session('pls_1', 'First plan'), session('pls_2', 'Second plan')]);
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
    // Selecting in place keeps the route; the list stays visible.
    expect(result.getPathname()).toBe('/planning/pls_2');
    expect(screen.getByTestId('planning-list')).toBeTruthy();
  });
});
