/** An event payload for display: pretty-printed JSON when it parses, else as stored; null when there is none. */
export function formatEventPayload(payload: unknown): string | null {
  if (!payload) return null;
  try {
    const parsed: unknown = JSON.parse(typeof payload === 'object' ? JSON.stringify(payload) : String(payload));
    return JSON.stringify(parsed, null, 2);
  } catch {
    return String(payload);
  }
}
