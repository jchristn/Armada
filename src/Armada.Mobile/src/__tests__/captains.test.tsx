import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react-native';
import * as client from '@dashboard/api/client';
import type { Captain, CaptainToolAccessResult, Dock } from '@dashboard/types/models';
import { CaptainDetailScreen } from '../screens/captains/CaptainDetailScreen';
import { CaptainsTab, filterCaptains } from '../screens/captains/CaptainsTab';
import { chatHistory, metricsLine } from '../screens/captains/CaptainChat';
import { DocksTab, filterDocks } from '../screens/captains/DocksTab';
import { BuildProviders, buildSockets, deliver, page } from '../test/buildFixtures';

jest.mock('@dashboard/api/client', () => require('../test/buildClientMock').buildClientMockFactory());

// The swipe container has no gesture runtime under Jest; render its rows directly (SwipeRow keeps the actions as
// accessibility actions, which the tests use).
jest.mock('react-native-gesture-handler/ReanimatedSwipeable', () => {
  const { forwardRef } = jest.requireActual('react');
  return { __esModule: true, default: forwardRef(({ children }: { children: unknown }, _ref: unknown) => children) };
});

const mockAuth = { admin: true };
jest.mock('../auth/AuthContext', () => {
  const actual = jest.requireActual('../auth/AuthContext');
  return {
    ...actual,
    useAuth: () => ({ ...actual.useAuth(), isAdmin: mockAuth.admin, isTenantAdmin: mockAuth.admin }),
  };
});

const mockRouter = { push: jest.fn(), replace: jest.fn(), back: jest.fn(), setParams: jest.fn() };
jest.mock('expo-router', () => ({
  ...jest.requireActual('expo-router'),
  useRouter: () => mockRouter,
  useLocalSearchParams: () => ({}),
  Stack: { Screen: () => null },
}));

const api = client as jest.Mocked<typeof client>;
const NOW = '2026-10-07T12:00:00Z';

function captain(over: Partial<Captain> = {}): Captain {
  return {
    id: 'cpt_1', tenantId: 'ten_1', name: 'Ada', runtime: 'ClaudeCode', supportsPlanningSessions: true, planningSessionSupportReason: null,
    systemInstructions: null, model: null, allowedPersonas: null, preferredPersona: null, state: 'Idle', currentMissionId: null,
    currentDockId: null, processId: null, recoveryAttempts: 0, lastHeartbeatUtc: NOW, createdUtc: NOW, lastUpdateUtc: NOW,
    ...over,
  };
}

function dock(over: Partial<Dock> = {}): Dock {
  return { id: 'dck_1', tenantId: null, vesselId: 'vsl_1', captainId: 'cpt_1', worktreePath: '/tmp/w1', branchName: 'armada/fix', active: true, createdUtc: NOW, lastUpdateUtc: NOW, ...over };
}

const ADA = captain();
const BOB = captain({ id: 'cpt_2', name: 'Bob', runtime: 'Codex', state: 'Working', currentMissionId: 'msn_123456789', tier: 'Premium' });

beforeEach(() => {
  jest.clearAllMocks();
  mockAuth.admin = true;
  api.listCaptains.mockResolvedValue(page([ADA, BOB]));
  api.listModelEndpoints.mockResolvedValue([]);
  api.listUsers.mockResolvedValue(page([]));
  api.listMissionSummaries.mockResolvedValue(page([]));
});

describe('captain list logic', () => {
  it('filters and sorts like the dashboard table', () => {
    const base = { search: '', runtime: '', state: '', sort: 'name' as const, dir: 'asc' as const };
    expect(filterCaptains([BOB, ADA], base).map((c) => c.name)).toEqual(['Ada', 'Bob']);
    expect(filterCaptains([BOB, ADA], { ...base, dir: 'desc' }).map((c) => c.name)).toEqual(['Bob', 'Ada']);
    expect(filterCaptains([BOB, ADA], { ...base, search: 'bo' }).map((c) => c.name)).toEqual(['Bob']);
    expect(filterCaptains([BOB, ADA], { ...base, runtime: 'codex' }).map((c) => c.name)).toEqual(['Bob']);
    expect(filterCaptains([BOB, ADA], { ...base, state: 'Idle' }).map((c) => c.name)).toEqual(['Ada']);
  });

  it('filters docks by branch, path, or id and by active state', () => {
    const docks = [dock(), dock({ id: 'dck_2', branchName: 'main', worktreePath: '/srv/x', active: false })];
    expect(filterDocks(docks, 'fix', '').map((d) => d.id)).toEqual(['dck_1']);
    expect(filterDocks(docks, '/srv', '').map((d) => d.id)).toEqual(['dck_2']);
    expect(filterDocks(docks, '', 'inactive').map((d) => d.id)).toEqual(['dck_2']);
    expect(filterDocks(docks, '', 'active').map((d) => d.id)).toEqual(['dck_1']);
  });

  it('builds chat history from user and assistant turns and formats metrics', () => {
    expect(chatHistory([{ role: 'user', text: 'hi' }, { role: 'system', text: 'Stopped.' }, { role: 'assistant', text: 'hello' }]))
      .toEqual([{ role: 'user', content: 'hi' }, { role: 'assistant', content: 'hello' }]);
    expect(metricsLine({ timeToFirstTokenMs: null, streamingMs: null, totalMs: 1234, promptTokens: null, completionTokens: null, totalTokens: 50, tokensPerSecond: 40.4 }))
      .toBe('1.2 s \u00b7 50 tokens \u00b7 40 tok/s');
    expect(metricsLine(null)).toBe('');
  });
});

