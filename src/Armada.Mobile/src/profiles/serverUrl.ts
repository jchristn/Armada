/**
 * Server URL rules for profiles: http or https only, no credentials, query, or fragment; trailing slashes removed.
 * Plain HTTP is allowed (LAN Admirals) but always flagged so the UI can warn.
 */

export type ServerUrlError = 'empty' | 'scheme' | 'host' | 'credentials' | 'queryOrFragment';

export interface ServerUrlResult {
  url: string | null;
  error: ServerUrlError | null;
}

const PRIVATE_V4 = [
  /^10\./,
  /^127\./,
  /^192\.168\./,
  /^172\.(1[6-9]|2\d|3[01])\./,
  /^169\.254\./,
];

/** Normalize user input into a profile URL. A bare host gets https:// prepended. */
export function normalizeServerUrl(input: string): ServerUrlResult {
  let value = input.trim();
  if (!value) return { url: null, error: 'empty' };
  if (!/^[a-z][a-z0-9+.-]*:\/\//i.test(value)) value = `https://${value}`;
  const match = /^([a-z][a-z0-9+.-]*):\/\/([^/?#]*)([^?#]*)(.*)$/i.exec(value);
  if (!match) return { url: null, error: 'host' };
  const scheme = match[1].toLowerCase();
  if (scheme !== 'http' && scheme !== 'https') return { url: null, error: 'scheme' };
  const authority = match[2];
  if (authority.includes('@')) return { url: null, error: 'credentials' };
  if (!authority || !/^(\[[0-9a-f:.]+\]|[a-z0-9.-]+)(:\d{1,5})?$/i.test(authority)) return { url: null, error: 'host' };
  if (match[4]) return { url: null, error: 'queryOrFragment' };
  const path = match[3].replace(/\/+$/, '');
  return { url: `${scheme}://${authority.toLowerCase()}${path}`, error: null };
}

/** The host name of a normalized URL (without port or brackets). */
export function serverHost(url: string): string {
  const match = /^[a-z]+:\/\/(\[[^\]]+\]|[^/:?#]+)/i.exec(url);
  return match ? match[1].replace(/^\[|\]$/g, '').toLowerCase() : '';
}

/** True when traffic to this URL is unencrypted. */
export function isInsecureUrl(url: string): boolean {
  return /^http:\/\//i.test(url);
}

/**
 * True for hosts on this device or a private network (loopback, RFC 1918, link-local, .local, unqualified names,
 * and the Android emulator's 10.0.2.2 alias for the host machine).
 */
export function isLocalNetworkHost(host: string): boolean {
  const h = host.toLowerCase();
  if (h === 'localhost' || h === '::1' || h.endsWith('.local') || (!h.includes('.') && !h.includes(':'))) return true;
  if (/^f[cd][0-9a-f]{2}:/.test(h) || h.startsWith('fe80:')) return true;
  return PRIVATE_V4.some((re) => re.test(h));
}

/** How loudly to warn about this URL: none (https), lan (http on a private network), public (http elsewhere). */
export function urlSecurityLevel(url: string): 'secure' | 'lan' | 'public' {
  if (!isInsecureUrl(url)) return 'secure';
  return isLocalNetworkHost(serverHost(url)) ? 'lan' : 'public';
}
