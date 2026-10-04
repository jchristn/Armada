import { describe, it, expect } from 'vitest';
import { dashboardItem, askArmadaItem, navSections, flattenNavCommands } from './navConfig';

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
      '/', '/ask', '/inbox', '/planning', '/dispatch', '/missions',
      '/delivery', '/fleet-actions', '/vessels', '/captains', '/configuration', '/activity',
      '/jobs', '/api-explorer', '/server',
    ];
    for (const target of present) {
      expect(allTargets).toContain(target);
    }
    // 15 top-level destinations (Jobs under Activity, Fleet Actions under Delivery); admin lives as tabs under Settings.
    expect(commands).toHaveLength(15);
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
