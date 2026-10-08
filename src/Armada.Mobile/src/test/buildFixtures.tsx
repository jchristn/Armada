/**
 * Test harness for the Build screens (W3: vessels, fleets, captains, fleet actions, planning, objectives). Mock the
 * client with buildClientMock.ts (every server call a jest.fn()):
 *
 *   jest.mock('@dashboard/api/client', () => require('../test/buildClientMock').buildClientMockFactory());
 *
 * `BuildProviders` nests the app's providers with a fake
 * socket (`buildSockets()` returns the sockets it opened, so a test can deliver live events) and `page()` builds an
 * EnumerationResult.
 */
import { useState, type ReactNode } from 'react';
import type { EnumerationResult, WebSocketMessage } from '@dashboard/types/models';
import { AuthProvider } from '../auth/AuthContext';
import { LocaleProvider } from '../i18n/LocaleContext';
import { NotificationProvider } from '../notifications/NotificationContext';
import { SocketProvider } from '../socket/SocketContext';
import { ThemeProvider } from '../theme/ThemeContext';
import { socketFactory, type FakeSocket } from './fakeSocket';

/** An enumeration page of `objects` (one page holding everything unless totals are given). */
export function page<T>(objects: T[], over: Partial<EnumerationResult<T>> = {}): EnumerationResult<T> {
  return { success: true, pageNumber: 1, pageSize: Math.max(objects.length, 25), totalPages: 1, totalRecords: objects.length, objects, totalMs: 1, ...over };
}

let sockets: FakeSocket[] = [];

/** The sockets the last rendered BuildProviders opened. */
export function buildSockets(): FakeSocket[] {
  return sockets;
}

/** Deliver a live event on every open test socket. */
export function deliver(msg: WebSocketMessage): void {
  for (const s of sockets) s.message(msg);
}

export function BuildProviders({ children }: { children: ReactNode }) {
  // One factory per mounted tree: re-renders must not reopen the socket.
  const [{ factory }] = useState(() => {
    const made = socketFactory();
    sockets = made.sockets;
    return made;
  });
  return (
    <ThemeProvider>
      <AuthProvider>
        <LocaleProvider serverUrl={null} bundledCatalog={() => ({ defaultLocale: 'en', supportedLocales: [], locales: {} })}>
          <SocketProvider serverUrl="http://h:1" token="tok" factory={factory}>
            <NotificationProvider>{children}</NotificationProvider>
          </SocketProvider>
        </LocaleProvider>
      </AuthProvider>
    </ThemeProvider>
  );
}
