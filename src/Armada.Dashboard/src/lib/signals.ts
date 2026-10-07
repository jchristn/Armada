/** Pure logic of the Signals pages, shared by the dashboard and the mobile app. */
import type { SendSignalRequest } from '../types/models';

/** Signal types offered by the filters and the Send Signal form, in display order. */
export const SIGNAL_TYPES = ['Nudge', 'Mail', 'Assignment', 'Progress', 'Completion', 'Error'] as const;

export interface SignalListFilterState {
  type: string;
  toCaptainId: string;
  unreadOnly: boolean;
  userId: string;
}

/** The server-side filters for listSignals (empty values are omitted). */
export function signalListFilters(state: SignalListFilterState): Record<string, string> {
  const filters: Record<string, string> = {};
  if (state.type) filters.type = state.type;
  if (state.toCaptainId) filters.toCaptainId = state.toCaptainId;
  if (state.unreadOnly) filters.unreadOnly = 'true';
  if (state.userId) filters.userId = state.userId;
  return filters;
}

/** The sendSignal request for the form (empty payload and recipient are left out; recipient '' is the Admiral). */
export function buildSendSignalRequest(form: SendSignalRequest): SendSignalRequest {
  return {
    type: form.type || 'Nudge',
    payload: form.payload || undefined,
    toCaptainId: form.toCaptainId || undefined,
  };
}

/** A signal payload for display: pretty-printed when it is JSON, else as sent; `emptyText` when there is none. */
export function formatSignalPayload(payload: string | null | undefined, emptyText: string): { isJson: boolean; formatted: string } {
  if (!payload) return { isJson: false, formatted: emptyText };
  try {
    const parsed: unknown = JSON.parse(payload);
    return { isJson: true, formatted: JSON.stringify(parsed, null, 2) };
  } catch {
    return { isJson: false, formatted: payload };
  }
}
