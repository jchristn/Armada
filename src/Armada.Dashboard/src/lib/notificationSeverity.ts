import type {
  CaptainState,
  DeploymentStatus,
  DeploymentVerificationStatus,
  IncidentStatus,
  MissionStatus,
  ObjectiveStatus,
  VoyageStatus,
} from '../types/models';

/** Severity of a notification or toast. */
export type Severity = 'info' | 'success' | 'warning' | 'error';

/** Entity kinds that raise state-change notifications. */
export type NotificationEntityKind = 'Mission' | 'Voyage' | 'Captain' | 'Deployment' | 'Objective' | 'Incident';

// Each table maps an exact enum wire value to a severity. Values not listed are 'info'. Unknown values (for
// example a newer server enum value) are also 'info'; nothing is classified by substring.

const MISSION_SEVERITY: Partial<Record<MissionStatus, Severity>> = {
  Complete: 'success',
  Failed: 'error',
  LandingFailed: 'error',
  Cancelled: 'warning',
};

const VOYAGE_SEVERITY: Partial<Record<VoyageStatus, Severity>> = {
  Complete: 'success',
  Failed: 'error',
  Cancelled: 'warning',
};

const CAPTAIN_SEVERITY: Partial<Record<CaptainState, Severity>> = {
  Stalled: 'warning',
  Stopping: 'warning',
  Quarantined: 'warning',
};

const DEPLOYMENT_SEVERITY: Partial<Record<DeploymentStatus, Severity>> = {
  Succeeded: 'success',
  VerificationFailed: 'error',
  Failed: 'error',
  Denied: 'warning',
  RollingBack: 'warning',
  RolledBack: 'warning',
};

const VERIFICATION_SEVERITY: Partial<Record<DeploymentVerificationStatus, Severity>> = {
  Failed: 'error',
  Partial: 'warning',
};

const OBJECTIVE_SEVERITY: Partial<Record<ObjectiveStatus, Severity>> = {
  Completed: 'success',
  Blocked: 'warning',
  Cancelled: 'warning',
};

const INCIDENT_SEVERITY: Partial<Record<IncidentStatus, Severity>> = {
  RolledBack: 'warning',
};

const SEVERITY_RANK: Record<Severity, number> = { info: 0, success: 1, warning: 2, error: 3 };

function lookup<K extends string>(table: Partial<Record<K, Severity>>, value: string | null | undefined): Severity {
  if (!value) return 'info';
  return Object.prototype.hasOwnProperty.call(table, value) ? (table[value as K] ?? 'info') : 'info';
}

/**
 * Severity of a deployment from its status and verification status. A failed (or partial) verification makes a
 * Succeeded deployment an error (or warning); the worse of the two severities wins.
 */
export function deploymentSeverity(
  status: DeploymentStatus | string | null | undefined,
  verificationStatus: DeploymentVerificationStatus | string | null | undefined,
): Severity {
  const fromStatus = lookup(DEPLOYMENT_SEVERITY, status);
  const fromVerification = lookup(VERIFICATION_SEVERITY, verificationStatus);
  return SEVERITY_RANK[fromVerification] > SEVERITY_RANK[fromStatus] ? fromVerification : fromStatus;
}

/**
 * Severity for an entity state change, from the typed status value of that entity kind (exact enum value match).
 * For deployments pass the verification status as well.
 */
export function entityStatusSeverity(
  kind: NotificationEntityKind,
  status: string | null | undefined,
  verificationStatus?: string | null,
): Severity {
  switch (kind) {
    case 'Mission': return lookup(MISSION_SEVERITY, status);
    case 'Voyage': return lookup(VOYAGE_SEVERITY, status);
    case 'Captain': return lookup(CAPTAIN_SEVERITY, status);
    case 'Deployment': return deploymentSeverity(status, verificationStatus);
    case 'Objective': return lookup(OBJECTIVE_SEVERITY, status);
    case 'Incident': return lookup(INCIDENT_SEVERITY, status);
    default: return 'info';
  }
}
