import { decodeParam } from './routeMatch';

/**
 * Map links into app paths. Accepted forms, all resolving to the same screen as on the dashboard:
 *   armada://missions/msn_1            (custom scheme; host is the first path segment)
 *   armada:///missions/msn_1
 *   https://admiral.example/dashboard/missions/msn_1?tab=log   (a pasted dashboard URL)
 *   /dashboard/missions/msn_1, /missions/msn_1                  (paths)
 * The dashboard's root "/" is the mobile Home screen "/home". Anything that is not a safe relative app path
 * (other schemes, "..", control characters, and segments that decode to any of those or contain an encoded '/' or
 * '\\', or are malformed percent-encoding) maps to null and is ignored.
 */
export function appPathFromLink(link: string | null | undefined): string | null {
  if (!link) return null;
  let value = link.trim();
  if (!value || /[\u0000-\u001f\u007f]/.test(value)) return null;

  const scheme = /^([a-z][a-z0-9+.-]*):/i.exec(value);
  if (scheme) {
    const name = scheme[1].toLowerCase();
    if (name === 'armada') {
      value = value.slice(scheme[0].length).replace(/^\/\//, '/');
    } else if (name === 'http' || name === 'https') {
      const m = /^[a-z]+:\/\/[^/?#]*(.*)$/i.exec(value);
      value = m ? m[1] || '/' : '/';
    } else {
      return null;
    }
  }

  if (!value.startsWith('/')) value = `/${value}`;
  const queryIndex = value.search(/[?#]/);
  let path = queryIndex >= 0 ? value.slice(0, queryIndex) : value;
  const query = queryIndex >= 0 ? value.slice(queryIndex).split('#')[0] : '';

  path = path.replace(/\/{2,}/g, '/');
  if (path === '/dashboard' || path.startsWith('/dashboard/')) path = path.slice('/dashboard'.length) || '/';
  if (path.split('/').some((seg) => seg === '..' || seg === '.')) return null;
  // Also after decoding: screens build API paths from these segments, so '..%2Fusers' must not get through.
  if (path.split('/').some((seg) => seg !== '' && decodeParam(seg) === null)) return null;
  if (path.length > 1) path = path.replace(/\/+$/, '');
  if (path === '/') path = '/home';
  return `${path}${query}`;
}

/** True when a link names no page (armada://, armada:///, a bare path '/'): it just opens the app. */
export function isRootLink(link: string | null | undefined): boolean {
  if (!link) return true;
  const value = link.trim().replace(/^armada:/i, '').replace(/[?#].*$/, '');
  return /^\/*$/.test(value);
}
