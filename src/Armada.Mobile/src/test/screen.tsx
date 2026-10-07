import { act, render, type RenderOptions } from '@testing-library/react-native';
import type { ReactElement, ReactNode } from 'react';
import type { I18nCatalog } from '@dashboard/i18n/catalog';
import { AuthProvider } from '../auth/AuthContext';
import { LocaleProvider } from '../i18n/LocaleContext';
import { NotificationProvider } from '../notifications/NotificationContext';
import { SocketProvider } from '../socket/SocketContext';
import { ThemeProvider } from '../theme/ThemeContext';
import { socketFactory, type FakeSocket } from './fakeSocket';

const EMPTY_CATALOG: I18nCatalog = { defaultLocale: 'en', supportedLocales: [], locales: {} };

/**
 * Renders a screen with the providers a signed-in screen needs (theme, auth, locale, a fake socket that is already
 * open, notifications). Returns the fake socket so tests can push live events.
 */
export async function renderScreen(ui: ReactElement, options?: Omit<RenderOptions, 'wrapper'>): Promise<{ socket: FakeSocket }> {
  const { sockets, factory } = socketFactory();
  function Providers({ children }: { children: ReactNode }) {
    return (
      <ThemeProvider>
        <AuthProvider>
          <LocaleProvider serverUrl={null} bundledCatalog={() => EMPTY_CATALOG}>
            <SocketProvider serverUrl="http://admiral.test:7890" token="token" factory={factory}>
              <NotificationProvider schedule={() => 0}>{children}</NotificationProvider>
            </SocketProvider>
          </LocaleProvider>
        </AuthProvider>
      </ThemeProvider>
    );
  }
  await render(ui, { wrapper: Providers, ...options });
  await act(async () => { sockets[0]?.open(); });
  return { socket: sockets[0] };
}
