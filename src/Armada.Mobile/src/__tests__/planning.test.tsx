import AsyncStorage from '@react-native-async-storage/async-storage';
import { act, fireEvent, screen, waitFor } from '@testing-library/react-native';
import { Slot, Stack } from 'expo-router';
import { renderRouter } from 'expo-router/testing-library';
import { Text } from 'react-native';
import * as client from '@dashboard/api/client';
import type { Captain, PlanningSession, PlanningSessionDetail, PlanningSessionMessage, Vessel } from '@dashboard/types/models';
import PlanningIndexRoute from '../app/(app)/(work)/planning/index';
import PlanningSessionRoute from '../app/(app)/(work)/planning/[id]';
import { dispatchPrefillFromParams } from '../screens/operations/w24/dispatchLink';
import { planningDispatchHref } from '../screens/planning/planningDispatch';
import { prefillFromParams } from '../screens/planning/PlanningScreen';
import { BuildProviders, buildSockets, page } from '../test/buildFixtures';

jest.mock('@dashboard/api/client', () => require('../test/buildClientMock').buildClientMockFactory());

const api = client as jest.Mocked<typeof client>;
const NOW = '2026-10-07T12:00:00Z';

function session(over: Partial<PlanningSession> = {}): PlanningSession {
  return {
    id: 'pls_1', tenantId: null, userId: null, captainId: 'cpt_1', vesselId: 'vsl_1', fleetId: null, dockId: null, branchName: 'armada/plan-1',
    title: 'Plan login fix', status: 'Active', pipelineId: null, processId: null, failureReason: null, createdUtc: NOW, startedUtc: NOW,
    completedUtc: null, lastUpdateUtc: NOW, selectedPlaybooks: [], ...over,
  };
}

function message(over: Partial<PlanningSessionMessage>): PlanningSessionMessage {
  return { id: 'pmg_1', planningSessionId: 'pls_1', tenantId: null, userId: null, role: 'User', sequence: 1, content: 'hello', isSelectedForDispatch: false, createdUtc: NOW, lastUpdateUtc: NOW, ...over };
}

const CAPTAIN = { id: 'cpt_1', name: 'Ada', runtime: 'ClaudeCode', state: 'Idle', supportsPlanningSessions: true } as Captain;
const BUSY_CAPTAIN = { id: 'cpt_2', name: 'Bob', runtime: 'ClaudeCode', state: 'Working', supportsPlanningSessions: true } as Captain;
const VESSEL = { id: 'vsl_1', name: 'demo-api', fleetId: null } as unknown as Vessel;

function detail(over: Partial<PlanningSessionDetail> = {}): PlanningSessionDetail {
  return { session: session(), messages: [], captain: CAPTAIN, vessel: VESSEL, ...over };
}

const ROUTES = {
  _layout: () => <BuildProviders><Slot /></BuildProviders>,
  '(work)/_layout': () => <Stack />,
  '(work)/planning/index': PlanningIndexRoute,
  '(work)/planning/[id]': PlanningSessionRoute,
  '(work)/voyages/[id]': () => <Text>Voyage screen</Text>,
  '(work)/dispatch': () => <Text>Dispatch screen</Text>,
};

async function renderAt(initialUrl: string) {
  const result = renderRouter(ROUTES, { initialUrl });
  await result;
  await act(async () => { buildSockets()[0]?.open(); });
  await act(async () => { await Promise.resolve(); });
  return { getPathname: () => result.getPathname() };
}

async function emit(type: string, data: unknown) {
  await act(async () => { buildSockets()[0].message({ type, data }); });
}

beforeEach(async () => {
  await AsyncStorage.clear();
  api.listCaptains.mockResolvedValue(page([CAPTAIN, BUSY_CAPTAIN]));
  api.listFleets.mockResolvedValue(page([]));
  api.listVessels.mockResolvedValue(page([VESSEL]));
  api.listPipelines.mockResolvedValue(page([{ id: 'ppl_1', name: 'Reviewed' } as never]));
  api.listPlaybooks.mockResolvedValue(page([]));
  api.listPlanningSessions.mockResolvedValue([session()]);
  api.getVesselReadiness.mockResolvedValue(null as never);
});

