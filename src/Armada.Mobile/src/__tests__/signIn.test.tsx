import AsyncStorage from '@react-native-async-storage/async-storage';
import * as SecureStore from 'expo-secure-store';
import { act, screen, userEvent, waitFor, within } from '@testing-library/react-native';
import * as client from '@dashboard/api/client';
import type { WhoAmIResult } from '@dashboard/types/models';
import { useAuth } from '../auth/AuthContext';
import { PREF_KEYS } from '../storage/prefs';
import { PasswordChangeScreen } from '../screens/PasswordChangeScreen';
import { SignInScreen } from '../screens/SignInScreen';
import { renderWithProviders } from '../test/render';
import type { ServerProfile } from '../profiles/types';

jest.mock('@dashboard/api/client', () => require('../test/mockClient').clientMockFactory());

const api = client as jest.Mocked<typeof client>;
const secureStore = (SecureStore as unknown as { __store: Map<string, string> }).__store;

const ME = { tenant: { id: 'ten_1', name: 'Default' }, user: { id: 'usr_1', email: 'admin@armada', isAdmin: true }, passwordChangeRequired: false } as unknown as WhoAmIResult;

const PROFILE: ServerProfile = {
  id: 'prf_test', name: 'Local Admiral', kind: 'Direct', url: 'http://10.0.2.2:44010', signInMethod: 'password',
  biometricUnlock: false, lastEmail: null, lastTenantId: null, lastTenantName: null, lastUserEmail: null, createdUtc: '2026-10-07T00:00:00Z',
};

let statusProbe = '';
function Probe() {
  statusProbe = useAuth().status;
  return null;
}

async function renderSignIn() {
  await AsyncStorage.setItem(PREF_KEYS.profiles, JSON.stringify([PROFILE]));
  await AsyncStorage.setItem(PREF_KEYS.activeProfile, JSON.stringify(PROFILE.id));
  await renderWithProviders(<><SignInScreen /><Probe /></>);
  await screen.findByText('Local Admiral');
}

beforeEach(async () => {
  await AsyncStorage.clear();
  secureStore.clear();
  jest.clearAllMocks();
  statusProbe = '';
  api.whoami.mockResolvedValue(ME);
});

describe('sign-in', () => {
  it('with no profile, asks for a server first and warns about plain HTTP', async () => {
    const user = userEvent.setup();
    await renderWithProviders(<SignInScreen />);
    await screen.findByTestId('sign-in-add-server');
    await user.type(screen.getByTestId('profile-url'), 'http://203.0.113.9:7890');
    expect(screen.getByTestId('insecure-url-warning')).toBeTruthy();
    await user.press(screen.getByTestId('profile-save'));
    await screen.findByTestId('sign-in');
    expect(JSON.parse((await AsyncStorage.getItem(PREF_KEYS.profiles)) ?? '[]')[0].url).toBe('http://203.0.113.9:7890');
  });

  it('one tenant: email goes straight to password, then signs in', async () => {
    const user = userEvent.setup();
    api.lookupTenants.mockResolvedValue({ tenants: [{ id: 'ten_1', name: 'Default' }] });
    api.authenticate.mockResolvedValue({ success: true, token: 'tok_pw' } as never);
    await renderSignIn();
    await user.type(screen.getByTestId('sign-in-email'), 'admin@armada');
    await user.press(screen.getByTestId('sign-in-continue'));
    await screen.findByTestId('sign-in-password');
    expect(screen.getByTestId('sign-in-context')).toHaveTextContent('Signing in as admin@armada to Default');
    await user.type(screen.getByTestId('sign-in-password'), 'password');
    await user.press(screen.getByTestId('sign-in-submit'));
    await waitFor(() => expect(statusProbe).toBe('signedIn'));
    expect(api.authenticate).toHaveBeenCalledWith({ email: 'admin@armada', password: 'password', tenantId: 'ten_1' });
    expect(secureStore.get('armada.token.prf_test')).toBe('tok_pw');
  });

  it('several tenants: the user picks one before the password', async () => {
    const user = userEvent.setup();
    api.lookupTenants.mockResolvedValue({ tenants: [{ id: 'ten_a', name: 'Alpha' }, { id: 'ten_b', name: 'Beta' }] });
    await renderSignIn();
    await user.type(screen.getByTestId('sign-in-email'), 'me@x.com');
    await user.press(screen.getByTestId('sign-in-continue'));
    await screen.findByTestId('sign-in-tenant-ten_b');
    expect(screen.getByTestId('sign-in-tenant-continue')).toBeDisabled();
    await user.press(screen.getByTestId('sign-in-tenant-ten_b'));
    await user.press(screen.getByTestId('sign-in-tenant-continue'));
    expect(await screen.findByTestId('sign-in-context')).toHaveTextContent('Signing in as me@x.com to Beta');
  });

  it('no tenant for the email is reported', async () => {
    const user = userEvent.setup();
    api.lookupTenants.mockResolvedValue({ tenants: [] });
    await renderSignIn();
    await user.type(screen.getByTestId('sign-in-email'), 'nobody@x.com');
    await user.press(screen.getByTestId('sign-in-continue'));
    expect(within(await screen.findByTestId('sign-in-error')).getByText('No tenants found for this email.')).toBeTruthy();
  });

  it('a server that cannot be reached says so instead of a tenant lookup failure', async () => {
    const user = userEvent.setup();
    api.lookupTenants.mockRejectedValue(new client.NetworkError('fetch failed: The resource could not be loaded', null));
    await renderSignIn();
    await user.type(screen.getByTestId('sign-in-email'), 'admin@armada');
    await user.press(screen.getByTestId('sign-in-continue'));
    expect(within(await screen.findByTestId('sign-in-error')).getByText('Could not reach the server. Check the address, the port, and your connection.')).toBeTruthy();
  });

  it('a server error during tenant lookup keeps the lookup message', async () => {
    const user = userEvent.setup();
    api.lookupTenants.mockRejectedValue(new client.ApiError('boom', 500, null));
    await renderSignIn();
    await user.type(screen.getByTestId('sign-in-email'), 'admin@armada');
    await user.press(screen.getByTestId('sign-in-continue'));
    expect(within(await screen.findByTestId('sign-in-error')).getByText('Failed to look up tenants.')).toBeTruthy();
  });

  it('rate limiting (429) has its own message, by status not text', async () => {
    const user = userEvent.setup();
    api.lookupTenants.mockResolvedValue({ tenants: [{ id: 'ten_1', name: 'Default' }] });
    api.authenticate.mockRejectedValue(new client.ApiError('anything', 429, null));
    await renderSignIn();
    await user.type(screen.getByTestId('sign-in-email'), 'admin@armada');
    await user.press(screen.getByTestId('sign-in-continue'));
    await user.type(await screen.findByTestId('sign-in-password'), 'nope');
    await user.press(screen.getByTestId('sign-in-submit'));
    expect(within(await screen.findByTestId('sign-in-error')).getByText('Too many failed sign-in attempts. Wait a few minutes and try again.')).toBeTruthy();
    expect(statusProbe).toBe('signedOut');
  });

  it('API key sign-in validates the key with whoami', async () => {
    const user = userEvent.setup();
    await renderSignIn();
    await user.press(screen.getByTestId('sign-in-mode-apikey'));
    await user.type(screen.getByTestId('sign-in-apikey'), 'default');
    await user.press(screen.getByTestId('sign-in-connect'));
    await waitFor(() => expect(statusProbe).toBe('signedIn'));
    expect(secureStore.get('armada.token.prf_test')).toBe('default');
  });

  it('a rejected API key shows the dashboard message', async () => {
    const user = userEvent.setup();
    await renderSignIn();
    api.whoami.mockRejectedValueOnce(new client.ApiError('Unauthorized', 401, null));
    await user.press(screen.getByTestId('sign-in-mode-apikey'));
    await user.type(screen.getByTestId('sign-in-apikey'), 'wrong');
    await user.press(screen.getByTestId('sign-in-connect'));
    expect(within(await screen.findByTestId('sign-in-error')).getByText('API key authentication failed.')).toBeTruthy();
  });
});

