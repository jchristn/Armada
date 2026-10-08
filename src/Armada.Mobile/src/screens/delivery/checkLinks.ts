import type { CheckRun, CheckRunRequest, CheckRunType } from '@dashboard/types/models';
import { ALL_CHECK_TYPES } from '@dashboard/lib/deliveryForms';
import { param, prefillQuery } from '../../resource/links';

/** Query keys a Run Check link can prefill (Environments, Deployments, Releases, and Runbooks link with ?run=1). */
const KEYS = ['vesselId', 'workflowProfileId', 'missionId', 'voyageId', 'deploymentId', 'environmentName', 'label', 'branchName', 'commitHash', 'commandOverride'] as const;

/**
 * The run-check prefill in a /delivery?tab=checks link, or null when the link does not ask to run one. Delivery
 * screens link with ?run=1&...; W3's Vessels and Workspace screens link with the prefill alone (vesselId, branchName
 * or branch, label), as the dashboard's router-state prefill on /checks opens the Run Check form either way.
 */
export function checkPrefillFrom(params: Record<string, string | string[] | undefined>): Partial<CheckRunRequest> | null {
  const out: Partial<CheckRunRequest> = {};
  for (const key of KEYS) {
    const value = param(params[key]);
    if (value) out[key] = value;
  }
  const branch = param(params.branch);
  if (branch && !out.branchName) out.branchName = branch;
  const type = param(params.type);
  if (type && (ALL_CHECK_TYPES as string[]).includes(type)) out.type = type as CheckRunType;
  if (param(params.run) !== '1' && Object.keys(out).length === 0) return null;
  return out;
}

/** The dashboard's "Draft Release" from a run: /releases/new prefilled with the run's vessel, voyage, mission, and id. */
export function draftReleaseLink(run: CheckRun): string {
  return `/releases/new${prefillQuery({
    vesselId: run.vesselId,
    voyageIds: run.voyageId ?? '',
    missionIds: run.missionId ?? '',
    checkRunIds: run.id,
    title: run.label ? `${run.label} Release` : `${run.type} Release`,
  })}`;
}
