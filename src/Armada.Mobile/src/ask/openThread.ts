/**
 * The Ask conversation currently on screen, readable outside the Ask tab (the approval toasts skip items that
 * belong to it, since its confirm cards are already in front of the user). Set by AskProvider.
 */
let openThreadId: string | null = null;

export function setOpenAskThread(id: string | null): void {
  openThreadId = id;
}

export function getOpenAskThread(): string | null {
  return openThreadId;
}
