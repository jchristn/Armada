/** Router state that opens Ask Armada on a new conversation with text already in the message box. */
export interface AskNavigationState {
  /** Text placed in the message box (not sent). */
  draft?: string;
}

/**
 * Query parameter that opens Ask Armada on a new conversation with text already in the message box, for links that
 * cannot pass router state (the VS Code extension, a copied URL): `/ask?prompt=<urlencoded text>`. The page clears it
 * from the URL once read.
 */
export const ASK_PREFILL_QUERY_PARAM = 'prompt';

/** The Ask Armada path that pre-fills `text` in a new conversation (relative to the dashboard's router base). */
export function askPrefillPath(text: string): string {
  return `/ask?${ASK_PREFILL_QUERY_PARAM}=${encodeURIComponent(text)}`;
}

/** The pre-filled text in a location search string, and the search string without it; null when absent or blank. */
export function readAskPrefill(search: string): { text: string; rest: string } | null {
  const params = new URLSearchParams(search);
  const text = params.get(ASK_PREFILL_QUERY_PARAM);
  if (text === null || !text.trim()) return null;
  params.delete(ASK_PREFILL_QUERY_PARAM);
  const rest = params.toString();
  return { text, rest: rest ? `?${rest}` : '' };
}
