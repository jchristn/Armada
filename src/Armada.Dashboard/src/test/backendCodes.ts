import backendCodesJson from './fixtures/backendCodes.json?raw';

/**
 * Backend code constants and enum member names, generated from the C# types by the .NET suite
 * Models.DashboardCodeList (which fails when this fixture drifts from the backend). Test-only.
 */
export interface BackendCodes {
  FleetActionReasonCodes: string[];
  VesselImportCodes: string[];
  VesselHealthDetailCodes: string[];
  FleetActionRunStatusEnum: string[];
  FleetActionTargetStatusEnum: string[];
  VesselImportCandidateStatusEnum: string[];
  VesselImportOutcomeEnum: string[];
  VesselImportBatchStatusEnum: string[];
  VesselHealthCriterionEnum: string[];
  VesselHealthStatusEnum: string[];
  VulnerabilitySeverityEnum: string[];
  DependencyDriftEnum: string[];
}

export const backendCodes: BackendCodes = JSON.parse(backendCodesJson) as BackendCodes;