describe('Captains tab', () => {
  it('lists captains, searches, and opens a captain', async () => {
    await render(<BuildProviders><CaptainsTab /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('captain-row-Ada')).toBeTruthy());
    expect(screen.getByTestId('captain-row-Bob')).toBeTruthy();
    expect(screen.getByText('Premium')).toBeTruthy();
    await act(async () => { fireEvent.changeText(screen.getByTestId('captains-list-search'), 'ad'); });
    expect(screen.queryByTestId('captain-row-Bob')).toBeNull();
    await fireEvent.press(screen.getByTestId('captain-row-Ada'));
    expect(mockRouter.push).toHaveBeenCalledWith('/captains/cpt_1');
  });

  it('reloads on captain events', async () => {
    await render(<BuildProviders><CaptainsTab /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('captain-row-Ada')).toBeTruthy());
    api.listCaptains.mockResolvedValue(page([ADA, BOB, captain({ id: 'cpt_3', name: 'Cy' })]));
    await act(async () => { buildSockets()[0]?.open(); });
    await act(async () => deliver({ type: 'captain.changed', data: { id: 'cpt_3' } }));
    await waitFor(() => expect(screen.getByTestId('captain-row-Cy')).toBeTruthy());
  });

  it('creates a captain through the form, validating Mux endpoints', async () => {
    api.createCaptain.mockResolvedValue(captain({ id: 'cpt_9', name: 'Neo' }));
    await render(<BuildProviders><CaptainsTab /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('captain-row-Ada')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('captains-list-new'));
    await act(async () => { fireEvent.changeText(screen.getByTestId('captain-form-name'), 'Neo'); });
    await fireEvent.press(screen.getByTestId('captain-form-runtime'));
    await fireEvent.press(screen.getByTestId('captain-form-runtime-option-Mux'));
    api.listMuxEndpoints.mockResolvedValue({ contractVersion: 1, success: true, configDirectory: '', errorCode: '', errorMessage: '', endpoints: [] });
    await fireEvent.press(screen.getByTestId('captain-form-save'));
    expect(await screen.findByText('Mux captains require a named Mux endpoint.')).toBeTruthy();
    expect(api.createCaptain).not.toHaveBeenCalled();
    await fireEvent.press(screen.getByTestId('captain-form-runtime'));
    await fireEvent.press(screen.getByTestId('captain-form-runtime-option-Codex'));
    await act(async () => { fireEvent.changeText(screen.getByTestId('captain-form-model'), ' gpt-5 '); });
    await fireEvent.press(screen.getByTestId('captain-form-save'));
    await waitFor(() => expect(api.createCaptain).toHaveBeenCalledWith(expect.objectContaining({ name: 'Neo', runtime: 'Codex', model: 'gpt-5', runtimeOptionsJson: null })));
    expect(api.createCaptain.mock.calls[0][0]).not.toHaveProperty('cliPermissionPolicy');
  });

  it('stops all captains after the confirmation', async () => {
    await render(<BuildProviders><CaptainsTab /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('captain-row-Ada')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('captains-list-stop-all'));
    expect(screen.getByText('Stop ALL captains? All captain processes will be terminated. This cannot be undone.')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('captain-confirm-confirm'));
    await waitFor(() => expect(api.stopAllCaptains).toHaveBeenCalled());
  });

  it('deletes and restarts from the long-press menu with confirmations', async () => {
    await render(<BuildProviders><CaptainsTab /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('captain-row-Bob')).toBeTruthy());
    await act(async () => { fireEvent(screen.getByTestId('captain-row-Bob'), 'longPress'); });
    const menu = screen.getByTestId('captain-menu');
    await fireEvent.press(within(menu).getByText('Restart'));
    expect(screen.getByText('Restart captain "Bob"? The captain will be deleted and recreated with the same saved configuration.')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('captain-confirm-confirm'));
    await waitFor(() => expect(api.restartCaptain).toHaveBeenCalledWith('cpt_2'));

    await act(async () => { fireEvent(screen.getByTestId('captain-row-Ada'), 'longPress'); });
    await fireEvent.press(within(screen.getByTestId('captain-menu')).getByText('Delete'));
    expect(screen.getByText('Delete captain "Ada"? This cannot be undone.')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('captain-confirm-confirm'));
    await waitFor(() => expect(api.deleteCaptain).toHaveBeenCalledWith('cpt_1'));
  });
});

