/*
 * Where each place in the Manage view opens in a dashboard page tab: hubs, the Missions tabs, each record kind's list
 * (the dashboard hub tab that shows it) and each record (its detail page, when the dashboard has one). The routes are
 * the dashboard's own (Armada.Dashboard src/AppRoutes.tsx and lib/dashboardNavigation.ts). Pure, for tests.
 */

/** The page a hub opens (NavHub keys, navLayout.ts). */
export const HUB_ROUTES: Record<string, string> = {
  overview: '/',
  needsYou: '/inbox',
  dispatch: '/dispatch',
  fleetActions: '/fleet-actions',
  missions: '/missions',
  activity: '/activity',
  vessels: '/vessels',
  captains: '/captains',
  delivery: '/delivery',
  configuration: '/configuration',
  apiExplorer: '/api-explorer',
  settings: '/server',
};

/** The page each tree source opens (NavSource ids, extension.ts). */
export const SOURCE_ROUTES: Record<string, string> = {
  armadaNeedsYou: '/inbox',
  armadaMissions: '/missions',
  armadaVoyages: '/missions?tab=voyages',
  armadaMergeQueue: '/missions?tab=merge-queue',
  armadaFailures: '/missions?tab=blackbox',
  armadaFleet: '/vessels',
  armadaCaptains: '/captains',
};

/** The dashboard list page of each record kind (ResourceSpec keys). */
export const KIND_ROUTES: Record<string, string> = {
  mission: '/missions',
  voyage: '/missions?tab=voyages',
  blackBoxEntry: '/missions?tab=blackbox',
  vessel: '/vessels',
  fleet: '/vessels?tab=fleets',
  captain: '/captains',
  planningSession: '/dispatch?tab=planning',
  backlog: '/dispatch?tab=backlog',
  fleetAction: '/fleet-actions?tab=actions',
  fleetActionRun: '/fleet-actions?tab=runs',
  stageOutcome: '/missions',
  fingerprint: '/missions?tab=blackbox',
  recoveryStats: '/missions?tab=blackbox',
  history: '/activity?source=history',
  requestHistory: '/activity?source=requests',
  event: '/activity?source=events',
  signal: '/activity?source=signals',
  tokenUsage: '/activity?source=tokens',
  captainUsage: '/activity?source=usage-overview',
  job: '/activity?source=events',
  importBatch: '/vessels/import',
  vesselHealth: '/vessels?tab=health',
  dock: '/captains?tab=docks',
  batteryPack: '/captains?tab=battery',
  evaluation: '/evaluations',
  deployment: '/delivery?tab=deployments',
  environment: '/delivery?tab=environments',
  release: '/delivery?tab=releases',
  incident: '/delivery?tab=incidents',
  checkRun: '/delivery?tab=checks',
  runbook: '/delivery?tab=runbooks',
  runbookExecution: '/delivery?tab=runbooks',
  workflowProfile: '/configuration?tab=workflow-profiles',
  projectProfile: '/configuration?tab=project-profiles',
  skill: '/configuration?tab=skills',
  persona: '/configuration?tab=personas',
  pipeline: '/configuration?tab=pipelines',
  promptTemplate: '/configuration?tab=prompts',
  playbook: '/configuration?tab=playbooks',
  modelEndpoint: '/configuration?tab=endpoints',
  muxEndpoint: '/configuration?tab=endpoints',
  harbor: '/configuration?tab=harbors',
  memory: '/configuration?tab=memory',
  failureCauseRule: '/missions?tab=blackbox',
  server: '/server',
  settings: '/server',
  storage: '/server?tab=storage',
  storageHost: '/server?tab=storage',
  retention: '/server?tab=storage',
  cliPermissionRule: '/server?tab=cli-permissions',
  diagnostics: '/server?tab=diagnostics',
  tenant: '/server?tab=tenants',
  user: '/server?tab=users',
  credential: '/server?tab=credentials',
};

