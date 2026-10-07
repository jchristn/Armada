import AsyncStorage from '@react-native-async-storage/async-storage';
import { act, fireEvent, render, screen } from '@testing-library/react-native';
import { AppState } from 'react-native';
import { NotificationProvider, useNotifications, type NotificationState } from '../notifications/NotificationContext';
import { NotificationCenterScreen } from '../screens/NotificationCenterScreen';
import { SocketProvider } from '../socket/SocketContext';
import { socketFactory } from '../test/fakeSocket';
import { AppProviders } from '../test/render';

jest.mock('@dashboard/api/client', () => require('../test/mockClient').clientMockFactory());
jest.mock('expo-router', () => ({ useRouter: () => ({ push: jest.fn(), dismissTo: jest.fn() }) }));

let notif: NotificationState | null = null;
function Probe() {
  notif = useNotifications();
  return null;
}

beforeEach(async () => {
  await AsyncStorage.clear();
  Object.defineProperty(AppState, 'currentState', { get: () => 'active', configurable: true });
});

describe('notification center (W1.4)', () => {
  it('filters to unread notifications and shows each severity', async () => {
    const { sockets, factory } = socketFactory();
    await render(
      <AppProviders>
        <SocketProvider serverUrl="http://h:1" token="t" factory={factory}>
          <NotificationProvider schedule={() => 0}>
            <Probe />
            <NotificationCenterScreen />
          </NotificationProvider>
        </SocketProvider>
      </AppProviders>,
    );
    await act(async () => { sockets[0].open(); });
    await act(async () => {
      sockets[0].message({ type: 'mission.changed', data: { id: 'msn_1', title: 'Fix login', status: 'Failed' } });
      sockets[0].message({ type: 'voyage.changed', data: { id: 'vyg_1', title: 'Batch', status: 'Complete' } });
    });
    expect(screen.getByText('Mission Failed')).toBeTruthy();
    expect(screen.getByText('Voyage Complete')).toBeTruthy();
    expect(screen.getByLabelText('error')).toBeTruthy();

    const failed = notif!.notifications.find((n) => n.missionId === 'msn_1')!;
    await act(async () => { notif!.markRead(failed.id); });
    await act(async () => { await fireEvent.press(screen.getByTestId('notifications-filter-unread')); });
    expect(screen.queryByText('Mission Failed')).toBeNull();
    expect(screen.getByText('Voyage Complete')).toBeTruthy();
    await act(async () => { await fireEvent.press(screen.getByTestId('notifications-filter-all')); });
    expect(screen.getByText('Mission Failed')).toBeTruthy();
  });
});
