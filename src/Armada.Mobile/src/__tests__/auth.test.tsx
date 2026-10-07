import AsyncStorage from '@react-native-async-storage/async-storage';
import * as SecureStore from 'expo-secure-store';
import * as LocalAuthentication from 'expo-local-authentication';
import { act, renderHook, waitFor } from '@testing-library/react-native';
import type { ReactNode } from 'react';
import { AppState } from 'react-native';
import * as client from '@dashboard/api/client';
import type { WhoAmIResult } from '@dashboard/types/models';
import { AuthProvider, LOCK_AFTER_BACKGROUND_MS, useAuth } from '../auth/AuthContext';
import { PREF_KEYS } from '../storage/prefs';
import { tokenKey } from '../storage/secure';

jest.mock('@dashboard/api/client', () => require('../test/mockClient').clientMockFactory());

const api = client as jest.Mocked<typeof client> & { __fireUnauthorized: () => void };
const secureStore = (SecureStore as unknown as { __store: Map<string, string> }).__store;

const ME = { tenant: { id: 'ten_1', name: 'Default' }, user: { id: 'usr_1', email: 'admin@armada', isAdmin: true, isTenantAdmin: true }, passwordChangeRequired: false } as unknown as WhoAmIResult;
const DRAFT = { name: 'Local', url: 'http://10.0.2.2:44010', kind: 'Direct' as const, biometricUnlock: false };

let clock = 1_000_000;
const now = () => clock;

function wrapper({ children }: { children: ReactNode }) {
  return <AuthProvider now={now}>{children}</AuthProvider>;
}

async function mount() {
  const hook = await renderHook(() => useAuth(), { wrapper });
  await waitFor(() => expect(hook.result.current.status).not.toBe('loading'));
  return hook;
}

type AppStateHandler = (state: string) => void;
let appStateHandlers: AppStateHandler[] = [];

beforeEach(async () => {
  await AsyncStorage.clear();
  secureStore.clear();
  jest.clearAllMocks();
  appStateHandlers = [];
  jest.spyOn(AppState, 'addEventListener').mockImplementation(((_type: string, handler: AppStateHandler) => {
    appStateHandlers.push(handler);
    return { remove: () => { appStateHandlers = appStateHandlers.filter((h) => h !== handler); } };
  }) as unknown as typeof AppState.addEventListener);
  api.whoami.mockResolvedValue(ME);
});

describe('server profiles and secure storage', () => {
  it('starts signed out with no profiles', async () => {
    const { result } = await mount();
    expect(result.current.status).toBe('signedOut');
    expect(result.current.profiles).toEqual([]);
  });

  it('a new profile becomes active and is stored without secrets', async () => {
    const { result } = await mount();
    await act(async () => { await result.current.saveProfile(DRAFT); });
    expect(result.current.activeProfile?.url).toBe('http://10.0.2.2:44010');
    const stored = await AsyncStorage.getItem(PREF_KEYS.profiles);
    expect(JSON.parse(stored ?? '[]')).toHaveLength(1);
    expect(api.configureClient).toHaveBeenCalledWith({ baseUrl: 'http://10.0.2.2:44010' });
  });

  it('login keeps the token in secure storage keyed by profile, never in AsyncStorage', async () => {
    const { result } = await mount();
    await act(async () => { await result.current.saveProfile(DRAFT); });
    await act(async () => { await result.current.login('tok_secret', { method: 'password', email: 'admin@armada', tenantId: 'ten_1', tenantName: 'Default' }); });
    expect(result.current.status).toBe('signedIn');
    expect(result.current.isAdmin).toBe(true);
    const id = result.current.activeProfile!.id;
    expect(secureStore.get(tokenKey(id))).toBe('tok_secret');
    const keys = await AsyncStorage.getAllKeys();
    const values = await Promise.all(keys.map((k) => AsyncStorage.getItem(k)));
    expect(values.join('|')).not.toContain('tok_secret');
    expect(result.current.activeProfile?.lastTenantName).toBe('Default');
    expect(result.current.activeProfile?.lastUserEmail).toBe('admin@armada');
  });

  it('a rejected token does not sign in and is not stored', async () => {
    const { result } = await mount();
    await act(async () => { await result.current.saveProfile(DRAFT); });
    api.whoami.mockRejectedValueOnce(new client.ApiError('Unauthorized', 401, null));
    await act(async () => {
      await expect(result.current.login('bad', { method: 'token' })).rejects.toBeInstanceOf(client.ApiError);
    });
    expect(result.current.status).toBe('signedOut');
    expect(secureStore.size).toBe(0);
  });

  it('restores a stored session on launch', async () => {
    const first = await mount();
    await act(async () => { await first.result.current.saveProfile(DRAFT); });
    await act(async () => { await first.result.current.login('tok_1', { method: 'token' }); });
    await first.unmount();

    const second = await mount();
    await waitFor(() => expect(second.result.current.status).toBe('signedIn'));
    expect(second.result.current.sessionToken).toBe('tok_1');
  });

  it('a stored token the server rejects (401) is deleted on launch', async () => {
    const first = await mount();
    await act(async () => { await first.result.current.saveProfile(DRAFT); });
    await act(async () => { await first.result.current.login('tok_old', { method: 'token' }); });
    await first.unmount();

    api.whoami.mockRejectedValueOnce(new client.ApiError('Unauthorized', 401, null));
    const second = await mount();
    expect(second.result.current.status).toBe('signedOut');
    expect(secureStore.size).toBe(0);
  });

  it('an unreachable server keeps the token and offers retry', async () => {
    const first = await mount();
    await act(async () => { await first.result.current.saveProfile(DRAFT); });
    await act(async () => { await first.result.current.login('tok_1', { method: 'token' }); });
    await first.unmount();

    api.whoami.mockRejectedValueOnce(new TypeError('Network request failed'));
    const second = await mount();
    expect(second.result.current.status).toBe('unreachable');
    expect(secureStore.size).toBe(1);
    await act(async () => { await second.result.current.retry(); });
    expect(second.result.current.status).toBe('signedIn');
  });

  it('sign-out deletes the stored token but keeps the profile', async () => {
    const { result } = await mount();
    await act(async () => { await result.current.saveProfile(DRAFT); });
    await act(async () => { await result.current.login('tok_1', { method: 'token' }); });
    await act(async () => { await result.current.logout(); });
    expect(result.current.status).toBe('signedOut');
    expect(secureStore.size).toBe(0);
    expect(result.current.profiles).toHaveLength(1);
  });

  it('a 401 from any request signs out', async () => {
    const { result } = await mount();
    await act(async () => { await result.current.saveProfile(DRAFT); });
    await act(async () => { await result.current.login('tok_1', { method: 'token' }); });
    await act(async () => { api.__fireUnauthorized(); });
    await waitFor(() => expect(result.current.status).toBe('signedOut'));
  });

  it('switching profiles restores each profile its own session', async () => {
    const { result } = await mount();
    await act(async () => { await result.current.saveProfile(DRAFT); });
    await act(async () => { await result.current.login('tok_a', { method: 'token' }); });
    const firstId = result.current.activeProfile!.id;
    await act(async () => { await result.current.saveProfile({ ...DRAFT, name: 'Other', url: 'https://other.example' }); });
    expect(result.current.status).toBe('signedOut');
    await act(async () => { await result.current.selectProfile(firstId); });
    expect(result.current.status).toBe('signedIn');
    expect(result.current.sessionToken).toBe('tok_a');
  });

  it('deleting a profile removes its token', async () => {
    const { result } = await mount();
    await act(async () => { await result.current.saveProfile(DRAFT); });
    await act(async () => { await result.current.login('tok_1', { method: 'token' }); });
    await act(async () => { await result.current.deleteProfile(result.current.activeProfile!.id); });
    expect(result.current.profiles).toEqual([]);
    expect(secureStore.size).toBe(0);
    expect(result.current.status).toBe('signedOut');
  });

  it('changing a profile URL drops the token for the old server', async () => {
    const { result } = await mount();
    await act(async () => { await result.current.saveProfile(DRAFT); });
    await act(async () => { await result.current.login('tok_1', { method: 'token' }); });
    const id = result.current.activeProfile!.id;
    await act(async () => { await result.current.saveProfile({ ...DRAFT, url: 'https://moved.example' }, id); });
    expect(secureStore.size).toBe(0);
    expect(result.current.status).toBe('signedOut');
  });
});

