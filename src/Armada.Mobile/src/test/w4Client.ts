/**
 * `jest.mock('@dashboard/api/client', () => require('../test/w4Client').autoMockClient())`: keeps the client's error
 * classes and pure helpers and replaces every other exported function with a jest.fn() (resolving to undefined until
 * the test configures it), so a screen under test can never reach the network. No app imports here: the mock factory
 * runs while the app modules that import the client are still loading.
 */
const KEEP_REAL = new Set(['ApiError', 'TimeoutError', 'NetworkError', 'RequestCancelledError', 'isApiStatus', 'apiErrorCode', 'apiErrorMessage', 'getClientBaseUrl']);

export function autoMockClient(): Record<string, unknown> {
  const actual = jest.requireActual('@dashboard/api/client') as Record<string, unknown>;
  const out: Record<string, unknown> = {};
  for (const [name, value] of Object.entries(actual)) {
    out[name] = typeof value === 'function' && !KEEP_REAL.has(name) ? jest.fn(async () => undefined) : value;
  }
  // configureClient / setAuthToken / setOnUnauthorized are synchronous in the real client.
  out.configureClient = jest.fn();
  out.setAuthToken = jest.fn();
  out.setOnUnauthorized = jest.fn();
  return out;
}