describe('planning helpers', () => {
  it('builds the Dispatch route params the dashboard passes as router state', () => {
    const s = session({ pipelineId: 'ppl_1', selectedPlaybooks: [{ playbookId: 'pbk_1', deliveryMode: 'InlineFullContent' }] });
    // The Dispatch screen reads the link back into the dashboard's prefill state.
    const read = (href: string) => dispatchPrefillFromParams(Object.fromEntries(new URLSearchParams(href.split('?')[1]).entries()));
    expect(planningDispatchHref(s, [{ id: 'ppl_1', name: 'Reviewed' }], 'Do it', ' Voyage ')).toMatch(/^\/dispatch\?from=planning&/);
    expect(read(planningDispatchHref(s, [{ id: 'ppl_1', name: 'Reviewed' }], 'Do it', ' Voyage '))).toEqual({
      fromVessel: false, fromPlanning: true, fromWorkspace: false, fromIncident: false, fromObjective: false,
      vesselId: 'vsl_1', prompt: 'Do it', pipelineName: 'Reviewed', voyageTitle: 'Voyage',
      selectedPlaybooks: [{ playbookId: 'pbk_1', deliveryMode: 'InlineFullContent' }],
    });
    expect(read(planningDispatchHref(session(), [], 'Do it', null))).toMatchObject({ fromPlanning: true, vesselId: 'vsl_1', prompt: 'Do it' });
  });

  it('reads a start prefill from route params', () => {
    expect(prefillFromParams({})).toEqual({ open: false, source: null, prefill: null });
    expect(prefillFromParams({ new: '1' }).open).toBe(true);
    const r = prefillFromParams({ vesselId: 'vsl_1', objectiveId: 'obj_1', prompt: 'Plan it', from: 'objective' });
    expect(r.open).toBe(true);
    expect(r.source).toBe('objective');
    expect(r.prefill).toMatchObject({ vesselId: 'vsl_1', objectiveId: 'obj_1', initialPrompt: 'Plan it' });
  });
});

