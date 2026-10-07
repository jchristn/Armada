import AsyncStorage from '@react-native-async-storage/async-storage';
import * as SecureStore from 'expo-secure-store';
import { act, render, waitFor } from '@testing-library/react-native';
import { useEffect, type ReactNode } from 'react';
import * as client from '@dashboard/api/client';
import type { I18nCatalog } from '@dashboard/i18n/catalog';
import type { InboxItem, WhoAmIResult } from '@dashboard/types/models';
import type { ServerSession } from '../api/serverSession';
import { AuthProvider, useAuth, type AuthState } from '../auth/AuthContext';
import { LocaleProvider } from '../i18n/LocaleContext';
import { takePendingLink, setSignedInForLinks } from '../navigation/pendingLink';
import { ApprovalsProvider } from '../notifications/ApprovalsContext';
import { NotificationProvider, useNotifications, type NotificationState } from '../notifications/NotificationContext';
import type { ApprovalActions } from '../push/approvalActions';
import type { PushNative, PushResponse } from '../push/nativeAdapter';
import { ACTION_APPROVE, ACTION_DENY } from '../push/payload';
import { PushProvider, createPushAuthHooks, usePush, type PushDeps, type PushState } from '../push/PushContext';
import { PushResponseHandler } from '../push/PushResponseHandler';
import type { PushApi } from '../push/pushApi';
import type { PermissionState } from '../push/registration';
import { secureRegistrationStore } from '../push/registrationStore';
import { PUSH_CATEGORIES, type PushDevice } from '../push/types';
import { SocketProvider } from '../socket/SocketContext';
import { ThemeProvider } from '../theme/ThemeContext';

jest.mock('@dashboard/api/client', () => require('../test/mockClient').clientMockFactory());

const mockRouter = { push: jest.fn() };
jest.mock('expo-router', () => ({ useRouter: () => mockRouter }));

const api = client as jest.Mocked<typeof client> & { __fireUnauthorized: () => void };
const secureStore = (SecureStore as unknown as { __store: Map<string, string> }).__store;

const EMPTY_CATALOG: I18nCatalog = { defaultLocale: 'en', supportedLocales: [], locales: {} };
const ME = { tenant: { id: 'ten_1', name: 'Default' }, user: { id: 'usr_1', email: 'admin@armada', isAdmin: true, isTenantAdmin: true }, passwordChangeRequired: false } as unknown as WhoAmIResult;
const DRAFT = { name: 'Local', url: 'http://10.0.2.2:44010', kind: 'Direct' as const, biometricUnlock: false };
const SESSION_A = { baseUrl: 'http://10.0.2.2:44010', token: 'A1', headers: null };

interface FakeNative extends PushNative {
  permissionState: PermissionState;
  respond: (response: PushResponse) => void;
  rotateToken: (token: string) => void;
  setLast: (response: PushResponse | null) => void;
  setBadge: jest.Mock;
}

function fakeNative(): FakeNative {
  let responseListener: ((r: PushResponse) => void) | null = null;
  let tokenListener: (() => void) | null = null;
  let last: PushResponse | null = null;
  let token = 'ExponentPushToken[one]';
  const native: FakeNative = {
    permissionState: 'granted',
    environment: {
      permission: async () => native.permissionState,
      expoToken: async () => ({ token, reason: null }),
      platform: 'Ios',
      deviceName: 'Test iPhone',
      appVersion: '1.0.0',
      locale: () => 'en-US',
      nowUtc: () => '2026-10-07T12:00:00Z',
    },
    configurePresentation: jest.fn(),
    registerCategory: jest.fn(async () => undefined),
    ensureChannel: jest.fn(async () => undefined),
    requestPermission: jest.fn(async () => { native.permissionState = 'granted'; return 'granted' as PermissionState; }),
    setBadge: jest.fn(async () => undefined),
    addTokenListener: (listener) => { tokenListener = listener; return { remove: () => { tokenListener = null; } }; },
    addResponseListener: (listener) => { responseListener = listener; return { remove: () => { responseListener = null; } }; },
    getLastResponse: () => last,
    clearLastResponse: jest.fn(() => { last = null; }),
    respond: (response) => responseListener?.(response),
    rotateToken: (next) => { token = next; tokenListener?.(); },
    setLast: (response) => { last = response; },
  };
  return native;
}

let deviceSeq = 0;
function fakeApi(): jest.Mocked<PushApi> {
  return {
    register: jest.fn(async (_s, body) => {
      deviceSeq += 1;
      return { id: `pdv_${deviceSeq}`, platform: 'Ios', expoPushToken: `masked-${body.expoPushToken.slice(-5)}`, categories: [...PUSH_CATEGORIES], active: true } as PushDevice;
    }),
    updateCategories: jest.fn(async (_s, id, categories) => ({ id, platform: 'Ios', expoPushToken: 'm', categories, active: true } as PushDevice)),
    remove: jest.fn(async (_s: ServerSession, _id: string): Promise<void> => undefined),
    sendTest: jest.fn(async (_s, id) => ({ deviceId: id, status: 'Sent' as const })),
  };
}

