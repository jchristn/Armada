import AsyncStorage from '@react-native-async-storage/async-storage';
import * as SecureStore from 'expo-secure-store';
import * as LocalAuthentication from 'expo-local-authentication';
import { act, fireEvent, render, renderHook, screen, userEvent, waitFor, within } from '@testing-library/react-native';
import type { ReactNode } from 'react';
import { Pressable, Text } from 'react-native';
import * as client from '@dashboard/api/client';
import type { I18nCatalog } from '@dashboard/i18n/catalog';
import type { WhoAmIResult } from '@dashboard/types/models';
import { AuthProvider, useAuth, type PasswordToRemember } from '../auth/AuthContext';
import { biometricKindFor } from '../auth/biometrics';
import { savedCredentialsKey } from '../auth/savedCredentials';
import { ProxyPortalForm } from '../components/app/ProxySignIn';
import { SavePasswordOfferSheet } from '../components/app/SavePasswordOfferSheet';
import { SignInSecuritySection } from '../components/app/SignInSecurity';
import { useSignOut } from '../components/app/useSignOut';
import { LocaleProvider } from '../i18n/LocaleContext';
import type { ServerProfile } from '../profiles/types';
import { ProxyError, type ProxyClient } from '../proxy/proxyApi';
import { PasswordChangeScreen } from '../screens/PasswordChangeScreen';
import { SignInScreen } from '../screens/SignInScreen';
import { PREF_KEYS } from '../storage/prefs';
import { tokenKey } from '../storage/secure';
import { ThemeProvider } from '../theme/ThemeContext';

jest.mock('@dashboard/api/client', () => require('../test/mockClient').clientMockFactory());

const api = client as jest.Mocked<typeof client> & { __fireUnauthorized: () => void };
const secureMock = SecureStore as unknown as {
  __store: Map<string, string>;
  __writeOptions: Map<string, SecureStore.SecureStoreOptions>;
  getItemAsync: jest.Mock;
  setItemAsync: jest.Mock;
  canUseBiometricAuthentication: jest.Mock;
};
const secureStore = secureMock.__store;
const localAuth = LocalAuthentication as unknown as {
  hasHardwareAsync: jest.Mock;
  isEnrolledAsync: jest.Mock;
  supportedAuthenticationTypesAsync: jest.Mock;
};

const SECRET = 'correct-horse-battery';
const ME = { tenant: { id: 'ten_1', name: 'Default' }, user: { id: 'usr_1', email: 'admin@armada', isAdmin: true }, passwordChangeRequired: false } as unknown as WhoAmIResult;
const DRAFT = { name: 'Local', url: 'http://10.0.2.2:44010', kind: 'Direct' as const, biometricUnlock: false };
const DETAILS = { method: 'password' as const, email: 'admin@armada', tenantId: 'ten_1', tenantName: 'Default' };
const SAVE: PasswordToRemember = { password: SECRET, save: true, canSave: true };
const DONT_SAVE: PasswordToRemember = { password: SECRET, save: false, canSave: true };

const PROFILE: ServerProfile = {
  id: 'prf_test', name: 'Local Admiral', kind: 'Direct', url: 'http://10.0.2.2:44010', signInMethod: 'password',
  biometricUnlock: false, lastEmail: 'admin@armada', lastTenantId: 'ten_1', lastTenantName: 'Default', lastUserEmail: 'admin@armada',
  createdUtc: '2026-10-07T00:00:00Z',
};
const SAVED_PROFILE: ServerProfile = { ...PROFILE, savedSignIn: { email: 'admin@armada', tenantId: 'ten_1', tenantName: 'Default' } };
const ADMIRAL_KEY = savedCredentialsKey('prf_test', 'admiral');
const PROXY_KEY = savedCredentialsKey('prf_proxy', 'proxy');

const CONNECTED = { isAuthenticated: true, selectedInstanceId: 'armada-1', selectedInstance: { instanceId: 'armada-1', state: 'connected' }, relay: { api: true, websocket: true, dashboard: true } };

function mockProxy(): jest.Mocked<ProxyClient> {
  return {
    baseUrl: 'https://proxy.example',
    login: jest.fn(async () => ({ token: 'P1', expiresUtc: null, selectedInstanceId: null })),
    listInstances: jest.fn(async () => [{ instanceId: 'armada-1', state: 'connected', capabilities: ['dashboard.http.relay'] }]),
    selectInstance: jest.fn(async () => CONNECTED),
    sessionContext: jest.fn(async () => CONNECTED),
    logoutInstance: jest.fn(async () => CONNECTED),
    logout: jest.fn(async () => undefined),
  } as unknown as jest.Mocked<ProxyClient>;
}

