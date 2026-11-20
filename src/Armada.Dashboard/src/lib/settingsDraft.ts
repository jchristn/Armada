/**
 * Merges a new server copy into a settings form that is still being edited.
 *
 * `base` is the server copy the form started from. `draft` is what the form shows. `next` is a reload or a
 * save response. A field the operator has not edited adopts `next`. A field the operator has edited keeps
 * the draft. Plain objects merge one field at a time. Any other value is kept or replaced as a whole.
 */

type Plain = Record<string, unknown>;

function isPlainObject(value: unknown): value is Plain {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

/** Structural equality for JSON-shaped values. */
function sameValue(left: unknown, right: unknown): boolean {
  if (left === right) return true;
  if (typeof left !== 'object' || typeof right !== 'object' || left === null || right === null) return false;
  return JSON.stringify(left) === JSON.stringify(right);
}

/**
 * Merges `next` into `draft`. An edited field keeps the draft. An untouched field takes `next`.
 */
export function mergeDraft<T>(base: T, draft: T, next: T): T {
  if (isPlainObject(base) && isPlainObject(draft) && isPlainObject(next)) {
    const merged: Plain = { ...next };
    for (const key of Object.keys(draft)) {
      if (!(key in next)) {
        merged[key] = draft[key];
        continue;
      }
      merged[key] = mergeDraft(base[key], draft[key], next[key]);
    }
    return merged as T;
  }
  return sameValue(draft, base) ? next : draft;
}

/**
 * Base for a save response. Fields in `sent` count as saved, so the response replaces them. Fields left
 * out of `sent` stay drafts when the operator has edited them.
 */
export function baseAfterSave<T extends object>(base: T, sent: Partial<T>): T {
  return { ...base, ...sent };
}
