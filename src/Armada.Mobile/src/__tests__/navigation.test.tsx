import fs from 'fs';
import path from 'path';
import { act, screen, userEvent } from '@testing-library/react-native';
import { Slot } from 'expo-router';
import { renderRouter } from 'expo-router/testing-library';
import type { ReactNode } from 'react';
import * as RN from 'react-native';
import { askArmadaModelItem, dashboardModelItem, navModelSections } from '@dashboard/lib/navModel';
import AppTabsLayout from '../app/(app)/_layout';
import Index from '../app/index';
import { notePendingLink, setSignedInForLinks, takePendingLink } from '../navigation/pendingLink';
import { AuthProvider } from '../auth/AuthContext';
import { LocaleProvider } from '../i18n/LocaleContext';
import { appPathFromLink, isRootLink } from '../navigation/deepLinks';
import { redirectSystemPath } from '../app/+native-intent';
import { DASHBOARD_ROUTES } from '../navigation/dashboardRoutes.generated';
import { NAV_SECTIONS, activeNavKey, allNavItems, sectionsForTab } from '../navigation/navItems';
import { matchRoute } from '../navigation/routeMatch';
import { TabStack } from '../navigation/TabStack';
import { layoutFor, TABLET_MIN_WIDTH } from '../navigation/useLayout';
import { ApprovalsProvider } from '../notifications/ApprovalsContext';
import { NotificationProvider } from '../notifications/NotificationContext';
import { RoutePlaceholder } from '../screens/RoutePlaceholder';
import { SocketProvider } from '../socket/SocketContext';
import { ThemeProvider } from '../theme/ThemeContext';

jest.mock('@dashboard/api/client', () => require('../test/mockClient').clientMockFactory());

// The window size drives the phone/tablet switch; tests set it per case.
const mockWindow = { width: 390, height: 844 };
jest.mock('react-native/Libraries/Utilities/useWindowDimensions', () => ({
  __esModule: true,
  default: () => ({ width: mockWindow.width, height: mockWindow.height, scale: 2, fontScale: 1 }),
}));

const APP_DIR = path.join(__dirname, '..', 'app');

describe('adaptive layout switch', () => {
  it('switches to the tablet layout at 768 dp', () => {
    expect(layoutFor(767, 1024).isTablet).toBe(false);
    expect(layoutFor(TABLET_MIN_WIDTH, 1024).isTablet).toBe(true);
    expect(layoutFor(1180, 820).landscape).toBe(true);
  });
});

describe('navigation model mirrors the dashboard', () => {
  it('has the dashboard sections, in order, with the same items', () => {
    expect(NAV_SECTIONS.map((s) => s.label)).toEqual(navModelSections.map((s) => s.label));
    for (const [i, section] of navModelSections.entries()) {
      expect(NAV_SECTIONS[i].items.map((x) => x.label)).toEqual(section.items.filter((x) => !x.hidden).map((x) => x.label));
    }
  });

  it('phones split the sections between Work and More without losing any', () => {
    const split = [...sectionsForTab('work'), ...sectionsForTab('more')].map((s) => s.key).sort();
    expect(split).toEqual(NAV_SECTIONS.map((s) => s.key).sort());
  });

  it('every nav destination resolves to a screen', () => {
    for (const item of allNavItems()) {
      const known = matchRoute(item.to) || ['/approvals', '/notification-center', '/preferences', '/profiles'].includes(item.to);
      expect([item.to, !!known]).toEqual([item.to, true]);
    }
    expect(allNavItems().map((i) => i.to)).toEqual(expect.arrayContaining([askArmadaModelItem.to, '/home']));
    expect(dashboardModelItem.to).toBe('/');
  });

  it('highlights the closest nav item for a path', () => {
    expect(activeNavKey('/missions/msn_1')).toBe('/missions');
    expect(activeNavKey('/ask/thr_1')).toBe('ask');
    expect(activeNavKey('/nowhere')).toBeNull();
  });
});

describe('deep links and dashboard paths', () => {
  it.each([
    ['armada://missions/msn_1', '/missions/msn_1'],
    ['armada:///missions/msn_1?tab=log', '/missions/msn_1?tab=log'],
    ['https://admiral.example:7890/dashboard/vessels/vsl_1/history', '/vessels/vsl_1/history'],
    ['https://admiral.example/dashboard', '/home'],
    ['armada://', '/home'],
    ['/dashboard/ask/thr_9', '/ask/thr_9'],
    ['missions/', '/missions'],
  ])('%s opens %s', (link, expected) => {
    expect(appPathFromLink(link)).toBe(expected);
  });

  it.each(['javascript:alert(1)', 'file:///etc/passwd', 'armada://missions/../server', '', 'armada://a\u0000b'])('rejects %j', (link) => {
    expect(appPathFromLink(link)).toBeNull();
  });

  it('a link that names no page opens the app without becoming a pending link', () => {
    setSignedInForLinks(false);
    expect(isRootLink('armada://')).toBe(true);
    expect(isRootLink('/')).toBe(true);
    expect(isRootLink('armada://missions')).toBe(false);
    expect(redirectSystemPath({ path: '/', initial: true })).toBe('/');
    expect(redirectSystemPath({ path: 'armada://', initial: true })).toBe('/');
    expect(takePendingLink()).toBeNull();
  });

  it('links to unknown screens are ignored; links to real pages are remembered for after sign-in', () => {
    setSignedInForLinks(false);
    expect(redirectSystemPath({ path: 'armada://expo-development-client/?url=x', initial: true })).toBeNull();
    expect(takePendingLink()).toBeNull();
    expect(redirectSystemPath({ path: 'armada://missions/msn_1', initial: true })).toBe('/missions/msn_1');
    expect(takePendingLink()).toBe('/missions/msn_1');
    expect(redirectSystemPath({ path: 'armada://profiles', initial: false })).toBe('/profiles');
    takePendingLink();
  });

  it('static segments win over parameters', () => {
    expect(matchRoute('/voyages/create')?.route.pattern).toBe('/voyages/create');
    expect(matchRoute('/voyages/vyg_1')?.route.pattern).toBe('/voyages/:id');
    expect(matchRoute('/releases/new')?.route.pattern).toBe('/releases/new');
    expect(matchRoute('/missions/msn%201')?.params).toEqual({ id: 'msn 1' });
  });

  it('every dashboard route has a screen file, and redirects resolve to a route', () => {
    for (const route of DASHBOARD_ROUTES) {
      expect([route.pattern, fs.existsSync(path.join(APP_DIR, route.file))]).toEqual([route.pattern, true]);
      if (route.redirect) {
        const target = appPathFromLink(route.redirect);
        expect([route.pattern, !!target && !!matchRoute(target)]).toEqual([route.pattern, true]);
      }
    }
  });
});

