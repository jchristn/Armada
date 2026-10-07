/**
 * expo-router mock for screen tests that render a screen outside a navigator:
 *
 *   jest.mock('expo-router', () => require('../test/routerMock').routerMockFactory());
 *   import { mockRouter, setMockParams } from '../test/routerMock';
 *
 * useRouter() returns `mockRouter` (jest.fn() methods), useLocalSearchParams() returns what setMockParams set
 * (setParams merges into it, as the real router does), and Stack.Screen renders nothing.
 */
let params: Record<string, string> = {};

export const mockRouter = {
  push: jest.fn(),
  replace: jest.fn(),
  navigate: jest.fn(),
  back: jest.fn(),
  dismiss: jest.fn(),
  canGoBack: jest.fn(() => true),
  setParams: jest.fn((next: Record<string, string>) => { params = { ...params, ...next }; }),
};

export function setMockParams(next: Record<string, string>): void {
  params = { ...next };
}

export function routerMockFactory(): Record<string, unknown> {
  const actual = jest.requireActual('expo-router') as Record<string, unknown>;
  const Screen = () => null;
  return {
    ...actual,
    useRouter: () => mockRouter,
    useLocalSearchParams: () => params,
    useGlobalSearchParams: () => params,
    useFocusEffect: () => undefined,
    Stack: Object.assign(() => null, { Screen }),
  };
}
