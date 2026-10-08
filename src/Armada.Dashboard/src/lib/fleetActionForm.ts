import type {
  FleetAction,
  FleetActionKind,
  FleetActionRun,
  FleetActionRunRequest,
  FleetActionUpsertRequest,
} from '../types/models';
import { findUnknownTemplateVariables } from './fleetActionTemplate';

/**
 * Pure logic of the fleet action forms (create / edit / duplicate an action, and the run flow with saved or ad hoc
 * actions), shared by the dashboard's FleetActionFormModal and RunActionModal and the mobile app's sheets.
 * Validators return field -> English message; callers translate.
 */

/** Largest target set the server accepts in one run. */
export const MAX_RUN_VESSELS = 500;

/** Editable fields of the action form (numbers as typed text). */
export interface FleetActionFormState {
  name: string;
  description: string;
  kind: FleetActionKind;
  commandText: string;
  promptTemplate: string;
  pipelineId: string;
  persona: string;
  timeoutSeconds: string;
  defaultConcurrency: string;
  requiresCleanWorkingTree: boolean;
}

/** Initial form: blank (create), a copy of `source` named with `copySuffix` (create from source), or `source` (edit). */
export function formFromAction(source: FleetAction | null | undefined, mode: 'create' | 'edit', defaultTimeout: number, copySuffix: string): FleetActionFormState {
  if (!source) {
    return {
      name: '', description: '', kind: 'Command', commandText: '', promptTemplate: '', pipelineId: '', persona: '',
      timeoutSeconds: String(defaultTimeout), defaultConcurrency: '4', requiresCleanWorkingTree: true,
    };
  }
  return {
    name: mode === 'create' ? `${source.name} ${copySuffix}`.slice(0, 200) : source.name,
    description: source.description ?? '',
    kind: source.kind,
    commandText: source.commandText ?? '',
    promptTemplate: source.promptTemplate ?? '',
    pipelineId: source.pipelineId ?? '',
    persona: source.persona ?? '',
    timeoutSeconds: String(source.timeoutSeconds || defaultTimeout),
    defaultConcurrency: String(source.defaultConcurrency || 4),
    requiresCleanWorkingTree: source.requiresCleanWorkingTree,
  };
}

/** Validate the action form; returns field -> English message (callers translate). */
export function validateActionForm(form: FleetActionFormState): Record<string, string> {
  const errors: Record<string, string> = {};
  const name = form.name.trim();
  if (!name) errors.name = 'Name is required.';
  else if (name.length > 200) errors.name = 'Name must be 200 characters or fewer.';
  const body = form.kind === 'Command' ? form.commandText : form.promptTemplate;
  if (!body.trim()) errors.body = form.kind === 'Command' ? 'Command text is required.' : 'Prompt template is required.';
  const timeout = Number(form.timeoutSeconds);
  if (form.kind === 'Command' && (!Number.isInteger(timeout) || timeout < 5 || timeout > 7200)) errors.timeout = 'Timeout must be a whole number of seconds from 5 to 7200.';
  const concurrency = Number(form.defaultConcurrency);
  if (!Number.isInteger(concurrency) || concurrency < 1 || concurrency > 32) errors.concurrency = 'Concurrency must be a whole number from 1 to 32.';
  return errors;
}

/** The create / update body for a valid form. On update an empty string clears an optional field; on create it is omitted (null). */
export function buildActionUpsertPayload(form: FleetActionFormState, mode: 'create' | 'edit'): FleetActionUpsertRequest {
  const clearValue = mode === 'edit' ? '' : null;
  return {
    Name: form.name.trim(),
    Description: form.description.trim() || clearValue,
    Kind: form.kind,
    CommandText: form.kind === 'Command' ? form.commandText : null,
    PromptTemplate: form.kind === 'Mission' ? form.promptTemplate : null,
    PipelineId: form.kind === 'Mission' && form.pipelineId ? form.pipelineId : clearValue,
    Persona: form.kind === 'Mission' && form.persona ? form.persona : clearValue,
    TimeoutSeconds: form.kind === 'Command' ? Number(form.timeoutSeconds) : null,
    DefaultConcurrency: Number(form.defaultConcurrency),
    RequiresCleanWorkingTree: form.kind === 'Command' ? form.requiresCleanWorkingTree : false,
  };
}

/** Run flow source: a saved action or an ad hoc definition. */
export type RunMode = 'saved' | 'adhoc';

/** Ad hoc definition fields of the run flow (numbers as typed text). */
export interface AdHocRunForm {
  name: string;
  kind: FleetActionKind;
  commandText: string;
  promptTemplate: string;
  pipelineId: string;
  timeoutSeconds: string;
  requiresCleanWorkingTree: boolean;
}

function parseIntStrict(value: string): number | null {
  if (!/^\s*\d+\s*$/.test(value)) return null;
  return parseInt(value, 10);
}

