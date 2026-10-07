import { askArmadaModelItem, dashboardModelItem, navModelSections, type NavModelSection } from '@dashboard/lib/navModel';
import type { IconName } from '../components/ui/Icon';
import type { TabKey } from './routeTypes';

/**
 * The mobile navigation, built from the dashboard's shared nav model (lib/navModel.ts) so both clients list the
 * same destinations in the same groups: Ask, Approvals, Notifications, then OPERATIONS, DELIVERY, BUILD,
 * CONFIGURATION, ACTIVITY, SYSTEM. Phones split the sections over the Work and More tabs; tablets show them all in
 * the sidebar.
 */
export interface MobileNavItem {
  key: string;
  /** App path (dashboard paths, except '/' which is '/home'). */
  to: string;
  label: string;
  icon: IconName;
  tooltip?: string;
}

export interface MobileNavSection {
  key: string;
  label: string;
  items: MobileNavItem[];
}

/** Dashboard nav icon keys (navIcons.tsx) to Ionicons glyphs. Unknown keys fall back to a neutral glyph. */
const ICONS: Record<string, IconName> = {
  dashboard: 'speedometer-outline',
  ask: 'chatbubbles-outline',
  needsYou: 'alert-circle-outline',
  planning: 'bulb-outline',
  dispatch: 'paper-plane-outline',
  fleetActions: 'git-network-outline',
  missions: 'flag-outline',
  delivery: 'rocket-outline',
  vessels: 'boat-outline',
  captains: 'person-circle-outline',
  configuration: 'options-outline',
  activity: 'pulse-outline',
  cliPermissions: 'shield-checkmark-outline',
  apiExplorer: 'code-slash-outline',
  server: 'server-outline',
};

export function iconFor(key: string): IconName {
  return ICONS[key] ?? 'ellipse-outline';
}

/** Dashboard path to app path ('/' is the Home screen at '/home'). */
export function appPathFor(dashboardPath: string): string {
  return dashboardPath === '/' ? '/home' : dashboardPath;
}

function fromSection(section: NavModelSection): MobileNavSection {
  return {
    key: section.key,
    label: section.label,
    items: section.items.filter((item) => !item.hidden).map((item) => ({
      key: item.to,
      to: appPathFor(item.to),
      label: item.label,
      icon: iconFor(item.icon),
      tooltip: item.tooltip,
    })),
  };
}

export const ASK_ITEM: MobileNavItem = { key: 'ask', to: askArmadaModelItem.to, label: askArmadaModelItem.label, icon: iconFor(askArmadaModelItem.icon), tooltip: askArmadaModelItem.tooltip };
export const APPROVALS_ITEM: MobileNavItem = { key: 'approvals', to: '/approvals', label: 'Approvals', icon: 'checkmark-done-circle-outline' };
export const NOTIFICATIONS_ITEM: MobileNavItem = { key: 'notifications', to: '/notification-center', label: 'Notifications', icon: 'notifications-outline' };
export const HOME_ITEM: MobileNavItem = { key: 'home', to: appPathFor(dashboardModelItem.to), label: dashboardModelItem.label, icon: iconFor(dashboardModelItem.icon), tooltip: dashboardModelItem.tooltip };
export const PREFERENCES_ITEM: MobileNavItem = { key: 'preferences', to: '/preferences', label: 'Preferences', icon: 'color-palette-outline' };
export const PROFILES_ITEM: MobileNavItem = { key: 'profiles', to: '/profiles', label: 'Servers', icon: 'swap-horizontal-outline' };

/** Every dashboard nav section, in dashboard order. */
export const NAV_SECTIONS: MobileNavSection[] = navModelSections.map(fromSection);

/** Which phone tab lists each section (Work: day-to-day operations; More: setup and administration). */
export const SECTION_TAB: Record<string, TabKey> = {
  operations: 'work',
  delivery: 'work',
  fleet: 'work',
  configuration: 'more',
  activity: 'more',
  system: 'more',
};

export function sectionsForTab(tab: TabKey): MobileNavSection[] {
  return NAV_SECTIONS.filter((s) => (SECTION_TAB[s.key] ?? 'more') === tab);
}

/** Every item reachable from the navigation (for the tablet sidebar and tests). */
export function allNavItems(): MobileNavItem[] {
  return [ASK_ITEM, APPROVALS_ITEM, NOTIFICATIONS_ITEM, HOME_ITEM, ...NAV_SECTIONS.flatMap((s) => s.items), PREFERENCES_ITEM, PROFILES_ITEM];
}

/** The nav item to highlight for a path: the longest item path that equals or prefixes it. */
export function activeNavKey(pathname: string, items: MobileNavItem[] = allNavItems()): string | null {
  let best: MobileNavItem | null = null;
  for (const item of items) {
    if (pathname === item.to || pathname.startsWith(item.to + '/')) {
      if (!best || item.to.length > best.to.length) best = item;
    }
  }
  return best?.key ?? null;
}
