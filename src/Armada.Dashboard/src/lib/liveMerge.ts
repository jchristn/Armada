/**
 * Helpers for applying a REST response to state that WebSocket events may already have advanced.
 *
 * A send POST on Planning or Backlog Refinement returns the session as it was when the turn started. With a fast
 * captain the WebSocket events that complete the turn arrive before that response, so applying the response blindly
 * would roll the page back to "Responding". These helpers keep whichever copy is newer by lastUpdateUtc.
 */

interface Timestamped {
  id: string;
  lastUpdateUtc: string;
}

interface Sequenced extends Timestamped {
  sequence: number;
}

interface SessionDetailShape<S extends Timestamped, M extends Sequenced> {
  session: S;
  messages: M[];
  captain?: unknown;
}

const FRACTION_PATTERN = /\.(\d+)/;

/**
 * Compares two ISO-8601 UTC timestamps with sub-millisecond precision (the server emits up to seven fractional
 * digits and trims trailing zeros, so neither Date.parse nor string comparison is exact).
 * Returns a negative number when a is older, positive when newer, 0 when equal or unparseable.
 */
export function compareUtc(a: string | null | undefined, b: string | null | undefined): number {
  if (!a || !b) return 0;
  const aMs = Date.parse(a.replace(FRACTION_PATTERN, ''));
  const bMs = Date.parse(b.replace(FRACTION_PATTERN, ''));
  if (Number.isNaN(aMs) || Number.isNaN(bMs)) return 0;
  if (aMs !== bMs) return aMs - bMs;
  const aFraction = (FRACTION_PATTERN.exec(a)?.[1] ?? '').padEnd(9, '0');
  const bFraction = (FRACTION_PATTERN.exec(b)?.[1] ?? '').padEnd(9, '0');
  if (aFraction === bFraction) return 0;
  return aFraction < bFraction ? -1 : 1;
}

/** Returns current unless incoming is strictly newer. */
export function newerOf<T extends Timestamped>(current: T, incoming: T): T {
  return compareUtc(incoming.lastUpdateUtc, current.lastUpdateUtc) > 0 ? incoming : current;
}

/** Upserts by id, but never replaces an item with an older copy. */
export function upsertIfNewer<T extends Timestamped>(items: T[], incoming: T): T[] {
  const index = items.findIndex((item) => item.id === incoming.id);
  if (index < 0) return [...items, incoming];
  const kept = newerOf(items[index], incoming);
  if (kept === items[index]) return items;
  const next = [...items];
  next[index] = kept;
  return next;
}

/**
 * Merges a session detail from a REST response into the current state: the session and each message keep the newer
 * copy, messages only present on one side are kept, and messages stay ordered by sequence. The captain keeps the
 * current copy, since captain.changed events update its state without a timestamp.
 */
export function mergeSessionDetail<S extends Timestamped, M extends Sequenced, D extends SessionDetailShape<S, M>>(
  current: D | null,
  incoming: D,
): D {
  if (!current || current.session.id !== incoming.session.id) return incoming;

  let messages: M[] = [...current.messages];
  for (const message of incoming.messages) messages = upsertIfNewer(messages, message);
  messages.sort((a, b) => a.sequence - b.sequence);

  return {
    ...incoming,
    session: newerOf(current.session, incoming.session),
    messages,
    captain: current.captain ?? incoming.captain,
  };
}
