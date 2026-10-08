import AsyncStorage from '@react-native-async-storage/async-storage';
import { act, fireEvent, screen, waitFor, within } from '@testing-library/react-native';
import { Slot } from 'expo-router';
import { renderRouter } from 'expo-router/testing-library';
import { Alert, Linking, Text } from 'react-native';
import * as client from '@dashboard/api/client';
import type { Objective, ObjectiveRefinementMessage, ObjectiveRefinementSession, ObjectiveRefinementSessionDetail } from '@dashboard/types/models';
import { DEFAULT_BACKLOG_FILTERS } from '@dashboard/lib/backlogUtils';
import { emptyObjectiveForm } from '@dashboard/lib/backlogForm';
import * as auth from '../auth/AuthContext';
import ObjectivesRoute from '../app/(app)/(work)/objectives/index';
import ObjectiveRoute from '../app/(app)/(work)/objectives/[id]';
import BacklogItemRoute from '../app/(app)/(work)/backlog/[id]';
import { parseGitHubNumber } from '../screens/objectives/GitHubImportSheet';
import { withPrimaryVessel } from '../screens/objectives/ObjectiveForm';
import { activeFilterCount } from '../screens/objectives/ObjectivesList';
import { armadaLinkGroups, backlogItemPath, dispatchHref, historyHref, planningHref, releaseHref } from '../screens/objectives/objectiveLinks';
import { dispatchPrefillFromParams } from '../screens/operations/w24/dispatchLink';
import { prefillFromParams } from '../screens/planning/PlanningScreen';
import { BuildProviders, buildSockets, deliver, page } from '../test/buildFixtures';

jest.mock('@dashboard/api/client', () => require('../test/buildClientMock').buildClientMockFactory());

const api = client as jest.Mocked<typeof client>;
const realUseAuth = auth.useAuth;
const NOW = '2026-10-07T12:00:00Z';

function objective(over: Partial<Objective> = {}): Objective {
  return {
    id: 'obj_a', tenantId: 'ten_1', userId: 'usr_1', title: 'Alpha', description: 'Do alpha', status: 'Draft', kind: 'Feature',
    category: null, priority: 'P2', rank: 2, backlogState: 'Inbox', effort: 'M', owner: 'ada', targetVersion: null, dueUtc: null,
    parentObjectiveId: null, blockedByObjectiveIds: [], refinementSummary: null, suggestedPipelineId: null, suggestedPlaybooks: [],
    refinementSessionIds: [], sourceProvider: null, sourceType: null, sourceId: null, sourceUrl: null, sourceUpdatedUtc: null,
    tags: [], acceptanceCriteria: [], nonGoals: [], rolloutConstraints: [], evidenceLinks: [], fleetIds: [], vesselIds: [],
    planningSessionIds: [], voyageIds: [], missionIds: [], checkRunIds: [], releaseIds: [], deploymentIds: [], incidentIds: [],
    createdUtc: NOW, lastUpdateUtc: NOW, completedUtc: null, ...over,
  };
}

const A = objective();
const B = objective({ id: 'obj_b', title: 'Beta', rank: 1, priority: 'P0', backlogState: 'ReadyForDispatch' });
const C = objective({ id: 'obj_c', title: 'Gamma', rank: 3, status: 'Blocked', backlogState: 'ReadyForPlanning' });

function session(over: Partial<ObjectiveRefinementSession> = {}): ObjectiveRefinementSession {
  return {
    id: 'ors_1', objectiveId: 'obj_a', tenantId: null, userId: null, captainId: 'cpt_1', fleetId: null, vesselId: null, title: 'Refine alpha',
    status: 'Active', processId: null, failureReason: null, createdUtc: NOW, startedUtc: NOW, completedUtc: null, lastUpdateUtc: NOW, ...over,
  } as ObjectiveRefinementSession;
}

function message(over: Partial<ObjectiveRefinementMessage> = {}): ObjectiveRefinementMessage {
  return {
    id: 'orm_1', objectiveRefinementSessionId: 'ors_1', objectiveId: 'obj_a', tenantId: null, userId: null, role: 'Assistant',
    sequence: 1, content: 'Scope it down', isSelected: false, createdUtc: NOW, lastUpdateUtc: NOW, ...over,
  };
}

