import * as vscode from 'vscode';
import { AuthHeaderMode, normalizeBaseUrl } from './api/blackbox';
import { BlackBoxAudience } from './api/types';

/** Snapshot of the `armada.*` settings. */
export interface ArmadaSettings {
  url: string;
  tenant: string;
  authHeader: AuthHeaderMode;
  dashboardUrl: string;
  briefAudience: BlackBoxAudience;
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
  const url = String(c.get<string>('url', 'http://localhost:7890') ?? '').trim();
  const dashboard = String(c.get<string>('dashboardUrl', '') ?? '').trim();
  const audience = c.get<string>('briefAudience', 'agent');
  return {
    url,
    tenant: String(c.get<string>('tenant', '') ?? '').trim(),
    authHeader: c.get<string>('authHeader', 'Bearer') === 'X-Api-Key' ? 'X-Api-Key' : 'Bearer',
    dashboardUrl: dashboard ? dashboard.replace(/\/+$/, '') : url ? `${normalizeBaseUrl(url)}/dashboard` : '',
    briefAudience: audience === 'user' || audience === 'maintainer' ? audience : 'agent',
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
