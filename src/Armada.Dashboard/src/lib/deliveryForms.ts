import type {
  CheckRun,
  CheckRunType,
  DeploymentStatus,
  DeploymentVerificationStatus,
  IncidentSeverity,
  IncidentStatus,
  ReleaseStatus,
  RunbookExecution,
  RunbookExecutionStatus,
  RunbookParameter,
  RunbookStep,
  WorkflowProfile,
} from '../types/models';

/** Deployment statuses in the order the Deployments page filters by them. */
export const DEPLOYMENT_STATUSES: DeploymentStatus[] = [
  'PendingApproval',
  'Running',
  'Succeeded',
  'VerificationFailed',
  'Failed',
  'Denied',
  'RollingBack',
  'RolledBack',
];

/** Deployment verification statuses in filter order. */
export const VERIFICATION_STATUSES: DeploymentVerificationStatus[] = [
  'NotRun',
  'Running',
  'Passed',
  'Failed',
  'Partial',
  'Skipped',
];

/** Release statuses in the order the Releases pages offer them. */
export const RELEASE_STATUSES: ReleaseStatus[] = ['Draft', 'Candidate', 'Shipped', 'Failed', 'RolledBack'];

/** Incident statuses and severities in the order the Incidents pages offer them. */
export const INCIDENT_STATUSES: IncidentStatus[] = ['Open', 'Monitoring', 'Mitigated', 'RolledBack', 'Closed'];
export const INCIDENT_SEVERITIES: IncidentSeverity[] = ['Critical', 'High', 'Medium', 'Low'];

/** Splits a newline- or comma-separated id list into trimmed, non-empty values. */
export function splitList(value: string): string[] {
  return value
    .split(/\r?\n|,/)
    .map((item) => item.trim())
    .filter(Boolean);
}

/** One value per line (the inverse of splitList). */
export function joinList(values: string[] | null | undefined): string {
  return (values || []).join('\n');
}

