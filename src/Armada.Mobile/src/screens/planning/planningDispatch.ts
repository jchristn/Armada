import type { PlanningSession, Pipeline } from '@dashboard/types/models';

/**
 * Route params for opening the Dispatch page with planning output (the dashboard passes the same fields as router
 * state: fromPlanning, vesselId, pipelineName, selectedPlaybooks, prompt, voyageTitle). Mobile routes carry
 * strings, so the playbooks travel as JSON; empty values are left out.
 */
export interface PlanningDispatchHref {
  pathname: '/dispatch';
  params: Record<string, string>;
}

export function planningDispatchHref(session: PlanningSession, pipelines: Pick<Pipeline, 'id' | 'name'>[], prompt: string, voyageTitle?: string | null): PlanningDispatchHref {
  const params: Record<string, string> = { fromPlanning: '1', vesselId: session.vesselId, prompt };
  const pipelineName = pipelines.find((p) => p.id === session.pipelineId)?.name;
  if (pipelineName) params.pipelineName = pipelineName;
  const title = voyageTitle?.trim();
  if (title) params.voyageTitle = title;
  if (session.selectedPlaybooks && session.selectedPlaybooks.length > 0) params.selectedPlaybooks = JSON.stringify(session.selectedPlaybooks);
  return { pathname: '/dispatch', params };
}
