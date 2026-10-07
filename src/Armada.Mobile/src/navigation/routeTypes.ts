/** The bottom tab (phone) whose stack hosts a route. Tablets show the same stacks behind the sidebar. */
export type TabKey = 'ask' | 'approvals' | 'work' | 'more';

/** One dashboard route and its mobile screen (generated into dashboardRoutes.generated.ts). */
export interface DashboardRoute {
  /** Mobile URL pattern (the dashboard's, with '/' as '/home' and optional segments split). */
  pattern: string;
  /** The dashboard route pattern it came from. */
  dashboard: string;
  tab: TabKey;
  /** English title, translated at render time. */
  title: string;
  /** MOBILE_APP_PLAN.md workstream that implements the screen. */
  workstream: string;
  /** Dashboard redirect target (path plus query) when the route only redirects. */
  redirect: string | null;
  /** Expo Router file under src/app. */
  file: string;
}
