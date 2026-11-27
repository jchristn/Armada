/*
 * The Manage view: the dashboard's navigation as a table of contents. Armada Overview, then the hubs of its
 * Operations, Build, Configuration and System sections in the dashboard's order, each hub listing its tabs in the
 * dashboard's order (Armada.Dashboard src/lib/navModel.ts and the hub registry in lib/dashboardNavigation.ts, which a
 * unit test compares this with). Every entry opens its dashboard page in an editor tab; nothing expands into records.
 * Pure, for tests.
 */

/** One page under a hub (a dashboard hub tab, or a page the hub links to). */
export interface NavPage {
  label: string;
  /** Codicon. */
  icon: string;
  /** The dashboard route it opens ("/activity?source=tokens"). */
  route: string;
  tooltip?: string;
}

/** A dashboard hub, or a leaf (Armada Overview, Needs You, API Explorer) when it has no pages. */
export interface NavHub {
  key: string;
  label: string;
  icon: string;
  tooltip: string;
  /** The dashboard section the hub belongs to (shown in its tooltip). */
  section?: string;
  /** The page the hub itself opens. */
  route: string;
  pages: NavPage[];
}

function tabs(path: string, param: string, entries: Array<[key: string, label: string, icon: string, tooltip?: string]>): NavPage[] {
  return entries.map(([key, label, icon, tooltip]) => ({ label, icon, route: `${path}?${param}=${key}`, tooltip }));
}

