import { sortByName } from './sortByName';

describe('sortByName (F26 vessel pickers)', () => {
  it('orders by name, case-insensitively and naturally, without mutating the input', () => {
    const input = [{ name: 'zeta' }, { name: 'Alpha' }, { name: 'svc10' }, { name: 'svc2' }, { name: 'beta' }];
    expect(sortByName(input).map((v) => v.name)).toEqual(['Alpha', 'beta', 'svc2', 'svc10', 'zeta']);
    expect(input[0].name).toBe('zeta');
    expect(sortByName(null)).toEqual([]);
  });
});
