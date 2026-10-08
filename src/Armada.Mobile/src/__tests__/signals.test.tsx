import { fireEvent, screen, waitFor } from '@testing-library/react-native';
import * as client from '@dashboard/api/client';
import type { Signal } from '@dashboard/types/models';
import { SignalDetail } from '../screens/operations/SignalDetail';
import { SignalsList, filterSignals } from '../screens/operations/SignalsList';
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

function signal(id: string, over: Partial<Signal> = {}): Signal {
  return { id, tenantId: null, fromCaptainId: 'cpt_1', toCaptainId: null, type: 'Progress', payload: `payload ${id}`, read: false, createdUtc: '2026-10-07T10:00:00Z', ...over };
}

beforeEach(() => {
  setMockParams({});
  api.listSignals.mockResolvedValue(page([signal('sig_1'), signal('sig_2', { read: true, type: 'Mail' })]) as never);
  api.listCaptains.mockResolvedValue(page([{ id: 'cpt_1', name: 'claude-1' }]) as never);
  api.markSignalRead.mockResolvedValue(undefined);
  api.sendSignal.mockResolvedValue(signal('sig_3'));
  api.deleteSignalsBatch.mockResolvedValue({} as never);
  api.getSignal.mockResolvedValue(signal('sig_1', { payload: '{"a":1}' }));
});

describe('signals', () => {
  it('text filter matches type, parties (the Admiral), and payload', () => {
    const label = (id: string | null) => (id ? 'claude-1' : 'Admiral');
    const rows = [signal('a'), signal('b', { fromCaptainId: null, payload: 'hello' })];
    expect(filterSignals(rows, 'hello', label).map((s) => s.id)).toEqual(['b']);
    expect(filterSignals(rows, 'admiral', label).map((s) => s.id)).toEqual(['a', 'b']);
  });

  it('applies the server-side filters', async () => {
    await renderScreen(<SignalsList onSelect={jest.fn()} />);
    await screen.findByTestId('signal-row-sig_1');
    await fireEvent.press(screen.getByTestId('signal-filters'));
    await fireEvent.press(await screen.findByTestId('signal-filter-type'));
    await fireEvent.press(await screen.findByTestId('signal-filter-type-option-Mail'));
    await fireEvent(screen.getByTestId('signal-filter-unread'), 'valueChange', true);
    await waitFor(() => expect(api.listSignals).toHaveBeenLastCalledWith({ pageNumber: 1, pageSize: 25, filters: { type: 'Mail', unreadOnly: 'true' } }));
  });

  it('marks read, sends, and bulk deletes', async () => {
    await renderScreen(<SignalsList onSelect={jest.fn()} />);
    await screen.findByTestId('signal-row-sig_1');
    await fireEvent(rowActionTarget(screen.getByTestId('signal-row-sig_1-swipe'), 'read'), 'accessibilityAction', { nativeEvent: { actionName: 'read' } });
    await waitFor(() => expect(api.markSignalRead).toHaveBeenCalledWith('sig_1'));

    await fireEvent.press(screen.getByTestId('signal-send'));
    await fireEvent.changeText(await screen.findByTestId('send-signal-payload'), 'wake up');
    await fireEvent.press(screen.getByTestId('send-signal-to'));
    await fireEvent.press(await screen.findByTestId('send-signal-to-option-cpt_1'));
    await fireEvent.press(screen.getByTestId('send-signal-submit'));
    await waitFor(() => expect(api.sendSignal).toHaveBeenCalledWith({ type: 'Nudge', payload: 'wake up', toCaptainId: 'cpt_1' }));

    await fireEvent(screen.getByTestId('signal-row-sig_1'), 'longPress');
    await fireEvent.press(screen.getByTestId('signal-row-sig_2'));
    await fireEvent.press(screen.getByTestId('signal-selection-delete'));
    await fireEvent.press(await screen.findByTestId('signal-confirm-confirm'));
    await waitFor(() => expect(api.deleteSignalsBatch).toHaveBeenCalledWith(['sig_1', 'sig_2']));
  });

  it('signal detail pretty-prints the payload, marks read, and deletes', async () => {
    await renderScreen(<SignalDetail id="sig_1" />);
    expect(await screen.findByTestId('signal-payload')).toHaveTextContent('{ "a": 1 }');
    api.getSignal.mockResolvedValue(signal('sig_1', { read: true }));
    await fireEvent.press(screen.getByTestId('signal-mark-read'));
    await waitFor(() => expect(screen.getByTestId('signal-read')).toHaveTextContent('Read'));
    expect(api.markSignalRead).toHaveBeenCalledWith('sig_1');
    expect(screen.queryByTestId('signal-mark-read')).toBeNull();
    await fireEvent.press(screen.getByTestId('signal-delete'));
    await fireEvent.press(await screen.findByTestId('signal-detail-confirm-confirm'));
    await waitFor(() => expect(api.deleteSignalsBatch).toHaveBeenCalledWith(['sig_1']));
    expect(mockRouter.back).toHaveBeenCalled();
  });
});
