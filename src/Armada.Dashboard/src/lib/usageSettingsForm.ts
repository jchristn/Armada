import type { UsageCaptureSettings } from '../types/models';

/** Server defaults (UsageCaptureSettings.cs): capture is disabled by default. */
export const USAGE_SETTINGS_DEFAULTS: UsageCaptureSettings = {
  enabled: false,
  minimumIntervalSeconds: 60,
  allowPtyFallback: false,
  probeTimeoutSeconds: 20,
  maximumConcurrentProbes: 4,
  maximumConcurrentProbesPerHost: 1,
  maximumBackoffSeconds: 3600,
  maximumExcerptBytes: 2048,
  rawSnapshotRetentionDays: 7,
  rollupRetentionDays: 90,
};

/** Editable numeric fields with the server's bounds. */
export const USAGE_SETTINGS_BOUNDS = {
  minimumIntervalSeconds: { min: 60, max: 86400 },
  probeTimeoutSeconds: { min: 1, max: 300 },
  maximumConcurrentProbes: { min: 1, max: 64 },
  maximumConcurrentProbesPerHost: { min: 1, max: 64 },
  maximumBackoffSeconds: { min: 60, max: 86400 },
} as const;

export type UsageSettingsNumericField = keyof typeof USAGE_SETTINGS_BOUNDS;

export interface UsageSettingsFormState {
  enabled: boolean;
  allowPtyFallback: boolean;
  values: Record<UsageSettingsNumericField, string>;
}

/** Form state from a (possibly partial or missing) server group; missing members fall back to the defaults. */
export function usageSettingsFormFrom(group: Partial<UsageCaptureSettings> | null | undefined): UsageSettingsFormState {
  const merged: UsageCaptureSettings = { ...USAGE_SETTINGS_DEFAULTS, ...(group ?? {}) };
  const values = {} as Record<UsageSettingsNumericField, string>;
  for (const key of Object.keys(USAGE_SETTINGS_BOUNDS) as UsageSettingsNumericField[]) values[key] = String(merged[key]);
  return { enabled: merged.enabled === true, allowPtyFallback: merged.allowPtyFallback === true, values };
}

/** Fields whose text is not a whole number inside the server bounds. */
export function usageSettingsErrors(form: UsageSettingsFormState): UsageSettingsNumericField[] {
  const bad: UsageSettingsNumericField[] = [];
  for (const key of Object.keys(USAGE_SETTINGS_BOUNDS) as UsageSettingsNumericField[]) {
    const text = form.values[key].trim();
    const n = Number(text);
    if (!/^\d+$/.test(text) || n < USAGE_SETTINGS_BOUNDS[key].min || n > USAGE_SETTINGS_BOUNDS[key].max) bad.push(key);
  }
  return bad;
}

/**
 * The full group for PUT /api/v1/settings. A supplied group replaces the stored one with defaults for omitted
 * members, so retention and excerpt values the form does not edit are carried over from the loaded group.
 */
export function buildUsageSettingsGroup(form: UsageSettingsFormState, loaded: Partial<UsageCaptureSettings> | null | undefined): UsageCaptureSettings {
  const base: UsageCaptureSettings = { ...USAGE_SETTINGS_DEFAULTS, ...(loaded ?? {}) };
  return {
    ...base,
    enabled: form.enabled,
    allowPtyFallback: form.allowPtyFallback,
    minimumIntervalSeconds: Number(form.values.minimumIntervalSeconds),
    probeTimeoutSeconds: Number(form.values.probeTimeoutSeconds),
    maximumConcurrentProbes: Number(form.values.maximumConcurrentProbes),
    maximumConcurrentProbesPerHost: Number(form.values.maximumConcurrentProbesPerHost),
    maximumBackoffSeconds: Number(form.values.maximumBackoffSeconds),
  };
}
