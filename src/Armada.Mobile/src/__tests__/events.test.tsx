import { fireEvent, screen, waitFor } from '@testing-library/react-native';
import * as client from '@dashboard/api/client';
import type { ArmadaEvent } from '@dashboard/types/models';
import { EventDetail } from '../screens/operations/EventDetail';
import { EVENTS_PAGE_SIZE, EventsList, filterEvents } from '../screens/operations/EventsList';
import { page } from '../test/operationsClient';
import { mockRouter, setMockParams } from '../test/routerMock';
import { renderScreen } from '../test/screen';

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

function event(id: string, over: Partial<ArmadaEvent> = {}): ArmadaEvent {
  return { id, tenantId: null, eventType: 'mission.completed', entityType: 'Mission', entityId: 'msn_1', captainId: 'cpt_1', missionId: 'msn_1',
    vesselId: 'vsl_1', voyageId: 'vyg_1', message: `message ${id}`, payload: null, createdUtc: '2026-10-07T10:00:00Z', ...over };
}

beforeEach(() => {
  setMockParams({});
  api.listEvents.mockResolvedValue(page([event('evt_1'), event('evt_2', { eventType: 'captain.stalled', entityType: 'Captain' })], 2, EVENTS_PAGE_SIZE) as never);
  api.listVessels.mockResolvedValue(page([{ id: 'vsl_1', name: 'api' }]) as never);
  api.listCaptains.mockResolvedValue(page([{ id: 'cpt_1', name: 'claude-1' }]) as never);
  api.deleteEventsBatch.mockResolvedValue({} as never);
  api.getEvent.mockResolvedValue(event('evt_1', { payload: '{"from":"InProgress"}' }));
});

describe('events', () => {
  it('filters by event type, entity type, and message', () => {
    const rows = [event('a'), event('b', { eventType: 'captain.stalled', entityType: 'Captain', message: 'stuck' })];
    expect(filterEvents(rows, { eventType: 'stall', entityType: '', message: '' }).map((e) => e.id)).toEqual(['b']);
    expect(filterEvents(rows, { eventType: '', entityType: 'mission', message: '' }).map((e) => e.id)).toEqual(['a']);
    expect(filterEvents(rows, { eventType: '', entityType: '', message: 'STUCK' }).map((e) => e.id)).toEqual(['b']);
  });

  it('pages 50 at a time and bulk deletes a selection', async () => {
    await renderScreen(<EventsList onSelect={jest.fn()} />);
    await screen.findByTestId('event-row-evt_1');
    expect(api.listEvents).toHaveBeenCalledWith({ pageNumber: 1, pageSize: 50, filters: undefined });
    await fireEvent(screen.getByTestId('event-row-evt_1'), 'longPress');
    await fireEvent.press(screen.getByTestId('event-row-evt_2'));
    await fireEvent.press(screen.getByTestId('event-selection-delete'));
    await fireEvent.press(await screen.findByTestId('event-confirm-confirm'));
    await waitFor(() => expect(api.deleteEventsBatch).toHaveBeenCalledWith(['evt_1', 'evt_2']));
  });

  it('reloads on any live event', async () => {
    const { socket } = await renderScreen(<EventsList onSelect={jest.fn()} />);
    await screen.findByTestId('event-row-evt_1');
    const calls = api.listEvents.mock.calls.length;
    socket.message({ type: 'voyage.changed', data: { id: 'vyg_1' } });
    await waitFor(() => expect(api.listEvents.mock.calls.length).toBeGreaterThan(calls));
  });

  it('event detail links the entity and related items and deletes', async () => {
    await renderScreen(<EventDetail id="evt_1" />);
    expect(await screen.findByTestId('event-payload')).toHaveTextContent('{ "from": "InProgress" }');
    await fireEvent.press(screen.getByTestId('event-entity'));
    expect(mockRouter.push).toHaveBeenCalledWith('/missions/msn_1');
    await fireEvent.press(screen.getByTestId('event-delete'));
    await fireEvent.press(await screen.findByTestId('event-detail-confirm-confirm'));
    await waitFor(() => expect(api.deleteEventsBatch).toHaveBeenCalledWith(['evt_1']));
    expect(mockRouter.back).toHaveBeenCalled();
  });
});