/** A UTC timestamp as a local `YYYY-MM-DDTHH:mm` value (datetime-local input), or '' when absent or invalid. */
export function toInputDateTime(value: string | null | undefined): string {
  if (!value) return '';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  const pad = (input: number) => String(input).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

/** A local date-time input value as an ISO UTC string, or null when blank or invalid. */
export function toUtcValue(value: string): string | null {
  if (!value.trim()) return null;
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

/** The hotfix prompt the incident page hands to Planning, Dispatch, and runbook executions. */
export function buildIncidentPrompt(incidentTitle: string, summary: string, impact: string, environmentName: string, deploymentId: string, releaseId: string): string {
  const lines: string[] = [
    `Investigate and mitigate the incident "${incidentTitle}".`,
    '',
  ];

  if (summary) {
    lines.push(`Summary: ${summary}`);
  }
  if (impact) {
    lines.push(`Impact: ${impact}`);
  }
  if (environmentName) {
    lines.push(`Environment: ${environmentName}`);
  }
  if (deploymentId) {
    lines.push(`Deployment: ${deploymentId}`);
  }
  if (releaseId) {
    lines.push(`Release: ${releaseId}`);
  }

  lines.push('');
  lines.push('Produce a concrete hotfix plan and, if appropriate, implementation scope for the linked vessel.');
  return lines.join('\n');
}

/** Every check type, in the order the Checks page offers them. */
export const ALL_CHECK_TYPES: CheckRunType[] = [
  'Lint',
  'Build',
  'UnitTest',
  'IntegrationTest',
  'E2ETest',
  'Migration',
  'SecurityScan',
  'Performance',
  'Package',
  'DeploymentVerification',
  'RollbackVerification',
  'PublishArtifact',
  'ReleaseVersioning',
  'Changelog',
  'Deploy',
  'Rollback',
  'SmokeTest',
  'HealthCheck',
  'Custom',
];

/** The check types a workflow profile has commands for (every type when no profile or none configured). */
export function getAvailableCheckTypes(profile: WorkflowProfile | null): CheckRunType[] {
  if (!profile) return ALL_CHECK_TYPES;
  const types: CheckRunType[] = [];
  if (profile.lintCommand) types.push('Lint');
  if (profile.buildCommand) types.push('Build');
  if (profile.unitTestCommand) types.push('UnitTest');
  if (profile.integrationTestCommand) types.push('IntegrationTest');
  if (profile.e2eTestCommand) types.push('E2ETest');
  if (profile.migrationCommand) types.push('Migration');
  if (profile.securityScanCommand) types.push('SecurityScan');
  if (profile.performanceCommand) types.push('Performance');
  if (profile.packageCommand) types.push('Package');
  if (profile.deploymentVerificationCommand) types.push('DeploymentVerification');
  if (profile.rollbackVerificationCommand) types.push('RollbackVerification');
  if (profile.publishArtifactCommand) types.push('PublishArtifact');
  if (profile.releaseVersioningCommand) types.push('ReleaseVersioning');
  if (profile.changelogGenerationCommand) types.push('Changelog');
  if (profile.environments.some((environment) => environment.deployCommand)) types.push('Deploy');
  if (profile.environments.some((environment) => environment.rollbackCommand)) types.push('Rollback');
  if (profile.environments.some((environment) => environment.smokeTestCommand)) types.push('SmokeTest');
  if (profile.environments.some((environment) => environment.healthCheckCommand)) types.push('HealthCheck');
  if (profile.environments.some((environment) => environment.deploymentVerificationCommand)) types.push('DeploymentVerification');
  if (profile.environments.some((environment) => environment.rollbackVerificationCommand)) types.push('RollbackVerification');
  return types.length > 0 ? types : ALL_CHECK_TYPES;
}

/** Whether a check type runs against a named environment. */
export function requiresEnvironment(type: CheckRunType): boolean {
  return type === 'Deploy'
    || type === 'Rollback'
    || type === 'SmokeTest'
    || type === 'HealthCheck'
    || type === 'DeploymentVerification'
    || type === 'RollbackVerification';
}

/** One-line parsed results of a run: "12 passed, 1 failed | 84% coverage" ('' when nothing was parsed). */
export function summarizeRunParsing(run: CheckRun): string {
  const parts: string[] = [];
  if (run.testSummary) {
    const testParts: string[] = [];
    if (run.testSummary.passed != null) testParts.push(`${run.testSummary.passed} passed`);
    if (run.testSummary.failed != null) testParts.push(`${run.testSummary.failed} failed`);
    if (run.testSummary.skipped != null && run.testSummary.skipped > 0) testParts.push(`${run.testSummary.skipped} skipped`);
    if (testParts.length > 0) parts.push(testParts.join(', '));
  }

  const lineCoverage = run.coverageSummary?.lines?.percentage;
  const statementCoverage = run.coverageSummary?.statements?.percentage;
  const coverage = lineCoverage ?? statementCoverage ?? null;
  if (coverage != null) parts.push(`${coverage.toFixed(coverage % 1 === 0 ? 0 : 2)}% coverage`);
  return parts.join(' | ');
}

/** A run duration: ms under a second, seconds under a minute, minutes otherwise ('-' when unknown). */
export function formatCheckDuration(durationMs: number | null | undefined): string {
  if (durationMs == null) return '-';
  if (durationMs < 1000) return `${Math.round(durationMs)} ms`;
  if (durationMs >= 60_000) return `${(durationMs / 60_000).toFixed(durationMs % 60_000 === 0 ? 0 : 2)} min`;
  return `${(durationMs / 1000).toFixed(durationMs % 1000 === 0 ? 0 : 2)} s`;
}

/** A coverage metric: "84% (840/1000)", either part alone, or null when neither is known. */
export function formatCoverageMetric(metric: { covered: number | null; total: number | null; percentage: number | null } | null | undefined): string | null {
  if (!metric) return null;
  const percentage = metric.percentage != null ? `${metric.percentage.toFixed(metric.percentage % 1 === 0 ? 0 : 2)}%` : null;
  const counts = metric.covered != null && metric.total != null ? `${metric.covered}/${metric.total}` : null;
  if (!percentage && !counts) return null;
  return `${percentage || counts}${percentage && counts ? ` (${counts})` : ''}`;
}

/** Check types a runbook can default to or launch, in the runbook pages' order. */
export const RUNBOOK_CHECK_TYPES: CheckRunType[] = [
  'Build',
  'UnitTest',
  'IntegrationTest',
  'E2ETest',
  'Migration',
  'SecurityScan',
  'Performance',
  'Deploy',
  'Rollback',
  'SmokeTest',
  'HealthCheck',
  'DeploymentVerification',
  'RollbackVerification',
  'Custom',
];

export const RUNBOOK_EXECUTION_STATUSES: RunbookExecutionStatus[] = ['Running', 'Completed', 'Cancelled'];

/** A new runbook parameter with the page's defaults. */
export function createDefaultParameter(): RunbookParameter {
  return {
    name: 'parameter',
    label: '',
    description: '',
    defaultValue: '',
    required: false,
  };
}

/** A new runbook step with a client-side id. */
export function createDefaultStep(now: number = Date.now(), random: () => number = Math.random): RunbookStep {
  return {
    id: `rbs_${now}_${random().toString(36).slice(2, 8)}`,
    title: 'Step',
    instructions: '',
  };
}

/** A copy of an execution whose maps and lists can be edited without touching the original. */
export function cloneExecution(execution: RunbookExecution): RunbookExecution {
  return {
    ...execution,
    parameterValues: { ...execution.parameterValues },
    completedStepIds: [...execution.completedStepIds],
    stepNotes: { ...execution.stepNotes },
  };
}
