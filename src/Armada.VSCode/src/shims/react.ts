/*
 * Stand-in for `react` in the extension bundle. A few shared dashboard modules (@dashboard/lib) export a React hook
 * next to the plain functions the extension uses (lib/cliPermissions useCountdown); the extension never calls those
 * hooks, so esbuild resolves `react` here (esbuild.mjs alias) instead of bundling React into the extension host.
 */

function unavailable(): never {
  throw new Error('React hooks are not available in the Armada extension host.');
}

/** Not available in the extension host. */
export function useState<T>(_initial?: T | (() => T)): [T, (value: T | ((prev: T) => T)) => void] {
  return unavailable();
}

/** Not available in the extension host. */
export function useEffect(_effect: () => void | (() => void), _deps?: unknown[]): void {
  unavailable();
}
