import type { InboxItem } from '@dashboard/types/models';
import { INBOX_KINDS, isApprovalKind } from '@dashboard/lib/inboxKinds';
import { appPathFromLink } from '../navigation/deepLinks';

/** Identity of an inbox item across polls. */
export function approvalKey(item: Pick<InboxItem, 'kind' | 'entityId' | 'href'>): string {
  return `${item.kind}:${item.entityId ?? item.href}`;
}

/**
 * The approval items that are new since the previous load and should raise an in-app toast. The first load raises
 * none (they were already waiting); items of the Ask conversation on screen raise none (their cards are already in
 * front of the user).
 */
export function newApprovalItems(previous: ReadonlySet<string> | null, items: InboxItem[], openAskThreadId: string | null): InboxItem[] {
  if (!previous) return [];
  return items.filter((item) => {
    if (!isApprovalKind(item.kind) || previous.has(approvalKey(item))) return false;
    if (openAskThreadId && (item.kind === INBOX_KINDS.askProposal || item.kind === INBOX_KINDS.cliPermission)) {
      const path = appPathFromLink(item.href);
      if (path === `/ask/${openAskThreadId}` || item.cliPermission?.threadId === openAskThreadId) return false;
    }
    return true;
  });
}
