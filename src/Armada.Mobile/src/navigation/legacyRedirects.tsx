import { Redirect, useLocalSearchParams, type Href } from 'expo-router';

/**
 * Routes that became hub tabs in the v1.2.0 nav consolidation, mirroring the dashboard's LegacyNavRedirects:
 * CLI Tool Permissions is the Settings hub's cli-permissions tab (its own Requests / Rules sub-tabs in ?section=),
 * Planning is the Dispatch hub's planning tab (an open session in ?session=), and the Black Box is the Missions
 * hub's blackbox tab (the dashboard's own Black Box sections in ?bbtab=). /jobs and /storage are plain
 * dashboard redirects (RouteRedirect). These keep the query, so push, inbox, and deep links such as
 * /cli-permissions?request=cpr_1 or /planning/pls_1?prompt=... still open the same request or session.
 */

type Params = Record<string, string | string[] | null | undefined>;

/** The Settings tab that hosts CLI Tool Permissions. */
export const CLI_PERMISSIONS_PATH = '/server?tab=cli-permissions';

/** The Dispatch tab that hosts Planning. */
export const PLANNING_PATH = '/dispatch?tab=planning';

/** The Missions tab that hosts the Black Box. */
export const BLACKBOX_PATH = '/missions?tab=blackbox';

/** The Activity source that lists background jobs. */
export const JOBS_PATH = '/activity?source=jobs';

function first(value: string | string[] | null | undefined): string | undefined {
  const v = Array.isArray(value) ? value[0] : value;
  return v ? v : undefined;
}

function build(path: string, head: Record<string, string>, rest: Params, skip: string[]): string {
  const pairs: [string, string][] = Object.entries(head);
  for (const [key, value] of Object.entries(rest)) {
    if (skip.includes(key) || key in head) continue;
    const v = first(value);
    if (v !== undefined) pairs.push([key, v]);
  }
  // encodeURIComponent, not URLSearchParams: a space must travel as %20 (the router does not read '+' as a space).
  return `${path}?${pairs.map(([key, v]) => `${encodeURIComponent(key)}=${encodeURIComponent(v)}`).join('&')}`;
}

/** Link to the CLI Tool Permissions tab, optionally at one request or on a sub-section (requests | rules). */
export function cliPermissionsHref(options: { requestId?: string | null; section?: 'requests' | 'rules' } = {}): string {
  const head: Record<string, string> = { tab: 'cli-permissions' };
  if (options.section) head.section = options.section;
  if (options.requestId) head.request = options.requestId;
  return build('/server', head, {}, []);
}

/** Link to the Planning tab, optionally opening one session; `extra` carries prefill or `prompt` params. */
export function planningHref(sessionId?: string | null, extra: Params = {}): string {
  const head: Record<string, string> = { tab: 'planning' };
  if (sessionId) head.session = sessionId;
  return build('/dispatch', head, extra, ['tab', 'session']);
}

/** /cli-permissions[?tab=rules&request=...] -> Settings > CLI Tool Permissions; the old `tab` becomes `section`. */
export function cliPermissionsRedirectTarget(params: Params): string {
  const head: Record<string, string> = { tab: 'cli-permissions' };
  const section = first(params.tab) ?? first(params.section);
  if (section) head.section = section;
  return build('/server', head, params, ['tab', 'section']);
}

/** /planning[/:id][?...] -> Dispatch > Planning, keeping the session id and every other query parameter. */
export function planningRedirectTarget(params: Params): string {
  return planningHref(first(params.id) ?? first(params.session) ?? null, Object.fromEntries(Object.entries(params).filter(([key]) => key !== 'id')));
}

/** /blackbox[?tab=fingerprints&vesselId=...] -> Missions > Black Box; the old `tab` becomes `bbtab`, filters are kept. */
export function blackBoxRedirectTarget(params: Params): string {
  const head: Record<string, string> = { tab: 'blackbox' };
  const section = first(params.tab) ?? first(params.bbtab);
  if (section) head.bbtab = section;
  return build('/missions', head, params, ['tab', 'bbtab']);
}

export function CliPermissionsRedirect() {
  const params = useLocalSearchParams<Record<string, string>>();
  return <Redirect href={cliPermissionsRedirectTarget(params) as Href} />;
}

export function PlanningRedirect() {
  const params = useLocalSearchParams<Record<string, string>>();
  return <Redirect href={planningRedirectTarget(params) as Href} />;
}

export function BlackBoxRedirect() {
  const params = useLocalSearchParams<Record<string, string>>();
  return <Redirect href={blackBoxRedirectTarget(params) as Href} />;
}
