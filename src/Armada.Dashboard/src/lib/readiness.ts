/** Pure helpers of the readiness panel (vessel readiness shown on Dispatch, vessel pages, and the app). */
import type { VesselReadinessResult, WorkflowInputReferenceProvider } from '../types/models';

export type ReadinessTone = 'ready' | 'warning' | 'error';

export function readinessTone(readiness: VesselReadinessResult | null): ReadinessTone {
  if (!readiness) return 'warning';
  if (readiness.errorCount > 0) return 'error';
  if (readiness.warningCount > 0) return 'warning';
  return 'ready';
}

/** English label for the readiness pill. */
export function readinessLabel(readiness: VesselReadinessResult | null): string {
  if (!readiness) return 'Unknown';
  if (readiness.errorCount > 0) return 'Blocked';
  if (readiness.warningCount > 0) return 'Needs Attention';
  return 'Ready';
}

/** English name of a workflow input provider. */
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
