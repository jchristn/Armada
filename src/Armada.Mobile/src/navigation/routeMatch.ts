import { DASHBOARD_ROUTES } from './dashboardRoutes.generated';
import type { DashboardRoute } from './routeTypes';

export interface RouteMatch {
  route: DashboardRoute;
  params: Record<string, string>;
}

function split(path: string): string[] {
  return path.split('/').filter(Boolean);
}

/** Find the dashboard route for an app path; static segments beat parameters (as in both routers). */
export function matchRoute(path: string, routes: DashboardRoute[] = DASHBOARD_ROUTES): RouteMatch | null {
  const clean = path.split(/[?#]/)[0];
  const parts = split(clean);
  let best: RouteMatch | null = null;
  let bestScore = -1;
  for (const route of routes) {
    const segs = split(route.pattern);
    if (segs.length !== parts.length) continue;
    const params: Record<string, string> = {};
    let score = 0;
    let ok = true;
    for (let i = 0; i < segs.length; i++) {
      if (segs[i].startsWith(':')) {
        params[segs[i].slice(1)] = decodeURIComponent(parts[i]);
      } else if (segs[i] === parts[i]) {
        score += 1;
      } else {
        ok = false;
        break;
      }
    }
    if (ok && score > bestScore) {
      best = { route, params };
      bestScore = score;
    }
  }
  return best;
}

/** The route entry for a pattern (for screens that know their own pattern). */
export function routeByPattern(pattern: string): DashboardRoute | undefined {
  return DASHBOARD_ROUTES.find((r) => r.pattern === pattern);
}
