import { appPathFromLink, isRootLink } from '../navigation/deepLinks';
import { notePendingLink } from '../navigation/pendingLink';
import { matchRoute } from '../navigation/routeMatch';

/** App-only screens a link may open besides the dashboard routes. */
const APP_ROUTES = ['/approvals', '/notification-center', '/more', '/preferences', '/profiles'];

/**
 * Every incoming link (armada://..., a pasted dashboard URL, a notification) is mapped to the app path for the same
 * dashboard page. A link that names no page just opens the app (Ask); unknown or unsafe links are ignored. A link to
 * a real page is also remembered, so it still opens if sign-in has to come first.
 */
export function redirectSystemPath({ path }: { path: string; initial: boolean }): string | null {
  try {
    if (isRootLink(path)) return '/';
    const mapped = appPathFromLink(path);
    if (!mapped) return null;
    const pathOnly = mapped.split(/[?#]/)[0];
    if (!matchRoute(pathOnly) && !APP_ROUTES.includes(pathOnly)) return null;
    notePendingLink(mapped);
    return mapped;
  } catch {
    return null;
  }
}
