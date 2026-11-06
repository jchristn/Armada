import { useCallback, useRef } from 'react';

/** Decision for one server page response. */
export interface ServerPageDecision {
  /**
   * True when this response belongs on the requested page and its rows should be shown.
   * False when the list now ends before that page. The caller discards the rows and loads `pageNumber`.
   */
  accept: boolean;
  /** Page to show. A page past the end moves to the last page. An empty total uses page 1. */
  pageNumber: number;
  /** Page count to show after an accepted response. An empty total is one page. */
  totalPages: number;
}

/**
 * Reconcile a requested page with the total the server reported.
 * A response past the end is not published. The caller keeps the rows already on screen and loads the returned page.
 * Each response moves at most once, so a shrinking list cannot loop on the same page.
 */
export function reconcileServerPage(requestedPage: number, reportedTotalPages: number | null | undefined): ServerPageDecision {
  const requested = normalizePage(requestedPage);
  const lastPage = Math.max(1, normalizeTotal(reportedTotalPages));
  if (requested > lastPage) {
    return { accept: false, pageNumber: lastPage, totalPages: lastPage };
  }
  return { accept: true, pageNumber: requested, totalPages: lastPage };
}

function normalizePage(page: number): number {
  if (typeof page !== 'number' || !Number.isFinite(page)) return 1;
  return Math.max(1, Math.floor(page));
}

function normalizeTotal(total: number | null | undefined): number {
  if (typeof total !== 'number' || !Number.isFinite(total)) return 0;
  return Math.max(0, Math.floor(total));
}

/** A list request. `isCurrent` is false after a later request starts. */
export interface ListRequest {
  /** False when a later list request has started. */
  isCurrent: () => boolean;
}

/**
 * Ignore a list response that belongs to an older request.
 * A slow page must not replace rows for a page the operator opened later.
 */
export function useListRequestGuard(): () => ListRequest {
  const generation = useRef(0);
  return useCallback(() => {
    generation.current += 1;
    const id = generation.current;
    return { isCurrent: () => generation.current === id };
  }, []);
}
