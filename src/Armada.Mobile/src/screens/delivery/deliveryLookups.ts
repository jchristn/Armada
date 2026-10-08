import { listDeployments, listEnvironments, listReleases, listWorkflowProfiles } from '@dashboard/api/client';
import type { Deployment, DeploymentEnvironment, Release, WorkflowProfile } from '@dashboard/types/models';
import { ALL, useReference } from '../../resource/lookups';

/** Reference lists the delivery screens use for pickers and names (the dashboard's pageSize 9999 loads). */
export function useWorkflowProfiles(): WorkflowProfile[] {
  return useReference<WorkflowProfile>(() => listWorkflowProfiles(ALL));
}

export function useEnvironments(): DeploymentEnvironment[] {
  return useReference<DeploymentEnvironment>(() => listEnvironments(ALL));
}

export function useReleases(): Release[] {
  return useReference<Release>(() => listReleases(ALL));
}

export function useDeployments(): Deployment[] {
  return useReference<Deployment>(() => listDeployments(ALL), ['deployment.']);
}
