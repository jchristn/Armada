import { fireEvent, screen, waitFor } from '@testing-library/react-native';
import * as client from '@dashboard/api/client';
import type { Dock } from '@dashboard/types/models';
import { DockDetail } from '../screens/operations/DockDetail';
import { DocksList, filterDocks } from '../screens/operations/DocksList';
import { page } from '../test/operationsClient';
import { mockRouter, setMockParams } from '../test/routerMock';
import { renderScreen } from '../test/screen';
import { rowActionTarget } from '../test/a11y';

jest.mock('@dashboard/api/client', () => require('../test/operationsClient').operationsClientMockFactory());
jest.mock('expo-router', () => require('../test/routerMock').routerMockFactory());

const api = client as jest.Mocked<typeof client>;
const realError = console.error;
beforeAll(() => {
  jest.spyOn(console, 'error').mockImplementation((...args: unknown[]) => {
    if (typeof args[0] === 'string' && args[0].includes('[react-native-gesture-handler]')) return;
    realError(...args);
  });
});
afterAll(() => { (console.error as jest.Mock).mockRestore(); });

function dock(id: string, over: Partial<Dock> = {}): Dock {
  return { id, tenantId: null, vesselId: 'vsl_1', captainId: 'cpt_1', worktreePath: `/docks/${id}`, branchName: `armada/${id}`, active: true,
    createdUtc: '2026-10-07T10:00:00Z', lastUpdateUtc: '2026-10-07T10:00:00Z', ...over };
}

beforeEach(() => {
  setMockParams({});
  api.listDocks.mockResolvedValue(page([dock('dck_1'), dock('dck_2', { active: false, createdUtc: '2026-10-07T11:00:00Z' })]) as never);
  api.listVessels.mockResolvedValue(page([{ id: 'vsl_1', name: 'api' }]) as never);
  api.listCaptains.mockResolvedValue(page([{ id: 'cpt_1', name: 'claude-1' }]) as never);
  api.deleteDock.mockResolvedValue(undefined);
  api.getDock.mockResolvedValue(dock('dck_1', { gitAnchorsJson: '{"startCommit":"abc123","targetBranch":"main","recentPathCommits":["abc fix"]}' }));
});

describe('docks', () => {
  it('filters by branch or path and sorts newest first', () => {
    const rows = [dock('a', { createdUtc: '2026-01-01T00:00:00Z' }), dock('b', { createdUtc: '2026-02-01T00:00:00Z', branchName: 'fix/z' })];
    expect(filterDocks(rows, '').map((d) => d.id)).toEqual(['b', 'a']);
    expect(filterDocks(rows, 'FIX').map((d) => d.id)).toEqual(['b']);
    expect(filterDocks(rows, '/docks/a').map((d) => d.id)).toEqual(['a']);
  });

  it('lists docks and deletes one and a selection', async () => {
    const onSelect = jest.fn();
    await renderScreen(<DocksList onSelect={onSelect} />);
    await fireEvent.press(await screen.findByTestId('dock-row-dck_1'));
    expect(onSelect).toHaveBeenCalledWith('dck_1');
    await fireEvent(rowActionTarget(screen.getByTestId('dock-row-dck_1-swipe'), 'delete'), 'accessibilityAction', { nativeEvent: { actionName: 'delete' } });
    await fireEvent.press(await screen.findByTestId('dock-confirm-confirm'));
    await waitFor(() => expect(api.deleteDock).toHaveBeenCalledWith('dck_1'));

    await fireEvent(screen.getByTestId('dock-row-dck_1'), 'longPress');
    await fireEvent.press(screen.getByTestId('dock-selection-all'));
    await fireEvent.press(screen.getByTestId('dock-selection-delete'));
    await fireEvent.press(await screen.findByTestId('dock-confirm-confirm'));
    await waitFor(() => expect(api.deleteDock).toHaveBeenCalledTimes(3));
  });

  it('dock detail links to the vessel and captain, shows the starting point, and deletes', async () => {
    await renderScreen(<DockDetail id="dck_1" />);
    expect(await screen.findByText('abc123')).toBeTruthy();
    expect(screen.getByText('abc fix')).toBeTruthy();
    await fireEvent.press(screen.getByTestId('dock-vessel'));
    expect(mockRouter.push).toHaveBeenCalledWith('/vessels/vsl_1');
    await fireEvent.press(screen.getByTestId('dock-captain'));
    expect(mockRouter.push).toHaveBeenCalledWith('/captains/cpt_1');
    await fireEvent.press(screen.getByTestId('dock-delete'));
    await fireEvent.press(await screen.findByTestId('dock-detail-confirm-confirm'));
    await waitFor(() => expect(api.deleteDock).toHaveBeenCalledWith('dck_1'));
    expect(mockRouter.back).toHaveBeenCalled();
  });
});
