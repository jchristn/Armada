import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import * as client from '@dashboard/api/client';
import { normalizeMissionResponse, relevantWorkflowProfiles, setupBacklogPath, upsertById } from '@dashboard/lib/setupWizard';
import MoreLayout from '../app/(app)/(more)/_layout';
import SetupRoute from '../app/(app)/(more)/setup';
import { page, renderW4Routes, resetW4 } from '../test/w4';

jest.mock('@dashboard/api/client', () => require('../test/w4Client').autoMockClient());

const api = client as jest.Mocked<typeof client>;

const ROUTES = {
  '(more)/_layout': MoreLayout,
  '(more)/more': () => null,
  '(more)/setup': SetupRoute,
  '(more)/missions/[id]': () => null,
};

const MISSION = { id: 'msn_1', title: 'Repository onboarding survey', status: 'Assigned', captainId: 'cpt_1', vesselId: 'vsl_1', branchName: 'armada/x' };

beforeEach(async () => {
  await resetW4();
  jest.clearAllMocks();
  api.listFleets.mockResolvedValue(page([{ id: 'flt_1', name: 'Main' }]) as never);
  api.listVessels.mockResolvedValue(page([{ id: 'vsl_1', name: 'armada', fleetId: 'flt_1', defaultBranch: 'main' }]) as never);
  api.listCaptains.mockResolvedValue(page([{ id: 'cpt_1', name: 'Ada', runtime: 'ClaudeCode', state: 'Idle' }]) as never);
  api.getVesselReadiness.mockResolvedValue({ setupChecklist: [{ title: 'Add a workflow profile', message: 'Teach Armada to build.', isSatisfied: false }], setupChecklistSatisfiedCount: 2, setupChecklistTotalCount: 5, errorCount: 0 } as never);
  api.listWorkflowProfiles.mockResolvedValue(page([{ id: 'wfp_1', scope: 'Global' }, { id: 'wfp_2', scope: 'Vessel', vesselId: 'vsl_other' }]) as never);
  api.listEnvironments.mockResolvedValue(page([]) as never);
});

describe('setup wizard helpers (shared lib)', () => {
  it('normalizes dispatch responses, upserts, and builds handoff links', () => {
    expect(normalizeMissionResponse({ mission: { id: 'm' }, warning: 'w' })).toEqual({ mission: { id: 'm' }, warning: 'w' });
    expect(normalizeMissionResponse({ id: 'm' })).toEqual({ mission: { id: 'm' } });
    expect(normalizeMissionResponse(null)).toEqual({ mission: null });
    expect(upsertById([{ id: 'a', n: 1 }], { id: 'a', n: 2 })).toEqual([{ id: 'a', n: 2 }]);
    expect(upsertById([{ id: 'a' }], { id: 'b' })).toEqual([{ id: 'b' }, { id: 'a' }]);
    expect(setupBacklogPath('flt_1', 'vsl_1')).toBe('/backlog?fleetId=flt_1&vesselId=vsl_1');
    expect(setupBacklogPath('', '')).toBe('/backlog');
    expect(relevantWorkflowProfiles([{ scope: 'Global' }, { scope: 'Fleet', fleetId: 'f' }, { scope: 'Vessel', vesselId: 'x' }] as never, 'f', 'v')).toHaveLength(2);
  });
});

describe('setup wizard', () => {
  it('uses existing resources, dispatches the first mission, and hands off', async () => {
    api.dispatchMission.mockResolvedValue(MISSION as never);
    api.getMission.mockResolvedValue({ ...MISSION, status: 'InProgress' } as never);
    const h = await renderW4Routes(ROUTES, '/setup');
    await waitFor(() => expect(screen.getByTestId('setup-next')).toBeTruthy());
    await waitFor(() => expect(screen.getByTestId('setup-next').props.accessibilityState.disabled).toBe(false));
    await fireEvent.press(screen.getByTestId('setup-next'));
    expect(screen.getByTestId('setup-step-count')).toHaveTextContent('Step 2 of 6');
    await act(async () => { await fireEvent.press(screen.getByTestId('setup-fleet-submit')); });
    await act(async () => { await fireEvent.press(screen.getByTestId('setup-vessel-submit')); });
    await act(async () => { await fireEvent.press(screen.getByTestId('setup-captain-submit')); });
    expect(screen.getByTestId('setup-step-count')).toHaveTextContent('Step 5 of 6');
    await act(async () => { await fireEvent.press(screen.getByTestId('setup-dispatch')); });
    expect(api.dispatchMission).toHaveBeenCalledWith(expect.objectContaining({ vesselId: 'vsl_1', title: 'Repository onboarding survey', priority: 100 }));
    await waitFor(() => expect(screen.getByTestId('setup-mission')).toBeTruthy());
    await waitFor(() => expect(screen.getByText('Teach Armada to build.')).toBeTruthy());
    expect(screen.getByText('2/5')).toBeTruthy();
    expect(screen.getByText('Create Environment')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('setup-open-mission'));
    await waitFor(() => expect(h.getPathname()).toBe('/missions/msn_1'));
  });

  it('creates a fleet and validates the vessel landing mode', async () => {
    api.listFleets.mockResolvedValue(page([]) as never);
    api.listVessels.mockResolvedValue(page([]) as never);
    api.createFleet.mockResolvedValue({ id: 'flt_9', name: 'Armada Starter Fleet' } as never);
    await renderW4Routes(ROUTES, '/setup');
    await waitFor(() => expect(screen.getByTestId('setup-next').props.accessibilityState.disabled).toBe(false));
    await fireEvent.press(screen.getByTestId('setup-next'));
    await act(async () => { await fireEvent.press(screen.getByTestId('setup-fleet-submit')); });
    expect(api.createFleet).toHaveBeenCalledWith({ name: 'Armada Starter Fleet', description: 'Created from the setup wizard.' });
    await fireEvent.changeText(screen.getByTestId('setup-vessel-name'), 'repo');
    await fireEvent.changeText(screen.getByTestId('setup-vessel-repo'), '/tmp/repo');
    await fireEvent.press(screen.getByTestId('setup-vessel-landing'));
    await waitFor(() => expect(screen.getByTestId('setup-vessel-landing-option-LocalMerge')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('setup-vessel-landing-option-LocalMerge'));
    await act(async () => { await fireEvent.press(screen.getByTestId('setup-vessel-submit')); });
    expect(screen.getByText('Local Merge needs a working directory to merge into.')).toBeTruthy();
    expect(api.createVessel).not.toHaveBeenCalled();
  });

  it('Skip Setup opens Missions', async () => {
    const h = await renderW4Routes({ ...ROUTES, '(more)/missions/index': () => null }, '/setup');
    await fireEvent.press(screen.getByTestId('setup-skip'));
    await waitFor(() => expect(h.getPathname()).toBe('/missions'));
  });
});