function detail(over: Partial<ObjectiveRefinementSessionDetail> = {}): ObjectiveRefinementSessionDetail {
  return { session: session(), messages: [message()], captain: { id: 'cpt_1', name: 'Ada', state: 'Idle' } as never, vessel: null, objective: null, ...over };
}

const ROUTES = {
  _layout: () => <BuildProviders><Slot /></BuildProviders>,
  '(work)/objectives/index': ObjectivesRoute,
  '(work)/objectives/[id]': ObjectiveRoute,
  '(work)/backlog/[id]': BacklogItemRoute,
  '(work)/planning/index': () => <Text>Planning screen</Text>,
};

async function renderAt(url: string) {
  const result = renderRouter(ROUTES, { initialUrl: url });
  await result;
  await act(async () => { buildSockets()[0]?.open(); });
  return { getPathname: () => result.getPathname() };
}

let admin = true;

beforeEach(async () => {
  await AsyncStorage.clear();
  admin = true;
  jest.spyOn(auth, 'useAuth').mockImplementation(() => ({ ...realUseAuth(), isTenantAdmin: admin, isAdmin: admin }));
  api.listBacklog.mockResolvedValue(page([A, B, C]));
  api.listFleets.mockResolvedValue(page([{ id: 'flt_1', name: 'Core' } as never]));
  api.listVessels.mockResolvedValue(page([{ id: 'vsl_1', name: 'demo-api', fleetId: 'flt_1' } as never]));
  api.listCaptains.mockResolvedValue(page([{ id: 'cpt_1', name: 'Ada', state: 'Idle' } as never]));
  api.listPipelines.mockResolvedValue(page([]));
  api.listUsers.mockResolvedValue(page([]));
  api.getBacklogItem.mockResolvedValue(A);
  api.listBacklogRefinementSessions.mockResolvedValue([]);
});
afterEach(() => jest.restoreAllMocks());

describe('backlog helpers', () => {
  it('builds the canonical route and the prefilled planning, dispatch, release, and history links', () => {
    const item = objective({ suggestedPipelineId: 'ppl_1', suggestedPlaybooks: [{ playbookId: 'pbk_1', deliveryMode: 'InlineFullContent' }] });
    expect(backlogItemPath('obj_a')).toBe('/backlog/obj_a');
    expect(backlogItemPath('obj_a', 'ors 1')).toBe('/backlog/obj_a?refinementSessionId=ors%201');
    const planning = new URLSearchParams(planningHref(item, 'flt_1', 'vsl_1').split('?')[1]);
    expect(planning.get('objectiveId')).toBe('obj_a');
    expect(planning.get('title')).toBe('Alpha Planning');
    expect(planning.get('pipelineId')).toBe('ppl_1');
    // The Planning and Dispatch screens read these links back into the dashboard's prefill state.
    const planningPrefill = prefillFromParams(Object.fromEntries(planning.entries()));
    expect(planningPrefill.source).toBe('objective');
    expect(planningPrefill.prefill?.initialPrompt).toContain('Backlog Item: Alpha');
    const dispatch = new URLSearchParams(dispatchHref(item, 'vsl_1', [{ id: 'ppl_1', name: 'Default' } as never]).split('?')[1]);
    const dispatchPrefill = dispatchPrefillFromParams(Object.fromEntries(dispatch.entries()));
    expect(dispatchPrefill).toMatchObject({ fromObjective: true, objectiveId: 'obj_a', vesselId: 'vsl_1', pipelineName: 'Default', voyageTitle: 'Alpha' });
    expect(dispatchPrefill?.selectedPlaybooks).toEqual([{ playbookId: 'pbk_1', deliveryMode: 'InlineFullContent' }]);
    expect(dispatchPrefill?.prompt).toContain('Implement backlog item: Alpha');
    expect(releaseHref(item, 'vsl_1')).toMatch(/^\/releases\/new\?objectiveIds=obj_a&vesselId=vsl_1&title=Alpha%20Release/);
    expect(historyHref(item)).toBe('/activity?source=history&objectiveId=obj_a');
    expect(armadaLinkGroups(objective({ missionIds: ['msn_1'], refinementSessionIds: ['ors_1'] })).map((g) => [g.key, g.href(g.ids[0])])).toEqual([
      ['refinement', '/backlog/obj_a?refinementSessionId=ors_1'],
      ['missions', '/missions/msn_1'],
    ]);
  });

  it('counts active filters, validates GitHub numbers, and links the primary vessel with its fleet', () => {
    expect(activeFilterCount(DEFAULT_BACKLOG_FILTERS)).toBe(0);
    expect(activeFilterCount({ ...DEFAULT_BACKLOG_FILTERS, kind: 'Bug', owner: 'x', search: 'y', sortBy: 'due' })).toBe(2);
    expect(parseGitHubNumber('12')).toBe(12);
    expect(parseGitHubNumber('0')).toBeNull();
    expect(parseGitHubNumber('')).toBeNull();
    expect(parseGitHubNumber('abc')).toBeNull();
    const form = { ...emptyObjectiveForm(), vesselIds: 'vsl_old\nvsl_2', fleetIds: 'flt_old' };
    expect(withPrimaryVessel(form, 'vsl_1', 'flt_1')).toMatchObject({ vesselIds: 'vsl_1\nvsl_2', fleetIds: 'flt_1' });
    expect(withPrimaryVessel(form, 'vsl_1', null)).toMatchObject({ vesselIds: 'vsl_1\nvsl_2', fleetIds: '' });
    expect(withPrimaryVessel(form, '', null)).toMatchObject({ vesselIds: '', fleetIds: '' });
  });
});

