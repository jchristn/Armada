/**
 * Test helpers for the W4 screens (Delivery, Configuration, Activity, System).
 *
 *   jest.mock('@dashboard/api/client', () => require('../test/w4Client').autoMockClient());
 *
 * renderW4() mounts a screen (or an expo-router route table) inside the app's providers, signed in with the
 * given role, with a fake WebSocket the test can push events through.
 */
/* eslint-disable react-hooks/immutability -- the harness records provider values for the test, like the W1 probes */
import AsyncStorage from '@react-native-async-storage/async-storage';
import { act, render, waitFor } from '@testing-library/react-native';
import { Slot } from 'expo-router';
import { renderRouter } from 'expo-router/testing-library';
import type { ComponentType, ReactElement, ReactNode } from 'react';
import { AppState } from 'react-native';
import type { WhoAmIResult } from '@dashboard/types/models';
import { AuthProvider, useAuth, type AuthState } from '../auth/AuthContext';
import { LocaleProvider } from '../i18n/LocaleContext';
import { NotificationProvider, useNotifications, type NotificationState } from '../notifications/NotificationContext';
import { SocketProvider } from '../socket/SocketContext';
import { ThemeProvider } from '../theme/ThemeContext';
import { socketFactory, type FakeSocket } from './fakeSocket';

export type Role = 'admin' | 'tenantAdmin' | 'user';

export function whoamiFor(role: Role): WhoAmIResult {
  return {
    tenant: { id: 'ten_1', name: 'Default' },
    user: { id: 'usr_1', email: 'someone@armada', isAdmin: role === 'admin', isTenantAdmin: role !== 'user', tenantId: 'ten_1' },
    passwordChangeRequired: false,
  } as unknown as WhoAmIResult;
}

/** What a test can reach after rendering: the fake sockets, auth, and notifications (toasts). */
export interface W4Harness {
  sockets: FakeSocket[];
  auth: () => AuthState;
  notifications: () => NotificationState;
}

const harness: { sockets: FakeSocket[]; auth: AuthState | null; notifications: NotificationState | null } = { sockets: [], auth: null, notifications: null };

function Probe() {
  harness.auth = useAuth();
  harness.notifications = useNotifications();
  return null;
}

function Providers({ children }: { children: ReactNode }) {
  const { sockets, factory } = socketFactory();
  harness.sockets = sockets;
  return (
    <ThemeProvider>
      <AuthProvider>
        <LocaleProvider serverUrl={null} bundledCatalog={() => ({ defaultLocale: 'en', supportedLocales: [], locales: {} })}>
          <SocketProvider serverUrl="http://h:1" token="tok" factory={factory}>
            <NotificationProvider>
              <Probe />
              {children}
            </NotificationProvider>
          </SocketProvider>
        </LocaleProvider>
      </AuthProvider>
    </ThemeProvider>
  );
}

/** Resets storage and app state; call from beforeEach. */
export async function resetW4(): Promise<void> {
  // renderRouter switches to fake timers; start every test from real ones.
  jest.useRealTimers();
  await AsyncStorage.clear();
  Object.defineProperty(AppState, 'currentState', { get: () => 'active', configurable: true });
}

async function signIn(role: Role, whoami: jest.Mock): Promise<void> {
  whoami.mockResolvedValue(whoamiFor(role));
  await waitFor(() => expect(harness.auth?.status).not.toBe('loading'));
  await act(async () => {
    await harness.auth!.saveProfile({ name: 'Test', kind: 'Direct', url: 'http://h:1', biometricUnlock: false });
  });
  await act(async () => {
    await harness.auth!.login('tok', { method: 'password', email: 'someone@armada', tenantId: 'ten_1', tenantName: 'Default' });
  });
  await act(async () => { harness.sockets[0]?.open(); });
}

function makeHarness(): W4Harness {
  return { sockets: harness.sockets, auth: () => harness.auth!, notifications: () => harness.notifications! };
}

/** Renders a component inside the providers, signed in as `role`. */
export async function renderW4(ui: ReactElement, role: Role = 'admin'): Promise<W4Harness> {
  const client = jest.requireMock('@dashboard/api/client') as { whoami: jest.Mock };
  harness.auth = null;
  harness.notifications = null;
  // RNTL 14 renders asynchronously.
  await render(<Providers>{ui}</Providers>);
  await signIn(role, client.whoami);
  return makeHarness();
}

/** Renders an expo-router route table (screens keyed by route file) at `url`, signed in as `role`. */
export async function renderW4Routes(
  routes: Record<string, ComponentType>,
  url: string,
  role: Role = 'admin',
): Promise<W4Harness & { getPathname: () => string; getSearchParams: () => Record<string, string | string[]> }> {
  const client = jest.requireMock('@dashboard/api/client') as { whoami: jest.Mock };
  harness.auth = null;
  harness.notifications = null;
  // RNTL 14 renders asynchronously: renderRouter returns the pending render with the router helpers attached.
  const result = renderRouter({ _layout: () => <Providers><Slot /></Providers>, ...routes }, { initialUrl: url });
  await result;
  // renderRouter switches to fake timers; the screens under test use real timers (debounced live reloads).
  jest.useRealTimers();
  await signIn(role, client.whoami);
  return {
    ...makeHarness(),
    getPathname: () => result.getPathname(),
    getSearchParams: () => result.getSearchParams(),
  };
}

/** Pushes a WebSocket event to the screen. */
export async function emit(h: W4Harness, message: Record<string, unknown>): Promise<void> {
  await act(async () => { h.sockets[h.sockets.length - 1]?.message(message); });
}

/** A paged enumeration result as the server returns it. */
export function page<T>(objects: T[], extra: Record<string, unknown> = {}) {
  return { objects, totalRecords: objects.length, totalPages: 1, pageNumber: 1, pageSize: objects.length || 25, success: true, ...extra };
}