/** Kinds the dashboard creates on a page of their own; every other kind is created from its list page. */
export const CREATE_ROUTES: Record<string, string> = {
  backlog: '/backlog/new',
  release: '/releases/new',
  promptTemplate: '/prompt-templates/create',
  voyage: '/voyages/create',
  mission: '/dispatch?tab=dispatch',
  planningSession: '/dispatch?tab=planning',
  vessel: '/vessels',
};

/** Where a new record of a kind is created. */
export function createRoute(kind: string): string | undefined {
  return CREATE_ROUTES[kind] ?? KIND_ROUTES[kind];
}

/** The dashboard's routed paths (AppRoutes.tsx), as patterns; `:x` is one segment. */
const ROUTE_PATTERNS: RegExp[] = [
  '/', '/dispatch', '/backlog/:id', '/objectives/:id', '/fleets/:id', '/vessels', '/vessels/import', '/vessels/health',
  '/vessels/:id', '/vessels/:id/onboarding', '/vessels/:id/history', '/workspace/:id', '/workspace/:id/:panel',
  '/captains', '/captains/:id', '/evaluations', '/evaluations/:id', '/missions', '/missions/:id', '/voyages/create',
  '/voyages/:id', '/activity', '/signals/:id', '/events/:id', '/docks/:id', '/merge-queue/:id', '/configuration',
  '/personas/:id', '/pipelines/:id', '/prompt-templates/create', '/prompt-templates/:id', '/playbooks/:id',
  '/workflow-profiles/:id', '/project-profiles/:id', '/skills/:id', '/inbox', '/blackbox/:id', '/delivery',
  '/fleet-actions', '/fleet-actions/runs/:id', '/checks/:id', '/environments/:id', '/deployments/:id', '/releases/new',
  '/releases/:id', '/incidents/:id', '/runbooks/:id', '/requests/:id', '/api-explorer', '/api-explorer/:id', '/server',
].map((pattern) => new RegExp(`^${pattern.replace(/:[a-z]+/g, '[^/]+')}$`));

/** True when the dashboard has a page at this route (query and hash ignored). */
export function isDashboardRoute(route: string | undefined): boolean {
  if (!route || !route.startsWith('/')) return false;
  const pathname = route.split(/[?#]/)[0].replace(/\/+$/, '') || '/';
  return ROUTE_PATTERNS.some((pattern) => pattern.test(pathname));
}

/** The page for a record: its own route when the dashboard has it, else its kind's list. */
export function recordRoute(kind: string, route: string | undefined): string {
  if (route && isDashboardRoute(route)) return route;
  return KIND_ROUTES[kind] ?? '/';
}

/** The detail page of a mission, voyage, captain or merge entry. */
export function entityRoute(kind: 'mission' | 'voyage' | 'captain' | 'merge', id: string): string {
  const encoded = encodeURIComponent(id);
  if (kind === 'mission') return `/missions/${encoded}`;
  if (kind === 'voyage') return `/voyages/${encoded}`;
  if (kind === 'captain') return `/captains/${encoded}`;
  return `/merge-queue/${encoded}`;
}

/** The page an entity id opens by its prefix (msn_, vyg_, cpt_, mrg_, vsl_, flt_, dck_), or undefined. */
export function routeForId(id: string): string | undefined {
  const encoded = encodeURIComponent(id);
  if (id.startsWith('msn_')) return `/missions/${encoded}`;
  if (id.startsWith('vyg_')) return `/voyages/${encoded}`;
  if (id.startsWith('cpt_')) return `/captains/${encoded}`;
  if (id.startsWith('mrg_')) return `/merge-queue/${encoded}`;
  if (id.startsWith('vsl_')) return `/vessels/${encoded}`;
  if (id.startsWith('flt_')) return `/fleets/${encoded}`;
  if (id.startsWith('dck_')) return `/docks/${encoded}`;
  return undefined;
}

/** The tab group a route belongs to (its first path segment), so one tab per area is reused. */
export function routeArea(route: string): string {
  const segment = route.split(/[?#]/)[0].split('/').filter(Boolean)[0] ?? '';
  return segment || 'overview';
}
