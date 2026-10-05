import { expect } from 'vitest';

interface CallRecorder<TArgs extends unknown[]> {
  mock: { calls: TArgs[] };
}

/**
 * The arguments of a mock's only call. Fails unless the mock was called exactly once, so a test never reads
 * "call number N" when other calls could sit at that index. Test-only.
 */
export function onlyCallArgs<TArgs extends unknown[]>(fn: CallRecorder<TArgs>): TArgs {
  expect(fn.mock.calls).toHaveLength(1);
  return fn.mock.calls[0];
}

/**
 * The arguments of every call whose arguments satisfy a predicate, in call order. Test-only.
 */
export function callsMatching<TArgs extends unknown[]>(fn: CallRecorder<TArgs>, predicate: (args: TArgs) => boolean): TArgs[] {
  return fn.mock.calls.filter(predicate);
}
