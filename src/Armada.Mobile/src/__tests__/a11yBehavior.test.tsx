/**
 * Screen-reader and system-setting behavior of the UI kit: focus moves to a sheet's or dialog's title when it opens
 * and back to its opener when it closes, sheets fade instead of sliding under Reduce Motion, toasts and error
 * states are announced (queued, without moving focus) and toasts stay longer with a screen reader, and following
 * the system theme honors Increase Contrast in dark mode.
 */
import AsyncStorage from '@react-native-async-storage/async-storage';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import type { ReactNode } from 'react';
import * as RN from 'react-native';
import { AccessibilityInfo } from 'react-native';
import { TOAST_TIMEOUT_MS } from '@dashboard/lib/notificationEvents';
import { ToastHost } from '../components/app/ToastHost';
import { BottomSheet, ConfirmDialog, ErrorState, SelectField } from '../components/ui';
import { LocaleProvider } from '../i18n/LocaleContext';
import { NotificationProvider, SCREEN_READER_TOAST_FACTOR, useNotifications, type NotificationState } from '../notifications/NotificationContext';
import { SocketProvider } from '../socket/SocketContext';
import { socketFactory } from '../test/fakeSocket';
import { resolveTheme } from '../theme/palette';
import { ThemeProvider, useTheme, type ThemeState } from '../theme/ThemeContext';

jest.mock('expo-router', () => ({ useRouter: () => ({ push: jest.fn() }) }));

const info = AccessibilityInfo as jest.Mocked<typeof AccessibilityInfo>;

/** The handler the app registered for a system accessibility setting change. */
function settingListener(event: string): ((on: boolean) => void) | undefined {
  const calls = info.addEventListener.mock.calls as unknown as [string, (on: boolean) => void][];
  return calls.find(([name]) => name === event)?.[1];
}

function Themed({ children }: { children: ReactNode }) {
  return <ThemeProvider>{children}</ThemeProvider>;
}

interface Node { type: unknown; props: Record<string, unknown>; children: (Node | string)[] }

/** The rendered Modals (their host elements carry the Modal's props). */
function modals(): Node[] {
  const out: Node[] = [];
  const walk = (n: Node | string) => {
    if (typeof n === 'string') return;
    if (typeof n.props.onShow === 'function' || n.props.animationType !== undefined) out.push(n);
    n.children.forEach(walk);
  };
  walk(screen.root as unknown as Node);
  return out;
}

/** Fires the Modal's onShow, as the platform does once it has appeared. */
async function showModal(index = 0): Promise<void> {
  const modal = modals()[index];
  await act(async () => { (modal.props.onShow as () => void)(); });
}

/** The host elements focus was sent to, by their text. */
function focusedTexts(): string[] {
  return info.sendAccessibilityEvent.mock.calls
    .filter(([, type]) => type === 'focus')
    .map(([handle]) => {
      const node = handle as unknown as { props?: { children?: unknown; accessibilityLabel?: string; testID?: string } };
      return String(node.props?.testID ?? node.props?.accessibilityLabel ?? node.props?.children ?? '');
    });
}

beforeEach(async () => {
  await AsyncStorage.clear();
  jest.clearAllMocks();
  info.isReduceMotionEnabled.mockResolvedValue(false);
  info.isScreenReaderEnabled.mockResolvedValue(false);
  info.isDarkerSystemColorsEnabled.mockResolvedValue(false);
});

describe('focus in sheets and dialogs', () => {
  it('a bottom sheet moves screen-reader focus to its title when it has appeared', async () => {
    await render(<Themed><BottomSheet open title="Filters" onClose={() => undefined} closeLabel="Close"><></></BottomSheet></Themed>);
    await showModal();
    expect(focusedTexts()).toEqual(['Filters']);
  });

  it('a confirm dialog moves focus to its title', async () => {
    await render(<Themed><ConfirmDialog open title="Delete vessel?" message="This cannot be undone." confirmLabel="Delete" cancelLabel="Cancel" onConfirm={() => undefined} onCancel={() => undefined} /></Themed>);
    await showModal();
    expect(focusedTexts()).toEqual(['Delete vessel?']);
  });

  it('a select field returns focus to itself when its sheet closes', async () => {
    const onChange = jest.fn();
    await render(<Themed>
      <SelectField testID="status" label="Status" value="" options={[{ value: 'a', label: 'Active' }]} onChange={onChange} placeholder="All" closeLabel="Close" />
    </Themed>);
    await fireEvent.press(screen.getByTestId('status'));
    await showModal();
    await fireEvent.press(screen.getByTestId('status-option-a'));
    expect(onChange).toHaveBeenCalledWith('a');
    await waitFor(() => expect(focusedTexts()).toEqual(['Status', 'status']));
  });
});

