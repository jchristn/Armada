import type { AskQuickAction } from '../types/models';

export type QuickActionForm = 'dispatch' | 'fleet-action' | 'import' | 'none';

/**
 * Built-in quick actions, used when `GET /ask/quick-actions` is unavailable and to fill gaps in the server's
 * catalog. Titles and descriptions are English keys rendered through `t`.
 */
export const DEFAULT_QUICK_ACTIONS: AskQuickAction[] = [
  { name: 'dispatch', command: '/dispatch', toolName: 'dispatch', title: 'Dispatch', description: 'Start a voyage of one or more missions on a vessel' },
  { name: 'fleet-action', command: '/fleet-action', toolName: 'run_fleet_action', title: 'Fleet action', description: 'Run a saved fleet action across selected vessels' },
  { name: 'status', command: '/status', toolName: 'status', title: 'Status', description: 'Summarize all active work in Armada' },
  { name: 'health', command: '/health', toolName: 'evaluate_vessel_health', title: 'Health', description: 'Evaluate the health of every active vessel' },
  { name: 'import', command: '/import', toolName: null, title: 'Import', description: 'Discover and import local repositories as vessels' },
];

function commandOf(action: AskQuickAction): string {
  const raw = action.command || `/${action.name}`;
  return raw.startsWith('/') ? raw : `/${raw}`;
}

/** Server catalog first (in its order) with any missing built-ins appended; every entry gets a command. */
export function mergeQuickActions(server: AskQuickAction[] | null | undefined): AskQuickAction[] {
  const result: AskQuickAction[] = [];
  const seen = new Set<string>();
  for (const action of server ?? []) {
    if (!action || !action.name) continue;
    const key = action.name.toLowerCase();
    if (seen.has(key)) continue;
    seen.add(key);
    const builtIn = DEFAULT_QUICK_ACTIONS.find((d) => d.name === key);
    result.push({ ...builtIn, ...action, command: commandOf(action), toolName: action.toolName !== undefined ? action.toolName : builtIn?.toolName ?? null });
  }
  for (const action of DEFAULT_QUICK_ACTIONS) if (!seen.has(action.name)) result.push(action);
  return result;
}

/** The inline form a quick action opens. Unknown actions with a tool run with no arguments. */
export function quickActionForm(action: AskQuickAction): QuickActionForm {
  const name = action.name.toLowerCase();
  if (name === 'dispatch') return 'dispatch';
  if (name === 'fleet-action' || name === 'fleetaction' || name === 'fleet_action') return 'fleet-action';
  if (name === 'import') return 'import';
  return 'none';
}

/** Actions whose command starts with what the user typed after `/` (the whole input when it is only a command). */
export function filterQuickActions(actions: AskQuickAction[], input: string): AskQuickAction[] {
  if (!input.startsWith('/')) return [];
  const typed = input.slice(1).split(/\s/)[0].toLowerCase();
  if (input.includes(' ')) return [];
  return actions.filter((a) => commandOf(a).slice(1).toLowerCase().startsWith(typed) || a.name.toLowerCase().startsWith(typed));
}

export interface DispatchMissionDraft {
  title: string;
  description: string;
}

export interface DispatchDraft {
  vesselId: string;
  title: string;
  missions: DispatchMissionDraft[];
  pipelineId: string;
}

/** Validation errors keyed by field (English keys); empty when the draft can be submitted. */
export function validateDispatch(draft: DispatchDraft): Record<string, string> {
  const errors: Record<string, string> = {};
  if (!draft.vesselId) errors.vessel = 'Choose a vessel.';
  const missions = draft.missions.filter((m) => m.title.trim() || m.description.trim());
  if (missions.length === 0) errors.missions = 'Add at least one mission.';
  else if (missions.some((m) => !m.title.trim())) errors.missions = 'Every mission needs a title.';
  return errors;
}

/** MCP `dispatch` arguments (camelCase, as the tool's schema declares them). */
export function buildDispatchArguments(draft: DispatchDraft): Record<string, unknown> {
  const missions = draft.missions
    .filter((m) => m.title.trim() || m.description.trim())
    .map((m) => ({ title: m.title.trim(), description: m.description.trim() || m.title.trim() }));
  const title = draft.title.trim() || missions[0]?.title || '';
  const args: Record<string, unknown> = { title, vesselId: draft.vesselId, missions };
  if (draft.pipelineId) args.pipelineId = draft.pipelineId;
  return args;
}

export interface FleetActionDraft {
  actionId: string;
  vesselIds: string[];
}

export function validateFleetAction(draft: FleetActionDraft): Record<string, string> {
  const errors: Record<string, string> = {};
  if (!draft.actionId) errors.action = 'Choose an action.';
  if (draft.vesselIds.length === 0) errors.vessels = 'Choose at least one vessel.';
  return errors;
}

/** MCP `run_fleet_action` arguments. */
export function buildFleetActionArguments(draft: FleetActionDraft): Record<string, unknown> {
  return { actionId: draft.actionId, vesselIds: [...draft.vesselIds] };
}
