/*
 * The dashboard's navigation (Armada.Dashboard src/lib/navModel.ts and dashboardNavigation.ts) as the Armada tree's
 * hubs: the same sections, hubs and tab order, with each record kind under the hub whose tab shows it on the
 * dashboard. Pure, for tests.
 */

import { NavHub, NavSource } from './navTree';

/** Record kinds (ResourceSpec keys) per hub, in the dashboard's tab order. */
export const HUB_RESOURCES: Record<string, string[]> = {
  dispatch: ['planningSession', 'backlog'],
  fleetActions: ['fleetAction', 'fleetActionRun'],
  missions: ['stageOutcome', 'fingerprint', 'recoveryStats'],
  activity: ['history', 'requestHistory', 'event', 'signal', 'tokenUsage', 'captainUsage', 'job', 'importBatch'],
  vessels: ['vesselHealth'],
  captains: ['dock', 'batteryPack', 'evaluation'],
  delivery: ['deployment', 'environment', 'release', 'incident', 'checkRun', 'runbook', 'runbookExecution'],
  configuration: ['workflowProfile', 'projectProfile', 'skill', 'persona', 'pipeline', 'promptTemplate', 'playbook', 'modelEndpoint', 'muxEndpoint', 'harbor', 'memory', 'failureCauseRule'],
  settings: ['server', 'settings', 'storage', 'storageHost', 'retention', 'cliPermissionRule', 'diagnostics', 'tenant', 'user', 'credential'],
};

/** The trees the hubs show. */
export interface NavSources {
  needsYou: NavSource;
  missions: NavSource;
  voyages: NavSource;
  merge: NavSource;
  failures: NavSource;
  fleet: NavSource;
  captains: NavSource;
  /** The record kinds of a hub (HUB_RESOURCES), or undefined when it has none. */
  resources(hubKey: string): NavSource | undefined;
}

/** The hubs, in the dashboard's order. */
export function dashboardHubs(s: NavSources): NavHub[] {
  const records = (key: string): NavHub['children'] => {
    const source = s.resources(key);
    return source ? [{ kind: 'source', source }] : [];
  };
  return [
    { key: 'overview', label: 'Armada Overview', icon: 'home', tooltip: 'Fleet status at a glance: what needs you, what is running, and recent activity', children: [], command: { command: 'armada.home' } },
    { key: 'needsYou', label: 'Needs You', icon: 'bell', section: 'Operations', tooltip: 'Reviews, failures, and stalls awaiting your attention', children: [{ kind: 'source', source: s.needsYou }] },
    {
      key: 'dispatch', label: 'Dispatch', icon: 'send', section: 'Operations', tooltip: 'Send work to vessels, plan with a captain, and capture and refine backlog',
      children: [
        { kind: 'command', label: 'Dispatch a Mission...', icon: 'add', command: 'armada.dispatch' },
        { kind: 'command', label: 'Dispatch a Voyage...', icon: 'rocket', command: 'armada.dispatchVoyage' },
        ...records('dispatch'),
      ],
    },
    { key: 'fleetActions', label: 'Fleet Actions', icon: 'run-all', section: 'Operations', tooltip: 'Run a command or mission across many vessels and watch each run', children: records('fleetActions') },
    {
      key: 'missions', label: 'Missions', icon: 'tasklist', section: 'Operations', tooltip: 'Work units, plus Voyages, the Merge Queue, and the Black Box of failures',
      children: [
        { kind: 'source', source: s.missions, label: 'Missions', icon: 'tasklist' },
        { kind: 'source', source: s.voyages, label: 'Voyages', icon: 'rocket' },
        { kind: 'source', source: s.merge, label: 'Merge Queue', icon: 'git-merge' },
        { kind: 'source', source: s.failures, label: 'Black Box', icon: 'error', tooltip: 'Failure records for missions and voyages: where, how and why each failed, and how to recover' },
        ...records('missions'),
      ],
    },
    { key: 'activity', label: 'Activity', icon: 'pulse', section: 'Operations', tooltip: 'Requests, events, signals, history, token usage and captain usage', children: records('activity') },
    { key: 'vessels', label: 'Vessels', icon: 'repo', section: 'Build', tooltip: 'Repositories grouped by fleet, and their health', children: [{ kind: 'source', source: s.fleet }, ...records('vessels')] },
    { key: 'captains', label: 'Captains', icon: 'person', section: 'Build', tooltip: 'AI coding agents that execute missions, their docks, battery packs and evaluations', children: [{ kind: 'source', source: s.captains }, ...records('captains')] },
    { key: 'delivery', label: 'Delivery', icon: 'package', section: 'Build', tooltip: 'Deployments, Environments, Releases, Incidents, Checks, and Runbooks', children: records('delivery') },
    { key: 'configuration', label: 'Configuration', icon: 'settings', section: 'Configuration', tooltip: 'Workflow Profiles, Project Profiles, Skills, Personas, Pipelines, Prompts, Playbooks, Endpoints, Harbors and Memory', children: records('configuration') },
    { key: 'apiExplorer', label: 'API Explorer', icon: 'symbol-interface', section: 'System', tooltip: 'Call any Armada REST endpoint and see the response', children: [], command: { command: 'armada.api.call' } },
    { key: 'settings', label: 'Settings', icon: 'gear', section: 'System', tooltip: 'Server settings, Storage, CLI Tool Permissions, Diagnostics, and tenant, user and credential administration', children: records('settings') },
  ];
}

/** The old per-view tab commands (armada.tab.<viewId>) and the hub or tab each now opens. */
export const LEGACY_TAB_TARGETS: Record<string, string> = {
  armadaNeedsYou: 'armadaHub.needsYou',
  armadaMissions: 'armadaMissions',
  armadaVoyages: 'armadaVoyages',
  armadaCaptains: 'armadaHub.captains',
  armadaFleet: 'armadaHub.vessels',
  armadaMergeQueue: 'armadaMergeQueue',
  armadaFailures: 'armadaFailures',
  armadaBacklog: 'armadaHub.dispatch',
  armadaFleetActions: 'armadaHub.fleetActions',
  armadaDelivery: 'armadaHub.delivery',
  armadaConfiguration: 'armadaHub.configuration',
  armadaActivity: 'armadaHub.activity',
  armadaSystem: 'armadaHub.settings',
};
