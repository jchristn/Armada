/*
 * Whether records, hubs and Needs You items open as dashboard page tabs (the default) or as the extension's compact
 * detail views (armada.pages.openRecords = "details"). The commands that open things ask here first.
 */

import * as vscode from 'vscode';

type Opener = (route: string, options?: { newTab?: boolean; state?: unknown }) => void;

let opener: Opener | undefined;

/** Registers the page opener (DashboardPages.open). */
export function setPageOpener(open: Opener | undefined): void {
  opener = open;
}

/** True when records open as dashboard pages. */
export function pagesPreferred(): boolean {
  return vscode.workspace.getConfiguration('armada').get<string>('pages.openRecords', 'dashboardPage') !== 'details';
}

/**
 * Opens a route as a dashboard page when pages are preferred and available.
 * @returns true when it opened a page (the caller does nothing else), false to fall back.
 */
export function openAsPage(route: string | undefined, options?: { newTab?: boolean; state?: unknown; force?: boolean }): boolean {
  if (!route || !opener) return false;
  if (!options?.force && !pagesPreferred()) return false;
  opener(route, options);
  return true;
}
