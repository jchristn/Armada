import { matchPath } from 'react-router-dom';
import type { BreadcrumbItem } from '../components/shared/Breadcrumbs';

/** Route and hub ancestry. Every routed page and mounted hub tab is audited against this registry. */
export const dashboardHubs = [
  {
    path: '/vessels', label: 'Vessels', param: 'tab', defaultTab: 'vessels',
    tabs: [
      { key: 'vessels', label: 'Vessels' },
      { key: 'health', label: 'Health' },
      { key: 'fleets', label: 'Fleets' },
      { key: 'workspace', label: 'Workspace' },
    ],
  },
  {
    path: '/captains', label: 'Captains', param: 'tab', defaultTab: 'captains',
    tabs: [
      { key: 'captains', label: 'Captains' },
      { key: 'docks', label: 'Docks' },
      { key: 'battery', label: 'Battery Packs' },
    ],
  },
  {
    path: '/configuration', label: 'Configuration', param: 'tab', defaultTab: 'workflow-profiles',
    tabs: [
      { key: 'workflow-profiles', label: 'Workflow Profiles' },
      { key: 'project-profiles', label: 'Project Profiles' },
      { key: 'skills', label: 'Skills' },
      { key: 'personas', label: 'Personas' },
      { key: 'pipelines', label: 'Pipelines' },
      { key: 'prompts', label: 'Prompts' },
      { key: 'playbooks', label: 'Playbooks' },
      { key: 'endpoints', label: 'Endpoints' },
      { key: 'harbors', label: 'Harbors' },
      { key: 'memory', label: 'Memory' },
    ],
  },
  {
    path: '/missions', label: 'Missions', param: 'tab', defaultTab: 'missions',
    tabs: [
      { key: 'missions', label: 'Missions' },
      { key: 'voyages', label: 'Voyages' },
      { key: 'merge-queue', label: 'Merge Queue' },
      { key: 'blackbox', label: 'Black Box' },
    ],
  },
  {
    path: '/delivery', label: 'Delivery', param: 'tab', defaultTab: 'deployments',
    tabs: [
      { key: 'deployments', label: 'Deployments' },
      { key: 'environments', label: 'Environments' },
      { key: 'releases', label: 'Releases' },
      { key: 'incidents', label: 'Incidents' },
      { key: 'checks', label: 'Checks' },
      { key: 'runbooks', label: 'Runbooks' },
    ],
  },
  {
    path: '/dispatch', label: 'Dispatch', param: 'tab', defaultTab: 'dispatch',
    tabs: [
      { key: 'dispatch', label: 'Dispatch' },
      { key: 'planning', label: 'Planning' },
      { key: 'backlog', label: 'Backlog' },
    ],
  },
  {
    path: '/activity', label: 'Activity', param: 'source', defaultTab: 'history',
    tabs: [
      { key: 'history', label: 'All Activity' },
      { key: 'requests', label: 'API Requests' },
      { key: 'events', label: 'Events' },
      { key: 'signals', label: 'Signals' },
      { key: 'tokens', label: 'Token Usage' },
      { key: 'usage-overview', label: 'Captain Usage' },
      { key: 'jobs', label: 'Jobs' },
    ],
  },
  {
    path: '/server', label: 'Settings', param: 'tab', defaultTab: 'server',
    tabs: [
      { key: 'server', label: 'Server' },
      { key: 'storage', label: 'Storage' },
      { key: 'cli-permissions', label: 'CLI Tool Permissions' },
      { key: 'diagnostics', label: 'Diagnostics' },
      { key: 'tenants', label: 'Tenants' },
      { key: 'users', label: 'Users' },
      { key: 'credentials', label: 'Credentials' },
    ],
  },
  {
    path: '/fleet-actions', label: 'Fleet Actions', param: 'tab', defaultTab: 'actions',
    tabs: [
      { key: 'actions', label: 'Actions' },
      { key: 'runs', label: 'Runs' },
    ],
  },
] as const;

