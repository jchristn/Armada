import type { DispatchPrefillState } from '@dashboard/lib/dispatchRequest';
import type { PlaybookDeliveryMode, SelectedPlaybook } from '@dashboard/types/models';

/**
 * Dispatch prefill links. The dashboard opens /dispatch with router state ({ fromVessel: true, vesselId } and so on);
 * the app carries the same draft in the query string, so it also works as a deep link:
 *
 *   /dispatch?from=vessel&vesselId=vsl_1
 *   /dispatch?from=planning|workspace|incident|objective&vesselId=...&pipeline=...&prompt=...&voyageTitle=...
 *            &objectiveId=...&playbooks=[{"playbookId":"pbk_1","deliveryMode":"InlineFullContent"}]
 */
export type DispatchSource = 'vessel' | 'planning' | 'workspace' | 'incident' | 'objective';

const SOURCES: DispatchSource[] = ['vessel', 'planning', 'workspace', 'incident', 'objective'];
const MODES: PlaybookDeliveryMode[] = ['InlineFullContent', 'InstructionWithReference', 'AttachIntoWorktree'];

type Params = Record<string, string | string[] | undefined>;

function one(params: Params, key: string): string {
  const value = params[key];
  return (Array.isArray(value) ? value[0] : value) ?? '';
}

/** Parses the playbooks query value (a JSON array); anything malformed is dropped. */
export function parsePlaybooksParam(raw: string): SelectedPlaybook[] {
  if (!raw) return [];
  let parsed: unknown;
  try { parsed = JSON.parse(raw); } catch { return []; }
  if (!Array.isArray(parsed)) return [];
  const result: SelectedPlaybook[] = [];
  for (const item of parsed as unknown[]) {
    if (!item || typeof item !== 'object') continue;
    const candidate = item as { playbookId?: unknown; deliveryMode?: unknown };
    if (typeof candidate.playbookId !== 'string' || !candidate.playbookId) continue;
    const mode = MODES.find((m) => m === candidate.deliveryMode) ?? 'InlineFullContent';
    result.push({ playbookId: candidate.playbookId, deliveryMode: mode });
  }
  return result;
}

/** The dashboard prefill state for a /dispatch query, or null when the link carries no draft. */
export function dispatchPrefillFromParams(params: Params): DispatchPrefillState | null {
  const from = one(params, 'from') as DispatchSource;
  const vesselId = one(params, 'vesselId');
  // A bare ?vesselId= (no source) is treated as dispatching from that vessel.
  const source: DispatchSource | null = SOURCES.includes(from) ? from : vesselId ? 'vessel' : null;
  if (!source) return null;
  const prefill: DispatchPrefillState = {
    fromVessel: source === 'vessel',
    fromPlanning: source === 'planning',
    fromWorkspace: source === 'workspace',
    fromIncident: source === 'incident',
    fromObjective: source === 'objective',
  };
  if (vesselId) prefill.vesselId = vesselId;
  const pipeline = one(params, 'pipeline');
  if (pipeline) prefill.pipelineName = pipeline;
  const prompt = one(params, 'prompt');
  if (prompt) prefill.prompt = prompt;
  const voyageTitle = one(params, 'voyageTitle');
  if (voyageTitle) prefill.voyageTitle = voyageTitle;
  const objectiveId = one(params, 'objectiveId');
  if (objectiveId) prefill.objectiveId = objectiveId;
  const playbooks = parsePlaybooksParam(one(params, 'playbooks'));
  if (playbooks.length) prefill.selectedPlaybooks = playbooks;
  return prefill;
}

/** The app link that opens Dispatch with a draft (for Vessel detail, Planning, Workspace, Incident, Backlog). */
export function dispatchHref(source: DispatchSource, draft: Omit<DispatchPrefillState, 'fromVessel' | 'fromPlanning' | 'fromWorkspace' | 'fromIncident' | 'fromObjective'> = {}): string {
  const query: string[] = [`from=${source}`];
  const add = (key: string, value: string | undefined) => { if (value) query.push(`${key}=${encodeURIComponent(value)}`); };
  add('vesselId', draft.vesselId);
  add('pipeline', draft.pipelineName);
  add('prompt', draft.prompt);
  add('voyageTitle', draft.voyageTitle);
  add('objectiveId', draft.objectiveId);
  if (draft.selectedPlaybooks?.length) add('playbooks', JSON.stringify(draft.selectedPlaybooks));
  return `/dispatch?${query.join('&')}`;
}