describe('reduce motion', () => {
  it('sheets slide normally and fade when the system asks for reduced motion', async () => {
    const first = await render(<Themed><BottomSheet open title="A" onClose={() => undefined} closeLabel="Close"><></></BottomSheet></Themed>);
    await waitFor(() => expect(info.isReduceMotionEnabled).toHaveBeenCalled());
    expect(modals()[0].props.animationType).toBe('slide');
    await first.unmount();

    info.isReduceMotionEnabled.mockResolvedValue(true);
    await render(<Themed><BottomSheet open title="A" onClose={() => undefined} closeLabel="Close"><></></BottomSheet></Themed>);
    await waitFor(() => expect(modals()[0].props.animationType).toBe('fade'));
  });

  it('follows the setting when it changes while the sheet is mounted', async () => {
    await render(<Themed><BottomSheet open title="A" onClose={() => undefined} closeLabel="Close"><></></BottomSheet></Themed>);
    const listener = settingListener('reduceMotionChanged');
    expect(listener).toBeDefined();
    await act(async () => { listener?.(true); });
    expect(modals()[0].props.animationType).toBe('fade');
  });
});

describe('announcements', () => {
  let notifications: NotificationState | null = null;
  function Probe() {
    notifications = useNotifications();
    return null;
  }

  async function renderToasts(schedule: (fn: () => void, ms: number) => unknown) {
    const { factory } = socketFactory();
    await render(
      <ThemeProvider>
        <LocaleProvider serverUrl={null} bundledCatalog={() => ({ defaultLocale: 'en', supportedLocales: [], locales: {} })}>
          <SocketProvider serverUrl="http://h:1" token="t" factory={factory}>
            <NotificationProvider schedule={schedule}>
              <Probe />
              <ToastHost />
            </NotificationProvider>
          </SocketProvider>
        </LocaleProvider>
      </ThemeProvider>,
    );
  }

  it('a new toast is spoken once, queued behind current speech', async () => {
    await renderToasts(() => 0);
    await act(async () => { notifications!.pushToast('error', 'Mission "Fix login" - Failed'); });
    await act(async () => { notifications!.pushToast('success', 'Voyage "Batch" - Complete'); });
    expect(info.announceForAccessibilityWithOptions.mock.calls).toEqual([
      ['Mission "Fix login" - Failed', { queue: true }],
      ['Voyage "Batch" - Complete', { queue: true }],
    ]);
  });

  it('toasts stay longer while a screen reader is running', async () => {
    const schedule = jest.fn(() => 0);
    await renderToasts(schedule);
    await act(async () => { notifications!.pushToast('info', 'one'); });
    expect(schedule).toHaveBeenLastCalledWith(expect.any(Function), TOAST_TIMEOUT_MS);

    info.isScreenReaderEnabled.mockResolvedValue(true);
    const listener = settingListener('screenReaderChanged');
    await act(async () => { listener?.(true); });
    await act(async () => { notifications!.pushToast('info', 'two'); });
    expect(schedule).toHaveBeenLastCalledWith(expect.any(Function), TOAST_TIMEOUT_MS * SCREEN_READER_TOAST_FACTOR);
  });

  it('an error state is announced when it appears', async () => {
    await render(<Themed><ErrorState title="Something went wrong" message="Network request failed" /></Themed>);
    expect(info.announceForAccessibilityWithOptions).toHaveBeenCalledWith('Something went wrong. Network request failed', { queue: true });
  });
});

describe('increased contrast', () => {
  it('following a dark system with Increase Contrast gives the high-contrast theme; light stays light', () => {
    expect(resolveTheme('system', 'dark', true)).toBe('highContrast');
    expect(resolveTheme('system', 'dark', false)).toBe('dark');
    expect(resolveTheme('system', 'light', true)).toBe('light');
    expect(resolveTheme('dark', 'dark', true)).toBe('dark');
  });

  it('the provider reads the system setting', async () => {
    const scheme = jest.spyOn(RN, 'useColorScheme').mockReturnValue('dark');
    info.isDarkerSystemColorsEnabled.mockResolvedValue(true);
    let theme: ThemeState | null = null;
    function Probe() {
      theme = useTheme();
      return null;
    }
    await render(<ThemeProvider><Probe /></ThemeProvider>);
    await waitFor(() => expect(theme!.name).toBe('highContrast'));
    scheme.mockRestore();
  });
});
