/**
 * URLs of destinations that became hub tabs in the v1.2.0 nav consolidation. Pure strings (no router), so shared
 * modules can link to them; the old routes (/planning, /jobs, /storage, /cli-permissions, /blackbox) still redirect here.
 */

/** Settings tab that hosts CLI Tool Permissions. */
export const CLI_PERMISSIONS_PATH = '/server?tab=cli-permissions';

/** Dispatch tab that hosts Planning. */
export const PLANNING_PATH = '/dispatch?tab=planning';

/** Activity tab that lists background jobs. */
export const JOBS_PATH = '/activity?source=jobs';

/** Settings tab that hosts Storage (global admins). */
export const STORAGE_PATH = '/server?tab=storage';

/** Missions tab that hosts the Black Box (its own Entries, Fingerprints, ... sections live in `?bbtab=`). */
export const BLACKBOX_PATH = '/missions?tab=blackbox';

/** Query parameter of the Black Box tab's inner sections (the Missions hub owns `?tab=`). */
export const BLACKBOX_SECTION_PARAM = 'bbtab';

/**
 * Link to the Black Box tab of the Missions page, optionally on one section (entries, fingerprints, hotspots,
 * recovery, retention) with Entries filters such as `fingerprint`, `path` or `vesselId`.
 */
export function blackBoxHref(section?: string | null, filters: Record<string, string | null | undefined> = {}): string {
  const params = new URLSearchParams({ tab: 'blackbox' });
  if (section) params.set(BLACKBOX_SECTION_PARAM, section);
  for (const [key, value] of Object.entries(filters)) {
    if (value && key !== 'tab' && key !== BLACKBOX_SECTION_PARAM) params.set(key, value);
  }
  return `/missions?${params.toString()}`;
}

/** Link to the CLI Tool Permissions tab, optionally at one request or on the Rules section. */
export function cliPermissionsHref(options: { requestId?: string | null; section?: 'requests' | 'rules' } = {}): string {
  const params = new URLSearchParams({ tab: 'cli-permissions' });
  if (options.section) params.set('section', options.section);
  if (options.requestId) params.set('request', options.requestId);
  return `/server?${params.toString()}`;
}

/** Link to the Planning tab, optionally opening one session. */
export function planningHref(sessionId?: string | null, extra?: Record<string, string>): string {
  const params = new URLSearchParams({ tab: 'planning' });
  if (sessionId) params.set('session', sessionId);
  for (const [key, value] of Object.entries(extra ?? {})) params.set(key, value);
  return `/dispatch?${params.toString()}`;
}