function Providers({ children }: { children: ReactNode }) {
  return (
    <ThemeProvider>
      <AuthProvider>
        <LocaleProvider serverUrl={null}>
          <SocketProvider serverUrl={null} token={null}>
            <NotificationProvider>
              <ApprovalsProvider enabled={false}>{children}</ApprovalsProvider>
            </NotificationProvider>
          </SocketProvider>
        </LocaleProvider>
      </AuthProvider>
    </ThemeProvider>
  );
}

const ROUTES = {
  _layout: () => <Providers><Slot /></Providers>,
  index: Index,
  '(app)/_layout': AppTabsLayout,
  '(app)/(ask)/_layout': TabStack,
  '(app)/(ask)/ask/index': () => <RN.Text>Ask screen</RN.Text>,
  '(app)/(approvals)/_layout': TabStack,
  '(app)/(approvals)/approvals': () => <RN.Text>Approvals screen</RN.Text>,
  '(app)/(work)/_layout': TabStack,
  '(app)/(work)/home': () => <RN.Text>Work screen</RN.Text>,
  '(app)/(work)/missions/[id]': () => <RoutePlaceholder pattern="/missions/:id" />,
  '(app)/(more)/_layout': TabStack,
  '(app)/(more)/more': () => <RN.Text>More screen</RN.Text>,
};

function setWindow(width: number, height: number) {
  mockWindow.width = width;
  mockWindow.height = height;
}

async function renderApp(initialUrl: string) {
  // RNTL 14 renders asynchronously: renderRouter returns the pending render with the router helpers attached.
  const result = renderRouter(ROUTES, { initialUrl });
  await result;
  await act(async () => { jest.runOnlyPendingTimers(); });
  return { getPathname: () => result.getPathname() };
}

describe('adaptive shell', () => {
  afterEach(() => {
    jest.restoreAllMocks();
    jest.useRealTimers();
  });

  it('phones get bottom tabs and no sidebar', async () => {
    setWindow(390, 844);
    await renderApp('/ask');
    expect(screen.getByTestId('tab-ask')).toBeTruthy();
    expect(screen.getByTestId('tab-approvals')).toBeTruthy();
    expect(screen.getByTestId('tab-work')).toBeTruthy();
    expect(screen.getByTestId('tab-more')).toBeTruthy();
    expect(screen.queryByTestId('sidebar')).toBeNull();
  });

  it('tablets get the dashboard sidebar instead of the tab bar', async () => {
    setWindow(1024, 1366);
    await renderApp('/ask');
    expect(screen.getByTestId('sidebar')).toBeTruthy();
    expect(screen.queryByTestId('tab-ask')).toBeNull();
    for (const section of NAV_SECTIONS) expect(screen.getByText(section.label)).toBeTruthy();
  });

  it('a deep link opens the matching screen with its parameters', async () => {
    setWindow(390, 844);
    const app = await renderApp('/missions/msn_42');
    expect(app.getPathname()).toBe('/missions/msn_42');
    expect(screen.getByTestId('route-placeholder-title')).toHaveTextContent('Mission');
    expect(screen.getByTestId('route-placeholder-workstream')).toHaveTextContent('Coming in W2.2');
    expect(screen.getByText('msn_42')).toBeTruthy();
  });

  it('the app opens into Ask', async () => {
    setWindow(390, 844);
    const app = await renderApp('/');
    expect(app.getPathname()).toBe('/ask');
  });

  it('a link that arrived before sign-in opens once the session is ready', async () => {
    setWindow(390, 844);
    setSignedInForLinks(false);
    notePendingLink('/missions/msn_7');
    const app = await renderApp('/');
    expect(app.getPathname()).toBe('/missions/msn_7');
    // Consumed: the next launch opens Ask again.
    const again = await renderApp('/');
    expect(again.getPathname()).toBe('/ask');
  });

  it('the sidebar navigates between tabs', async () => {
    setWindow(1024, 1366);
    const app = await renderApp('/ask');
    const user = userEvent.setup({ advanceTimers: jest.advanceTimersByTime });
    await user.press(screen.getByTestId('sidebar-/approvals'));
    await act(async () => { jest.runOnlyPendingTimers(); });
    expect(app.getPathname()).toBe('/approvals');
  });
});
