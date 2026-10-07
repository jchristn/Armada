import { readPref, writePref } from '../../storage/prefs';

/** Preference key of the recently opened Workspace vessels (most recent first). */
export const RECENT_WORKSPACE_VESSELS_KEY = 'armada.workspace.recentVessels';

/** How many recent vessels the picker remembers (the dashboard keeps eight). */
const MAX_RECENT = 8;

export async function readRecentWorkspaceVessels(): Promise<string[]> {
  const value = await readPref<unknown>(RECENT_WORKSPACE_VESSELS_KEY);
  return Array.isArray(value) ? value.filter((v): v is string => typeof v === 'string') : [];
}

/** Move a vessel to the front of the recent list. */
export async function rememberWorkspaceVessel(vesselId: string): Promise<string[]> {
  const next = [vesselId, ...(await readRecentWorkspaceVessels()).filter((id) => id !== vesselId)].slice(0, MAX_RECENT);
  await writePref(RECENT_WORKSPACE_VESSELS_KEY, next);
  return next;
}