describe('Captain detail', () => {
  it('shows the captain, current mission and dock, and changes the CLI policy in place', async () => {
    api.getCaptain.mockResolvedValue(captain({ state: 'Working', currentMissionId: 'msn_1', currentDockId: 'dck_1' }));
    api.getMission.mockResolvedValue({ id: 'msn_1', title: 'Fix login', status: 'InProgress', priority: 1, branchName: 'armada/fix' } as never);
    api.setCaptainCliPermissionPolicy.mockResolvedValue(captain({ cliPermissionPolicy: 'Refuse' }));
    await render(<BuildProviders><CaptainDetailScreen id="cpt_1" /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('captain-detail')).toBeTruthy());
    expect(screen.getByText('Fix login')).toBeTruthy();
    expect(screen.getByTestId('captain-recall')).toBeTruthy();
    expect(screen.getByTestId('captain-stop')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('captain-current-dock'));
    expect(mockRouter.push).toHaveBeenCalledWith('/docks/dck_1');
    await fireEvent.press(screen.getByTestId('captain-cli-policy'));
    await fireEvent.press(screen.getByTestId('captain-cli-policy-option-Refuse'));
    await waitFor(() => expect(api.setCaptainCliPermissionPolicy).toHaveBeenCalledWith('cpt_1', 'Refuse'));
  });

  it('keeps the CLI policy read-only for non-admins', async () => {
    mockAuth.admin = false;
    api.getCaptain.mockResolvedValue(captain());
    await render(<BuildProviders><CaptainDetailScreen id="cpt_1" /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('captain-cli-policy')).toBeTruthy());
    expect(screen.getByTestId('captain-cli-policy').props.accessibilityState).toMatchObject({ disabled: true });
    expect(screen.queryByTestId('captain-stop')).toBeNull();
  });

  it('chats with the captain using the conversation as history, and stops a reply', async () => {
    api.getCaptain.mockResolvedValue(captain());
    api.chatWithCaptain.mockResolvedValueOnce({ success: true, reply: 'All **good**', model: 'opus', metrics: { timeToFirstTokenMs: null, streamingMs: null, totalMs: 1000, promptTokens: null, completionTokens: null, totalTokens: 10, tokensPerSecond: null }, error: null });
    await render(<BuildProviders><CaptainDetailScreen id="cpt_1" /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('captain-chat-input')).toBeTruthy());
    await act(async () => { fireEvent.changeText(screen.getByTestId('captain-chat-input'), 'status?'); });
    await fireEvent.press(screen.getByTestId('captain-chat-send'));
    await waitFor(() => expect(screen.getByTestId('captain-chat-turn-assistant')).toBeTruthy());
    expect(api.chatWithCaptain).toHaveBeenCalledWith('cpt_1', { message: 'status?', history: [], showThinking: true }, expect.objectContaining({ signal: expect.anything() }));

    let reject: (e: Error) => void = () => undefined;
    api.chatWithCaptain.mockImplementationOnce((_id, _body, opts) => new Promise((_resolve, rej) => {
      reject = rej;
      opts?.signal?.addEventListener('abort', () => rej(new Error('aborted')));
    }));
    await act(async () => { fireEvent.changeText(screen.getByTestId('captain-chat-input'), 'again'); });
    await fireEvent.press(screen.getByTestId('captain-chat-send'));
    expect(api.chatWithCaptain.mock.calls[1][1].history).toEqual([{ role: 'user', content: 'status?' }, { role: 'assistant', content: 'All **good**' }]);
    await fireEvent.press(screen.getByTestId('captain-chat-stop'));
    await waitFor(() => expect(screen.getByText('Stopped.')).toBeTruthy());
    void reject;
  });

  it('loads tool access on request', async () => {
    api.getCaptain.mockResolvedValue(captain());
    const tools: CaptainToolAccessResult = {
      captainId: 'cpt_1', captainName: 'Ada', runtime: 'ClaudeCode', toolsAccessible: true, availabilityVerified: true, availabilitySource: 'probe',
      summary: 'Armada tools reachable', endpointName: null, toolsEnabled: true, effectiveToolCount: 2, armadaToolCount: 2, configuredServerCount: 1, reachableServerCount: 1,
      servers: [{ name: 'armada', sourceKind: 'McpServer', transport: 'http', target: 'x', url: 'http://h/mcp', command: null, workingDirectory: null, enabled: true, reachable: true, toolCount: 2, headerCount: 0, environmentVariableCount: 0, enabledToolFilterCount: 0, disabledToolFilterCount: 0, startupTimeoutSeconds: 0, toolTimeoutSeconds: 0, status: 'Reachable', errorMessage: null }],
      tools: [{ name: 'armada_dispatch', description: 'Dispatch', inputSchemaJson: null, registrationSource: 'armada', sourceKind: 'McpServer' }],
    };
    api.getCaptainTools.mockResolvedValue(tools);
    await render(<BuildProviders><CaptainDetailScreen id="cpt_1" /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('captain-tools')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('captain-tools-load'));
    await waitFor(() => expect(screen.getByTestId('captain-tools-summary')).toHaveTextContent('Armada tools reachable'));
    expect(screen.getByText('armada_dispatch')).toBeTruthy();
    expect(screen.getByText('Accessible')).toBeTruthy();
  });

  it('removes the captain after the confirmation and returns to the list', async () => {
    api.getCaptain.mockResolvedValue(captain());
    await render(<BuildProviders><CaptainDetailScreen id="cpt_1" /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('captain-remove')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('captain-remove'));
    expect(screen.getByText('Remove captain "Ada"? This cannot be undone.')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('captain-confirm-confirm'));
    await waitFor(() => expect(api.deleteCaptain).toHaveBeenCalledWith('cpt_1'));
    expect(mockRouter.replace).toHaveBeenCalledWith('/captains');
  });

  it('saves an edit and the changed CLI policy through its own endpoint', async () => {
    api.getCaptain.mockResolvedValue(captain({ allowedPersonas: '["Worker"]' }));
    api.updateCaptain.mockResolvedValue(captain());
    await render(<BuildProviders><CaptainDetailScreen id="cpt_1" /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('captain-edit')).toBeTruthy());
    await fireEvent.press(screen.getByTestId('captain-edit'));
    await act(async () => { fireEvent.changeText(screen.getByTestId('captain-form-name'), 'Ada 2'); });
    await fireEvent.press(screen.getByTestId('captain-form-cliPermissionPolicy'));
    await fireEvent.press(screen.getByTestId('captain-form-cliPermissionPolicy-option-ApproveInArmada'));
    await fireEvent.press(screen.getByTestId('captain-form-save'));
    await waitFor(() => expect(api.updateCaptain).toHaveBeenCalledWith('cpt_1', expect.objectContaining({ name: 'Ada 2', allowedPersonas: '["Worker"]' })));
    expect(api.updateCaptain.mock.calls[0][1]).not.toHaveProperty('cliPermissionPolicy');
    await waitFor(() => expect(api.setCaptainCliPermissionPolicy).toHaveBeenCalledWith('cpt_1', 'ApproveInArmada'));
  });
});

describe('Docks tab', () => {
  it('lists docks with names, opens a dock, and deletes with the confirmation', async () => {
    api.listDocks.mockResolvedValue(page([dock(), dock({ id: 'dck_2', branchName: null, active: false })]));
    api.listVessels.mockResolvedValue(page([{ id: 'vsl_1', name: 'api' } as never]));
    await render(<BuildProviders><DocksTab /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('dock-row-dck_1')).toBeTruthy());
    await waitFor(() => expect(screen.getByTestId('dock-row-dck_1')).toHaveTextContent(/Vessel: api/));
    expect(screen.getByTestId('dock-row-dck_1')).toHaveTextContent(/Captain: Ada/);
    await fireEvent.press(screen.getByTestId('dock-row-dck_2'));
    expect(mockRouter.push).toHaveBeenCalledWith('/docks/dck_2');
    await act(async () => { fireEvent(screen.getByTestId('dock-swipe-dck_1'), 'accessibilityAction', { nativeEvent: { actionName: 'delete' } }); });
    expect(screen.getByText('Delete dock dck_1? This will clean up the git worktree and cannot be undone.')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('dock-confirm-confirm'));
    await waitFor(() => expect(api.deleteDock).toHaveBeenCalledWith('dck_1'));
  });
});