/** Validate the run flow inputs; returns field -> English message (callers translate). */
export function validateRunInputs(args: {
  vesselCount: number;
  mode: RunMode;
  action: FleetAction | null;
  concurrency: string;
  adHoc: AdHocRunForm;
}): Record<string, string> {
  const errors: Record<string, string> = {};
  if (args.vesselCount < 1) errors.vessels = 'Select at least one vessel.';
  else if (args.vesselCount > MAX_RUN_VESSELS) errors.vessels = 'A run can target at most 500 vessels.';

  const c = parseIntStrict(args.concurrency);
  if (c === null || c < 1 || c > 32) errors.concurrency = 'Concurrency must be a whole number from 1 to 32.';

  if (args.mode === 'saved') {
    if (!args.action) errors.action = 'Choose an action.';
    return errors;
  }

  const name = args.adHoc.name.trim();
  if (!name) errors.name = 'Name is required.';
  else if (name.length > 200) errors.name = 'Name must be 200 characters or fewer.';
  const body = args.adHoc.kind === 'Command' ? args.adHoc.commandText : args.adHoc.promptTemplate;
  if (!body.trim()) errors.body = args.adHoc.kind === 'Command' ? 'Command text is required.' : 'Prompt template is required.';
  else if (findUnknownTemplateVariables(body).length > 0) errors.body = 'unknown-variables';
  if (args.adHoc.kind === 'Command') {
    const timeout = parseIntStrict(args.adHoc.timeoutSeconds);
    if (timeout === null || timeout < 5 || timeout > 7200) errors.timeout = 'Timeout must be a whole number of seconds from 5 to 7200.';
  }
  return errors;
}

/** A blank ad hoc definition of the given kind. */
export function emptyAdHoc(kind: FleetActionKind): AdHocRunForm {
  return { name: '', kind, commandText: '', promptTemplate: '', pipelineId: '', timeoutSeconds: '300', requiresCleanWorkingTree: kind === 'Command' };
}

/** An ad hoc form prefilled from a definition (re-running an ad hoc run). */
export function adHocFromDefinition(definition: FleetActionUpsertRequest): AdHocRunForm {
  return {
    name: definition.Name ?? '',
    kind: definition.Kind ?? 'Command',
    commandText: definition.CommandText ?? '',
    promptTemplate: definition.PromptTemplate ?? '',
    pipelineId: definition.PipelineId ?? '',
    timeoutSeconds: String(definition.TimeoutSeconds ?? 300),
    requiresCleanWorkingTree: definition.RequiresCleanWorkingTree ?? (definition.Kind !== 'Mission'),
  };
}

/** The ad hoc definition sent with a run. */
export function buildAdHocDefinition(adHoc: AdHocRunForm): FleetActionUpsertRequest {
  return {
    Name: adHoc.name.trim(),
    Kind: adHoc.kind,
    CommandText: adHoc.kind === 'Command' ? adHoc.commandText : null,
    PromptTemplate: adHoc.kind === 'Mission' ? adHoc.promptTemplate : null,
    PipelineId: adHoc.kind === 'Mission' && adHoc.pipelineId ? adHoc.pipelineId : null,
    TimeoutSeconds: adHoc.kind === 'Command' ? parseInt(adHoc.timeoutSeconds, 10) : null,
    RequiresCleanWorkingTree: adHoc.kind === 'Command' ? adHoc.requiresCleanWorkingTree : false,
  };
}

/** The run request for a saved action; the clean-tree override is sent only for Command actions when it differs. */
export function buildSavedRunRequest(vesselIds: string[], concurrency: number, action: FleetAction, cleanTreeOverride: boolean | null): FleetActionRunRequest {
  const request: FleetActionRunRequest = { VesselIds: vesselIds, Concurrency: concurrency };
  if (action.kind === 'Command' && cleanTreeOverride !== null && cleanTreeOverride !== action.requiresCleanWorkingTree) {
    request.Overrides = { RequiresCleanWorkingTree: cleanTreeOverride };
  }
  return request;
}

/** The ad hoc definition that re-runs a run whose action is gone or hidden (its snapshot). */
export function definitionFromRun(run: FleetActionRun): FleetActionUpsertRequest {
  return {
    Name: run.actionName,
    Kind: run.kind,
    CommandText: run.commandText,
    PromptTemplate: run.promptTemplate,
    PipelineId: run.pipelineId,
    TimeoutSeconds: run.timeoutSeconds,
    RequiresCleanWorkingTree: run.requiresCleanWorkingTree,
  };
}

/** Finished counts of a run (succeeded + failed + skipped + cancelled) and the rounded percent of its targets. */
export function runProgress(run: Pick<FleetActionRun, 'targetCount' | 'succeededCount' | 'failedCount' | 'skippedCount' | 'cancelledCount'>): { total: number; done: number; percent: number } {
  const total = Math.max(0, run.targetCount);
  const done = run.succeededCount + run.failedCount + run.skippedCount + run.cancelledCount;
  return { total, done, percent: total > 0 ? Math.round((done / total) * 100) : 0 };
}
