import * as vscode from 'vscode';

/**
 * Snapshot of the `armada.*` settings. These are preferences only: which Admiral to use and its credential come from
 * the Armada settings on this machine (connection/endpoints.ts) and the "Armada: Connect" picker.
 */
export interface ArmadaSettings {
  tenant: string;
  /** Explicit dashboard URL; empty means the connected Admiral's /dashboard. */
  dashboardUrl: string;
  showFailuresInExplorer: boolean;
  filterToWorkspaceVessel: boolean;
  includeResolved: boolean;
  maxEntries: number;
  useWebSocket: boolean;
  pollIntervalSeconds: number;
  decorationsEnabled: boolean;
  hotspotLimit: number;
  notifyOnCapture: boolean;
  previewFixThis: boolean;
}

/** Reads the current settings, clamping numbers into their allowed ranges. */
export function readSettings(): ArmadaSettings {
  const c = vscode.workspace.getConfiguration('armada');
  const dashboard = String(c.get<string>('dashboardUrl', '') ?? '').trim();
  return {
    tenant: String(c.get<string>('tenant', '') ?? '').trim(),
    dashboardUrl: dashboard ? dashboard.replace(/\/+$/, '') : '',
    showFailuresInExplorer: c.get<boolean>('failures.showInExplorer', false) === true,
    filterToWorkspaceVessel: c.get<boolean>('failures.filterToWorkspaceVessel', true) !== false,
    includeResolved: c.get<boolean>('failures.includeResolved', false) === true,
    maxEntries: clamp(c.get<number>('failures.maxEntries', 50), 1, 500, 50),
    useWebSocket: c.get<boolean>('liveUpdates.useWebSocket', true) !== false,
    pollIntervalSeconds: clamp(c.get<number>('liveUpdates.pollIntervalSeconds', 60), 10, 3600, 60),
    decorationsEnabled: c.get<boolean>('decorations.enabled', true) !== false,
    hotspotLimit: clamp(c.get<number>('decorations.hotspotLimit', 10), 0, 100, 10),
    notifyOnCapture: c.get<boolean>('notifications.onCapture', true) !== false,
    previewFixThis: c.get<boolean>('fixThis.openPreview', true) !== false,
  };
}

function clamp(value: number | undefined, min: number, max: number, fallback: number): number {
  if (typeof value !== 'number' || Number.isNaN(value)) return fallback;
  return Math.min(max, Math.max(min, Math.floor(value)));
}
