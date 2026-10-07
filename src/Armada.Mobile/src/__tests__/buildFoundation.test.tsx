import { act, fireEvent, render, screen, waitFor } from '@testing-library/react-native';
import { Text } from 'react-native';
import { matchesLive, useLiveResource } from '../build/useLiveResource';
import { usePagedList } from '../build/usePagedList';
import { BuildProviders, buildSockets, deliver, page } from '../test/buildFixtures';

jest.mock('@dashboard/api/client', () => require('../test/buildClientMock').buildClientMockFactory());

describe('matchesLive', () => {
  it('matches type prefixes or a predicate', () => {
    expect(matchesLive(['vessel.'], { type: 'vessel.changed' })).toBe(true);
    expect(matchesLive(['vessel.'], { type: 'mission.changed' })).toBe(false);
    expect(matchesLive((m) => m.type === 'x', { type: 'x' })).toBe(true);
    expect(matchesLive(undefined, { type: 'x' })).toBe(false);
  });
});

function Resource({ load }: { load: () => Promise<string> }) {
  const r = useLiveResource(load, [], { live: ['vessel.'] });
  return <Text testID="out">{r.loading ? 'loading' : r.error ? `error:${r.error}` : r.data}</Text>;
}

describe('useLiveResource', () => {
  it('loads, reloads on matching socket events only, and reports errors', async () => {
    let n = 0;
    const load = jest.fn(async () => { n += 1; if (n === 3) throw new Error('boom'); return `v${n}`; });
    await render(<BuildProviders><Resource load={load} /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('out')).toHaveTextContent('v1'));
    await act(async () => { buildSockets()[0]?.open(); });
    await act(async () => deliver({ type: 'mission.changed' }));
    await act(async () => deliver({ type: 'vessel.changed' }));
    await waitFor(() => expect(screen.getByTestId('out')).toHaveTextContent('v2'));
    expect(load).toHaveBeenCalledTimes(2);
    await act(async () => deliver({ type: 'vessel.deleted' }));
    await waitFor(() => expect(screen.getByTestId('out')).toHaveTextContent('error:boom'));
  });
});

function Paged({ load }: { load: (p: number, s: number) => Promise<ReturnType<typeof page<string>>> }) {
  const list = usePagedList(load, [], { pageSize: 2, live: ['fleet.'] });
  return (
    <>
      <Text testID="items">{list.items.join(',')}</Text>
      <Text testID="more" onPress={list.loadMore}>{list.hasMore ? 'more' : 'end'}</Text>
    </>
  );
}

describe('usePagedList', () => {
  it('appends pages, stops at the end, and reloads every shown row live', async () => {
    const all = ['a', 'b', 'c', 'd', 'e'];
    const load = jest.fn(async (pageNumber: number, pageSize: number) => {
      const start = (pageNumber - 1) * pageSize;
      return page(all.slice(start, start + pageSize), { pageNumber, pageSize, totalRecords: all.length, totalPages: Math.ceil(all.length / pageSize) });
    });
    await render(<BuildProviders><Paged load={load} /></BuildProviders>);
    await waitFor(() => expect(screen.getByTestId('items')).toHaveTextContent('a,b'));
    await fireEvent.press(screen.getByTestId('more'));
    await waitFor(() => expect(screen.getByTestId('items')).toHaveTextContent('a,b,c,d'));
    expect(load).toHaveBeenLastCalledWith(2, 2);
    await fireEvent.press(screen.getByTestId('more'));
    await waitFor(() => expect(screen.getByTestId('more')).toHaveTextContent('end'));
    expect(screen.getByTestId('items')).toHaveTextContent('a,b,c,d,e');
    all[0] = 'A';
    await act(async () => { buildSockets()[0]?.open(); });
    await act(async () => deliver({ type: 'fleet.changed' }));
    await waitFor(() => expect(screen.getByTestId('items')).toHaveTextContent('A,b,c,d,e'));
    expect(load).toHaveBeenLastCalledWith(1, 6);
  });
});
