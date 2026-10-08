/**
 * Query string from a prefill object, skipping blanks. Links between screens carry prefills this way (the dashboard
 * passes router state, which a deep link cannot carry): for example /deployments/new?environmentId=...&title=...
 */
export function prefillQuery(values: Record<string, string | null | undefined>): string {
  const parts = Object.entries(values)
    .filter(([, v]) => v !== null && v !== undefined && v !== '')
    .map(([k, v]) => `${encodeURIComponent(k)}=${encodeURIComponent(String(v))}`);
  return parts.length > 0 ? `?${parts.join('&')}` : '';
}

/** A single route parameter (expo-router may hand back an array for repeated keys). */
export function param(value: string | string[] | undefined): string {
  return Array.isArray(value) ? value[0] ?? '' : value ?? '';
}

/**
 * Whether a server-supplied URL may be opened outside the app: http and https only (security review F-47), so a
 * record cannot make the app open tel:, sms:, file:, app deep links, or other schemes.
 */
export function isWebUrl(url: string | null | undefined): url is string {
  if (!url) return false;
  return /^https?:\/\/[^\s]+$/i.test(url.trim());
}

/** Opens a server-supplied URL in the browser when it is http(s); anything else is ignored. */
export function openWebUrl(url: string | null | undefined, open: (u: string) => Promise<unknown>): void {
  if (isWebUrl(url)) void open(url.trim());
}