function fakeActions(): jest.Mocked<ApprovalActions> {
  return {
    approveAskProposal: jest.fn(async (_threadId: string, _proposalId: string): Promise<unknown> => ({})),
    rejectAskProposal: jest.fn(async (_threadId: string, _proposalId: string): Promise<unknown> => ({})),
    allowCliPermissionOnce: jest.fn(async (_requestId: string): Promise<unknown> => ({})),
    denyCliPermission: jest.fn(async (_requestId: string): Promise<unknown> => ({})),
  };
}

let native: FakeNative;
let pushApi: jest.Mocked<PushApi>;
let actions: jest.Mocked<ApprovalActions>;
let verify: jest.Mock;
let deps: PushDeps;

const probe: { auth: AuthState | null; push: PushState | null; notes: NotificationState | null } = { auth: null, push: null, notes: null };

function Probe() {
  const auth = useAuth();
  const push = usePush();
  const notes = useNotifications();
  useEffect(() => {
    probe.auth = auth;
    probe.push = push;
    probe.notes = notes;
  });
  return null;
}

function Session({ children }: { children: ReactNode }) {
  const { status } = useAuth();
  return <ApprovalsProvider enabled={status === 'signedIn'}>{children}</ApprovalsProvider>;
}

function Ready() {
  const { status, mustChangePassword } = useAuth();
  const ready = status === 'signedIn' && !mustChangePassword;
  setSignedInForLinks(ready);
  return ready ? <PushResponseHandler actions={actions} verify={verify} /> : null;
}

function App() {
  return (
    <ThemeProvider>
      <AuthProvider hooks={createPushAuthHooks(() => deps)}>
        <LocaleProvider serverUrl={null} bundledCatalog={() => EMPTY_CATALOG}>
          <SocketProvider serverUrl={null} token={null}>
            <NotificationProvider schedule={() => 0}>
              <Session>
                <PushProvider deps={deps}>
                  <Probe />
                  <Ready />
                </PushProvider>
              </Session>
            </NotificationProvider>
          </SocketProvider>
        </LocaleProvider>
      </AuthProvider>
    </ThemeProvider>
  );
}

async function mountApp() {
  const view = await render(<App />);
  await waitFor(() => expect(probe.auth?.status).not.toBe('loading'));
  return view;
}

async function signIn(draft = DRAFT, token = 'A1') {
  await act(async () => { await probe.auth!.saveProfile(draft); });
  await act(async () => { await probe.auth!.login(token, { method: 'token' }); });
  await waitFor(() => expect(probe.auth!.status).toBe('signedIn'));
}

async function registeredDeviceId(): Promise<string> {
  await waitFor(() => expect(probe.push!.registration?.state).toBe('registered'));
  const reg = probe.push!.registration;
  if (reg?.state !== 'registered') throw new Error('not registered');
  return reg.record.deviceId;
}

const askData = (deviceId: string | undefined) => ({
  url: '/ask/ath_t1', kind: 'ask_proposal', entityId: 'aap_p1', category: 'AskProposal', threadId: 'ath_t1', ...(deviceId ? { deviceId } : {}),
});

beforeEach(async () => {
  await AsyncStorage.clear();
  secureStore.clear();
  jest.clearAllMocks();
  takePendingLink();
  native = fakeNative();
  pushApi = fakeApi();
  actions = fakeActions();
  verify = jest.fn(async () => true);
  deps = { env: native.environment, api: pushApi, store: secureRegistrationStore, native };
  api.whoami.mockResolvedValue(ME);
  api.getInbox.mockResolvedValue([]);
});

