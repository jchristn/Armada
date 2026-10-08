import { listPipelines, listWorkflowProfiles } from '@dashboard/api/client';
import type { Pipeline, WorkflowProfile } from '@dashboard/types/models';
import { ALL, useFleets, useReference, useVessels } from '../../resource/lookups';

/** Reference lists the project profile forms pick from (fleets, vessels, pipelines, workflow profiles). */
export function useProjectReferences() {
  const fleets = useFleets();
  const vessels = useVessels();
  const pipelines = useReference<Pipeline>(() => listPipelines(ALL));
  const workflowProfiles = useReference<WorkflowProfile>(() => listWorkflowProfiles(ALL));
  return { fleets, vessels, pipelines, workflowProfiles };
}