let proxy: jest.Mocked<ProxyClient>;
const EMPTY_CATALOG: I18nCatalog = { defaultLocale: 'en', supportedLocales: [], locales: {} };

function Providers({ children }: { children: ReactNode }) {
  return (
    <ThemeProvider>
      <AuthProvider proxyClientFactory={() => proxy}>
        <LocaleProvider serverUrl={null} bundledCatalog={() => EMPTY_CATALOG}>{children}</LocaleProvider>
      </AuthProvider>
    </ThemeProvider>
  );
}

function hookWrapper({ children }: { children: ReactNode }) {
  return <AuthProvider proxyClientFactory={() => proxy}>{children}</AuthProvider>;
}

async function mount() {
  const hook = await renderHook(() => useAuth(), { wrapper: hookWrapper });
  await waitFor(() => expect(hook.result.current.status).not.toBe('loading'));
  return hook;
}

let auth: ReturnType<typeof useAuth> | null = null;
function Probe() {
  auth = useAuth();
  return null;
}

async function storeProfiles(profiles: ServerProfile[], activeId: string) {
  await AsyncStorage.setItem(PREF_KEYS.profiles, JSON.stringify(profiles));
  await AsyncStorage.setItem(PREF_KEYS.activeProfile, JSON.stringify(activeId));
}

function storeSavedAdmiral(password = SECRET) {
  secureStore.set(ADMIRAL_KEY, JSON.stringify({ email: 'admin@armada', tenantId: 'ten_1', tenantName: 'Default', password }));
}

/** Everything the app wrote outside the biometric-protected items: AsyncStorage values and plain secure-store items. */
async function unprotectedText(): Promise<string> {
  const keys = await AsyncStorage.getAllKeys();
  const values = await Promise.all(keys.map((k) => AsyncStorage.getItem(k)));
  const plainSecure = [...secureStore.entries()]
    .filter(([key]) => !secureMock.__writeOptions.get(key)?.requireAuthentication)
    .map(([, value]) => value);
  return [...values, ...plainSecure].join('|');
}

let logSpies: jest.SpyInstance[] = [];

beforeEach(async () => {
  await AsyncStorage.clear();
  secureStore.clear();
  secureMock.__writeOptions.clear();
  jest.clearAllMocks();
  auth = null;
  proxy = mockProxy();
  api.whoami.mockResolvedValue(ME);
  localAuth.hasHardwareAsync.mockResolvedValue(true);
  localAuth.isEnrolledAsync.mockResolvedValue(true);
  localAuth.supportedAuthenticationTypesAsync.mockResolvedValue([LocalAuthentication.AuthenticationType.FACIAL_RECOGNITION]);
  secureMock.canUseBiometricAuthentication.mockReturnValue(true);
  secureMock.getItemAsync.mockImplementation(async (key: string) => secureStore.get(key) ?? null);
  logSpies = (['log', 'info', 'warn', 'error', 'debug'] as const).map((level) => jest.spyOn(console, level));
});

afterEach(() => {
  // The password never reaches the console either.
  for (const spy of logSpies) {
    for (const call of spy.mock.calls) expect(JSON.stringify(call)).not.toContain(SECRET);
    spy.mockRestore();
  }
});

