import { describe, expect, it } from 'vitest';
import type { InboxItem } from '../types/models';
import { INBOX_SOCKET_REFRESH_THROTTLE_MS, shouldRefreshInboxOnSocket, summarizeInbox } from './inboxSummary';

function item(severity: InboxItem['severity']): InboxItem {
  return { kind: 'k', severity, title: 't', detail: '', entityType: null, entityId: null, href: '/' };
}

describe('inboxSummary', () => {
  it('counts items and reports the worst severity', () => {
    const s = summarizeInbox([item('Warning'), item('Critical')]);
    expect(s.count).toBe(2);
    expect(s.hasCritical).toBe(true);
    expect(s.hasWarning).toBe(true);
  });

  it('a non-array response counts as empty', () => {
    expect(summarizeInbox(null).count).toBe(0);
  });

  it('throttles socket-triggered refreshes', () => {
    expect(shouldRefreshInboxOnSocket(1000, 1000 + INBOX_SOCKET_REFRESH_THROTTLE_MS - 1)).toBe(false);
    expect(shouldRefreshInboxOnSocket(1000, 1000 + INBOX_SOCKET_REFRESH_THROTTLE_MS)).toBe(true);
  });
});
