import type { VesselReadinessResult, VesselSetupChecklistItem, WorkflowInputReferenceProvider } from '../types/models';

/** Overall tone of a vessel readiness result: errors block, warnings need attention. */
export type ReadinessTone = 'ready' | 'warning' | 'error';

export function getReadinessTone(readiness: VesselReadinessResult | null): ReadinessTone {
  if (!readiness) return 'warning';
  if (readiness.errorCount > 0) return 'error';
  if (readiness.warningCount > 0) return 'warning';
  return 'ready';
}

/** English label of the readiness tone (Unknown when there is no result). */
export function getReadinessLabel(readiness: VesselReadinessResult | null): string {
  if (!readiness) return 'Unknown';
  if (readiness.errorCount > 0) return 'Blocked';
  if (readiness.warningCount > 0) return 'Needs Attention';
  return 'Ready';
}

/** Display name of the provider of a workflow input a readiness issue is about. */
export function formatInputProvider(provider: WorkflowInputReferenceProvider | string | null | undefined): string {
  switch (provider) {
    case 'EnvironmentVariable':
      return 'Environment variable';
    case 'FilePath':
      return 'File path';
    case 'DirectoryPath':
      return 'Directory path';
    case 'AwsSecretsManager':
      return 'AWS Secrets Manager';
    case 'AzureKeyVaultSecret':
      return 'Azure Key Vault';
    case 'HashiCorpVault':
      return 'HashiCorp Vault';
    case 'OnePassword':
      return '1Password';
    default:
      return provider || 'Input';
  }
}

/** "main (detached HEAD)" for the readiness branch, or null when unknown. */
export function readinessBranchSummary(readiness: VesselReadinessResult | null): string | null {
  return readiness?.currentBranch
    ? `${readiness.currentBranch}${readiness.isDetachedHead ? ' (detached HEAD)' : ''}`
    : null;
}

/** "2 ahead / 1 behind" for the readiness remote drift, or null when neither count is known. */
export function readinessDriftSummary(readiness: VesselReadinessResult | null): string | null {
  return readiness && (readiness.commitsAhead != null || readiness.commitsBehind != null)
    ? `${readiness.commitsAhead ?? 0} ahead / ${readiness.commitsBehind ?? 0} behind`
    : null;
}

/** A titled group of onboarding checklist items (by item code). */
export interface ChecklistGroup {
  key: string;
  /** English title, translated at render time. */
  title: string;
  codes: string[];
}

/** The vessel onboarding page's checklist sections. */
export const CHECKLIST_GROUPS: ChecklistGroup[] = [
  {
    key: 'repository',
    title: 'Repository Basics',
    codes: ['working_directory', 'repository_context', 'default_branch', 'toolchains'],
  },
  {
    key: 'workflow',
    title: 'Workflow Profile',
    codes: ['workflow_profile', 'workflow_profile_valid', 'required_inputs'],
  },
  {
    key: 'delivery',
    title: 'Delivery Readiness',
    codes: ['deployment_environments', 'branch_policy', 'deploy_workflow'],
  },
];

export interface ChecklistGroupItems extends ChecklistGroup {
  items: VesselSetupChecklistItem[];
}

/** The setup checklist split into CHECKLIST_GROUPS, dropping empty groups. */
export function groupSetupChecklist(readiness: VesselReadinessResult | null): ChecklistGroupItems[] {
  const items = readiness?.setupChecklist || [];
  return CHECKLIST_GROUPS.map((group) => ({
    ...group,
    items: items.filter((item) => group.codes.includes(item.code)),
  })).filter((group) => group.items.length > 0);
}

/** The first unsatisfied checklist item (the next recommended onboarding step), or null. */
export function nextChecklistItem(readiness: VesselReadinessResult | null): VesselSetupChecklistItem | null {
  return (readiness?.setupChecklist || []).find((item) => !item.isSatisfied) || null;
}
