/**
 * Pure logic of the Dispatch page, shared by the dashboard and the mobile app: which pipeline steps get a captain
 * picker, how the per-step assignments are seeded from persona defaults, how a dispatch prefill (from a vessel,
 * planning session, workspace, incident, or backlog item) is described, and the createVoyage request it sends.
 */
import type {
  CaptainAssignmentOverride,
  CaptainTier,
  Persona,
  Pipeline,
  SelectedPlaybook,
  VoyageCreateRequest,
} from '../types/models';

/** A captain choice for one pipeline step. */
export interface StepAssignment {
  captainId: string | null;
  fallbackTier: CaptainTier | null;
}

/** Where a dispatch was opened from, with the draft it carries (the dashboard passes it as router state). */
export interface DispatchPrefillState {
  fromPlanning?: boolean;
  fromWorkspace?: boolean;
  fromIncident?: boolean;
  fromObjective?: boolean;
  fromVessel?: boolean;
  objectiveId?: string;
  vesselId?: string;
  pipelineName?: string;
  prompt?: string;
  selectedPlaybooks?: SelectedPlaybook[];
  voyageTitle?: string;
}

/** The wildcard step: with no explicit pipeline one captain choice applies to every step. */
export const ALL_STEPS_PERSONA = '*';

/** The distinct personas of a pipeline's stages, in stage order. */
export function pipelineStepPersonas(pipeline: Pipeline | null | undefined): string[] {
  if (!pipeline) return [];
  return Array.from(new Set(pipeline.stages.slice().sort((a, b) => a.order - b.order).map((s) => s.personaName)));
}

/** The steps that get a captain picker: the pipeline's personas, or the single wildcard step. */
export function effectiveStepPersonas(stepPersonas: string[]): string[] {
  return stepPersonas.length > 0 ? stepPersonas : [ALL_STEPS_PERSONA];
}

/** Keeps existing step choices and seeds new steps with the persona's default captain. */
export function seedStepAssignments(
  current: Record<string, StepAssignment>,
  stepPersonas: string[],
  personas: Persona[],
): Record<string, StepAssignment> {
  const next: Record<string, StepAssignment> = {};
  for (const personaName of stepPersonas) {
    if (current[personaName]) {
      next[personaName] = current[personaName];
    } else {
      const persona = personas.find((p) => p.name === personaName);
      next[personaName] = { captainId: persona?.defaultCaptainId ?? null, fallbackTier: null };
    }
  }
  return next;
}

/** Whether a prefill should be applied at all. */
export function hasDispatchPrefill(prefill: DispatchPrefillState | null | undefined): boolean {
  return !!(prefill?.fromPlanning || prefill?.fromWorkspace || prefill?.fromIncident || prefill?.fromObjective || prefill?.fromVessel);
}

/** The English notice shown above a prefilled draft (none for a plain vessel prefill). */
export function dispatchPrefillNotice(prefill: DispatchPrefillState | null | undefined): string | null {
  if (prefill?.fromObjective) return 'Prefilled from a backlog item. Review the scoped draft below and dispatch when ready.';
  if (prefill?.fromPlanning) return 'Prefilled from a planning session. Review the draft below and dispatch when ready.';
  if (prefill?.fromIncident) return 'Prefilled from an incident. Review the hotfix draft below and dispatch when ready.';
  if (prefill?.fromWorkspace) return 'Prefilled from Workspace selection. Review the scoped draft below and dispatch when ready.';
  return null;
}

/** The step choices that override routing (a captain or a fallback tier). */
export function captainAssignmentOverrides(assignments: Record<string, StepAssignment>): CaptainAssignmentOverride[] {
  return Object.entries(assignments)
    .filter(([, assignment]) => assignment.captainId || assignment.fallbackTier)
    .map(([persona, assignment]) => ({ persona, captainId: assignment.captainId, fallbackTier: assignment.fallbackTier }));
}

export interface DispatchDraft {
  vesselId: string;
  prompt: string;
  priority: number;
  voyageTitle: string;
  objectiveId: string;
  pipeline: string;
  selectedPlaybooks: SelectedPlaybook[];
  stepAssignments: Record<string, StepAssignment>;
  /** Title used when several tasks are dispatched and no title was given (translated by the caller). */
  multiTaskTitle: string;
}

/** The createVoyage request for a dispatch draft (one task: the trimmed prompt). */
export function buildDispatchVoyageRequest(draft: DispatchDraft): VoyageCreateRequest {
  const tasks = [draft.prompt.trim()];
  const captainAssignments = captainAssignmentOverrides(draft.stepAssignments);
  const title = draft.voyageTitle.trim() || (tasks.length > 1 ? draft.multiTaskTitle : tasks[0].substring(0, 80));
  const missions = tasks.map((task) => ({
    vesselId: draft.vesselId,
    title: task.substring(0, 80),
    description: task,
    priority: draft.priority,
  }));
  return {
    title,
    vesselId: draft.vesselId,
    missions,
    ...(draft.objectiveId ? { objectiveId: draft.objectiveId } : {}),
    ...(draft.pipeline ? { pipeline: draft.pipeline } : {}),
    ...(draft.selectedPlaybooks.length > 0 ? { selectedPlaybooks: draft.selectedPlaybooks } : {}),
    ...(captainAssignments.length > 0 ? { captainAssignments } : {}),
  };
}

/** Parses the Priority field like the page: anything that is not a number falls back to 100. */
export function parseDispatchPriority(value: string): number {
  return parseInt(value, 10) || 100;
}
