import { useCallback, useMemo, type KeyboardEvent, type ReactNode } from 'react';
import { useLocation, useSearchParams } from 'react-router-dom';
import { useDashboardBreadcrumbs } from './BreadcrumbContext';
import { dashboardHubs } from '../../lib/dashboardNavigation';
import { useLocale } from '../../context/LocaleContext';

export interface TabDef {
  /** Stable key stored in the URL query string. */
  key: string;
  /** Human-readable label (translated through `t`). */
  label: string;
  /** Rendered only while this tab is active (lazy-mounted). */
  render: () => ReactNode;
  hidden?: boolean;
  /** English hover text saying what the tab holds (translated through `t`). */
  tooltip?: string;
}

interface TabsProps {
  tabs: TabDef[];
  /** Query-string parameter that stores the active tab key. Defaults to `tab`. */
  param?: string;
  /** Fallback tab key when the URL has none or an unknown one. Defaults to the first visible tab. */
  defaultTabKey?: string;
  /** Optional content rendered on the right of the tab strip (actions, counts). */
  actions?: ReactNode;
  ariaLabel?: string;
}

/**
 * URL-synced tabbed surface. The active tab lives in the query string (default
 * `?tab=`), so bookmarks, deep links, and chart click-throughs can land on a
 * specific tab, and only the active tab's panel is mounted. Keyboard support
 * follows the WAI-ARIA tabs pattern (arrow keys, Home/End). This is the single
 * primitive every consolidated surface folds its former pages into.
 */
export default function Tabs({ tabs, param = 'tab', defaultTabKey, actions, ariaLabel }: TabsProps) {
  const { t } = useLocale();
  const [searchParams, setSearchParams] = useSearchParams();

  const visibleTabs = useMemo(() => tabs.filter((tab) => !tab.hidden), [tabs]);

  const fallbackKey = defaultTabKey && visibleTabs.some((tab) => tab.key === defaultTabKey)
    ? defaultTabKey
    : visibleTabs[0]?.key;

  const requestedKey = searchParams.get(param);
  const activeKey = requestedKey && visibleTabs.some((tab) => tab.key === requestedKey)
    ? requestedKey
    : fallbackKey;

  const selectTab = useCallback((key: string) => {
    const next = new URLSearchParams(searchParams);
    next.set(param, key);
    setSearchParams(next);
  }, [param, searchParams, setSearchParams]);

  const onKeyDown = useCallback((event: KeyboardEvent<HTMLDivElement>) => {
    if (visibleTabs.length === 0) return;
    const currentIndex = visibleTabs.findIndex((tab) => tab.key === activeKey);
    let nextIndex = -1;
    if (event.key === 'ArrowRight' || event.key === 'ArrowDown') nextIndex = (currentIndex + 1) % visibleTabs.length;
    else if (event.key === 'ArrowLeft' || event.key === 'ArrowUp') nextIndex = (currentIndex - 1 + visibleTabs.length) % visibleTabs.length;
    else if (event.key === 'Home') nextIndex = 0;
    else if (event.key === 'End') nextIndex = visibleTabs.length - 1;
    if (nextIndex >= 0) {
      event.preventDefault();
      selectTab(visibleTabs[nextIndex].key);
    }
  }, [activeKey, selectTab, visibleTabs]);

  const activeTab = visibleTabs.find((tab) => tab.key === activeKey);
  const location = useLocation();
  const hub = dashboardHubs.find(h => h.path === (location.pathname.replace(/\/+$/, '') || '/') && h.param === param);
  useDashboardBreadcrumbs(hub && activeTab ? [
    { label: t(hub.label), to: hub.path },
    ...(activeTab.label === hub.label ? [] : [{ label: t(activeTab.label) }]),
  ] : undefined, 0);

  return (
    <div className="page-tabs">
      <div className="page-tabs-bar">
        <div
          className="page-tabs-list"
          role="tablist"
          aria-label={ariaLabel ? t(ariaLabel) : undefined}
          onKeyDown={onKeyDown}
        >
          {visibleTabs.map((tab) => {
            const selected = tab.key === activeKey;
            return (
              <button
                key={tab.key}
                type="button"
                role="tab"
                id={`tab-${param}-${tab.key}`}
                aria-selected={selected}
                aria-controls={`tabpanel-${param}-${tab.key}`}
                tabIndex={selected ? 0 : -1}
                className={`page-tab${selected ? ' active' : ''}`}
                onClick={() => selectTab(tab.key)}
                title={tab.tooltip ? t(tab.tooltip) : undefined}
              >
                {t(tab.label)}
              </button>
            );
          })}
        </div>
        {actions && <div className="page-tabs-actions">{actions}</div>}
      </div>
      {activeTab && (
        <div
          role="tabpanel"
          id={`tabpanel-${param}-${activeTab.key}`}
          aria-labelledby={`tab-${param}-${activeTab.key}`}
          className="page-tab-panel"
        >
          {activeTab.render()}
        </div>
      )}
    </div>
  );
}
