/**
 * Single source of truth for the navigation model, shared by the dashboard and the mobile app. The dashboard's
 * `navConfig` attaches its SVG icons to these entries for `Layout` (the sidebar) and `CommandPalette` (the Cmd-K
 * launcher); the mobile app maps `icon` keys to its own glyphs. The nav-inventory tests assert against it, so the
 * navigation model lives in exactly one place. Pure data: no React, no DOM.
 *
 * Ordering rule: the nav reads Dashboard, then Ask Armada (the primary workflow
 * interface, a standalone top-level item), then the grouped sections. The
 * consolidation phases edit `navSections` in place as each hub is built; this
 * file always reflects the currently shipped nav.
 */

export interface NavModelItem {
  key?: string;
  to: string;
  label: string;
  /** Icon key; each client maps it to its own glyph (the dashboard's navIcons). */
  icon: string;
  hidden?: boolean;
  tooltip?: string;
}

export interface NavModelSection {
  key: string;
  label: string;
  matchers: string[];
  items: NavModelItem[];
}

export const dashboardModelItem: NavModelItem = {
  to: '/',
  label: 'Dashboard',
  tooltip: 'Overview of captains, missions, and voyages',
  icon: 'dashboard',
};

/**
 * Ask Armada is a standalone top-level item, rendered directly under Dashboard
 * and above every grouped section. It is expected to be the primary way people
 * drive Armada, so it is never nested inside a section, a tab, or a menu.
 */
export const askArmadaModelItem: NavModelItem = {
  to: '/ask',
  label: 'Ask Armada',
  tooltip: 'Ask about fleet state in plain language and drive work from the conversation',
  icon: 'ask',
};

export const navModelSections: NavModelSection[] = [
  {
    key: 'operations',
    label: 'OPERATIONS',
    matchers: [
      '/inbox', '/dispatch', '/fleet-actions', '/planning', '/backlog', '/objectives', '/voyages', '/missions', '/merge-queue',
      '/activity', '/history', '/requests', '/events', '/signals', '/jobs', '/blackbox',
    ],
    items: [
      { to: '/inbox', label: 'Needs You', tooltip: 'Reviews, failures, and stalls awaiting your attention', icon: 'needsYou' },
      { to: '/dispatch', label: 'Dispatch', tooltip: 'Send work to vessels, plan with a captain on the Planning tab, and capture and refine backlog on the Backlog tab', icon: 'dispatch' },
      { to: '/fleet-actions', label: 'Fleet Actions', tooltip: 'Run a command or mission across many vessels and watch each run', icon: 'fleetActions' },
      { to: '/missions', label: 'Missions', tooltip: 'Work units, plus Voyages, the full Merge Queue, and the Black Box of failures as tabs', icon: 'missions' },
      { to: '/activity', label: 'Activity', tooltip: 'One log across requests, events, signals, and history, plus background jobs on the Jobs tab', icon: 'activity' },
    ],
  },
  {
    key: 'fleet',
    label: 'BUILD',
    matchers: ['/fleets', '/vessels', '/workspace', '/captains', '/docks', '/delivery', '/checks', '/environments', '/deployments', '/releases', '/incidents', '/runbooks'],
    items: [
      { to: '/vessels', label: 'Vessels', tooltip: 'Repositories grouped by fleet, plus the vessel workspace, on one surface', icon: 'vessels' },
      { to: '/captains', label: 'Captains', tooltip: 'AI coding agents that execute missions', icon: 'captains' },
      { to: '/delivery', label: 'Delivery', tooltip: 'Deployments, Environments, Releases, Incidents, Checks, and Runbooks as tabs', icon: 'delivery' },
    ],
  },
  {
    key: 'configuration',
    label: 'CONFIGURATION',
    matchers: ['/configuration', '/workflow-profiles', '/project-profiles', '/skills', '/personas', '/pipelines', '/prompt-templates', '/playbooks'],
    items: [
      { to: '/configuration', label: 'Configuration', tooltip: 'Workflow Profiles, Project Profiles, Skills, Personas, Pipelines, Prompts, and Playbooks as tabs', icon: 'configuration' },
    ],
  },
  {
    key: 'system',
    label: 'SYSTEM',
    matchers: ['/storage', '/server', '/doctor', '/settings', '/admin', '/api-explorer', '/cli-permissions'],
    items: [
      { to: '/api-explorer', label: 'API Explorer', tooltip: 'Browse the live OpenAPI document, execute requests, and inspect responses', icon: 'apiExplorer' },
      { to: '/server', label: 'Settings', tooltip: 'Server settings, Storage, CLI Tool Permissions, Diagnostics, and tenant/user/credential administration as tabs', icon: 'server' },
    ],
  },
];

/** Section keys whose collapse-state defaults to open on first paint (daily drivers). */
export const DEFAULT_EXPANDED_SECTIONS: Record<string, boolean> = {
  operations: true,
  fleet: true,
  configuration: true,
  system: true,
};

/**
 * Destinations that used to be their own nav item and now live as a tab inside a hub. They are not sidebar items,
 * but the command palette still lists them under their old names so muscle memory ("Jobs", "Storage", "Black Box") keeps
 * working; each points at the tab's URL (the old routes redirect there too).
 */
export const navShortcuts: Array<NavModelItem & { section: string }> = [
  { to: '/dispatch?tab=planning', label: 'Planning', section: 'OPERATIONS', tooltip: 'Plan with a captain, preserve the transcript, and dispatch directly from the session', icon: 'planning' },
  { to: '/activity?source=jobs', label: 'Jobs', section: 'OPERATIONS', tooltip: 'Background jobs and their status', icon: 'activity' },
  { to: '/missions?tab=blackbox', label: 'Black Box', section: 'OPERATIONS', tooltip: 'Failure records for missions and voyages: where, how and why each failed, and how to recover', icon: 'blackbox' },
  { to: '/server?tab=storage', label: 'Storage', section: 'SYSTEM', tooltip: 'Inspect managed files, database allocation, host capacity and record counts', icon: 'server' },
  { to: '/server?tab=cli-permissions', label: 'CLI Tool Permissions', section: 'SYSTEM', tooltip: 'Approve CLI tool requests from captains and manage allow and deny rules', icon: 'cliPermissions' },
];

/**
 * Flatten every reachable destination (Dashboard, Ask Armada, each section item, then the hub-tab shortcuts) into a
 * simple list the command palette can search. Icons are dropped.
 */
export function flattenNavCommands(): Array<{ to: string; label: string; section: string }> {
  const commands: Array<{ to: string; label: string; section: string }> = [
    { to: dashboardModelItem.to, label: dashboardModelItem.label, section: '' },
    { to: askArmadaModelItem.to, label: askArmadaModelItem.label, section: '' },
  ];
  for (const section of navModelSections) {
    for (const item of section.items) {
      if (item.hidden) continue;
      commands.push({ to: item.to, label: item.label, section: section.label });
    }
  }
  for (const shortcut of navShortcuts) {
    commands.push({ to: shortcut.to, label: shortcut.label, section: shortcut.section });
  }
  return commands;
}
