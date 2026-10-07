import type { InboxItem } from '../types/models';
import type { Translate } from './deploymentApprovalLabel';

// The Needs You inbox kinds (Armada.Core.Models.InboxItemKinds) and how the inbox groups and labels them. Shared
// by the dashboard's Needs You page and the mobile Approvals center.

/** Wire values of InboxItem.kind. */
export const INBOX_KINDS = {
  review: 'review',
  landingFailed: 'landing_failed',
  failed: 'failed',
  stalledCaptain: 'stalled_captain',
  mergeFailed: 'merge_failed',
  deploymentApproval: 'deployment_approval',
  deploymentFailed: 'deployment_failed',
  askProposal: 'ask_proposal',
  cliPermission: 'cli_permission',
} as const;

/** Inbox kind of a pending CLI tool permission request (InboxItemKinds.CliPermission). */
export const CLI_PERMISSION_KIND = INBOX_KINDS.cliPermission;

/** Inbox kinds that wait on a decision from the user (approve or reject), as opposed to failures to fix. */
export const APPROVAL_KINDS: ReadonlySet<string> = new Set([
  INBOX_KINDS.review, INBOX_KINDS.deploymentApproval, INBOX_KINDS.askProposal, CLI_PERMISSION_KIND,
]);

export function isApprovalKind(kind: string | null | undefined): boolean {
  return !!kind && APPROVAL_KINDS.has(kind);
}

/** Inbox items split into decisions (approvals) and failures to fix (interventions), each in server order. */
export function splitInbox(items: InboxItem[]): { approvals: InboxItem[]; interventions: InboxItem[] } {
  return {
    approvals: items.filter((i) => isApprovalKind(i.kind)),
    interventions: items.filter((i) => !isApprovalKind(i.kind)),
  };
}

/** Label of the button that opens an approval item's page. */
export function inboxActionLabel(t: Translate, item: Pick<InboxItem, 'kind'>): string {
  if (item.kind === INBOX_KINDS.askProposal) return t('Open conversation');
  if (item.kind === INBOX_KINDS.deploymentApproval) return t('Open deployment');
  if (item.kind === INBOX_KINDS.review) return t('Review mission');
  if (item.kind === CLI_PERMISSION_KIND) return t('Open request');
  return t('Open');
}

/** Stable list key of an inbox item. */
export function inboxItemKey(item: Pick<InboxItem, 'kind' | 'entityId'>, index: number): string {
  return `${item.kind}:${item.entityId ?? index}`;
}
