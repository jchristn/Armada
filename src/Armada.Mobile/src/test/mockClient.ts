/**
 * Factory for `jest.mock('@dashboard/api/client', ...)`, mirroring the dashboard's vitest mocks: the real error
 * classes and pure helpers stay, every server call becomes a jest.fn() the test configures.
 *
 *   jest.mock('@dashboard/api/client', () => require('../test/mockClient').clientMockFactory());
 */
export function clientMockFactory(): Record<string, unknown> {
  const actual = jest.requireActual('@dashboard/api/client') as Record<string, unknown>;
  let authToken: string | null = null;
  let onUnauthorized: (() => void) | null = null;
  return {
    ...actual,
    configureClient: jest.fn(),
    setAuthToken: jest.fn((token: string | null) => { authToken = token; }),
    setOnUnauthorized: jest.fn((cb: () => void) => { onUnauthorized = cb; }),
    __getAuthToken: () => authToken,
    __fireUnauthorized: () => onUnauthorized?.(),
    whoami: jest.fn(),
    authenticate: jest.fn(),
    lookupTenants: jest.fn(),
    changePassword: jest.fn(),
    getInbox: jest.fn(async () => []),
    getHealth: jest.fn(),
  };
}