describe('push registration in the app', () => {
  it('registers with the server on sign-in when notifications are allowed', async () => {
    await mountApp();
    await signIn();
    await registeredDeviceId();
    expect(pushApi.register).toHaveBeenCalledWith(SESSION_A, expect.objectContaining({ platform: 'Ios', expoPushToken: 'ExponentPushToken[one]', categories: null }));
    expect(native.registerCategory).toHaveBeenCalledWith('Approve', 'Deny');
    expect(native.configurePresentation).toHaveBeenCalled();
  });

  it('does not ask at launch; the explanation sheet shows once signed in, and enabling registers', async () => {
    native.permissionState = 'undetermined';
    await mountApp();
    expect(native.requestPermission).not.toHaveBeenCalled();
    expect(probe.push!.promptVisible).toBe(false);
    await signIn();
    await waitFor(() => expect(probe.push!.promptVisible).toBe(true));
    expect(pushApi.register).not.toHaveBeenCalled();
    await act(async () => { probe.push!.dismissPrompt(); await probe.push!.requestPermission(); });
    expect(probe.push!.promptVisible).toBe(false);
    await registeredDeviceId();
  });

  it('"Not now" is remembered', async () => {
    native.permissionState = 'undetermined';
    const first = await mountApp();
    await signIn();
    await waitFor(() => expect(probe.push!.promptVisible).toBe(true));
    await act(async () => { probe.push!.dismissPrompt(); });
    await first.unmount();
    await mountApp();
    await waitFor(() => expect(probe.auth!.status).toBe('signedIn'));
    await act(async () => { await Promise.resolve(); });
    expect(probe.push!.promptVisible).toBe(false);
    expect(native.requestPermission).not.toHaveBeenCalled();
  });

  it('sign-out deletes the device on the server before the token goes', async () => {
    await mountApp();
    await signIn();
    const deviceId = await registeredDeviceId();
    await act(async () => { await probe.auth!.logout(); });
    expect(pushApi.remove).toHaveBeenCalledWith(SESSION_A, deviceId);
    expect(probe.auth!.status).toBe('signedOut');
    expect([...secureStore.keys()].filter((k) => k.startsWith('armada.push.'))).toEqual([]);
  });

  it('deleting a background profile removes its device using that profile\'s stored session', async () => {
    await mountApp();
    await signIn();
    const firstId = probe.auth!.activeProfile!.id;
    const firstDevice = await registeredDeviceId();
    await signIn({ ...DRAFT, name: 'Other', url: 'https://other.example' }, 'B1');
    await waitFor(() => expect(pushApi.register).toHaveBeenCalledTimes(2));
    await act(async () => { await probe.auth!.deleteProfile(firstId); });
    expect(pushApi.remove).toHaveBeenCalledWith(SESSION_A, firstDevice);
  });

  it('a new push token re-registers and removes the stale device', async () => {
    await mountApp();
    await signIn();
    const oldDevice = await registeredDeviceId();
    await act(async () => { native.rotateToken('ExponentPushToken[two]'); });
    await waitFor(() => expect(pushApi.register).toHaveBeenLastCalledWith(SESSION_A, expect.objectContaining({ expoPushToken: 'ExponentPushToken[two]' })));
    await waitFor(() => expect(pushApi.remove).toHaveBeenCalledWith(SESSION_A, oldDevice));
  });

  it('category toggles update the device on the server', async () => {
    await mountApp();
    await signIn();
    const deviceId = await registeredDeviceId();
    await act(async () => { await probe.push!.setCategoryEnabled('VoyageFinished', false); });
    expect(pushApi.updateCategories).toHaveBeenCalledWith(SESSION_A, deviceId, PUSH_CATEGORIES.filter((c) => c !== 'VoyageFinished'));
    const reg = probe.push!.registration;
    expect(reg?.state === 'registered' && reg.record.categories.includes('VoyageFinished')).toBe(false);
  });

  it('the app badge is the Approvals count while signed in, and cleared when signed out', async () => {
    api.getInbox.mockResolvedValue([
      { kind: 'ask_proposal', id: 'a' }, { kind: 'cli_permission', id: 'b' }, { kind: 'review', id: 'c' },
    ] as unknown as InboxItem[]);
    await mountApp();
    await signIn();
    await waitFor(() => expect(native.setBadge.mock.calls.some((c) => (c[0] as number) > 0)).toBe(true));
    await act(async () => { await probe.auth!.logout(); });
    await waitFor(() => expect(native.setBadge).toHaveBeenLastCalledWith(0));
  });
});

