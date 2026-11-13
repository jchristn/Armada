import type { CaptainSelectionStrategy, UsageSelectionCapabilities, UsageSelectionSettings } from '../types/models';

// Shared form rules for the server-wide captain-selection policy (UsageSelectionSettings.cs). Used by the dashboard
// Server settings panel and the mobile Server settings section so both validate identically.

/** Numeric policy fields. */
export type UsageSelectionNumericField = Exclude<keyof UsageSelectionSettings, 'mode' | 'strategy' | 'unknownUsageBehavior'>;

/** Inclusive server bounds and input step for each numeric field; a step of 1 means a whole number. */
export const USAGE_SELECTION_BOUNDS: Record<UsageSelectionNumericField, { min: number; max: number; step: number }> = {
  reservePercent: { min: 0, max: 95, step: 0.1 },
  comfortableHeadroomPercent: { min: 1, max: 100, step: 0.1 },
  perAttemptReservationPercent: { min: 0.1, max: 25, step: 0.1 },
  maximumFreshnessSeconds: { min: 120, max: 86400, step: 1 },
  unknownRetrySeconds: { min: 5, max: 3600, step: 1 },
  providerCooldownSeconds: { min: 5, max: 3600, step: 1 },
  settlementLagSeconds: { min: 0, max: 3600, step: 1 },
  maximumUnsettledSeconds: { min: 60, max: 86400, step: 1 },
  decisionRetentionDays: { min: 7, max: 1825, step: 1 },
  maximumCandidates: { min: 1, max: 10000, step: 1 },
};

/** Numeric fields in display order. */
export const USAGE_SELECTION_NUMERIC_FIELDS = Object.keys(USAGE_SELECTION_BOUNDS) as UsageSelectionNumericField[];

/** Translation keys of the strategies. */
export const USAGE_SELECTION_STRATEGY_KEYS: Record<CaptainSelectionStrategy, string> = {
  FirstAvailable: 'usageSelection.firstAvailable',
  MostRemainingAllowance: 'usageSelection.mostRemaining',
  MostUsedAllowance: 'usageSelection.mostUsed',
};

/** True when a value is a finite number inside the field's bounds (and whole when the field requires it). */
export function usageSelectionFieldValid(field: UsageSelectionNumericField, value: number): boolean {
  const bound = USAGE_SELECTION_BOUNDS[field];
  return Number.isFinite(value) && value >= bound.min && value <= bound.max && (bound.step !== 1 || Number.isInteger(value));
}

/** True when the whole policy is valid, including comfortable headroom strictly above the reserve. */
export function usageSelectionValid(policy: UsageSelectionSettings): boolean {
  return USAGE_SELECTION_NUMERIC_FIELDS.every(field => usageSelectionFieldValid(field, policy[field]))
    && policy.comfortableHeadroomPercent > policy.reservePercent;
}

/** An environment override or a non-live policy cannot be edited. */
export function usageSelectionReadOnly(capability: UsageSelectionCapabilities | undefined | null): boolean {
  return !!capability && (capability.environmentOverride || capability.appliedLive === false);
}

/** Translation key of the mode explanation. */
export function usageSelectionModeDescriptionKey(mode: UsageSelectionSettings['mode'] | undefined): string {
  return mode === 'Enforce' ? 'usageSelection.enforceDescription' : mode === 'Disabled' ? 'usageSelection.disabledDescription' : 'usageSelection.description';
}
