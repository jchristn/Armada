import { compareUtc, mergeSessionDetail, upsertIfNewer } from './liveMerge';

interface S { id: string; status: string; lastUpdateUtc: string }
interface M { id: string; sequence: number; content: string; lastUpdateUtc: string }
interface D { session: S; messages: M[]; captain: { id: string; state: string } | null }

describe('compareUtc', () => {
  it('orders sub-millisecond timestamps with trimmed trailing zeros', () => {
    expect(compareUtc('2026-10-05T00:00:01.12Z', '2026-10-05T00:00:01.1234567Z')).toBeLessThan(0);
    expect(compareUtc('2026-10-05T00:00:01.1234568Z', '2026-10-05T00:00:01.1234567Z')).toBeGreaterThan(0);
    expect(compareUtc('2026-10-05T00:00:01.5Z', '2026-10-05T00:00:01.5000000Z')).toBe(0);
    expect(compareUtc('2026-10-05T00:00:02Z', '2026-10-05T00:00:01.9999999Z')).toBeGreaterThan(0);
    expect(compareUtc('not a date', '2026-10-05T00:00:01Z')).toBe(0);
  });
});

describe('mergeSessionDetail (F16: refinement and planning send responses)', () => {
  const completed: D = {
    session: { id: 'ors_1', status: 'Active', lastUpdateUtc: '2026-10-05T00:00:01.2Z' },
    messages: [
      { id: 'u', sequence: 1, content: 'Refine it', lastUpdateUtc: '2026-10-05T00:00:01.1Z' },
      { id: 'a', sequence: 2, content: 'Done refining', lastUpdateUtc: '2026-10-05T00:00:01.2Z' },
    ],
    captain: { id: 'cpt_1', state: 'Idle' },
  };
  const staleResponse: D = {
    session: { id: 'ors_1', status: 'Responding', lastUpdateUtc: '2026-10-05T00:00:01.1Z' },
    messages: [
      { id: 'u', sequence: 1, content: 'Refine it', lastUpdateUtc: '2026-10-05T00:00:01.1Z' },
      { id: 'a', sequence: 2, content: '', lastUpdateUtc: '2026-10-05T00:00:01.1Z' },
    ],
    captain: { id: 'cpt_1', state: 'Refining' },
  };

  it('does not roll a completed turn back to Responding', () => {
    const merged = mergeSessionDetail<S, M, D>(completed, staleResponse);
    expect(merged.session.status).toBe('Active');
    expect(merged.messages.map((m) => m.content)).toEqual(['Refine it', 'Done refining']);
    expect(merged.captain?.state).toBe('Idle');
  });

  it('applies the response when it is newer or the state is empty', () => {
    expect(mergeSessionDetail<S, M, D>(null, staleResponse)).toBe(staleResponse);
    const merged = mergeSessionDetail<S, M, D>(staleResponse, completed);
    expect(merged.session.status).toBe('Active');
    expect(merged.messages[1].content).toBe('Done refining');
  });

  it('adds messages only present in the response, in sequence order', () => {
    const before: D = { ...completed, messages: [completed.messages[1]] };
    const merged = mergeSessionDetail<S, M, D>(before, staleResponse);
    expect(merged.messages.map((m) => m.id)).toEqual(['u', 'a']);
  });

  it('upsertIfNewer keeps the newer copy', () => {
    const items = [{ id: 'x', lastUpdateUtc: '2026-10-05T00:00:02Z' }];
    expect(upsertIfNewer(items, { id: 'x', lastUpdateUtc: '2026-10-05T00:00:01Z' })).toBe(items);
    expect(upsertIfNewer(items, { id: 'x', lastUpdateUtc: '2026-10-05T00:00:03Z' })[0].lastUpdateUtc).toBe('2026-10-05T00:00:03Z');
  });
});
