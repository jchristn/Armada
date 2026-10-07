import type { SelectedPlaybook, VoyageCreateRequest } from '../types/models';

/**
 * Pure logic of the Create Voyage form, shared by the dashboard page (pages/VoyageCreate.tsx) and the mobile app.
 */

/** One mission row of the form. */
export interface VoyageMissionRow {
  title: string;
  description: string;
  priority: number;
}

/** Everything the form collects. `landingMode` '' inherits (vessel, then the global setting). */
export interface VoyageFormState {
  title: string;
  description: string;
  vesselId: string;
  pipeline: string;
  landingMode: string;
  selectedPlaybooks: SelectedPlaybook[];
  missions: VoyageMissionRow[];
}

/** The priority a new mission row starts with (and the fallback for 0). */
export const DEFAULT_MISSION_PRIORITY = 100;

export function newVoyageMissionRow(): VoyageMissionRow {
  return { title: '', description: '', priority: DEFAULT_MISSION_PRIORITY };
}

export function emptyVoyageForm(): VoyageFormState {
  return { title: '', description: '', vesselId: '', pipeline: '', landingMode: '', selectedPlaybooks: [], missions: [newVoyageMissionRow()] };
}

/**
 * The first validation problem (English source text, translated by the caller), or null when the form can be sent.
 */
export function validateVoyageForm(form: VoyageFormState): string | null {
  if (!form.title.trim()) return 'Voyage title is required.';
  if (!form.vesselId) return 'Please select a vessel.';
  if (!form.missions.some((m) => m.title.trim())) return 'At least one mission with a title is required.';
  return null;
}

/**
 * The createVoyage payload: rows without a title are dropped, a mission without a description uses its title,
 * and the optional fields (pipeline, playbooks, landing mode) are sent only when set.
 */
export function buildVoyageCreateRequest(form: VoyageFormState): VoyageCreateRequest {
  const missions = form.missions
    .filter((m) => m.title.trim())
    .map((m) => ({
      title: m.title.trim(),
      description: m.description.trim() || m.title.trim(),
      vesselId: form.vesselId,
      priority: m.priority || DEFAULT_MISSION_PRIORITY,
    }));
  return {
    title: form.title.trim(),
    description: form.description.trim() || undefined,
    vesselId: form.vesselId,
    ...(form.pipeline ? { pipeline: form.pipeline } : {}),
    missions,
    ...(form.selectedPlaybooks.length > 0 ? { selectedPlaybooks: form.selectedPlaybooks } : {}),
    ...(form.landingMode ? { landingMode: form.landingMode } : {}),
  };
}