describe('saving the password (AuthContext)', () => {
  it('saves email, tenant, and password behind biometrics on a successful sign-in with the switch on', async () => {
    const { result } = await mount();
    await act(async () => { await result.current.saveProfile(DRAFT); });
    const id = result.current.activeProfile!.id;
    await act(async () => { await result.current.login('tok_1', DETAILS, SAVE); });
    expect(result.current.status).toBe('signedIn');
    const key = savedCredentialsKey(id, 'admiral');
    expect(JSON.parse(secureStore.get(key) ?? '{}')).toEqual({ email: 'admin@armada', tenantId: 'ten_1', tenantName: 'Default', password: SECRET });
    expect(secureMock.__writeOptions.get(key)).toMatchObject({
      requireAuthentication: true,
      keychainService: 'armada.signin',
      keychainAccessible: SecureStore.WHEN_PASSCODE_SET_THIS_DEVICE_ONLY,
    });
    expect(result.current.activeProfile?.savedSignIn).toEqual({ email: 'admin@armada', tenantId: 'ten_1', tenantName: 'Default' });
    // Every write that carried the password was a protected one; nothing else holds it.
    for (const [, value, options] of secureMock.setItemAsync.mock.calls as [string, string, SecureStore.SecureStoreOptions | undefined][]) {
      if (value.includes(SECRET)) expect(options?.requireAuthentication).toBe(true);
    }
    expect(await unprotectedText()).not.toContain(SECRET);
    expect(result.current.savePasswordOffer).toBeNull();
  });

  it('saves nothing when the switch is off (and offers once instead)', async () => {
    const { result } = await mount();
    await act(async () => { await result.current.saveProfile(DRAFT); });
    await act(async () => { await result.current.login('tok_1', DETAILS, DONT_SAVE); });
    expect([...secureStore.keys()].filter((k) => k.startsWith('armada.signin'))).toEqual([]);
    expect(result.current.activeProfile?.savedSignIn ?? null).toBeNull();
    expect(result.current.savePasswordOffer).toMatchObject({ admiral: true, proxy: false });
    // The offer holds the password in memory only.
    expect(await unprotectedText()).not.toContain(SECRET);
  });

  it('saves nothing when the sign-in fails', async () => {
    const { result } = await mount();
    await act(async () => { await result.current.saveProfile(DRAFT); });
    api.whoami.mockRejectedValueOnce(new client.ApiError('Unauthorized', 401, null));
    await act(async () => {
      await expect(result.current.login('bad', DETAILS, SAVE)).rejects.toBeInstanceOf(client.ApiError);
    });
    expect(secureStore.size).toBe(0);
    expect(secureMock.setItemAsync).not.toHaveBeenCalled();
    expect(result.current.savePasswordOffer).toBeNull();
  });

  it('saves and offers nothing when the device cannot protect the item with biometrics', async () => {
    const { result } = await mount();
    await act(async () => { await result.current.saveProfile(DRAFT); });
    await act(async () => { await result.current.login('tok_1', DETAILS, { ...SAVE, canSave: false }); });
    expect([...secureStore.keys()].filter((k) => k.startsWith('armada.signin'))).toEqual([]);
    expect(result.current.savePasswordOffer).toBeNull();
  });

  it('turning the switch off on a later sign-in forgets the saved password', async () => {
    const { result } = await mount();
    await act(async () => { await result.current.saveProfile(DRAFT); });
    const key = savedCredentialsKey(result.current.activeProfile!.id, 'admiral');
    await act(async () => { await result.current.login('tok_1', DETAILS, SAVE); });
    await act(async () => { await result.current.logout(); });
    await act(async () => { await result.current.login('tok_2', DETAILS, DONT_SAVE); });
    expect(secureStore.has(key)).toBe(false);
    expect(result.current.activeProfile?.savedSignIn).toBeNull();
  });
});

describe('the one-time offer', () => {
  it('is shown once: "Not now" is remembered for the profile', async () => {
    const { result } = await mount();
    await act(async () => { await result.current.saveProfile(DRAFT); });
    await act(async () => { await result.current.login('tok_1', DETAILS, DONT_SAVE); });
    expect(result.current.savePasswordOffer).not.toBeNull();
    await act(async () => { await result.current.declineSavePasswordOffer(); });
    expect(result.current.savePasswordOffer).toBeNull();
    expect(result.current.activeProfile?.savePasswordOfferDeclined).toBe(true);
    await act(async () => { await result.current.logout(); });
    await act(async () => { await result.current.login('tok_2', DETAILS, DONT_SAVE); });
    expect(result.current.savePasswordOffer).toBeNull();
  });

  it('the sheet saves the password on "Use Face ID"', async () => {
    const user = userEvent.setup();
    await storeProfiles([PROFILE], PROFILE.id);
    await render(<><SavePasswordOfferSheet /><Probe /></>, { wrapper: Providers });
    await waitFor(() => expect(auth?.status).toBe('signedOut'));
    await act(async () => { await auth!.login('tok_1', DETAILS, DONT_SAVE); });
    expect(await screen.findByText('Use Face ID next time?')).toBeTruthy();
    await user.press(screen.getByTestId('save-password-offer-accept'));
    await waitFor(() => expect(auth?.activeProfile?.savedSignIn?.email).toBe('admin@armada'));
    expect(JSON.parse(secureStore.get(ADMIRAL_KEY) ?? '{}').password).toBe(SECRET);
    expect(secureMock.__writeOptions.get(ADMIRAL_KEY)?.requireAuthentication).toBe(true);
    expect(screen.queryByTestId('save-password-offer-accept')).toBeNull();
  });

  it('the sheet remembers "Not now" and does not come back', async () => {
    const user = userEvent.setup();
    await storeProfiles([PROFILE], PROFILE.id);
    await render(<><SavePasswordOfferSheet /><Probe /></>, { wrapper: Providers });
    await waitFor(() => expect(auth?.status).toBe('signedOut'));
    await act(async () => { await auth!.login('tok_1', DETAILS, DONT_SAVE); });
    await user.press(await screen.findByTestId('save-password-offer-decline'));
    await waitFor(() => expect(auth?.activeProfile?.savePasswordOfferDeclined).toBe(true));
    await act(async () => { await auth!.logout(); });
    await act(async () => { await auth!.login('tok_2', DETAILS, DONT_SAVE); });
    expect(screen.queryByTestId('save-password-offer-accept')).toBeNull();
    expect(secureStore.has(ADMIRAL_KEY)).toBe(false);
  });
});

