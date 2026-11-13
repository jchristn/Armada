# Dashboard navigation and breadcrumbs

The dashboard layout owns one breadcrumb position above the routed page. Every signed-in route uses it, including loading, error, and not-found views. The shared renderer owns the HTML, accessibility attributes, separators, and CSS. Pages supply labels and links; they do not render or style their own breadcrumb navigation.

## Components

- `src/Armada.Dashboard/src/components/shared/Breadcrumbs.tsx` is the reusable component. Inside the dashboard provider it registers a page's trail; outside the provider it renders the same shared markup directly.
- `PageHeader` accepts `breadcrumb` items and delegates to `Breadcrumbs`.
- `DashboardBreadcrumbProvider` owns route-scoped registrations. Navigating by pathname, query, or hash clears the previous route's trail. Detail registrations take precedence over enclosing hub tabs, and closing a detail restores the hub trail.
- `DashboardBreadcrumbs` renders once in `Layout`. Page-loading Suspense boundaries live below it so lazy loading cannot replace the navigation shell.
- `lib/dashboardNavigation.ts` defines route fallbacks, hub/tab ancestry, and canonical collection links. Every trail starts at Dashboard. Collection links go directly to the owning hub tab, rather than through retired list routes. Loaded entity names replace fallback IDs through page registration.
- Shared `Tabs` registers the actual selected, visible outer tab. A bookmarked admin-only or unknown tab therefore shows the same fallback tab in the breadcrumb and the page.

For example, a captain evaluation uses `Dashboard > Captains > {captain name} > Evaluations > {run}`. The captain and filtered evaluations list are links. A persona uses `Dashboard > Configuration > Personas > {name}`. Vessel history uses `Dashboard > Vessels > {vessel name} > History`.

## Adding a page

1. Register its route in `App.tsx` and add its fallback ancestry to `dashboardNavigation.ts`. Every page must be reachable through a route or a mounted hub tab.
2. For a detail page, supply the loaded entity label through `PageHeader`:

```tsx
<PageHeader
  title={captain.name}
  breadcrumb={[
    { label: t('Captains'), to: '/captains' },
    { label: captain.name },
  ]}
/>
```

The layout supplies Dashboard, canonical hub ancestry, and the shared rendering. A page with a custom header can use `<Breadcrumbs items={...} />` instead. Do not add breadcrumb HTML, separators, inline styles, or another global renderer to a page.

3. Keep hub tab metadata aligned with the actual mounted tabs. Unknown URLs must reach the not-found page with recovery navigation. Retired collection URLs use `LegacyPageRedirect` to keep filters, hash, and router state when moving to a hub; special Planning, CLI Permissions, and Black Box redirects translate their query keys.
4. Run `npm --prefix src/Armada.Dashboard run build` and `npm --prefix src/Armada.Dashboard run test:run`.

## Audit coverage

The 2026-11-02 audit covers 87 page modules, 52 active page-route patterns including the home page, nine hubs, and 47 mounted tabs. The tests derive the actual router/page import graph and compare it with the breadcrumb registry. They reject unreachable page modules, unused router page loaders, missing route metadata, and sidebar, command-palette, or source link targets without an explicit route. A catch-all route cannot mask an invalid target in these checks.

Rendered tests cover a single global breadcrumb, router basenames, translated/static labels, loaded entity names, canonical hub links, current-page accessibility, route/query changes, StrictMode, nested detail precedence and cleanup, role-hidden tabs, lazy loading, and not-found recovery.

The unused legacy `pages/Settings.tsx` was removed. Its unique account usage-capture controls now appear on the active Server/Settings page, with their tests retained on `UsageCaptureSettingsPanel`. The old `/settings` URL remains a redirect to the live page.
