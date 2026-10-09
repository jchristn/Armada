/** Pure helpers of the readiness panel (vessel readiness shown on Dispatch, vessel pages, onboarding, and the app). */
import type { VesselCheckoutErrorCode, VesselReadinessResult, VesselSetupChecklistItem, WorkflowInputReferenceProvider } from '../types/models';

/** Overall tone of a vessel readiness result: errors block, warnings need attention. */
export type ReadinessTone = 'ready' | 'warning' | 'error';

export function readinessTone(readiness: VesselReadinessResult | null): ReadinessTone {
  if (!readiness) return 'warning';
  if (readiness.errorCount > 0) return 'error';
  if (readiness.warningCount > 0) return 'warning';
  return 'ready';
}

/** English label of the readiness tone (Unknown when there is no result). */
export function readinessLabel(readiness: VesselReadinessResult | null): string {
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

/**
 * Where the vessel's checkout lives, from readiness: on a Harbor (with its ID and path), on the Admiral host (with the
 * working directory), or unavailable (with the typed reason). Texts are English templates, translated at render time.
 */
export type ReadinessCheckout =
  | { kind: 'harbor'; harborId: string; harborName: string; path: string | null }
  | { kind: 'admiral'; path: string }
  | { kind: 'unavailable'; code: VesselCheckoutErrorCode | null; reason: string };

export function readinessCheckout(readiness: VesselReadinessResult | null): ReadinessCheckout | null {
  if (!readiness) return null;
  if (readiness.harborId) {
    return { kind: 'harbor', harborId: readiness.harborId, harborName: readiness.harborName || readiness.harborId, path: readiness.checkoutPath ?? null };
  }
  if (readiness.checkoutPath) return { kind: 'admiral', path: readiness.checkoutPath };
  // An Admiral from before checkout reporting sends no path; there is nothing to say then.
  if (readiness.hasWorkingDirectory) return null;
  const code = readiness.checkoutErrorCode ?? null;
  return { kind: 'unavailable', code, reason: checkoutUnavailableReason(code) };
}

/** English reason no checkout is available, from the typed checkoutErrorCode. */
export function checkoutUnavailableReason(code: VesselCheckoutErrorCode | string | null | undefined): string {
  switch (code) {
    case 'NoHarborConnected':
      return 'Unavailable: no Harbor is connected, and the vessel has no working directory on the Admiral';
    case 'NoHarborCheckout':
      return 'Unavailable: no connected Harbor has a checkout of this vessel, and it has no working directory on the Admiral';
    default:
      return 'Unavailable: the vessel has no working directory on the Admiral';
  }
}

/** The English template and parameters of the checkout line ("on Harbor {{name}} at {{path}}", ...). */
export function readinessCheckoutText(checkout: ReadinessCheckout): { template: string; params?: Record<string, string> } {
  if (checkout.kind === 'harbor') {
    return checkout.path
      ? { template: 'on Harbor {{name}} at {{path}}', params: { name: checkout.harborName, path: checkout.path } }
      : { template: 'on Harbor {{name}}', params: { name: checkout.harborName } };
  }
  if (checkout.kind === 'admiral') return { template: 'on the Admiral at {{path}}', params: { path: checkout.path } };
  return { template: checkout.reason };
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