describe('sign-in screen', () => {
  async function renderSignIn(profile: ServerProfile) {
    await storeProfiles([profile], profile.id);
    await render(<><SignInScreen /><Probe /></>, { wrapper: Providers });
    await screen.findByText(profile.name);
  }

  async function toPasswordStep(user: ReturnType<typeof userEvent.setup>) {
    api.lookupTenants.mockResolvedValue({ tenants: [{ id: 'ten_1', name: 'Default' }] });
    await user.clear(screen.getByTestId('sign-in-email'));
    await user.type(screen.getByTestId('sign-in-email'), 'admin@armada');
    await user.press(screen.getByTestId('sign-in-continue'));
    await screen.findByTestId('sign-in-password');
  }

  it('the password step offers "Save password and use Face ID" and saves it after the sign-in succeeds', async () => {
    const user = userEvent.setup();
    api.authenticate.mockResolvedValue({ success: true, token: 'tok_pw' } as never);
    await renderSignIn(PROFILE);
    await toPasswordStep(user);
    expect(await screen.findByText('Save password and use Face ID')).toBeTruthy();
    await act(async () => { fireEvent(screen.getByTestId('sign-in-save-password'), 'valueChange', true); });
    await user.type(screen.getByTestId('sign-in-password'), SECRET);
    await user.press(screen.getByTestId('sign-in-submit'));
    await waitFor(() => expect(auth?.status).toBe('signedIn'));
    expect(JSON.parse(secureStore.get(ADMIRAL_KEY) ?? '{}')).toMatchObject({ email: 'admin@armada', tenantId: 'ten_1', password: SECRET });
    expect(await unprotectedText()).not.toContain(SECRET);
  });

  it('a failed password sign-in saves nothing even with the switch on', async () => {
    const user = userEvent.setup();
    api.authenticate.mockRejectedValue(new client.ApiError('Unauthorized', 401, null));
    await renderSignIn(PROFILE);
    await toPasswordStep(user);
    await act(async () => { fireEvent(await screen.findByTestId('sign-in-save-password'), 'valueChange', true); });
    await user.type(screen.getByTestId('sign-in-password'), 'wrong');
    await user.press(screen.getByTestId('sign-in-submit'));
    expect(await screen.findByTestId('sign-in-error')).toBeTruthy();
    expect(secureStore.has(ADMIRAL_KEY)).toBe(false);
    expect(auth?.status).toBe('signedOut');
  });

  it('the label follows the device: Touch ID, and fingerprint on Android', async () => {
    const user = userEvent.setup();
    localAuth.supportedAuthenticationTypesAsync.mockResolvedValue([LocalAuthentication.AuthenticationType.FINGERPRINT]);
    await renderSignIn(PROFILE);
    await toPasswordStep(user);
    expect(await screen.findByText('Save password and use Touch ID')).toBeTruthy();
    const { FINGERPRINT, FACIAL_RECOGNITION } = LocalAuthentication.AuthenticationType;
    expect(biometricKindFor([FINGERPRINT], 'android')).toBe('fingerprint');
    expect(biometricKindFor([FINGERPRINT, FACIAL_RECOGNITION], 'android')).toBe('biometrics');
    expect(biometricKindFor([FACIAL_RECOGNITION], 'ios')).toBe('faceId');
  });

  it('hides the switch when biometrics are not enrolled', async () => {
    const user = userEvent.setup();
    localAuth.isEnrolledAsync.mockResolvedValue(false);
    await renderSignIn(PROFILE);
    await toPasswordStep(user);
    await act(async () => undefined);
    expect(screen.queryByTestId('sign-in-save-password')).toBeNull();
  });

  it('hides the switch when the keychain cannot bind an item to biometrics', async () => {
    const user = userEvent.setup();
    secureMock.canUseBiometricAuthentication.mockReturnValue(false);
    await renderSignIn(PROFILE);
    await toPasswordStep(user);
    await act(async () => undefined);
    expect(screen.queryByTestId('sign-in-save-password')).toBeNull();
  });

  it('with a saved password, prompts on open and signs in with Face ID', async () => {
    storeSavedAdmiral();
    api.authenticate.mockResolvedValue({ success: true, token: 'tok_face' } as never);
    await renderSignIn(SAVED_PROFILE);
    await waitFor(() => expect(auth?.status).toBe('signedIn'));
    expect(secureMock.getItemAsync).toHaveBeenCalledWith(ADMIRAL_KEY, expect.objectContaining({ requireAuthentication: true }));
    expect(api.authenticate).toHaveBeenCalledWith({ email: 'admin@armada', password: SECRET, tenantId: 'ten_1' });
    expect(secureStore.get(tokenKey('prf_test'))).toBe('tok_face');
    expect(secureStore.has(ADMIRAL_KEY)).toBe(true);
  });

  it('the "Sign in with Face ID" button retries after a cancelled prompt', async () => {
    const user = userEvent.setup();
    storeSavedAdmiral();
    secureMock.getItemAsync.mockImplementation(async (key: string, options?: SecureStore.SecureStoreOptions) => {
      if (options?.requireAuthentication) throw new Error('User canceled the operation.');
      return secureStore.get(key) ?? null;
    });
    api.authenticate.mockResolvedValue({ success: true, token: 'tok_face' } as never);
    await renderSignIn(SAVED_PROFILE);
    // Cancelled: back to the password form for the saved account, and the saved password stays.
    expect(within(await screen.findByTestId('sign-in-notice')).getByText(/^Face\ ID\ sign\-in\ was\ cancelled\.\ Sign\ in\ with\ your\ password\./)).toBeTruthy();
    expect(screen.getByTestId('sign-in-password')).toBeTruthy();
    expect(screen.getByTestId('sign-in-context')).toHaveTextContent('Signing in as admin@armada to Default');
    expect(secureStore.has(ADMIRAL_KEY)).toBe(true);
    expect(api.authenticate).not.toHaveBeenCalled();
    expect(auth?.status).toBe('signedOut');

    secureMock.getItemAsync.mockImplementation(async (key: string) => secureStore.get(key) ?? null);
    await user.press(screen.getByTestId('sign-in-biometric'));
    await waitFor(() => expect(auth?.status).toBe('signedIn'));
  });

  it('a saved password the server rejects is deleted and the password form explains why', async () => {
    storeSavedAdmiral('old-password');
    api.authenticate.mockRejectedValue(new client.ApiError('Unauthorized', 401, null));
    await renderSignIn(SAVED_PROFILE);
    expect(within(await screen.findByTestId('sign-in-notice')).getByText(/^The\ saved\ password\ was\ not\ accepted,\ so\ it\ was\ removed\ from\ this\ device\.\ Sign\ in\ with\ your\ password\./)).toBeTruthy();
    expect(secureStore.has(ADMIRAL_KEY)).toBe(false);
    expect(auth?.activeProfile?.savedSignIn).toBeNull();
    expect(screen.getByTestId('sign-in-password')).toBeTruthy();
    expect(screen.queryByTestId('sign-in-biometric')).toBeNull();
    expect(auth?.status).toBe('signedOut');
  });

  it('an unreachable server keeps the saved password', async () => {
    storeSavedAdmiral();
    api.authenticate.mockRejectedValue(new client.NetworkError('fetch failed', null));
    await renderSignIn(SAVED_PROFILE);
    expect(within(await screen.findByTestId('sign-in-error')).getByText('Could not reach the server. Check the address, the port, and your connection.')).toBeTruthy();
    expect(secureStore.has(ADMIRAL_KEY)).toBe(true);
    expect(auth?.activeProfile?.savedSignIn).not.toBeNull();
  });

  it('an item invalidated by a biometric change counts as no saved password', async () => {
    storeSavedAdmiral();
    // The OS no longer returns the item (iOS biometryCurrentSet, Android KeyPermanentlyInvalidatedException).
    secureMock.getItemAsync.mockImplementation(async (key: string, options?: SecureStore.SecureStoreOptions) => (
      options?.requireAuthentication ? null : secureStore.get(key) ?? null));
    await renderSignIn(SAVED_PROFILE);
    expect(within(await screen.findByTestId('sign-in-notice')).getByText(/^Your\ saved\ password\ is\ no\ longer\ available/)).toBeTruthy();
    await waitFor(() => expect(auth?.activeProfile?.savedSignIn).toBeNull());
    expect(secureStore.has(ADMIRAL_KEY)).toBe(false);
    expect(screen.queryByTestId('sign-in-biometric')).toBeNull();
    expect(screen.getByTestId('sign-in-password')).toBeTruthy();
    expect(api.authenticate).not.toHaveBeenCalled();
  });

  it('does not prompt by itself right after the user signed out, but offers the button', async () => {
    storeSavedAdmiral();
    secureStore.set(tokenKey('prf_test'), 'tok_1');
    await storeProfiles([SAVED_PROFILE], SAVED_PROFILE.id);
    await render(<Probe />, { wrapper: Providers });
    await waitFor(() => expect(auth?.status).toBe('signedIn'));
    await act(async () => { await auth!.logout(); });
    await screen.rerender(<><SignInScreen /><Probe /></>);
    expect(await screen.findByTestId('sign-in-biometric')).toBeTruthy();
    await act(async () => undefined);
    expect(secureMock.getItemAsync).not.toHaveBeenCalledWith(ADMIRAL_KEY, expect.anything());
  });

  it('prompts by itself after the session expired (401)', async () => {
    storeSavedAdmiral();
    secureStore.set(tokenKey('prf_test'), 'tok_1');
    api.authenticate.mockResolvedValue({ success: true, token: 'tok_face' } as never);
    await storeProfiles([SAVED_PROFILE], SAVED_PROFILE.id);
    await render(<Probe />, { wrapper: Providers });
    await waitFor(() => expect(auth?.status).toBe('signedIn'));
    await act(async () => { api.__fireUnauthorized(); });
    await waitFor(() => expect(auth?.status).toBe('signedOut'));
    await screen.rerender(<><SignInScreen /><Probe /></>);
    await waitFor(() => expect(auth?.status).toBe('signedIn'));
    expect(api.authenticate).toHaveBeenCalledWith({ email: 'admin@armada', password: SECRET, tenantId: 'ten_1' });
  });
});