describe('Planning list', () => {
  it('lists sessions with names resolved and reloads on planning-session events', async () => {
    await renderAt('/planning');
    await waitFor(() => expect(screen.getByText('Plan login fix')).toBeTruthy());
    expect(screen.getByText(/Ada \u00b7 demo-api/)).toBeTruthy();
    api.listPlanningSessions.mockResolvedValue([session(), session({ id: 'pls_2', title: 'Second plan', lastUpdateUtc: '2026-10-07T13:00:00Z' })]);
    await emit('planning-session.message.updated', { sessionId: 'pls_1' });
    expect(api.listPlanningSessions).toHaveBeenCalledTimes(1);
    await emit('planning-session.changed', { session: session({ id: 'pls_2' }) });
    await waitFor(() => expect(screen.getByText('Second plan')).toBeTruthy());
  });

  it('starts a session with an idle captain and opens it', async () => {
    api.createPlanningSession.mockResolvedValue(detail());
    api.getPlanningSession.mockResolvedValue(detail());
    const { getPathname } = await renderAt('/planning');
    await waitFor(() => expect(screen.getByText('Plan login fix')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('planning-new'));
    await waitFor(() => expect(screen.getByTestId('planning-start-captain')).toBeTruthy());
    expect(screen.getByTestId('planning-start-submit')).toBeDisabled();
    await fireEvent.press(screen.getByTestId('planning-start-captain'));
    // Working captains cannot plan.
    expect(screen.getByTestId('planning-start-captain-option-cpt_2')).toBeDisabled();
    await fireEvent.press(screen.getByTestId('planning-start-captain-option-cpt_1'));
    await fireEvent.press(screen.getByTestId('planning-start-vessel'));
    await fireEvent.press(screen.getByTestId('planning-start-vessel-option-vsl_1'));
    await waitFor(() => expect(api.getVesselReadiness).toHaveBeenCalledWith('vsl_1'));
    await fireEvent.changeText(screen.getByTestId('planning-start-title'), 'Plan login fix');
    await fireEvent.press(screen.getByTestId('planning-start-submit'));
    await waitFor(() => expect(api.createPlanningSession).toHaveBeenCalledWith({
      title: 'Plan login fix', captainId: 'cpt_1', vesselId: 'vsl_1', fleetId: undefined, pipelineId: undefined, selectedPlaybooks: [], objectiveId: undefined,
    }));
    await waitFor(() => expect(getPathname()).toBe('/planning/pls_1'));
    await waitFor(() => expect(screen.getByTestId('planning-session')).toBeTruthy());
  });

  it('deletes every session only after the typed confirmation', async () => {
    api.listPlanningSessions.mockResolvedValue([session(), session({ id: 'pls_2', title: 'Second plan' })]);
    api.deletePlanningSession.mockImplementation(async (id: string) => { if (id === 'pls_2') throw new Error('running'); });
    await renderAt('/planning');
    await waitFor(() => expect(screen.getByText('Second plan')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('planning-list-delete-all'));
    expect(screen.getByTestId('planning-delete-all-confirm-confirm')).toBeDisabled();
    await fireEvent.changeText(screen.getByTestId('planning-delete-all-confirm-typed'), 'delete');
    await fireEvent.press(screen.getByTestId('planning-delete-all-confirm-confirm'));
    await waitFor(() => expect(api.deletePlanningSession).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(screen.queryByText('Plan login fix')).toBeNull());
    expect(screen.getByText('Second plan')).toBeTruthy();
  });
});

describe('Planning session', () => {
  const reply = message({ id: 'pmg_2', role: 'Assistant', sequence: 2, content: 'Step 1: fix the login form.' });

  it('streams messages, tools, and thinking live and sends with the stream and thinking options', async () => {
    api.getPlanningSession.mockResolvedValue(detail({ messages: [message({})] }));
    await renderAt('/planning/pls_1');
    await waitFor(() => expect(screen.getByText('hello')).toBeTruthy());

    await emit('planning-session.changed', { session: session({ status: 'Responding' }) });
    await waitFor(() => expect(screen.getByTestId('planning-thinking')).toBeTruthy());
    expect(screen.getByTestId('planning-stop')).toBeTruthy();
    await emit('planning-session.message.created', { sessionId: 'pls_1', message: { ...reply, content: '' } });
    await emit('planning-session.tool', { sessionId: 'pls_1', messageId: 'pmg_2', phase: 'started', id: 'tc_1', name: 'Read' });
    await emit('planning-session.thinking', { sessionId: 'pls_1', messageId: 'pmg_2', delta: 'Looking at the form' });
    await emit('planning-session.message.updated', { sessionId: 'pls_1', message: reply });
    // Another session's events are ignored.
    await emit('planning-session.message.created', { sessionId: 'pls_9', message: message({ id: 'x', content: 'elsewhere' }) });
    await waitFor(() => expect(screen.getByText('Step 1: fix the login form.')).toBeTruthy());
    expect(screen.getByTestId('tool-chip-tc_1')).toBeTruthy();
    expect(screen.queryByText('elsewhere')).toBeNull();

    api.stopPlanningTurn.mockResolvedValue(detail({ messages: [message({}), reply] }));
    await fireEvent.press(screen.getByTestId('planning-stop'));
    await waitFor(() => expect(api.stopPlanningTurn).toHaveBeenCalledWith('pls_1'));
    await waitFor(() => expect(screen.getByTestId('planning-send')).toBeTruthy());

    // The latest reply seeds the dispatch draft.
    expect(screen.getByTestId('planning-dispatch-description').props.value).toBe('Step 1: fix the login form.');
    expect(screen.getByTestId('planning-dispatch-title').props.value).toBe('Plan login fix');

    api.sendPlanningSessionMessage.mockResolvedValue(detail({ messages: [message({}), reply, message({ id: 'pmg_3', sequence: 3, content: 'And tests?' })] }));
    await fireEvent.press(screen.getByTestId('planning-details'));
    await fireEvent(screen.getByTestId('planning-show-thinking'), 'valueChange', true);
    await fireEvent.changeText(screen.getByTestId('planning-input'), 'And tests?');
    await fireEvent.press(screen.getByTestId('planning-send'));
    await waitFor(() => expect(api.sendPlanningSessionMessage).toHaveBeenCalledWith('pls_1', { content: 'And tests?', showThinking: true, stream: true }));
    await waitFor(() => expect(screen.getByText('And tests?')).toBeTruthy());
    expect(screen.getByTestId('planning-input').props.value).toBe('');
  });

  it('summarizes the selected reply into the draft and dispatches a voyage', async () => {
    api.getPlanningSession.mockResolvedValue(detail({ messages: [message({}), reply] }));
    api.summarizePlanningSession.mockResolvedValue({ sessionId: 'pls_1', messageId: 'pmg_2', title: 'Fix login', description: 'Summarized plan', method: 'Captain' });
    api.dispatchPlanningSession.mockResolvedValue({ id: 'vyg_1' } as never);
    const { getPathname } = await renderAt('/planning/pls_1');
    await waitFor(() => expect(screen.getByText('Step 1: fix the login form.')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('planning-summarize'));
    await waitFor(() => expect(api.summarizePlanningSession).toHaveBeenCalledWith('pls_1', { messageId: 'pmg_2', title: 'Plan login fix' }));
    await waitFor(() => expect(screen.getByTestId('planning-dispatch-description').props.value).toBe('Summarized plan'));
    expect(screen.getByTestId('planning-dispatch-title').props.value).toBe('Fix login');
    await fireEvent.press(screen.getByTestId('planning-dispatch'));
    await waitFor(() => expect(api.dispatchPlanningSession).toHaveBeenCalledWith('pls_1', { messageId: 'pmg_2', title: 'Fix login', description: 'Summarized plan' }));
    await waitFor(() => expect(getPathname()).toBe('/voyages/vyg_1'));
  });

  it('opens a reply in Dispatch after releasing the captain', async () => {
    api.getPlanningSession.mockResolvedValue(detail({ messages: [message({}), reply] }));
    api.stopPlanningSession.mockResolvedValue(detail({ session: session({ status: 'Stopped' }), messages: [message({}), reply] }));
    const { getPathname } = await renderAt('/planning/pls_1');
    await waitFor(() => expect(screen.getByText('Step 1: fix the login form.')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('planning-msg-2-dispatch'));
    await waitFor(() => expect(api.stopPlanningSession).toHaveBeenCalledWith('pls_1'));
    await waitFor(() => expect(getPathname()).toBe('/dispatch'));
  });

  it('ends the session after confirming, and leaves when it is deleted', async () => {
    api.getPlanningSession.mockResolvedValue(detail());
    api.stopPlanningSession.mockResolvedValue(detail({ session: session({ status: 'Stopping' }) }));
    const { getPathname } = await renderAt('/planning/pls_1');
    await waitFor(() => expect(screen.getByTestId('planning-session')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('planning-end'));
    expect(api.stopPlanningSession).not.toHaveBeenCalled();
    await fireEvent.press(screen.getByTestId('planning-end-confirm-confirm'));
    await waitFor(() => expect(api.stopPlanningSession).toHaveBeenCalledWith('pls_1'));
    await waitFor(() => expect(screen.getByText('Ending...')).toBeTruthy());

    await emit('planning-session.deleted', { sessionId: 'pls_1' });
    await waitFor(() => expect(getPathname()).toBe('/planning'));
  });

  it('deletes the session after confirming', async () => {
    api.getPlanningSession.mockResolvedValue(detail());
    api.deletePlanningSession.mockResolvedValue(undefined);
    const { getPathname } = await renderAt('/planning/pls_1');
    await waitFor(() => expect(screen.getByTestId('planning-session')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('planning-delete'));
    await fireEvent.press(screen.getByTestId('planning-delete-confirm-confirm'));
    await waitFor(() => expect(api.deletePlanningSession).toHaveBeenCalledWith('pls_1'));
    await waitFor(() => expect(getPathname()).toBe('/planning'));
  });
});
