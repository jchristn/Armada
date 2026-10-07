import { fireEvent, screen, waitFor } from '@testing-library/react-native';
import * as client from '@dashboard/api/client';
import { DispatchHubScreen } from '../screens/operations/DispatchHubScreen';
import { DispatchForm } from '../screens/operations/DispatchForm';
import { dispatchHref, dispatchPrefillFromParams, parsePlaybooksParam } from '../screens/operations/w24/dispatchLink';
import { page } from '../test/operationsClient';
import { mockRouter, setMockParams } from '../test/routerMock';
import { renderScreen } from '../test/screen';

jest.mock('@dashboard/api/client', () => ({
  ...require('../test/operationsClient').operationsClientMockFactory(),
  getVesselReadiness: jest.fn(),
}));
jest.mock('expo-router', () => require('../test/routerMock').routerMockFactory());

const api = client as jest.Mocked<typeof client>;

const PIPELINE = {
  id: 'ppl_1', name: 'Reviewed',
  stages: [
    { id: 's1', pipelineId: 'ppl_1', order: 1, personaName: 'Worker', isOptional: false, description: null, requiresReview: false, reviewDenyAction: 'RetryStage' },
    { id: 's2', pipelineId: 'ppl_1', order: 2, personaName: 'Judge', isOptional: false, description: null, requiresReview: false, reviewDenyAction: 'RetryStage' },
  ],
};

beforeEach(() => {
  setMockParams({});
  api.listVessels.mockResolvedValue(page([{ id: 'vsl_b', name: 'web' }, { id: 'vsl_a', name: 'api' }]) as never);
  api.listPipelines.mockResolvedValue(page([PIPELINE]) as never);
  api.listCaptains.mockResolvedValue(page([{ id: 'cpt_1', name: 'claude-1', tier: 'Premium', runtime: 'ClaudeCode' }]) as never);
  api.listPersonas.mockResolvedValue(page([{ name: 'Worker', defaultCaptainId: 'cpt_1' }, { name: 'Judge' }]) as never);
  api.listPlaybooks.mockResolvedValue(page([{ id: 'pbk_1', fileName: 'style.md', active: true, description: 'Style' }]) as never);
  (api.getVesselReadiness as jest.Mock).mockResolvedValue({
    vesselId: 'vsl_a', hasWorkingDirectory: true, hasRepositoryContext: true, availableCheckTypes: [], issues: [], errorCount: 0, warningCount: 0,
    workflowProfileName: null, workflowProfileScope: null, setupChecklist: [], setupChecklistSatisfiedCount: 0, setupChecklistTotalCount: 0,
  });
  api.createVoyage.mockResolvedValue({ id: 'vyg_9' } as never);
});

describe('dispatch links', () => {
  it('reads the vessel prefill and other drafts from the query', () => {
    expect(dispatchPrefillFromParams({})).toBeNull();
    expect(dispatchPrefillFromParams({ from: 'vessel', vesselId: 'vsl_1' })).toMatchObject({ fromVessel: true, vesselId: 'vsl_1' });
    expect(dispatchPrefillFromParams({ vesselId: 'vsl_1' })).toMatchObject({ fromVessel: true });
    const draft = dispatchPrefillFromParams({ from: 'incident', vesselId: 'v', pipeline: 'P', prompt: 'fix', voyageTitle: 'T', objectiveId: 'obj_1',
      playbooks: '[{"playbookId":"pbk_1","deliveryMode":"AttachIntoWorktree"},{"bad":1}]' });
    expect(draft).toMatchObject({ fromIncident: true, fromVessel: false, pipelineName: 'P', prompt: 'fix', voyageTitle: 'T', objectiveId: 'obj_1',
      selectedPlaybooks: [{ playbookId: 'pbk_1', deliveryMode: 'AttachIntoWorktree' }] });
    expect(parsePlaybooksParam('not json')).toEqual([]);
  });

  it('builds links that parse back to the same draft', () => {
    const href = dispatchHref('vessel', { vesselId: 'vsl_1' });
    expect(href).toBe('/dispatch?from=vessel&vesselId=vsl_1');
    const query = Object.fromEntries(href.split('?')[1].split('&').map((kv) => kv.split('=').map(decodeURIComponent)));
    expect(dispatchPrefillFromParams(query)).toMatchObject({ fromVessel: true, vesselId: 'vsl_1' });
  });
});

