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

