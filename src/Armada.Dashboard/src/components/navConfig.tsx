import { type ReactNode } from 'react';
import { icons } from './navIcons';
import {
  askArmadaModelItem,
  dashboardModelItem,
  navModelSections,
  type NavModelItem,
} from '../lib/navModel';

/**
 * The dashboard's sidebar navigation: the shared model in `lib/navModel.ts` (one source of truth for the
 * dashboard and the mobile app) with the dashboard's SVG icons attached. `Layout` (the sidebar) and
 * `CommandPalette` (the Cmd-K launcher) consume this, and the nav-inventory test asserts against it.
 */

export { DEFAULT_EXPANDED_SECTIONS, flattenNavCommands } from '../lib/navModel';

export interface NavItem extends Omit<NavModelItem, 'icon'> {
  icon: ReactNode;
}

export interface NavSection {
  key: string;
  label: string;
  matchers: string[];
  items: NavItem[];
}

function withIcon(item: NavModelItem): NavItem {
  return { ...item, icon: icons[item.icon] };
}

export const dashboardItem: NavItem = withIcon(dashboardModelItem);

export const askArmadaItem: NavItem = withIcon(askArmadaModelItem);

export const navSections: NavSection[] = navModelSections.map((section) => ({
  ...section,
  items: section.items.map(withIcon),
}));
