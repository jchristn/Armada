import { NetworkError, TimeoutError, isApiStatus } from '@dashboard/api/client';

/** The message to show for a failed call: the server's error text when there is one, else the fallback. */
export function errorMessage(error: unknown, fallback: string): string {
  if (error instanceof Error && error.message) return error.message;
  if (typeof error === 'string' && error) return error;
  return fallback;
}

/**
 * True when a call failed without an answer from the server: no response arrived (NetworkError: offline, DNS,
 * connection refused) or the request timed out (TimeoutError). Decided by error class, never by message text.
 */
export function isConnectionError(error: unknown): boolean {
  return error instanceof NetworkError || error instanceof TimeoutError;
}

/** Title and message for a detail screen whose record did not load. */
export interface DetailLoadError {
  title: string;
  message: string | null;
}

/**
 * Title and message for a detail screen whose record did not load, from what the load threw: a connection error
 * when the server was not reached, `notFound` only for a 404 (or when nothing was thrown, so the record is simply
 * absent), and `failed` with the server's message otherwise. `notFound` and `failed` are already translated.
 */
export function detailLoadError(
  t: (text: string) => string,
  failure: unknown,
  message: string | null,
  notFound: string,
  failed: string,
): DetailLoadError {
  if (isConnectionError(failure)) {
    return { title: t('Cannot reach the server'), message: t('Could not reach the server. Check the address, the port, and your connection.') };
  }
  if (failure == null || isApiStatus(failure, 404)) return { title: notFound, message };
  return { title: failed, message };
}