describe('biometric unlock', () => {
  async function signedInBiometric() {
    const first = await mount();
    await act(async () => { await first.result.current.saveProfile({ ...DRAFT, biometricUnlock: true }); });
    await act(async () => { await first.result.current.login('tok_1', { method: 'token' }); });
    return first;
  }

  it('a biometric profile starts locked and unlocks with the OS prompt', async () => {
    await (await signedInBiometric()).unmount();
    const { result } = await mount();
    expect(result.current.status).toBe('locked');
    let ok = false;
    await act(async () => { ok = await result.current.unlock('Unlock', 'Cancel'); });
    expect(ok).toBe(true);
    expect(result.current.status).toBe('signedIn');
  });

  it('a cancelled prompt stays locked', async () => {
    await (await signedInBiometric()).unmount();
    const { result } = await mount();
    (LocalAuthentication.authenticateAsync as jest.Mock).mockResolvedValueOnce({ success: false, error: 'user_cancel' });
    await act(async () => { await result.current.unlock('Unlock', 'Cancel'); });
    expect(result.current.status).toBe('locked');
  });

  it('locks again after a long stay in the background, not after a short one', async () => {
    const { result } = await signedInBiometric();
    expect(result.current.status).toBe('signedIn');
    await act(async () => { appStateHandlers.forEach((h) => h('background')); });
    clock += 1000;
    await act(async () => { appStateHandlers.forEach((h) => h('active')); });
    expect(result.current.status).toBe('signedIn');
    await act(async () => { appStateHandlers.forEach((h) => h('background')); });
    clock += LOCK_AFTER_BACKGROUND_MS;
    await act(async () => { appStateHandlers.forEach((h) => h('active')); });
    expect(result.current.status).toBe('locked');
  });
});

describe('default password skip', () => {
  it('skipping is remembered per profile and user, and forgotten once the password changes', async () => {
    api.whoami.mockResolvedValue({ ...ME, passwordChangeRequired: true });
    const first = await mount();
    await act(async () => { await first.result.current.saveProfile(DRAFT); });
    await act(async () => { await first.result.current.login('tok_1', { method: 'password' }); });
    expect(first.result.current.mustChangePassword).toBe(true);
    await act(async () => { await first.result.current.skipPasswordChange(); });
    expect(first.result.current.mustChangePassword).toBe(false);
    expect(first.result.current.passwordChangeSkipped).toBe(true);
    await first.unmount();

    const second = await mount();
    await waitFor(() => expect(second.result.current.status).toBe('signedIn'));
    expect(second.result.current.mustChangePassword).toBe(false);

    api.whoami.mockResolvedValue(ME);
    await act(async () => { await second.result.current.refresh(); });
    expect(second.result.current.passwordChangeSkipped).toBe(false);
    expect(JSON.parse((await AsyncStorage.getItem(PREF_KEYS.passwordChangeSkipped)) ?? '[]')).toEqual([]);
  });
});
