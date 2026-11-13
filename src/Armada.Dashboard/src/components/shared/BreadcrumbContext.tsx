import { createContext, useCallback, useContext, useLayoutEffect, useMemo, useState, type ReactNode } from 'react';
import { useLocation } from 'react-router-dom';
import { useLocale } from '../../context/LocaleContext';
import { dashboardBreadcrumbs, normalizeDashboardBreadcrumbs } from '../../lib/dashboardNavigation';
import type { BreadcrumbItem } from './Breadcrumbs';

interface Registration {
  routeKey: string;
  register: (routeKey: string, items: readonly BreadcrumbItem[], priority: number) => () => void;
}
interface Override { routeKey: string; owner: symbol; priority: number; items: readonly BreadcrumbItem[] }
const RegistrationContext = createContext<Registration | null>(null);
const TrailContext = createContext<readonly BreadcrumbItem[]>([]);

/** Owns the single breadcrumb location for every dashboard route, including loading and error states. */
export function DashboardBreadcrumbProvider({ children }: { children: ReactNode }) {
  const location = useLocation();
  const { t } = useLocale();
  const routeKey = `${location.key}:${location.pathname}${location.search}${location.hash}`;
  const [overrides, setOverrides] = useState<Override[]>([]);
  const register = useCallback((key: string, items: readonly BreadcrumbItem[], priority: number) => {
    const owner = Symbol('breadcrumb');
    setOverrides(current => [...current, { routeKey: key, owner, items, priority }]);
    return () => setOverrides(current => current.filter(item => item.owner !== owner));
  }, []);
  const registration = useMemo(() => ({ routeKey, register }), [routeKey, register]);
  // A loaded detail trail wins over its containing hub tab, regardless of effect mount order.
  const override = overrides.reduce<Override | null>((selected, item) => item.routeKey === routeKey
    && (!selected || item.priority >= selected.priority) ? item : selected, null);
  const items = override
    ? normalizeDashboardBreadcrumbs(override.items, t)
    : dashboardBreadcrumbs(location.pathname, location.search, t);
  return (
    <RegistrationContext.Provider value={registration}>
      <TrailContext.Provider value={items}>{children}</TrailContext.Provider>
    </RegistrationContext.Provider>
  );
}

/** Detail pages provide loaded names; URL-scoped ownership prevents stale labels after navigation. */
export function useDashboardBreadcrumbs(items: readonly BreadcrumbItem[] | undefined, priority = 1): boolean {
  const registration = useContext(RegistrationContext);
  // Page JSX commonly supplies a new array each render. Register only actual content changes.
  const signature = JSON.stringify(items);
  const stableItems = useMemo(() => items, [signature]); // eslint-disable-line react-hooks/exhaustive-deps
  useLayoutEffect(() => {
    if (registration && stableItems?.length) return registration.register(registration.routeKey, stableItems, priority);
  }, [registration, stableItems, priority]);
  return registration !== null;
}

/** Current global trail; only the layout renderer consumes updates. */
export function useDashboardBreadcrumbTrail() {
  return useContext(TrailContext);
}