describe('backlog list', () => {
  it('lists items in rank order with group counts, filters by group and search, and reloads live', async () => {
    await renderAt('/objectives');
    await waitFor(() => expect(screen.getByTestId('objective-row-Alpha')).toBeTruthy());
    const titles = () => screen.getAllByTestId(/^objective-row-/).map((el) => el.props.testID as string);
    expect(titles()).toEqual(['objective-row-Beta', 'objective-row-Alpha', 'objective-row-Gamma']);
    expect(screen.getByText('Showing 3 of 3 backlog items.')).toBeTruthy();

    await fireEvent.press(screen.getByTestId('objective-group-blocked'));
    expect(titles()).toEqual(['objective-row-Gamma']);
    await fireEvent.press(screen.getByTestId('objective-group-all'));
    await fireEvent.changeText(screen.getByTestId('objectives-list-search'), 'bet');
    expect(titles()).toEqual(['objective-row-Beta']);
    await fireEvent.changeText(screen.getByTestId('objectives-list-search'), '');

    api.listBacklog.mockResolvedValue(page([A, B, C, objective({ id: 'obj_d', title: 'Delta', rank: 4 })]));
    await act(async () => deliver({ type: 'objective.changed', data: { id: 'obj_d' } }));
    await waitFor(() => expect(screen.getByTestId('objective-row-Delta')).toBeTruthy());
  });

  it('applies the filter sheet (priority) and the admin user scope reloads with userId', async () => {
    api.listUsers.mockResolvedValue(page([{ id: 'usr_2', email: 'bo@armada', firstName: 'Bo', lastName: null } as never]));
    await renderAt('/objectives');
    await waitFor(() => expect(screen.getByTestId('objective-row-Alpha')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('objectives-list-filters'));
    await fireEvent.press(screen.getByTestId('objective-filter-priority'));
    await fireEvent.press(screen.getByTestId('objective-filter-priority-option-P0'));
    await waitFor(() => expect(screen.queryByTestId('objective-row-Alpha')).toBeNull());
    expect(screen.getByTestId('objective-row-Beta')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('objective-filters-clear'));
    await waitFor(() => expect(screen.getByTestId('objective-row-Alpha')).toBeTruthy());

    await fireEvent.press(screen.getByTestId('objective-filter-user'));
    await waitFor(() => expect(screen.getByTestId('objective-filter-user-option-usr_2')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('objective-filter-user-option-usr_2'));
    await waitFor(() => expect(api.listBacklog).toHaveBeenLastCalledWith({ pageSize: 9999, userId: 'usr_2' }));
  });

  it('moves an item up by swapping ranks and deletes after confirmation (swipe actions)', async () => {
    api.reorderBacklog.mockResolvedValue([{ ...A, rank: 1 }, { ...B, rank: 2 }]);
    await renderAt('/objectives');
    await waitFor(() => expect(screen.getByTestId('objective-row-Alpha')).toBeTruthy());
    await act(async () => { fireEvent(screen.getByTestId('objective-swipe-Alpha'), 'accessibilityAction', { nativeEvent: { actionName: 'up' } }); });
    await waitFor(() => expect(api.reorderBacklog).toHaveBeenCalledWith({ items: [{ objectiveId: 'obj_a', rank: 1 }, { objectiveId: 'obj_b', rank: 2 }] }));
    await waitFor(() => expect(screen.getAllByTestId(/^objective-row-/)[0].props.testID).toBe('objective-row-Alpha'));

    api.listBacklog.mockResolvedValue(page([B, C]));
    await act(async () => { fireEvent(screen.getByTestId('objective-swipe-Alpha'), 'accessibilityAction', { nativeEvent: { actionName: 'delete' } }); });
    expect(screen.getByText(/Delete "Alpha"\?/)).toBeTruthy();
    await fireEvent.press(screen.getByTestId('objective-delete-confirm-confirm'));
    await waitFor(() => expect(api.deleteBacklogItem).toHaveBeenCalledWith('obj_a'));
    await waitFor(() => expect(screen.queryByTestId('objective-row-Alpha')).toBeNull());
  });

  it('hides management actions from non-administrators', async () => {
    admin = false;
    await renderAt('/objectives');
    await waitFor(() => expect(screen.getByTestId('objective-row-Alpha')).toBeTruthy());
    expect(screen.queryByTestId('objectives-list-new')).toBeNull();
    expect(screen.queryByTestId('objectives-list-import')).toBeNull();
    expect(screen.getByTestId('objectives-list-filters')).toBeTruthy();
  });

  it('imports from GitHub and opens the imported item', async () => {
    api.importObjectiveFromGitHub.mockResolvedValue(objective({ id: 'obj_gh', title: 'From GitHub' }));
    api.getBacklogItem.mockResolvedValue(objective({ id: 'obj_gh', title: 'From GitHub' }));
    const result = await renderAt('/objectives');
    await waitFor(() => expect(screen.getByTestId('objective-row-Alpha')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('objectives-list-import'));
    await fireEvent.press(screen.getByTestId('objective-import-submit'));
    expect(screen.getByText('Select a vessel and enter a valid GitHub issue or pull-request number.')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('objective-import-vessel'));
    await fireEvent.press(screen.getByTestId('objective-import-vessel-option-vsl_1'));
    await fireEvent.changeText(screen.getByTestId('objective-import-number'), '42');
    await fireEvent.press(screen.getByTestId('objective-import-submit'));
    await waitFor(() => expect(api.importObjectiveFromGitHub).toHaveBeenCalledWith({ vesselId: 'vsl_1', sourceType: 'Issue', number: 42 }));
    await waitFor(() => expect(result.getPathname()).toBe('/backlog/obj_gh'));
  });
});

describe('backlog item', () => {
  it('creates a new item with the prefilled vessel and its fleet, then opens it', async () => {
    api.createBacklogItem.mockResolvedValue(objective({ id: 'obj_new', title: 'New thing' }));
    const result = await renderAt('/backlog/new?vesselId=vsl_1');
    await waitFor(() => expect(screen.getByTestId('objective-form-title')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('objective-form-save'));
    expect(screen.getByText('Backlog item title is required.')).toBeTruthy();
    expect(api.createBacklogItem).not.toHaveBeenCalled();
    await waitFor(() => expect(screen.getByTestId('objective-form-vessel').props.accessibilityValue.text).toBe('demo-api (Core)'));
    await fireEvent.changeText(screen.getByTestId('objective-form-title'), 'New thing');
    await fireEvent.changeText(screen.getByTestId('objective-form-acceptance'), 'one\ntwo');
    await fireEvent.press(screen.getByTestId('objective-form-save'));
    await waitFor(() => expect(api.createBacklogItem).toHaveBeenCalled());
    expect(api.createBacklogItem.mock.calls[0][0]).toMatchObject({ title: 'New thing', vesselIds: ['vsl_1'], fleetIds: ['flt_1'], acceptanceCriteria: ['one', 'two'], status: 'Draft' });
    await waitFor(() => expect(result.getPathname()).toBe('/backlog/obj_new'));
  });

  it('shows an item, saves edits, keeps unsaved edits over live changes, and deletes with confirmation', async () => {
    api.updateBacklogItem.mockImplementation(async (_id, payload) => objective({ title: payload.title ?? '' }));
    await renderAt('/objectives/obj_a');
    await waitFor(() => expect(screen.getByTestId('objective-detail')).toBeTruthy());
    await waitFor(() => expect(screen.getByTestId('objective-form-title').props.value).toBe('Alpha'));
    expect(screen.getByText('This backlog item can be refined now, but it still needs a vessel before repository-aware planning or dispatch can start.')).toBeTruthy();
    expect(screen.getByTestId('objective-start-planning').props.accessibilityState.disabled).toBe(true);

    await fireEvent.changeText(screen.getByTestId('objective-form-title'), 'Alpha 2');
    await act(async () => deliver({ type: 'objective.changed', data: objective({ title: 'Changed elsewhere' }) }));
    expect(screen.getByTestId('objective-form-title').props.value).toBe('Alpha 2');
    await fireEvent.press(screen.getByTestId('objective-form-save'));
    await waitFor(() => expect(api.updateBacklogItem).toHaveBeenCalledWith('obj_a', expect.objectContaining({ title: 'Alpha 2' })));

    await act(async () => deliver({ type: 'objective.changed', data: objective({ title: 'Live title', vesselIds: ['vsl_1'] }) }));
    await waitFor(() => expect(screen.getByTestId('objective-form-title').props.value).toBe('Live title'));
    expect(screen.getByTestId('objective-start-planning').props.accessibilityState.disabled).toBe(false);

    await fireEvent.press(screen.getByTestId('objective-delete'));
    await fireEvent.press(screen.getByTestId('objective-detail-delete-confirm-confirm'));
    await waitFor(() => expect(api.deleteBacklogItem).toHaveBeenCalledWith('obj_a'));
  });

  it('a source link with another scheme is shown but never opened', async () => {
    const open = jest.spyOn(Linking, 'openURL').mockResolvedValue(true);
    const alert = jest.spyOn(Alert, 'alert').mockImplementation(() => undefined);
    try {
      api.getBacklogItem.mockResolvedValue(objective({ sourceProvider: 'GitHub', sourceUrl: 'sms:+15550100?body=pay' }));
      await renderAt('/objectives/obj_a');
      await waitFor(() => expect(screen.getByText('sms:+15550100?body=pay')).toBeTruthy());
      await fireEvent.press(screen.getByText('Source Link'));
      expect(alert).not.toHaveBeenCalled();
      expect(open).not.toHaveBeenCalled();
    } finally {
      open.mockRestore();
      alert.mockRestore();
    }
  });

  it('an http(s) source link opens only after the destination host was shown', async () => {
    const open = jest.spyOn(Linking, 'openURL').mockResolvedValue(true);
    const alert = jest.spyOn(Alert, 'alert').mockImplementation(() => undefined);
    try {
      api.getBacklogItem.mockResolvedValue(objective({ sourceProvider: 'GitHub', sourceUrl: 'https://evil.example/issues/1' }));
      await renderAt('/objectives/obj_a');
      await waitFor(() => expect(screen.getByText('https://evil.example/issues/1')).toBeTruthy());
      await fireEvent.press(screen.getByText('Source Link'));
      expect(alert).toHaveBeenCalledWith('Open evil.example?', expect.stringContaining('https://evil.example/issues/1'), expect.any(Array));
      expect(open).not.toHaveBeenCalled();
    } finally {
      open.mockRestore();
      alert.mockRestore();
    }
  });

  it('starts a refinement session, streams the transcript live, summarizes, and applies', async () => {
    api.createBacklogRefinementSession.mockResolvedValue(detail({ messages: [] }));
    api.listBacklogRefinementSessions.mockResolvedValueOnce([]).mockResolvedValue([session()]);
    api.getObjectiveRefinementSession.mockResolvedValue(detail({ messages: [] }));
    api.applyObjectiveRefinementSummary.mockResolvedValue({
      summary: { sessionId: 'ors_1', messageId: 'orm_1', summary: 'Applied', acceptanceCriteria: [], nonGoals: [], rolloutConstraints: [], suggestedPipelineId: null, method: 'Assistant' },
      objective: objective({ refinementSummary: 'Applied', backlogState: 'ReadyForPlanning' }),
    });
    await renderAt('/backlog/obj_a');
    await waitFor(() => expect(screen.getByTestId('objective-refine-start')).toBeTruthy());
    expect(screen.getByTestId('objective-refine-start').props.accessibilityState.disabled).toBe(true);
    await fireEvent.press(screen.getByTestId('objective-refine-captain'));
    await fireEvent.press(screen.getByTestId('objective-refine-captain-option-cpt_1'));
    await fireEvent.changeText(screen.getByTestId('objective-refine-initial'), 'Focus on scope');
    await fireEvent.press(screen.getByTestId('objective-refine-start'));
    await waitFor(() => expect(api.createBacklogRefinementSession).toHaveBeenCalledWith('obj_a', { captainId: 'cpt_1', fleetId: undefined, vesselId: undefined, title: undefined, initialMessage: 'Focus on scope' }));
    await waitFor(() => expect(screen.getByTestId('objective-refine-transcript')).toBeTruthy());

    await act(async () => deliver({ type: 'objective-refinement-session.message.created', data: { sessionId: 'ors_1', objectiveId: 'obj_a', message: message() } }));
    await waitFor(() => expect(screen.getByText('Scope it down')).toBeTruthy());
    // Another item's events are ignored.
    await act(async () => deliver({ type: 'objective-refinement-session.message.created', data: { sessionId: 'ors_1', objectiveId: 'obj_x', message: message({ id: 'orm_x', sequence: 2, content: 'Not mine' }) } }));
    expect(screen.queryByText('Not mine')).toBeNull();

    await act(async () => deliver({ type: 'objective-refinement-session.summary.created', data: { sessionId: 'ors_1', messageId: 'orm_1', summary: { summary: 'Draft summary', acceptanceCriteria: ['a1'], method: 'Assistant' } } }));
    await waitFor(() => expect(within(screen.getByTestId('objective-refine-summary')).getByText('Draft summary')).toBeTruthy());

    await fireEvent.press(screen.getByTestId('objective-refine-apply'));
    await waitFor(() => expect(api.applyObjectiveRefinementSummary).toHaveBeenCalledWith('ors_1', { messageId: 'orm_1', markMessageSelected: true, promoteBacklogState: true }));
    await waitFor(() => expect(screen.getByTestId('objective-form-refinement-summary').props.value).toBe('Applied'));

    await act(async () => deliver({ type: 'objective-refinement-session.deleted', data: { sessionId: 'ors_1', objectiveId: 'obj_a' } }));
    await waitFor(() => expect(screen.queryByTestId('objective-refine-transcript')).toBeNull());
  });

  it('sends a refinement message and keeps the newer live state', async () => {
    api.listBacklogRefinementSessions.mockResolvedValue([session()]);
    api.getObjectiveRefinementSession.mockResolvedValue(detail());
    api.sendObjectiveRefinementMessage.mockResolvedValue(detail({ messages: [message(), message({ id: 'orm_2', role: 'User', sequence: 2, content: 'Tighten it' })] }));
    await renderAt('/backlog/obj_a?refinementSessionId=ors_1');
    await waitFor(() => expect(screen.getByText('Scope it down')).toBeTruthy());
    await fireEvent.changeText(screen.getByTestId('objective-refine-input'), 'Tighten it');
    await fireEvent.press(screen.getByTestId('objective-refine-send'));
    await waitFor(() => expect(api.sendObjectiveRefinementMessage).toHaveBeenCalledWith('ors_1', { content: 'Tighten it' }));
    await waitFor(() => expect(screen.getByText('Tighten it')).toBeTruthy());
    expect(screen.getByTestId('objective-refine-input').props.value).toBe('');
  });
});
