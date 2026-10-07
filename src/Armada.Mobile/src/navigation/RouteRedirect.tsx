import { Redirect } from 'expo-router';
import type { Href } from 'expo-router';
import { appPathFromLink } from './deepLinks';
import { routeByPattern } from './routeMatch';

/** A dashboard redirect route (e.g. /voyages -> /missions?tab=voyages), resolved like any other link. */
export function RouteRedirect({ pattern }: { pattern: string }) {
  const target = appPathFromLink(routeByPattern(pattern)?.redirect ?? '/') ?? '/home';
  return <Redirect href={target as Href} />;
}
