import type { Objective, Pipeline } from '@dashboard/types/models';
import {
  buildObjectiveDispatchPrompt,
  buildObjectivePlanningPrompt,
  buildObjectiveReleaseNotes,
  joinSuggestedPlaybooks,
} from '@dashboard/lib/backlogUtils';

/**
 * Where a backlog item leads: its canonical route and the prefilled Planning, Dispatch, and Release drafts the
 * dashboard opens with router state. Mobile routes cannot carry state, so the same prefill travels as query
 * parameters (the receiving screens read them; names match the dashboard's state fields).
 */

/** The canonical route of a backlog item (the dashboard links /backlog/:id; /objectives/:id shows the same page). */
export function backlogItemPath(id: string, refinementSessionId?: string | null): string {
  const base = `/backlog/${encodeURIComponent(id)}`;
  return refinementSessionId ? `${base}?refinementSessionId=${encodeURIComponent(refinementSessionId)}` : base;
}

function withQuery(path: string, params: Record<string, string | null | undefined>): string {
  const parts = Object.entries(params)
    .filter((entry): entry is [string, string] => typeof entry[1] === 'string' && entry[1].length > 0)
    .map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(value)}`);
  return parts.length > 0 ? `${path}?${parts.join('&')}` : path;
}

/** Start Planning: a planning session prefilled from the item (needs a primary vessel). */
export function planningHref(objective: Objective, fleetId: string, vesselId: string): string {
  return withQuery('/planning', {
    fromObjective: 'true',
    objectiveId: objective.id,
    title: `${objective.title} Planning`,
    fleetId,
    vesselId,
    pipelineId: objective.suggestedPipelineId,
    initialPrompt: buildObjectivePlanningPrompt(objective),
  });
}

/** Open In Dispatch: dispatch prefilled from the item (prompt, pipeline, playbooks as `id:mode` lines). */
export function dispatchHref(objective: Objective, vesselId: string, pipelines: Pipeline[]): string {
  return withQuery('/dispatch', {
    fromObjective: 'true',
    objectiveId: objective.id,
    vesselId,
    pipelineName: pipelines.find((pipeline) => pipeline.id === objective.suggestedPipelineId)?.name,
    selectedPlaybooks: joinSuggestedPlaybooks(objective.suggestedPlaybooks),
    prompt: buildObjectiveDispatchPrompt(objective),
    voyageTitle: objective.title,
  });
}

/** Draft Release: a new release prefilled from the item. */
export function releaseHref(objective: Objective, vesselId: string): string {
  return withQuery('/releases/new', {
    objectiveIds: objective.id,
    vesselId,
    title: `${objective.title} Release`,
    summary: objective.description,
    notes: buildObjectiveReleaseNotes(objective),
    status: 'Draft',
  });
}

/** The history timeline filtered to the item (the dashboard's /history?objectiveId=). */
export function historyHref(objective: Objective): string {
  return withQuery('/activity', { source: 'history', objectiveId: objective.id });
}

export interface LinkGroup {
  key: string;
  /** English label, translated by the caller. */
  label: string;
  ids: string[];
  href: (id: string) => string;
}

/** The Armada Links section: records that reference the item, each linking to its page. Empty groups are left out. */
export function armadaLinkGroups(objective: Objective): LinkGroup[] {
  const groups: LinkGroup[] = [
    { key: 'planning', label: 'Planning Sessions', ids: objective.planningSessionIds, href: (id) => `/planning/${encodeURIComponent(id)}` },
    { key: 'refinement', label: 'Refinement Sessions', ids: objective.refinementSessionIds, href: (id) => backlogItemPath(objective.id, id) },
    { key: 'voyages', label: 'Voyages', ids: objective.voyageIds, href: (id) => `/voyages/${encodeURIComponent(id)}` },
    { key: 'missions', label: 'Missions', ids: objective.missionIds, href: (id) => `/missions/${encodeURIComponent(id)}` },
    { key: 'checks', label: 'Checks', ids: objective.checkRunIds, href: (id) => `/checks/${encodeURIComponent(id)}` },
    { key: 'releases', label: 'Releases', ids: objective.releaseIds, href: (id) => `/releases/${encodeURIComponent(id)}` },
    { key: 'deployments', label: 'Deployments', ids: objective.deploymentIds, href: (id) => `/deployments/${encodeURIComponent(id)}` },
    { key: 'incidents', label: 'Incidents', ids: objective.incidentIds, href: (id) => `/incidents/${encodeURIComponent(id)}` },
  ];
  return groups.filter((group) => group.ids.length > 0);
}