/** The hubs, in the dashboard's order. */
export function dashboardHubs(): NavHub[] {
  return [
    { key: 'overview', label: 'Armada Overview', icon: 'home', route: '/', pages: [], tooltip: 'Fleet status at a glance: what needs you, mission history, API requests and token usage over time' },
    { key: 'needsYou', label: 'Needs You', icon: 'bell', section: 'Operations', route: '/inbox', pages: [], tooltip: 'Reviews, failures, stalls, CLI permission requests and model alerts awaiting your attention' },
    {
      key: 'dispatch', label: 'Dispatch', icon: 'send', section: 'Operations', route: '/dispatch', tooltip: 'Send work to vessels, plan with a captain, and capture and refine backlog',
      pages: [
        ...tabs('/dispatch', 'tab', [['dispatch', 'Dispatch', 'send', 'Dispatch a mission: vessel, pipeline, captain per step, playbooks'], ['planning', 'Planning', 'comment-discussion', 'Plan with a captain on a vessel or Harbor, then dispatch from the transcript'], ['backlog', 'Backlog', 'lightbulb', 'Backlog items: capture, refine with a captain, plan and dispatch']]),
        { label: 'Create Voyage', icon: 'rocket', route: '/voyages/create', tooltip: 'Several missions on one vessel as a voyage' },
      ],
    },
    {
      key: 'fleetActions', label: 'Fleet Actions', icon: 'run-all', section: 'Operations', route: '/fleet-actions', tooltip: 'Run a command or mission across many vessels and watch each run',
      pages: tabs('/fleet-actions', 'tab', [['actions', 'Actions', 'run-all'], ['runs', 'Runs', 'play-circle']]),
    },
    {
      key: 'missions', label: 'Missions', icon: 'tasklist', section: 'Operations', route: '/missions', tooltip: 'Work units, plus Voyages, the Merge Queue, and the Black Box of failures',
      pages: tabs('/missions', 'tab', [['missions', 'Missions', 'tasklist'], ['voyages', 'Voyages', 'rocket'], ['merge-queue', 'Merge Queue', 'git-merge'], ['blackbox', 'Black Box', 'debug-alt', 'Failure records: where, how and why each mission or voyage failed, and how to recover']]),
    },
    {
      key: 'activity', label: 'Activity', icon: 'pulse', section: 'Operations', route: '/activity', tooltip: 'Everything that happened, API requests, events, signals, token and captain usage, concurrency',
      pages: tabs('/activity', 'source', [
        ['history', 'All Activity', 'timeline-open', 'Everything that happened, newest first, over a time range'],
        ['requests', 'API Requests', 'history', 'Requests over time (chart) and every request with its status, timing and caller'],
        ['events', 'Events', 'zap'],
        ['signals', 'Signals', 'mail'],
        ['tokens', 'Token Usage', 'graph', 'Token usage heatmap and charts by captain, model and Harbor'],
        ['usage-overview', 'Captain Usage', 'dashboard', 'Captain usage windows, allowances and charts'],
        ['concurrency', 'Concurrency', 'layers', 'Wait time by cause, slot usage and percentiles'],
      ]),
    },
    {
      key: 'vessels', label: 'Vessels', icon: 'repo', section: 'Build', route: '/vessels', tooltip: 'Repositories grouped by fleet, their health, and the vessel workspace',
      pages: [
        ...tabs('/vessels', 'tab', [['vessels', 'Vessels', 'repo'], ['health', 'Health', 'heart'], ['fleets', 'Fleets', 'folder-library'], ['workspace', 'Workspace', 'files']]),
        { label: 'Import Vessels', icon: 'cloud-download', route: '/vessels/import', tooltip: 'Import repositories from GitHub or a folder and group them into fleets' },
      ],
    },
    {
      key: 'captains', label: 'Captains', icon: 'person', section: 'Build', route: '/captains', tooltip: 'AI coding agents that execute missions, the model profiles they run, their docks, battery packs and evaluations',
      pages: [
        ...tabs('/captains', 'tab', [['captains', 'Captains', 'person'], ['profiles', 'Model Profiles', 'hubot'], ['reliability', 'Reliability', 'pulse'], ['docks', 'Docks', 'git-branch'], ['battery', 'Battery Packs', 'beaker'], ['battery-runs', 'Battery Runs', 'history']]),
        { label: 'Evaluations', icon: 'checklist', route: '/evaluations', tooltip: 'Model evaluations and their scorecards' },
      ],
    },
    {
      key: 'delivery', label: 'Delivery', icon: 'package', section: 'Build', route: '/delivery', tooltip: 'Deployments, Environments, Releases, Incidents, Checks, and Runbooks',
      pages: tabs('/delivery', 'tab', [['deployments', 'Deployments', 'cloud-upload'], ['environments', 'Environments', 'server-environment'], ['releases', 'Releases', 'tag'], ['incidents', 'Incidents', 'flame'], ['checks', 'Checks', 'pass'], ['runbooks', 'Runbooks', 'notebook']]),
    },
    {
      key: 'configuration', label: 'Configuration', icon: 'settings', section: 'Configuration', route: '/configuration', tooltip: 'Workflow Profiles, Project Profiles, Skills, Personas, Pipelines, Prompts, Playbooks, Endpoints, Harbors and Memory',
      pages: tabs('/configuration', 'tab', [
        ['workflow-profiles', 'Workflow Profiles', 'workflow'],
        ['project-profiles', 'Project Profiles', 'project'],
        ['skills', 'Skills', 'tools'],
        ['personas', 'Personas', 'account'],
        ['pipelines', 'Pipelines', 'type-hierarchy'],
        ['prompts', 'Prompts', 'symbol-text'],
        ['playbooks', 'Playbooks', 'book'],
        ['endpoints', 'Endpoints', 'plug'],
        ['harbors', 'Harbors', 'vm'],
        ['memory', 'Memory', 'database'],
      ]),
    },
    { key: 'apiExplorer', label: 'API Explorer', icon: 'symbol-interface', section: 'System', route: '/api-explorer', pages: [], tooltip: 'Call any Armada REST endpoint and see the response' },
    {
      key: 'settings', label: 'Settings', icon: 'gear', section: 'System', route: '/server', tooltip: 'Server settings, Storage, CLI Tool Permissions, Diagnostics, and tenant, user and credential administration',
      pages: tabs('/server', 'tab', [['server', 'Server', 'server'], ['storage', 'Storage', 'database'], ['cli-permissions', 'CLI Tool Permissions', 'shield'], ['diagnostics', 'Diagnostics', 'pulse'], ['tenants', 'Tenants', 'organization'], ['users', 'Users', 'account'], ['credentials', 'Credentials', 'key']]),
    },
  ];
}

/** The old per-view tab commands (armada.tab.<viewId>) and the page each now opens. */
export const LEGACY_TAB_TARGETS: Record<string, string> = {
  armadaNeedsYou: '/inbox',
  armadaMissions: '/missions',
  armadaVoyages: '/missions?tab=voyages',
  armadaCaptains: '/captains',
  armadaFleet: '/vessels',
  armadaMergeQueue: '/missions?tab=merge-queue',
  armadaFailures: '/missions?tab=blackbox',
  armadaBacklog: '/dispatch?tab=backlog',
  armadaFleetActions: '/fleet-actions',
  armadaDelivery: '/delivery',
  armadaConfiguration: '/configuration',
  armadaActivity: '/activity',
  armadaSystem: '/server',
};
