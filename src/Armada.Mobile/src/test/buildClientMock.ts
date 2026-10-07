/**
 * `jest.mock('@dashboard/api/client', ...)` factory for the Build screens: every server call of the shared client
 * becomes a jest.fn() resolving to undefined (configure the ones a test needs); the error classes and pure helpers
 * stay real. Kept free of app imports: the factory runs while the mocked module is first required.
 *
 *   jest.mock('@dashboard/api/client', () => require('../test/buildClientMock').buildClientMockFactory());
 */
/** Client exports that are not server calls; they keep their real implementation. */
const PURE = new Set([
  'configureClient', 'getClientBaseUrl', 'ApiError', 'TimeoutError', 'RequestCancelledError', 'isApiStatus', 'apiErrorCode',
  'setAuthToken', 'setOnUnauthorized', 'camelizeKeys',
]);

export function buildClientMockFactory(): Record<string, unknown> {
  const base = (jest.requireActual('./mockClient') as { clientMockFactory: () => Record<string, unknown> }).clientMockFactory();
  const actual = jest.requireActual('@dashboard/api/client') as Record<string, unknown>;
  const mocks: Record<string, unknown> = {};
  for (const [name, value] of Object.entries(actual)) {
    if (typeof value !== 'function' || PURE.has(name) || name in base) continue;
    if (/^[A-Z]/.test(name)) continue;
    mocks[name] = jest.fn(async () => undefined);
  }
  return { ...mocks, ...base };
}