describe('keeping and forgetting', () => {
  async function signedInWithSaved() {
    const hook = await mount();
    await act(async () => { await hook.result.current.saveProfile(DRAFT); });
    await act(async () => { await hook.result.current.login('tok_1', DETAILS, SAVE); });
    return { ...hook, key: savedCredentialsKey(hook.result.current.activeProfile!.id, 'admiral') };
  }

  it('sign-out keeps the saved password', async () => {
    const { result, key } = await signedInWithSaved();
    await act(async () => { await result.current.logout(); });
    expect(result.current.status).toBe('signedOut');
    expect(secureStore.has(key)).toBe(true);
    expect(result.current.activeProfile?.savedSignIn).not.toBeNull();
  });

  it('sign-out with "forget" deletes it', async () => {
    const { result, key } = await signedInWithSaved();
    await act(async () => { await result.current.logout({ forgetSavedPassword: true }); });
    expect(secureStore.has(key)).toBe(false);
    expect(result.current.activeProfile?.savedSignIn).toBeNull();
  });

  it('removing the profile deletes it', async () => {
    const { result, key } = await signedInWithSaved();
    await act(async () => { await result.current.deleteProfile(result.current.activeProfile!.id); });
    expect(secureStore.has(key)).toBe(false);
  });

  it('changing the profile URL deletes it', async () => {
    const { result, key } = await signedInWithSaved();
    const id = result.current.activeProfile!.id;
    await act(async () => { await result.current.saveProfile({ ...DRAFT, url: 'https://moved.example' }, id); });
    expect(secureStore.has(key)).toBe(false);
    expect(result.current.activeProfile?.savedSignIn).toBeNull();
  });

  it('changing the profile kind deletes it', async () => {
    const { result, key } = await signedInWithSaved();
    const id = result.current.activeProfile!.id;
    await act(async () => { await result.current.saveProfile({ ...DRAFT, kind: 'Proxy' }, id); });
    expect(secureStore.has(key)).toBe(false);
  });

  it('renaming the profile keeps it', async () => {
    const { result, key } = await signedInWithSaved();
    const id = result.current.activeProfile!.id;
    await act(async () => { await result.current.saveProfile({ ...DRAFT, name: 'Renamed' }, id); });
    expect(secureStore.has(key)).toBe(true);
  });

  it('the sign-out confirmation offers "forget"; without a saved password it signs out at once', async () => {
    const user = userEvent.setup();
    function SignOutButton() {
      const { signOut, signOutSheet } = useSignOut();
      return <><AppButton onPress={signOut} />{signOutSheet}</>;
    }
    storeSavedAdmiral();
    secureStore.set(tokenKey('prf_test'), 'tok_1');
    await storeProfiles([SAVED_PROFILE], SAVED_PROFILE.id);
    await render(<><SignOutButton /><Probe /></>, { wrapper: Providers });
    await waitFor(() => expect(auth?.status).toBe('signedIn'));
    await user.press(screen.getByTestId('test-sign-out'));
    expect(await screen.findByText('Sign out of Local Admiral?')).toBeTruthy();
    await user.press(screen.getByTestId('sign-out-confirm-forget'));
    await waitFor(() => expect(auth?.status).toBe('signedOut'));
    expect(secureStore.has(ADMIRAL_KEY)).toBe(false);
  });

  it('Preferences shows both settings and forgets the saved password', async () => {
    const user = userEvent.setup();
    storeSavedAdmiral();
    secureStore.set(tokenKey('prf_test'), 'tok_1');
    await storeProfiles([SAVED_PROFILE], SAVED_PROFILE.id);
    await render(<><SignInSecuritySection /><Probe /></>, { wrapper: Providers });
    expect(await screen.findByText('Unlock with Face ID')).toBeTruthy();
    expect(screen.getByText('Saved password for Face ID sign-in')).toBeTruthy();
    expect(screen.getByTestId('prefs-saved-password-status')).toHaveTextContent('Saved for admin@armada');
    await act(async () => { fireEvent(screen.getByTestId('prefs-app-lock'), 'valueChange', true); });
    await waitFor(() => expect(auth?.activeProfile?.biometricUnlock).toBe(true));
    // The two settings are independent: the app lock did not touch the saved password.
    expect(secureStore.has(ADMIRAL_KEY)).toBe(true);
    await user.press(screen.getByTestId('prefs-saved-password-forget'));
    await waitFor(() => expect(auth?.activeProfile?.savedSignIn).toBeNull());
    expect(secureStore.has(ADMIRAL_KEY)).toBe(false);
    expect(auth?.activeProfile?.biometricUnlock).toBe(true);
    expect(screen.getByTestId('prefs-saved-password-status')).toHaveTextContent('Not saved');
  });
});

