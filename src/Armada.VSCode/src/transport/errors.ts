/*
 * User-facing explanations for the errors the shared client throws (ApiError with the HTTP status and the server's
 * data.code, NetworkError, TimeoutError, RequestCancelledError) and for the host's own NotConnectedError.
 */

import { ApiError, apiErrorCode, NetworkError, RequestCancelledError, TimeoutError } from '@dashboard/api/client';

/** Thrown by a command that needs a connection when there is none. */
export class NotConnectedError extends Error {
  public constructor(message = 'Armada is not connected.') {
    super(message);
    this.name = 'NotConnectedError';
  }
}

/** True when the fix is to connect (again): no connection, or the Admiral rejected the credential. */
export function needsConnection(err: unknown): boolean {
  return err instanceof NotConnectedError || (err instanceof ApiError && err.status === 401);
}

/** True when the error is an ApiError with the status. */
export function hasStatus(err: unknown, status: number): boolean {
  return err instanceof ApiError && err.status === status;
}

/** The server's message for an ApiError, or null. */
export function serverMessage(err: unknown): string | null {
  return err instanceof ApiError && err.message && !/^\d{3}: /.test(err.message) ? err.message : null;
}

/** A user-facing explanation for any error. Never throws. */
export function describeError(err: unknown): string {
  if (err instanceof NotConnectedError) return 'Armada is not connected. Run "Armada: Connect", or start the local Admiral.';
  if (err instanceof TimeoutError) return 'The Armada server did not respond in time. Try again, or check the Admiral.';
  if (err instanceof RequestCancelledError) return 'The request was cancelled.';
  if (err instanceof NetworkError) return `Cannot reach the Armada server: ${networkMessage(err)}. Check that the Admiral is running and armada.url is right.`;
  if (err instanceof ApiError) {
    const code = apiErrorCode(err);
    const message = serverMessage(err);
    const detail = message ? ` (${message})` : '';
    switch (err.status) {
      case 401:
        return 'Armada rejected the credential (401). Run "Armada: Connect" and enter a valid API key or token.';
      case 403:
        return `Your Armada credential is not allowed to do this (403)${detail}.`;
      case 404:
        return code === 'BlackBoxNotFound'
          ? `No black box entry was found (404)${detail}. The mission may not have failed, or the entry expired.`
          : `Not found (404)${detail}. The id may be wrong, or this Armada server does not have the route yet.`;
      case 409:
        return `The request conflicts with the current state (409)${detail}.`;
      case 410:
        return `A black box object is missing from storage (410 ${code ?? 'BlackBoxObjectMissing'})${detail}.`;
      case 400:
      case 422:
        return `Armada rejected the request (${err.status})${detail}.`;
      case 501:
        return `This Armada server does not support that operation (501 ${code ?? 'NotImplemented'})${detail}. Upgrade the Admiral.`;
      case 503:
        if (code === 'BlackBoxActionsUnavailable') return `This Armada server cannot run black box recovery actions (503 BlackBoxActionsUnavailable)${detail}. Follow the steps by hand.`;
        if (code === 'BlackBoxStoreUnavailable') return `The black box store is unavailable (503 BlackBoxStoreUnavailable)${detail}. Try again later.`;
        return `The Armada server is unavailable (503 ${code ?? 'Unavailable'})${detail}. Try again later.`;
      default:
        return `Armada server error (${err.status})${detail}.`;
    }
  }
  if (err instanceof Error) return err.message;
  return String(err);
}

function networkMessage(err: NetworkError): string {
  const cause = err.cause;
  if (cause instanceof Error) {
    const inner = (cause as { cause?: unknown }).cause;
    if (inner instanceof Error && inner.message) return `${cause.message} (${inner.message})`;
    if (cause.message && cause.message !== err.message) return `${err.message} (${cause.message})`;
  }
  return err.message;
}
