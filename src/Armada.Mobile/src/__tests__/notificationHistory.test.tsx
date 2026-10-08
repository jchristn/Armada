import AsyncStorage from '@react-native-async-storage/async-storage';
import { act, render, waitFor } from '@testing-library/react-native';
import { useEffect, type ReactNode } from 'react';
import { AppState } from 'react-native';
import * as client from '@dashboard/api/client';
import type { I18nCatalog } from '@dashboard/i18n/catalog';
import type { WhoAmIResult } from '@dashboard/types/models';
import { AuthProvider, useAuth, type AuthState } from '../auth/AuthContext';
import { LocaleProvider } from '../i18n/LocaleContext';
import { combineAuthHooks } from '../lib/authHooks';
import {
  NOTIFICATION_HISTORY_PREFIX,
  NotificationProvider,
  clearNotificationHistory,
  createNotificationAuthHooks,
  notificationScope,
  useNotifications,
  type NotificationState,
} from '../notifications/NotificationContext';
import { SocketProvider } from '../socket/SocketContext';
import { socketFactory, type FakeSocket } from '../test/fakeSocket';
import { ThemeProvider } from '../theme/ThemeContext';

jest.mock('@dashboard/api/client', () => require('../test/mockClient').clientMockFactory());

const api = client as jest.Mocked<typeof client>;
const EMPTY_CATALOG: I18nCatalog = { defaultLocale: 'en', supportedLocales: [], locales: {} };

const probe: { notes: NotificationState | null; auth: AuthState | null } = { notes: null, auth: null };

function NotesProbe() {
  const notes = useNotifications();
  useEffect(() => { probe.notes = notes; });
  return null;
}

let sockets: FakeSocket[] = [];

function Harness({ scope }: { scope: string | null }) {
  const { factory, sockets: created } = socketFactory();
  sockets = created;
  return (
    <ThemeProvider>
      <AuthProvider>
        <LocaleProvider serverUrl={null} bundledCatalog={() => EMPTY_CATALOG}>
          <SocketProvider serverUrl="http://h:1" token="t" factory={factory}>
            <NotificationProvider scope={scope} schedule={() => 0}>
              <NotesProbe />
            </NotificationProvider>
          </SocketProvider>
        </LocaleProvider>
      </AuthProvider>
    </ThemeProvider>
  );
}

async function storedKeys(): Promise<string[]> {
  return (await AsyncStorage.getAllKeys()).filter((k) => k.startsWith(NOTIFICATION_HISTORY_PREFIX)).sort();
}

beforeEach(async () => {
  await AsyncStorage.clear();
  probe.notes = null;
  probe.auth = null;
  Object.defineProperty(AppState, 'currentState', { get: () => 'active', configurable: true });
});

describe('notification history per profile and user (F-46)', () => {
  const alice = notificationScope('prf_a', 'usr_alice');
  const bob = notificationScope('prf_a', 'usr_bob');

  it('another user, another server, or signed out sees none of it; it comes back for the same user', async () => {
    const view = await render(<Harness scope={alice} />);
    await act(async () => { sockets[0].open(); });
    await act(async () => { sockets[0].message({ type: 'mission.changed', data: { id: 'msn_1', title: 'Secret project', status: 'Failed' } }); });
    await waitFor(() => expect(probe.notes!.notifications.map((n) => n.missionId)).toEqual(['msn_1']));
    await waitFor(async () => expect(await storedKeys()).toEqual([`${NOTIFICATION_HISTORY_PREFIX}${alice}`]));

    await view.rerender(<Harness scope={null} />);
    await waitFor(() => expect(probe.notes!.notifications).toEqual([]));
    await view.rerender(<Harness scope={bob} />);
    await act(async () => { await Promise.resolve(); });
    expect(probe.notes!.notifications).toEqual([]);
    await view.rerender(<Harness scope={notificationScope('prf_b', 'usr_alice')} />);
    await act(async () => { await Promise.resolve(); });
    expect(probe.notes!.notifications).toEqual([]);

    await view.rerender(<Harness scope={alice} />);
    await waitFor(() => expect(probe.notes!.notifications.map((n) => n.missionId)).toEqual(['msn_1']));
  });

  it('the old global history (shared by every user and server) is removed and never shown', async () => {
    await AsyncStorage.setItem('armada.notifications', JSON.stringify([{ id: 'n1', title: 'Old', message: 'old', severity: 'info', read: false, timestampUtc: '2026-10-07T12:00:00Z', missionId: 'msn_old' }]));
    await render(<Harness scope={alice} />);
    await waitFor(async () => expect(await AsyncStorage.getItem('armada.notifications')).toBeNull());
    expect(probe.notes!.notifications).toEqual([]);
  });

  it('clearNotificationHistory forgets every user of that profile and nothing of other profiles', async () => {
    await AsyncStorage.setItem(`${NOTIFICATION_HISTORY_PREFIX}${alice}`, '[]');
    await AsyncStorage.setItem(`${NOTIFICATION_HISTORY_PREFIX}${bob}`, '[]');
    await AsyncStorage.setItem(`${NOTIFICATION_HISTORY_PREFIX}${notificationScope('prf_b', 'usr_alice')}`, '[]');
    await AsyncStorage.setItem(`${NOTIFICATION_HISTORY_PREFIX}${notificationScope('prf_ab', 'usr_x')}`, '[]');
    await clearNotificationHistory('prf_a');
    expect(await storedKeys()).toEqual([
      `${NOTIFICATION_HISTORY_PREFIX}${notificationScope('prf_ab', 'usr_x')}`,
      `${NOTIFICATION_HISTORY_PREFIX}${notificationScope('prf_b', 'usr_alice')}`,
    ]);
  });

  it('signing out clears the profile history through the auth hooks', async () => {
    const ME = { tenant: { id: 'ten_1', name: 'Default' }, user: { id: 'usr_1', email: 'admin@armada', isAdmin: true, isTenantAdmin: true }, passwordChangeRequired: false } as unknown as WhoAmIResult;
    api.whoami.mockResolvedValue(ME);
    function AuthProbe() {
      const auth = useAuth();
      useEffect(() => { probe.auth = auth; });
      return null;
    }
    function App({ children }: { children?: ReactNode }) {
      return (
        <ThemeProvider>
          <AuthProvider hooks={combineAuthHooks(createNotificationAuthHooks())}>
            <AuthProbe />
            {children}
          </AuthProvider>
        </ThemeProvider>
      );
    }
    await render(<App />);
    await waitFor(() => expect(probe.auth?.status).not.toBe('loading'));
    await act(async () => { await probe.auth!.saveProfile({ name: 'Local', url: 'http://10.0.2.2:44010', kind: 'Direct', biometricUnlock: false }); });
    await act(async () => { await probe.auth!.login('A1', { method: 'token' }); });
    await waitFor(() => expect(probe.auth!.status).toBe('signedIn'));
    const profileId = probe.auth!.activeProfile!.id;
    await AsyncStorage.setItem(`${NOTIFICATION_HISTORY_PREFIX}${notificationScope(profileId, 'usr_1')}`, '[{"id":"n1"}]');
    await act(async () => { await probe.auth!.logout(); });
    expect(probe.auth!.status).toBe('signedOut');
    expect(await storedKeys()).toEqual([]);
  });
});