describe('password change', () => {
  it('replaces the saved password after a successful change', async () => {
    const user = userEvent.setup();
    storeSavedAdmiral('password');
    secureStore.set(tokenKey('prf_test'), 'default');
    api.whoami.mockResolvedValue({ ...ME, passwordChangeRequired: true });
    await storeProfiles([SAVED_PROFILE], SAVED_PROFILE.id);
    await render(<><PasswordChangeScreen /><Probe /></>, { wrapper: Providers });
    await waitFor(() => expect(auth?.mustChangePassword).toBe(true));
    api.changePassword.mockResolvedValue(ME);
    await user.type(screen.getByTestId('password-current'), 'password');
    await user.type(screen.getByTestId('password-new'), SECRET);
    await user.type(screen.getByTestId('password-confirm'), SECRET);
    api.whoami.mockResolvedValue(ME);
    await user.press(screen.getByTestId('password-change-submit'));
    await waitFor(() => expect(auth?.mustChangePassword).toBe(false));
    expect(JSON.parse(secureStore.get(ADMIRAL_KEY) ?? '{}')).toEqual({ email: 'admin@armada', tenantId: 'ten_1', tenantName: 'Default', password: SECRET });
    expect(secureMock.__writeOptions.get(ADMIRAL_KEY)?.requireAuthentication).toBe(true);
    expect(await unprotectedText()).not.toContain(SECRET);
  });

  it('a failed change keeps the old saved password', async () => {
    const user = userEvent.setup();
    storeSavedAdmiral('password');
    secureStore.set(tokenKey('prf_test'), 'default');
    api.whoami.mockResolvedValue({ ...ME, passwordChangeRequired: true });
    await storeProfiles([SAVED_PROFILE], SAVED_PROFILE.id);
    await render(<><PasswordChangeScreen /><Probe /></>, { wrapper: Providers });
    await waitFor(() => expect(auth?.mustChangePassword).toBe(true));
    api.changePassword.mockRejectedValue(new client.ApiError('Bad', 400, null));
    await user.type(screen.getByTestId('password-current'), 'password');
    await user.type(screen.getByTestId('password-new'), SECRET);
    await user.type(screen.getByTestId('password-confirm'), SECRET);
    await user.press(screen.getByTestId('password-change-submit'));
    expect(await screen.findByTestId('password-change-error')).toBeTruthy();
    expect(JSON.parse(secureStore.get(ADMIRAL_KEY) ?? '{}').password).toBe('password');
  });
});