describe('password change required', () => {
  let skipped = false;
  let mustChange = false;
  function SkipProbe() {
    const auth = useAuth();
    skipped = auth.passwordChangeSkipped;
    mustChange = auth.mustChangePassword;
    return null;
  }

  async function renderChange() {
    api.whoami.mockResolvedValue({ ...ME, passwordChangeRequired: true });
    secureStore.set('armada.token.prf_test', 'default');
    await AsyncStorage.setItem(PREF_KEYS.profiles, JSON.stringify([PROFILE]));
    await AsyncStorage.setItem(PREF_KEYS.activeProfile, JSON.stringify(PROFILE.id));
    await renderWithProviders(<><PasswordChangeScreen /><SkipProbe /></>);
    await waitFor(() => expect(mustChange).toBe(true));
  }

  it('Skip asks for confirmation; Cancel keeps the screen', async () => {
    const user = userEvent.setup();
    await renderChange();
    await user.press(screen.getByTestId('password-change-skip'));
    expect(await screen.findByText('Keep the default password?')).toBeTruthy();
    await user.press(screen.getByTestId('password-skip-confirm-cancel'));
    expect(skipped).toBe(false);
    expect(mustChange).toBe(true);
  });

  it('confirming Skip continues and remembers the choice', async () => {
    const user = userEvent.setup();
    await renderChange();
    await user.press(screen.getByTestId('password-change-skip'));
    await user.press(await screen.findByTestId('password-skip-confirm-confirm'));
    await waitFor(() => expect(skipped).toBe(true));
    expect(mustChange).toBe(false);
    expect(JSON.parse((await AsyncStorage.getItem(PREF_KEYS.passwordChangeSkipped)) ?? '[]')).toEqual(['prf_test:usr_1']);
  });

  it('validates the new password like the dashboard', async () => {
    const user = userEvent.setup();
    await renderChange();
    await user.type(screen.getByTestId('password-current'), 'password');
    await user.type(screen.getByTestId('password-new'), 'short');
    await user.type(screen.getByTestId('password-confirm'), 'short');
    await user.press(screen.getByTestId('password-change-submit'));
    expect(await screen.findByTestId('password-change-error')).toHaveTextContent('The new password must be at least 8 characters.');
    expect(api.changePassword).not.toHaveBeenCalled();
  });

  it('a successful change refreshes whoami and leaves the screen', async () => {
    const user = userEvent.setup();
    await renderChange();
    api.changePassword.mockResolvedValue(ME);
    await user.type(screen.getByTestId('password-current'), 'password');
    await user.type(screen.getByTestId('password-new'), 'a-better-one-1');
    await user.type(screen.getByTestId('password-confirm'), 'a-better-one-1');
    api.whoami.mockResolvedValue(ME);
    await user.press(screen.getByTestId('password-change-submit'));
    await waitFor(() => expect(mustChange).toBe(false));
    expect(api.changePassword).toHaveBeenCalledWith({ CurrentPassword: 'password', NewPassword: 'a-better-one-1' });
    await act(async () => undefined);
  });
});
