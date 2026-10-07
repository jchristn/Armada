import { DEFAULT_GLOBAL_LANDING_MODE } from '@dashboard/lib/vesselForm';

/**
 * The landing mode a voyage that does not choose one ends up with: the vessel's mode, else the Admiral's global
 * setting (Settings `landingMode`), else the built-in default (Merge and Push). Shown next to the "Default" choice.
 */
export function effectiveDefaultLandingMode(vesselMode: string | null | undefined, globalMode: string | null | undefined): string {
  return vesselMode || globalMode || DEFAULT_GLOBAL_LANDING_MODE;
}

/** The global landing mode from a getSettings() response (absent or not a string means unset). */
export function globalLandingModeFrom(settings: Record<string, unknown> | null | undefined): string | null {
  const value = settings?.landingMode;
  return typeof value === 'string' && value ? value : null;
}