describe('Proxy profiles', () => {
  const PROXY_PROFILE: ServerProfile = {
    ...PROFILE, id: 'prf_proxy', name: 'Remote', kind: 'Proxy', url: 'https://proxy.example', proxyInstanceId: 'armada-1',
  };

  it('the proxy password is saved behind biometrics with the switch on', async () => {
    const user = userEvent.setup();
    await storeProfiles([PROXY_PROFILE], PROXY_PROFILE.id);
    await render(<><ProxyPortalForm /><Probe /></>, { wrapper: Providers });
    await act(async () => { fireEvent(await screen.findByTestId('proxy-save-password'), 'valueChange', true); });
    await user.type(screen.getByTestId('proxy-password'), SECRET);
    await user.press(screen.getByTestId('proxy-sign-in'));
    await waitFor(() => expect(auth?.activeProfile?.proxyPasswordSaved).toBe(true));
    expect(JSON.parse(secureStore.get(PROXY_KEY) ?? '{}')).toEqual({ password: SECRET });
    expect(secureMock.__writeOptions.get(PROXY_KEY)?.requireAuthentication).toBe(true);
    expect(await unprotectedText()).not.toContain(SECRET);
  });

  it('signs in to the proxy with Face ID; a rejected proxy password is deleted', async () => {
    secureStore.set(PROXY_KEY, JSON.stringify({ password: 'stale' }));
    proxy.login.mockRejectedValue(new ProxyError('unauthorized', 401, 'bad'));
    await storeProfiles([{ ...PROXY_PROFILE, proxyPasswordSaved: true }], PROXY_PROFILE.id);
    await render(<><ProxyPortalForm /><Probe /></>, { wrapper: Providers });
    expect(within(await screen.findByTestId('proxy-notice')).getByText(/^The\ saved\ password\ was\ not\ accepted/)).toBeTruthy();
    expect(proxy.login).toHaveBeenCalledWith('stale');
    expect(secureStore.has(PROXY_KEY)).toBe(false);
    expect(auth?.activeProfile?.proxyPasswordSaved).toBe(false);
    expect(screen.getByTestId('proxy-password')).toBeTruthy();
  });

  it('signs in to the proxy with Face ID', async () => {
    secureStore.set(PROXY_KEY, JSON.stringify({ password: SECRET }));
    await storeProfiles([{ ...PROXY_PROFILE, proxyPasswordSaved: true }], PROXY_PROFILE.id);
    await render(<><ProxyPortalForm /><Probe /></>, { wrapper: Providers });
    await waitFor(() => expect(proxy.login).toHaveBeenCalledWith(SECRET));
    // The remembered instance is selected again and the Admiral sign-in comes next.
    await waitFor(() => expect(auth?.proxyStage).toBe('admiral'));
    expect(secureStore.has(PROXY_KEY)).toBe(true);
  });
});

function AppButton({ onPress }: { onPress: () => void }) {
  return <Pressable testID="test-sign-out" onPress={onPress}><Text>Sign out</Text></Pressable>;
}
