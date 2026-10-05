import { describe, expect, it, vi } from 'vitest';
import { act, render } from '@testing-library/react';
import type { WebSocketMessage } from '../types/models';
import { NotificationProvider, useNotifications, type Notification } from './NotificationContext';

const listeners: Array<(msg: WebSocketMessage) => void> = [];

vi.mock('./WebSocketContext', () => ({
  useWebSocket: () => ({
    subscribe: (cb: (msg: WebSocketMessage) => void) => {
      listeners.push(cb);
      return () => {};
    },
  }),
}));

vi.mock('./LocaleContext', () => ({
  useLocale: () => ({
    t: (text: string, params?: Record<string, string>) =>
      Object.entries(params ?? {}).reduce((acc, [k, v]) => acc.split(`{{${k}}}`).join(String(v)), text),
  }),
}));

let latest: Notification[] = [];
function Probe() {
  latest = useNotifications().notifications;
  return null;
}

describe('NotificationProvider severity', () => {
  it('a deployment "Succeeded" with verification "Failed" is an error; display text still shows both', () => {
    localStorage.clear();
    render(<NotificationProvider><Probe /></NotificationProvider>);
    act(() => {
      for (const l of listeners) {
        l({ type: 'deployment.changed', data: { id: 'dpl_1', title: 'Fix: login', status: 'Succeeded', verificationStatus: 'Failed' } } as unknown as WebSocketMessage);
      }
    });
    expect(latest).toHaveLength(1);
    expect(latest[0].severity).toBe('error');
    expect(latest[0].message).toBe('Deployment "Fix: login" - Succeeded / Failed');
  });
});
