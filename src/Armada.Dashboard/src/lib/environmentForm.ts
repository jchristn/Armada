import type { DeploymentVerificationDefinition, EnvironmentKind } from '../types/models';

/** Environment kinds in the order the Environments pages offer them. */
export const ENVIRONMENT_KINDS: EnvironmentKind[] = ['Development', 'Test', 'Staging', 'Production', 'CustomerHosted', 'Custom'];

/** Parses "Header-Name: value" lines into a header map; blank lines and lines without a name are skipped. */
export function parseHeaderLines(value: string): Record<string, string> {
  const headers: Record<string, string> = {};
  for (const rawLine of value.split(/\r?\n/)) {
    const line = rawLine.trim();
    if (!line) continue;
    const separatorIndex = line.indexOf(':');
    if (separatorIndex < 1) continue;
    const key = line.substring(0, separatorIndex).trim();
    const headerValue = line.substring(separatorIndex + 1).trim();
    if (!key) continue;
    headers[key] = headerValue;
  }
  return headers;
}

/** The inverse of parseHeaderLines: one "Header-Name: value" line per header. */
export function serializeHeaderLines(value: Record<string, string> | null | undefined): string {
  return Object.entries(value || {}).map(([key, headerValue]) => `${key}: ${headerValue}`).join('\n');
}

/** A new verification definition with the pages' defaults (GET /health, expect 200, active). */
export function createVerificationDefinition(now: number = Date.now(), random: () => number = Math.random): DeploymentVerificationDefinition {
  return {
    id: `dvd_${now}_${random().toString(36).slice(2, 8)}`,
    name: 'Verification',
    method: 'GET',
    path: '/health',
    requestBody: null,
    headers: {},
    expectedStatusCode: 200,
    mustContainText: null,
    active: true,
  };
}
