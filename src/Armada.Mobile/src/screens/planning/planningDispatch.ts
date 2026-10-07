import type { PlanningSession, Pipeline } from '@dashboard/types/models';
import { dispatchHref } from '../operations/w24/dispatchLink';

/**
 * The Dispatch link for planning output (the dashboard passes the same fields as router state: fromPlanning,
 * vesselId, pipelineName, selectedPlaybooks, prompt, voyageTitle). Built with the Dispatch screen's own link format
 * (operations/w24/dispatchLink.ts), so the Dispatch form opens with this draft.
 */
export function planningDispatchHref(session: PlanningSession, pipelines: Pick<Pipeline, 'id' | 'name'>[], prompt: string, voyageTitle?: string | null): string {
  return dispatchHref('planning', {
    vesselId: session.vesselId,
    prompt,
    pipelineName: pipelines.find((p) => p.id === session.pipelineId)?.name,
    voyageTitle: voyageTitle?.trim() || undefined,
    selectedPlaybooks: session.selectedPlaybooks && session.selectedPlaybooks.length > 0 ? session.selectedPlaybooks : undefined,
  });
}
