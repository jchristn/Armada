import AsyncStorage from '@react-native-async-storage/async-storage';
import { act, render, waitFor } from '@testing-library/react-native';
import { AppState } from 'react-native';
import * as client from '@dashboard/api/client';
import type { InboxItem } from '@dashboard/types/models';
import { LocaleProvider } from '../i18n/LocaleContext';
import { ApprovalsProvider, useApprovals, type ApprovalsState } from '../notifications/ApprovalsContext';
import { NotificationProvider, notificationHref, useNotifications, type NotificationState } from '../notifications/NotificationContext';
import { SocketProvider } from '../socket/SocketContext';
import { PREF_KEYS } from '../storage/prefs';
import { socketFactory, type FakeSocket } from '../test/fakeSocket';

jest.mock('@dashboard/api/client', () => require('../test/mockClient').clientMockFactory());

const api = client as jest.Mocked<typeof client>;

let notif: NotificationState | null = null;
let approvals: ApprovalsState | null = null;
function Probe() {
  notif = useNotifications();
  approvals = useApprovals();
  return null;
}

const scheduled: (() => void)[] = [];
const schedule = (fn: () => void) => { scheduled.push(fn); return scheduled.length; };

function item(severity: InboxItem['severity']): InboxItem {
  return { kind: 'mission_review', severity, title: 't', detail: '', entityType: 'Mission', entityId: 'msn_1', href: '/missions/msn_1' };
}

async function mount(enabled = true): Promise<FakeSocket[]> {
  const { sockets, factory } = socketFactory();
  await render(
    <LocaleProvider serverUrl={null} bundledCatalog={() => ({ defaultLocale: 'en', supportedLocales: [], locales: {} })}>
      <SocketProvider serverUrl="http://h:1" token="t" factory={factory}>
        <NotificationProvider schedule={schedule}>
          <ApprovalsProvider enabled={enabled}><Probe /></ApprovalsProvider>
        </NotificationProvider>
      </SocketProvider>
    </LocaleProvider>,
  );
  await act(async () => { sockets[0].open(); });
  return sockets;
}

beforeEach(async () => {
  await AsyncStorage.clear();
  jest.clearAllMocks();
  scheduled.length = 0;
  Object.defineProperty(AppState, 'currentState', { get: () => 'active', configurable: true });
});

describe('notification center', () => {
  it('a state change raises a notification and a toast with the shared wording', async () => {
    const sockets = await mount();
    await act(async () => { sockets[0].message({ type: 'mission.changed', data: { id: 'msn_1', title: 'Fix login', status: 'Failed' } }); });
    expect(notif!.notifications).toHaveLength(1);
    expect(notif!.notifications[0]).toMatchObject({ severity: 'error', message: 'Mission "Fix login" - Failed', missionId: 'msn_1' });
    expect(notif!.unreadCount).toBe(1);
    expect(notif!.toasts).toHaveLength(1);
    expect(notif!.toasts[0].href).toBe('/missions/msn_1');
  });

  it('the same state reported twice is one notification', async () => {
    const sockets = await mount();
    const msg = { type: 'voyage.changed', data: { id: 'vyg_1', title: 'Batch', status: 'Complete' } };
    await act(async () => { sockets[0].message(msg); sockets[0].message(msg); });
    expect(notif!.notifications).toHaveLength(1);
  });

  it('toasts expire on the scheduled timer; read state and clear work', async () => {
    const sockets = await mount();
    await act(async () => { sockets[0].message({ type: 'captain.changed', data: { id: 'cpt_1', name: 'c', state: 'Stalled' } }); });
    await act(async () => { scheduled.forEach((fn) => fn()); });
    expect(notif!.toasts).toHaveLength(0);
    await act(async () => { notif!.markAllRead(); });
    expect(notif!.unreadCount).toBe(0);
    await act(async () => { notif!.clearHistory(); });
    expect(notif!.notifications).toHaveLength(0);
  });

  it('history persists on the device', async () => {
    const sockets = await mount();
    await act(async () => { sockets[0].message({ type: 'incident.changed', data: { id: 'inc_1', title: 'Outage', status: 'Open' } }); });
    await waitFor(async () => expect(JSON.parse((await AsyncStorage.getItem(PREF_KEYS.notifications)) ?? '[]')).toHaveLength(1));
  });

  it('links notifications to their item', () => {
    expect(notificationHref({ missionId: null, voyageId: 'vyg_1', captainId: null })).toBe('/voyages/vyg_1');
    expect(notificationHref({ missionId: null, voyageId: null, captainId: null })).toBeNull();
  });
});

describe('Approvals badge', () => {
  it('counts the inbox (the dashboard Needs You count)', async () => {
    api.getInbox.mockResolvedValue([item('Warning'), item('Critical')]);
    await mount();
    await waitFor(() => expect(approvals!.count).toBe(2));
    expect(approvals!.hasCritical).toBe(true);
  });

  it('refreshes on socket activity, throttled', async () => {
    api.getInbox.mockResolvedValue([]);
    const sockets = await mount();
    await waitFor(() => expect(api.getInbox).toHaveBeenCalledTimes(1));
    api.getInbox.mockResolvedValue([item('Warning')]);
    await act(async () => { sockets[0].message({ type: 'mission.changed', data: { id: 'x', status: 'Review' } }); });
    // Inside the throttle window: no second load yet.
    expect(api.getInbox).toHaveBeenCalledTimes(1);
    const later = Date.now() + 5000;
    const spy = jest.spyOn(Date, 'now').mockReturnValue(later);
    try {
      await act(async () => { sockets[0].message({ type: 'mission.changed', data: { id: 'y', status: 'Review' } }); });
      await waitFor(() => expect(approvals!.count).toBe(1));
      expect(api.getInbox).toHaveBeenCalledTimes(2);
    } finally {
      spy.mockRestore();
    }
  });

  it('shows nothing while signed out', async () => {
    api.getInbox.mockResolvedValue([item('Warning')]);
    await mount(false);
    expect(api.getInbox).not.toHaveBeenCalled();
    expect(approvals!.count).toBe(0);
  });
});
