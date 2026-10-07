import { describe, expect, it } from 'vitest';
import type { Playbook } from '../types/models';
import { activePlaybooksOf, availablePlaybooks, playbookOptionsForRow } from './playbookSelection';
import { userScopeLabel } from './userScope';

const pb = (id: string, active = true) => ({ id, fileName: `${id}.md`, active }) as Playbook;

describe('playbook selection', () => {
  const all = [pb('a'), pb('b'), pb('c', false)];

  it('hides inactive playbooks and already selected ones', () => {
    expect(activePlaybooksOf(all).map((p) => p.id)).toEqual(['a', 'b']);
    expect(availablePlaybooks(all, [{ playbookId: 'a', deliveryMode: 'InlineFullContent' }]).map((p) => p.id)).toEqual(['b']);
  });

  it('offers a row its own playbook, even when inactive, plus unchosen ones', () => {
    const selected = [{ playbookId: 'a', deliveryMode: 'InlineFullContent' as const }, { playbookId: 'c', deliveryMode: 'InlineFullContent' as const }];
    expect(playbookOptionsForRow(all, selected, 'a').map((p) => p.id)).toEqual(['a', 'b']);
    expect(playbookOptionsForRow(all, selected, 'c').map((p) => p.id)).toEqual(['c', 'b']);
  });
});

describe('userScopeLabel', () => {
  it('names users', () => {
    expect(userScopeLabel({ firstName: 'Ada', lastName: null, email: 'ada@x' })).toBe('Ada (ada@x)');
    expect(userScopeLabel({ firstName: null, lastName: null, email: 'ada@x' })).toBe('ada@x');
  });
});
