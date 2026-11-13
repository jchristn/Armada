import type {
  FleetAction,
  FleetActionInput,
  FleetActionKind,
  FleetActionRun,
  FleetActionRunRequest,
  FleetActionTargetStatus,
  FleetActionUpsertRequest,
  MissionMode,
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
  extensions?: FleetActionUpsertRequest;
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
  /** Mission kind only; Audit and Research are report-only. */
  missionMode: MissionMode;
  /** Optional shell check; a non-zero exit skips the vessel as NotApplicable. */
  applicabilityCommand: string;
  applicabilityCommandWindows: string;
}

/** Initial form: blank (create), a copy of `source` named with `copySuffix` (create from source), or `source` (edit). */
export function formFromAction(source: FleetAction | null | undefined, mode: 'create' | 'edit', defaultTimeout: number, copySuffix: string): FleetActionFormState {
  if (!source) {
    return {
      name: '', description: '', kind: 'Command', commandText: '', promptTemplate: '', pipelineId: '', persona: '',
      timeoutSeconds: String(defaultTimeout), defaultConcurrency: '4', requiresCleanWorkingTree: true,
      missionMode: 'Implementation', applicabilityCommand: '', applicabilityCommandWindows: '',
    };
  }
  return {
    extensions: {
      CommandWindows: source.commandWindows, OutputParser: source.outputParser, ScanSourceActionKey: source.scanSourceActionKey,
      RequiredTools: source.requiredTools, RequiresNetwork: source.requiresNetwork,
      Inputs: source.inputs?.map(i => ({ Name: i.name, Type: i.type, Required: i.required, Default: i.default, AllowedValues: i.allowedValues, Description: i.description })),
    },
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
    missionMode: source.kind === 'Mission' ? (source.missionMode ?? 'Implementation') : 'Implementation',
    applicabilityCommand: source.applicabilityCommand ?? '',
    applicabilityCommandWindows: source.applicabilityCommandWindows ?? '',
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

/**
 * The create / update body for a valid form. On update an empty string clears an optional field; on create it is
 * omitted (null). The applicability commands and mission mode are left out of a create body when unset, and a
 * Command action is saved with the Implementation mode on update (the server rejects any other for Command).
 */
export function buildActionUpsertPayload(form: FleetActionFormState, mode: 'create' | 'edit'): FleetActionUpsertRequest {
  const clearValue = mode === 'edit' ? '' : null;
  const optionalCommand = (value: string | undefined) => (value && value.trim() ? value : (mode === 'edit' ? '' : undefined));
  const missionMode = form.kind === 'Mission' ? (form.missionMode ?? 'Implementation') : (mode === 'edit' ? 'Implementation' : undefined);
  return {
    ...form.extensions,
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
    ...(optionalCommand(form.applicabilityCommand) !== undefined ? { ApplicabilityCommand: optionalCommand(form.applicabilityCommand) } : {}),
    ...(optionalCommand(form.applicabilityCommandWindows) !== undefined ? { ApplicabilityCommandWindows: optionalCommand(form.applicabilityCommandWindows) } : {}),
    ...(missionMode !== undefined ? { MissionMode: missionMode } : {}),
  };
}

/** Run flow source: a saved action or an ad hoc definition. */
export type RunMode = 'saved' | 'adhoc';

/** Ad hoc definition fields of the run flow (numbers as typed text). */
export interface AdHocRunForm {
  extensions?: FleetActionUpsertRequest;
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

/**
 * Validate the run flow inputs; returns field -> English message (callers translate). With `followUp` the targets
 * come from an earlier run, so the vessel count is not checked and at least one source status is required.
 */
export function validateRunInputs(args: {
  vesselCount: number;
  mode: RunMode;
  action: FleetAction | null;
  concurrency: string;
  adHoc: AdHocRunForm;
  followUp?: { statuses: FleetActionTargetStatus[] } | null;
}): Record<string, string> {
  const errors: Record<string, string> = {};
  if (args.followUp) {
    if (args.followUp.statuses.length === 0) errors.vessels = 'Choose at least one target status.';
  } else if (args.vesselCount < 1) errors.vessels = 'Select at least one vessel.';
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
  else if (findUnknownTemplateVariables(body, args.adHoc.extensions?.Inputs?.map(i => i.Name)).length > 0) errors.body = 'unknown-variables';
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

/** Definition keys the ad hoc form does not edit but carries through a re-run. */
const EXTENSION_KEYS = ['CommandWindows', 'OutputParser', 'ScanSourceActionKey', 'RequiredTools', 'RequiresNetwork', 'Inputs', 'ApplicabilityCommand', 'ApplicabilityCommandWindows', 'MissionMode'];

/** An ad hoc form prefilled from a definition (re-running an ad hoc run). */
export function adHocFromDefinition(definition: FleetActionUpsertRequest): AdHocRunForm {
  return {
    ...(Object.keys(definition).some(key => EXTENSION_KEYS.includes(key)) ? { extensions: definition } : {}),
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
    ...adHoc.extensions,
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
    CommandWindows: run.commandWindows, OutputParser: run.outputParser, ScanSourceActionKey: run.scanSourceActionKey,
    RequiredTools: run.requiredTools, RequiresNetwork: run.requiresNetwork,
    ApplicabilityCommand: run.applicabilityCommand, ApplicabilityCommandWindows: run.applicabilityCommandWindows,
    MissionMode: run.kind === 'Mission' ? run.missionMode : undefined,
    Inputs: run.inputs?.map(i => ({ Name: i.name, Type: i.type, Required: i.required, Default: run.inputValues?.[i.name] ?? i.default, AllowedValues: i.allowedValues, Description: i.description })),
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
export function runProgress(run: Pick<FleetActionRun, 'targetCount' | 'succeededCount' | 'failedCount' | 'skippedCount' | 'cancelledCount' | 'findingsCount'>): { total: number; done: number; percent: number } {
  const total = Math.max(0, run.targetCount);
  const done = run.succeededCount + run.failedCount + run.skippedCount + run.cancelledCount + (run.findingsCount ?? 0);
  return { total, done, percent: total > 0 ? Math.round((done / total) * 100) : 0 };
}

/** Same pattern the server applies to `package` inputs: no shell metacharacters, up to 200 characters. */
export const PACKAGE_INPUT_PATTERN = /^[A-Za-z0-9@][A-Za-z0-9@/._+:-]{0,199}$/;

/**
 * Validate typed run-time input values; returns input name -> English message (callers translate). Required inputs
 * must be set and `package` values must match PACKAGE_INPUT_PATTERN.
 */
export function validateInputValues(inputs: Pick<FleetActionInput, 'name' | 'type' | 'required'>[], values: Record<string, string>): Record<string, string> {
  const errors: Record<string, string> = {};
  for (const input of inputs) {
    const value = (values[input.name] ?? '').trim();
    if (!value) {
      if (input.required) errors[input.name] = 'This input is required.';
      continue;
    }
    if (input.type === 'package' && !PACKAGE_INPUT_PATTERN.test(value)) errors[input.name] = 'Use a package name such as Newtonsoft.Json or @scope/name.';
  }
  return errors;
}

/** Target statuses a follow-up run can draw from, in display order. */
export const FOLLOW_UP_STATUSES: FleetActionTargetStatus[] = ['Findings', 'Failed', 'Succeeded', 'Skipped', 'TimedOut', 'Cancelled'];

/** Statuses checked by default for a follow-up run (the server default too). */
export const DEFAULT_FOLLOW_UP_STATUSES: FleetActionTargetStatus[] = ['Findings', 'Failed'];

/** Turn a run request into a follow-up of `runId`: no explicit vessels, targets drawn from that run by status. */
export function asFollowUpRequest(request: FleetActionRunRequest, runId: string, statuses: FleetActionTargetStatus[]): FleetActionRunRequest {
  return { ...request, VesselIds: [], FromRunId: runId, FromTargetStatuses: FOLLOW_UP_STATUSES.filter(s => statuses.includes(s)) };
}

/**
 * Split actions for a follow-up of a run into suggested ones (whose scanSourceActionKey names the run's action by
 * built-in key or id, such as Fix failing tests after Run tests) and all others, each keeping its order.
 */
export function orderFollowUpActions(actions: FleetAction[], source: { actionId: string | null; builtInKey?: string | null }): { suggested: FleetAction[]; others: FleetAction[] } {
  const keys = [source.builtInKey, source.actionId].filter((k): k is string => Boolean(k));
  const isSuggested = (a: FleetAction) => Boolean(a.scanSourceActionKey && keys.includes(a.scanSourceActionKey));
  return { suggested: actions.filter(isSuggested), others: actions.filter(a => !isSuggested(a)) };
}