describe('Dispatch', () => {
  it('preselects the vessel from a vessel link, shows readiness, and dispatches a voyage', async () => {
    setMockParams({ from: 'vessel', vesselId: 'vsl_a' });
    const scheduled: (() => void)[] = [];
    await renderScreen(<DispatchForm prefill={dispatchPrefillFromParams({ from: 'vessel', vesselId: 'vsl_a' })} schedule={(fn) => { scheduled.push(fn); }} />);
    await waitFor(() => expect(screen.getByTestId('dispatch-vessel')).toHaveProp('accessibilityLabel', 'Vessel, api'));
    expect(api.getVesselReadiness).toHaveBeenCalledWith('vsl_a');
    expect(await screen.findByText('This vessel looks ready for the currently selected workflow surface.')).toBeTruthy();
    expect(screen.queryByTestId('dispatch-prefill-notice')).toBeNull();
    expect(screen.getByTestId('dispatch-submit')).toBeDisabled();

    await fireEvent.changeText(screen.getByTestId('dispatch-prompt'), '  Fix the login bug  ');
    await fireEvent.press(screen.getByTestId('dispatch-submit'));
    await waitFor(() => expect(api.createVoyage).toHaveBeenCalledWith({
      title: 'Fix the login bug',
      vesselId: 'vsl_a',
      missions: [{ vesselId: 'vsl_a', title: 'Fix the login bug', description: 'Fix the login bug', priority: 100 }],
    }));
    expect(await screen.findByText('Dispatched voyage with 1 mission(s)')).toBeTruthy();
    expect(mockRouter.push).not.toHaveBeenCalledWith('/voyages/vyg_9');
    scheduled.forEach((fn) => fn());
    expect(mockRouter.push).toHaveBeenCalledWith('/voyages/vyg_9');
  });

  it('sends the pipeline, priority, title, playbooks, and per-step captains', async () => {
    await renderScreen(<DispatchForm prefill={null} schedule={() => undefined} />);
    await fireEvent.press(await screen.findByTestId('dispatch-vessel'));
    await fireEvent.press(await screen.findByTestId('dispatch-vessel-option-vsl_b'));
    await fireEvent.press(screen.getByTestId('dispatch-pipeline'));
    await fireEvent.press(await screen.findByTestId('dispatch-pipeline-option-Reviewed'));
    // Worker is seeded from the persona default; Judge gets a fallback tier.
    expect(await screen.findByTestId('dispatch-step-Worker')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('dispatch-tier-Judge'));
    await fireEvent.press(await screen.findByTestId('dispatch-tier-Judge-option-Standard'));
    await fireEvent.changeText(screen.getByTestId('dispatch-priority'), '42');
    await fireEvent.changeText(screen.getByTestId('dispatch-voyage-title'), 'Named voyage');
    await fireEvent.press(await screen.findByTestId('dispatch-playbooks-add'));
    await fireEvent.press(await screen.findByTestId('dispatch-playbooks-add-option-pbk_1'));
    await fireEvent.press(screen.getByTestId('dispatch-playbooks-add-submit'));
    await fireEvent.changeText(screen.getByTestId('dispatch-prompt'), 'Do it');
    await fireEvent.press(screen.getByTestId('dispatch-submit'));
    await waitFor(() => expect(api.createVoyage).toHaveBeenCalledWith({
      title: 'Named voyage',
      vesselId: 'vsl_b',
      missions: [{ vesselId: 'vsl_b', title: 'Do it', description: 'Do it', priority: 42 }],
      pipeline: 'Reviewed',
      selectedPlaybooks: [{ playbookId: 'pbk_1', deliveryMode: 'InlineFullContent' }],
      captainAssignments: [
        { persona: 'Worker', captainId: 'cpt_1', fallbackTier: null },
        { persona: 'Judge', captainId: null, fallbackTier: 'Standard' },
      ],
    }));
    expect(await screen.findByText('Dispatched voyage with 2 pipeline stages')).toBeTruthy();
  });

  it('shows the server error when dispatch fails', async () => {
    api.createVoyage.mockRejectedValue(new Error('vessel is busy'));
    await renderScreen(<DispatchForm prefill={{ fromPlanning: true, vesselId: 'vsl_a', prompt: 'From planning' }} schedule={() => undefined} />);
    expect(await screen.findByTestId('dispatch-prefill-notice')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('dispatch-submit'));
    expect(await screen.findByText('Failed: vessel is busy')).toBeTruthy();
  });

  it('hub: Dispatch by default, Backlog tab points to W3.3', async () => {
    setMockParams({ tab: 'backlog' });
    await renderScreen(<DispatchHubScreen />);
    expect(await screen.findByTestId('dispatch-backlog-pending')).toBeTruthy();
    expect(screen.getByText('Coming in W3.3')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('dispatch-tab-dispatch'));
    expect(mockRouter.setParams).toHaveBeenCalledWith({ tab: 'dispatch' });
  });
});
