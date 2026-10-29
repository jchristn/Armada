import { describe, it, expect } from 'vitest';
import { dashboardItem, askArmadaItem, navSections, flattenNavCommands, DEFAULT_EXPANDED_SECTIONS } from './navConfig';

/**
 * Locks the consolidated navigation model so regressions cannot silently
 * re-add or re-nest the pages the consolidation folded away.
 */
describe('navConfig', () => {
  const commands = flattenNavCommands();
  const allTargets = commands.map((c) => c.to);

  it('leads with Dashboard then a standalone Ask Armada', () => {
    expect(dashboardItem.to).toBe('/');
    expect(askArmadaItem.to).toBe('/ask');
    expect(commands[0].to).toBe('/');
    expect(commands[1].to).toBe('/ask');
    // Ask Armada must be standalone (section === ''), not nested in a group.
    expect(commands[1].section).toBe('');
  });

  it('exposes exactly the consolidated destinations', () => {
    const present = [
      '/', '/ask', '/inbox', '/dispatch', '/missions',
      '/delivery', '/fleet-actions', '/vessels', '/captains', '/configuration', '/activity',
      '/api-explorer', '/server', '/blackbox',
    ];
    const sidebarTargets = [dashboardItem, askArmadaItem, ...navSections.flatMap((s) => s.items)].map((i) => i.to);
    expect(sidebarTargets).toEqual(expect.arrayContaining(present));
    // 14 sidebar destinations (Black Box joined OPERATIONS in v1.2.0); Planning, Jobs, Storage and CLI Tool
    // Permissions are hub tabs since v1.2.0.
    expect(sidebarTargets).toHaveLength(14);
  });

  it('lays out the v1.2.0 sections: Activity under Missions, Delivery in BUILD, no ACTIVITY or DELIVERY section', () => {
    expect(navSections.map((s) => s.label)).toEqual(['OPERATIONS', 'BUILD', 'CONFIGURATION', 'SYSTEM']);
    const byKey = Object.fromEntries(navSections.map((s) => [s.key, s.items.map((i) => i.to)]));
    expect(byKey.operations).toEqual(['/inbox', '/dispatch', '/fleet-actions', '/missions', '/activity', '/blackbox']);
    expect(byKey.fleet).toEqual(['/vessels', '/captains', '/delivery']);
    expect(byKey.system).toEqual(['/api-explorer', '/server']);
    const operations = navSections.find((s) => s.key === 'operations')!;
    // Old Activity and Delivery child routes still highlight the section that now hosts them.
    for (const route of ['/activity', '/jobs', '/events', '/signals', '/requests', '/history', '/planning']) {
      expect(operations.matchers).toContain(route);
    }
    expect(navSections.find((s) => s.key === 'fleet')!.matchers).toEqual(expect.arrayContaining(['/delivery', '/deployments', '/checks']));
    expect(Object.keys(DEFAULT_EXPANDED_SECTIONS).sort()).toEqual(navSections.map((s) => s.key).sort());
  });

  it('mentions the new tabs in the Activity, Dispatch, and Settings tooltips', () => {
    const items = navSections.flatMap((s) => s.items);
    expect(items.find((i) => i.to === '/activity')!.tooltip).toContain('Jobs tab');
    expect(items.find((i) => i.to === '/dispatch')!.tooltip).toContain('Planning tab');
    const settings = items.find((i) => i.to === '/server')!.tooltip!;
    expect(settings).toContain('Storage');
    expect(settings).toContain('CLI Tool Permissions');
  });

  it('keeps the folded pages searchable in the command palette, pointing at their new tabs', () => {
    expect(commands.slice(-4)).toEqual([
      { to: '/dispatch?tab=planning', label: 'Planning', section: 'OPERATIONS' },
      { to: '/activity?source=jobs', label: 'Jobs', section: 'OPERATIONS' },
      { to: '/server?tab=storage', label: 'Storage', section: 'SYSTEM' },
      { to: '/server?tab=cli-permissions', label: 'CLI Tool Permissions', section: 'SYSTEM' },
    ]);
    for (const old of ['/planning', '/jobs', '/storage', '/cli-permissions']) {
      expect(allTargets).not.toContain(old);
    }
  });

  it('no longer surfaces the folded-away pages as nav items', () => {
    const removed = [
      '/notifications', '/docks', '/doctor', '/fleets', '/workspace',
      '/history', '/requests', '/events', '/signals',
      '/workflow-profiles', '/project-profiles', '/skills', '/personas',
      '/pipelines', '/prompt-templates', '/playbooks',
      '/voyages', '/merge-queue', '/backlog',
      '/checks', '/environments', '/deployments', '/releases', '/incidents', '/runbooks',
      '/admin/tenants', '/admin/users', '/admin/credentials',
    ];
    for (const target of removed) {
      expect(allTargets).not.toContain(target);
    }
  });

  it('places Fleet Actions in the OPERATIONS section right after Dispatch, with a matcher for run detail routes', () => {
    const operations = navSections.find((s) => s.key === 'operations');
    expect(operations).toBeDefined();
    const targets = operations!.items.map((i) => i.to);
    expect(targets.indexOf('/fleet-actions')).toBe(targets.indexOf('/dispatch') + 1);
    expect(operations!.matchers).toContain('/fleet-actions');
    const item = operations!.items.find((i) => i.to === '/fleet-actions');
    expect(item!.icon).toBeTruthy();
    // Run detail pages (/fleet-actions/runs/:id) must highlight the same section.
    expect(operations!.matchers.some((m) => '/fleet-actions/runs/far_123'.startsWith(m))).toBe(true);
    // And no other section claims it.
    const others = navSections.filter((s) => s.key !== 'operations');
    expect(others.some((s) => s.items.some((i) => i.to === '/fleet-actions') || s.matchers.includes('/fleet-actions'))).toBe(false);
  });

  it('keeps section keys aligned with the collapse-default map', () => {
    const keys = navSections.map((s) => s.key);
    expect(new Set(keys).size).toBe(keys.length);
  });
});