export const dashboardCollections: Record<string, { label: string; to: string }> = {
  fleets: { label: 'Fleets', to: '/vessels?tab=fleets' },
  vessels: { label: 'Vessels', to: '/vessels' },
  captains: { label: 'Captains', to: '/captains' },
  evaluations: { label: 'Evaluations', to: '/evaluations' },
  missions: { label: 'Missions', to: '/missions' },
  voyages: { label: 'Voyages', to: '/missions?tab=voyages' },
  docks: { label: 'Docks', to: '/captains?tab=docks' },
  'merge-queue': { label: 'Merge Queue', to: '/missions?tab=merge-queue' },
  blackbox: { label: 'Black Box', to: '/missions?tab=blackbox' },
  objectives: { label: 'Backlog', to: '/dispatch?tab=backlog' },
  backlog: { label: 'Backlog', to: '/dispatch?tab=backlog' },
  workspace: { label: 'Workspace', to: '/vessels?tab=workspace' },
  signals: { label: 'Signals', to: '/activity?source=signals' },
  events: { label: 'Events', to: '/activity?source=events' },
  requests: { label: 'API Requests', to: '/activity?source=requests' },
  checks: { label: 'Checks', to: '/delivery?tab=checks' },
  environments: { label: 'Environments', to: '/delivery?tab=environments' },
  deployments: { label: 'Deployments', to: '/delivery?tab=deployments' },
  releases: { label: 'Releases', to: '/delivery?tab=releases' },
  incidents: { label: 'Incidents', to: '/delivery?tab=incidents' },
  runbooks: { label: 'Runbooks', to: '/delivery?tab=runbooks' },
  personas: { label: 'Personas', to: '/configuration?tab=personas' },
  pipelines: { label: 'Pipelines', to: '/configuration?tab=pipelines' },
  'prompt-templates': { label: 'Prompt Templates', to: '/configuration?tab=prompts' },
  playbooks: { label: 'Playbooks', to: '/configuration?tab=playbooks' },
  'workflow-profiles': { label: 'Workflow Profiles', to: '/configuration?tab=workflow-profiles' },
  'project-profiles': { label: 'Project Profiles', to: '/configuration?tab=project-profiles' },
  skills: { label: 'Skills', to: '/configuration?tab=skills' },
};

type Translate = (text: string) => string;
const identity: Translate = text => text;
const home = (t: Translate): BreadcrumbItem => ({ label: t('Dashboard'), to: '/' });

/** Collection links always go straight to the canonical hub/tab, without a redirect hop. */
export function canonicalDashboardLink(to: string): string {
  const url = new URL(to, 'https://dashboard.invalid');
  url.pathname = url.pathname.replace(/\/+$/, '') || '/';
  const collection = dashboardCollections[url.pathname.slice(1)];
  if (!collection || url.pathname === collection.to) return url.pathname + url.search + url.hash;
  const target = new URL(collection.to, url.origin);
  url.searchParams.forEach((value, key) => { if (!target.searchParams.has(key)) target.searchParams.set(key, value); });
  return target.pathname + target.search + url.hash;
}

function collectionTrail(to: string, t: Translate): BreadcrumbItem[] {
  const canonical = canonicalDashboardLink(to);
  const url = new URL(canonical, 'https://dashboard.invalid');
  const hub = dashboardHubs.find(h => h.path === url.pathname);
  if (hub) {
    const result = [home(t), { label: t(hub.label), to: hub.path }];
    const tab = hub.tabs.find(tab => tab.key === url.searchParams.get(hub.param));
    if (tab && tab.label !== hub.label) result.push({ label: t(tab.label), to: canonical });
    return result;
  }
  if (url.pathname === '/evaluations') return [home(t), { label: t('Captains'), to: '/captains' }, { label: t('Evaluations'), to: canonical }];
  if (url.pathname === '/') return [home(t)];
  const topLevel: Record<string, string> = { '/ask': 'Ask Armada', '/inbox': 'Needs You', '/api-explorer': 'API Explorer' };
  if (topLevel[url.pathname]) return [home(t), { label: t(topLevel[url.pathname]), to: canonical }];
  if (dashboardBreadcrumbRoutes.some(path => matchPath({ path, end: true }, url.pathname))) {
    const result = dashboardBreadcrumbs(url.pathname, url.search, t);
    result[result.length - 1].to = canonical;
    return result;
  }
  return [home(t), { label: t('Page not found') }];
}

