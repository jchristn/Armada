import { act, render, screen } from '@testing-library/react-native';
import { AppState } from 'react-native';
import { PrivacyOverlay, hidesContent } from '../components/app/PrivacyOverlay';
import { ThemeProvider } from '../theme/ThemeContext';

type Handler = (state: string) => void;
let handlers: Handler[] = [];
let currentState = 'active';

beforeEach(() => {
  handlers = [];
  currentState = 'active';
  jest.spyOn(AppState, 'addEventListener').mockImplementation(((_t: string, h: Handler) => {
    handlers.push(h);
    return { remove: () => { handlers = handlers.filter((x) => x !== h); } };
  }) as unknown as typeof AppState.addEventListener);
  Object.defineProperty(AppState, 'currentState', { get: () => currentState, configurable: true });
});

afterEach(() => jest.restoreAllMocks());

function setAppState(next: string) {
  currentState = next;
  handlers.forEach((h) => h(next));
}

describe('app switcher privacy (F-44)', () => {
  it('covers the app whenever it leaves the foreground and uncovers it on return', async () => {
    await render(<ThemeProvider><PrivacyOverlay /></ThemeProvider>);
    expect(screen.queryByTestId('privacy-overlay')).toBeNull();
    await act(async () => { setAppState('inactive'); });
    expect(screen.getByTestId('privacy-overlay')).toBeTruthy();
    await act(async () => { setAppState('background'); });
    expect(screen.getByTestId('privacy-overlay')).toBeTruthy();
    await act(async () => { setAppState('active'); });
    expect(screen.queryByTestId('privacy-overlay')).toBeNull();
  });

  it('starts covered when mounted in the background (a launch for a notification or a background fetch)', async () => {
    currentState = 'background';
    await render(<ThemeProvider><PrivacyOverlay /></ThemeProvider>);
    expect(screen.getByTestId('privacy-overlay')).toBeTruthy();
  });

  it('only the foreground shows content', () => {
    expect(hidesContent('active')).toBe(false);
    expect(hidesContent('inactive')).toBe(true);
    expect(hidesContent('background')).toBe(true);
  });
});