describe('notification taps and actions', () => {
  it('a tap opens the validated link', async () => {
    await mountApp();
    await signIn();
    await registeredDeviceId();
    await act(async () => { native.respond({ data: { url: '/missions/msn_1', kind: 'failed', entityId: 'msn_1', category: 'MissionFailed' }, actionIdentifier: null }); });
    await waitFor(() => expect(mockRouter.push).toHaveBeenCalledWith('/missions/msn_1'));
    expect(native.clearLastResponse).toHaveBeenCalled();
  });

  it('a tap with an unsafe link opens nothing', async () => {
    await mountApp();
    await signIn();
    await act(async () => { native.respond({ data: { url: 'javascript:alert(1)', kind: 'failed', entityId: 'msn_1', category: 'MissionFailed' }, actionIdentifier: null }); });
    await act(async () => { await Promise.resolve(); });
    expect(mockRouter.push).not.toHaveBeenCalled();
  });

  it('the notification that launched the app is opened once the stored session is restored', async () => {
    const first = await mountApp();
    await signIn();
    await first.unmount();
    native.setLast({ data: { url: '/voyages/vyg_1', kind: 'voyage_finished', entityId: 'vyg_1', category: 'VoyageFinished' }, actionIdentifier: null });
    await mountApp();
    await waitFor(() => expect(mockRouter.push).toHaveBeenCalledWith('/voyages/vyg_1'));
    expect(mockRouter.push).toHaveBeenCalledTimes(1);
    expect(native.getLastResponse()).toBeNull();
  });

  it('a launch notification while signed out waits for sign-in as a pending link', async () => {
    native.setLast({ data: { url: '/voyages/vyg_1', kind: 'voyage_finished', entityId: 'vyg_1', category: 'VoyageFinished' }, actionIdentifier: null });
    await mountApp();
    await waitFor(() => expect(probe.push!.pending).toBeNull());
    expect(takePendingLink()).toBe('/voyages/vyg_1');
    expect(mockRouter.push).not.toHaveBeenCalled();
  });

  it('Approve on an Ask proposal for this device approves, toasts, and opens the thread', async () => {
    await mountApp();
    await signIn();
    const deviceId = await registeredDeviceId();
    await act(async () => { native.respond({ data: askData(deviceId), actionIdentifier: ACTION_APPROVE }); });
    await waitFor(() => expect(actions.approveAskProposal).toHaveBeenCalledWith('ath_t1', 'aap_p1'));
    expect(verify).not.toHaveBeenCalled();
    await waitFor(() => expect(mockRouter.push).toHaveBeenCalledWith('/ask/ath_t1'));
    expect(probe.notes!.toasts.map((x) => x.message)).toContain('Approved.');
  });

  it('a biometric profile verifies before acting, and a failed check sends nothing', async () => {
    await mountApp();
    await signIn({ ...DRAFT, biometricUnlock: true });
    const deviceId = await registeredDeviceId();
    verify.mockResolvedValueOnce(false);
    const permissionData = { url: '/cli-permissions?request=cpr_r1', kind: 'cli_permission', entityId: 'cpr_r1', category: 'CliPermission', deviceId };
    await act(async () => { native.respond({ data: permissionData, actionIdentifier: ACTION_DENY }); });
    await waitFor(() => expect(verify).toHaveBeenCalledWith('Confirm to deny', 'Cancel'));
    await waitFor(() => expect(probe.notes!.toasts.map((x) => x.message)).toContain('Not verified. Nothing was approved or denied.'));
    expect(actions.denyCliPermission).not.toHaveBeenCalled();

    await act(async () => { native.respond({ data: permissionData, actionIdentifier: ACTION_DENY }); });
    await waitFor(() => expect(actions.denyCliPermission).toHaveBeenCalledWith('cpr_r1'));
  });

  it('a push naming a device this app never registered is opened but never acted on', async () => {
    await mountApp();
    await signIn();
    await registeredDeviceId();
    await act(async () => { native.respond({ data: askData('pdv_someoneelse'), actionIdentifier: ACTION_APPROVE }); });
    await waitFor(() => expect(mockRouter.push).toHaveBeenCalledWith('/ask/ath_t1'));
    expect(actions.approveAskProposal).not.toHaveBeenCalled();
  });

  it('a push from another profile switches to that profile first, then acts', async () => {
    await mountApp();
    await signIn();
    const firstId = probe.auth!.activeProfile!.id;
    const firstDevice = await registeredDeviceId();
    await signIn({ ...DRAFT, name: 'Other', url: 'https://other.example' }, 'B1');
    await waitFor(() => expect(pushApi.register).toHaveBeenCalledTimes(2));
    expect(probe.auth!.activeProfile!.id).not.toBe(firstId);
    await act(async () => { native.respond({ data: askData(firstDevice), actionIdentifier: ACTION_APPROVE }); });
    await waitFor(() => expect(probe.auth!.activeProfile!.id).toBe(firstId));
    await waitFor(() => expect(actions.approveAskProposal).toHaveBeenCalledWith('ath_t1', 'aap_p1'));
    expect(probe.auth!.sessionToken).toBe('A1');
  });

  it('while signed out a tap keeps only the link for after sign-in; an action is dropped', async () => {
    await mountApp();
    await signIn();
    const deviceId = await registeredDeviceId();
    await act(async () => { await probe.auth!.logout(); });
    await act(async () => { native.respond({ data: askData(deviceId), actionIdentifier: ACTION_APPROVE }); });
    await act(async () => { await Promise.resolve(); await Promise.resolve(); });
    await waitFor(() => expect(probe.push!.pending).toBeNull());
    expect(takePendingLink()).toBe('/ask/ath_t1');
    expect(actions.approveAskProposal).not.toHaveBeenCalled();
  });
});
