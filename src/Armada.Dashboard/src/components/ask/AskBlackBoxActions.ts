import { createContext, useContext } from 'react';

/** What a black box card can do in the open conversation. */
export interface AskBlackBoxActionsValue {
  /** Create a Pending proposal for a black box action (the user approves it on its confirm card). */
  propose: (toolName: string, args: Record<string, unknown>) => Promise<void>;
  /** Send a follow-up question to the captain. */
  ask: (prompt: string) => void;
  /** A captain can answer (false hides message follow-ups). */
  canAsk: boolean;
  /** An action is being submitted. */
  busy: boolean;
}

/** Provided by the Ask Armada page; a card rendered without it shows no action buttons. */
export const AskBlackBoxActionsContext = createContext<AskBlackBoxActionsValue | null>(null);

export function useAskBlackBoxActions(): AskBlackBoxActionsValue | null {
  return useContext(AskBlackBoxActionsContext);
}
