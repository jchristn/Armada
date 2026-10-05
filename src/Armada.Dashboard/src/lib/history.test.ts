import { describe, expect, it } from 'vitest';
import { canDeleteHistoryEntry } from './history';

describe('canDeleteHistoryEntry', () => {
  it('is decided by the typed source type, not by the id prefix', () => {
    expect(canDeleteHistoryEntry({ sourceType: 'Request', sourceId: 'abc123' })).toBe(true);
    // An id that happens to start with req_ on a non-request row is not deletable.
    expect(canDeleteHistoryEntry({ sourceType: 'Event', sourceId: 'req_123' })).toBe(false);
    expect(canDeleteHistoryEntry({ sourceType: 'Mission', sourceId: 'msn_1' })).toBe(false);
    expect(canDeleteHistoryEntry({ sourceType: 'Request', sourceId: '' })).toBe(false);
  });

  it('accepts the lowercase spelling some stubs use', () => {
    expect(canDeleteHistoryEntry({ sourceType: 'request' as 'Request', sourceId: 'abc' })).toBe(true);
  });
});
