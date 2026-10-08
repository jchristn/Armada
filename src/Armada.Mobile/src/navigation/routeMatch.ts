import { DASHBOARD_ROUTES } from './dashboardRoutes.generated';
import type { DashboardRoute } from './routeTypes';

export interface RouteMatch {
  route: DashboardRoute;
  params: Record<string, string>;
}

function split(path: string): string[] {
  return path.split('/').filter(Boolean);
}

/**
 * Decode one path parameter, or null when it is malformed percent-encoding or decodes to something that is not a
 * single path segment ('/', '\\', '.', '..', control characters). Screens put parameters into API paths, so an
 * encoded '..%2F' must never become a traversal there.
 */
export function decodeParam(segment: string): string | null {
  let value: string;
  try {
    value = decodeURIComponent(segment);
  } catch {
    return null;
  }
  if (!value || value === '.' || value === '..' || /[/\\\u0000-\u001f\u007f]/.test(value)) return null;
  return value;
}

/**
 * Find the dashboard route for an app path; static segments beat parameters (as in both routers). A parameter that
 * does not decode to a single safe segment matches nothing (see decodeParam).
 */
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
        const value = decodeParam(parts[i]);
        if (value === null) {
          ok = false;
          break;
        }
        params[segs[i].slice(1)] = value;
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