/** Complete route coverage includes loading/error states, before a detail page can supply its entity name. */
export const dashboardBreadcrumbRoutes = [
  "/dispatch",
  "/backlog/:id",
  "/objectives/:id",
  "/fleets/:id",
  "/vessels",
  "/vessels/import",
  "/vessels/health",
  "/vessels/:id",
  "/vessels/:id/onboarding",
  "/vessels/:id/history",
  "/workspace/:vesselId",
  "/workspace/:vesselId/:panel",
  "/captains",
  "/captains/:id",
  "/evaluations",
  "/evaluations/:id",
  "/missions",
  "/missions/:id",
  "/voyages/create",
  "/voyages/:id",
  "/activity",
  "/signals/:id",
  "/events/:id",
  "/docks/:id",
  "/merge-queue/:id",
  "/configuration",
  "/personas/:name",
  "/pipelines/:name",
  "/prompt-templates/create",
  "/prompt-templates/:name",
  "/playbooks/:id",
  "/workflow-profiles/:id",
  "/project-profiles/:id",
  "/skills/:id",
  "/ask/:threadId?",
  "/inbox",
  "/blackbox/:id",
  "/delivery",
  "/fleet-actions",
  "/fleet-actions/runs/:id",
  "/checks/:id",
  "/environments/:id",
  "/deployments/:id",
  "/releases/new",
  "/releases/:id",
  "/incidents/:id",
  "/runbooks/:id",
  "/requests/:id",
  "/api-explorer",
  "/api-explorer/:operationId",
  "/server"
] as const;

export function dashboardBreadcrumbs(pathname: string, search = '', t: Translate = identity): BreadcrumbItem[] {
  pathname = pathname.replace(/\/+$/, '') || '/';
  if (pathname === '/') return [home(t)];
  const pattern = dashboardBreadcrumbRoutes.find(path => matchPath({ path, end: true }, pathname));
  if (!pattern) return [home(t), { label: t('Page not found') }];
  const match = matchPath({ path: pattern, end: true }, pathname)!;
  const params = new URLSearchParams(search);
  const hub = dashboardHubs.find(h => h.path === pathname);
  if (hub) {
    const tab = hub.tabs.find(tab => tab.key === params.get(hub.param)) ?? hub.tabs.find(tab => tab.key === hub.defaultTab);
    return collectionTrail(hub.path + (tab ? `?${hub.param}=${tab.key}` : ''), t);
  }
  if (pathname === '/vessels/health') return [home(t), { label: t('Vessels'), to: '/vessels' }, { label: t('Health') }];
  if (pathname === '/vessels/import') return [home(t), { label: t('Vessels'), to: '/vessels' }, { label: t('Import') }];
  if (pathname === '/evaluations') {
    const captainId = params.get('captainId');
    return captainId ? [home(t), { label: t('Captains'), to: '/captains' }, { label: captainId, to: `/captains/${encodeURIComponent(captainId)}` }, { label: t('Evaluations') }]
      : collectionTrail('/evaluations', t);
  }
  const section = pathname.split('/')[1];
  const collection = dashboardCollections[section];
  let trail = collectionTrail(collection?.to ?? `/${section}`, t);
  if (section === 'fleet-actions') trail = [home(t), { label: t('Fleet Actions'), to: '/fleet-actions' }, { label: t('Runs'), to: '/fleet-actions?tab=runs' }];
  if (section === 'workspace') {
    trail.push({ label: match.params.vesselId ?? t('Workspace') });
  } else if (match.params.id || match.params.name || match.params.threadId || match.params.operationId) {
    const id = match.params.id ?? match.params.name ?? match.params.threadId ?? match.params.operationId!;
    trail.push({ label: id });
  } else if (pathname.endsWith('/create') || pathname.endsWith('/new')) {
    trail.push({ label: t('Create') });
  }
  if (pattern === '/vessels/:id/history' || pattern === '/vessels/:id/onboarding') {
    trail[trail.length - 1].to = `/vessels/${encodeURIComponent(match.params.id!)}`;
    trail.push({ label: t(pattern.endsWith('/history') ? 'History' : 'Onboarding') });
  }
  return trail;
}

/** Apply the same root, hub ancestry and canonical list links to page-supplied entity labels. */
export function normalizeDashboardBreadcrumbs(items: readonly BreadcrumbItem[], t: Translate = identity): BreadcrumbItem[] {
  if (!items.length) return [];
  const normalized = items.map(item => ({ ...item, to: item.to ? canonicalDashboardLink(item.to) : undefined }));
  const first = normalized[0];
  const prefix = first.to ? collectionTrail(first.to, t) : [home(t)];
  if (first.to && prefix[prefix.length - 1]?.to === first.to) {
    const url = new URL(first.to, 'https://dashboard.invalid');
    if (dashboardHubs.some(hub => hub.path === url.pathname) || url.pathname === '/evaluations' || url.pathname === '/')
      first.label = prefix[prefix.length - 1].label;
    prefix.pop();
  }
  return [...prefix, ...normalized];
}
