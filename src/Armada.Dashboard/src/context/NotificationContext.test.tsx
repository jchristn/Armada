import { describe, expect, it, vi } from 'vitest';
import { act, render } from '@testing-library/react';
import type { WebSocketMessage } from '../types/models';
import { NotificationProvider, useNotifications, useOptionalNotifications, type Notification } from './NotificationContext';

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

  it('a deployment waiting for approval is named environment first, then title', () => {
    localStorage.clear();
    latest = [];
    render(<NotificationProvider><Probe /></NotificationProvider>);
    act(() => {
      for (const l of listeners) {
        l({ type: 'deployment.changed', data: { id: 'dpl_9', title: 'Release 2.3 hotfix', environmentName: 'production', status: 'PendingApproval', verificationStatus: 'NotStarted' } } as unknown as WebSocketMessage);
      }
    });
    expect(latest).toHaveLength(1);
    expect(latest[0].message).toBe('Deployment "Deploy to production: Release 2.3 hotfix" - PendingApproval / NotStarted');
  });
});

describe('NotificationProvider toast links', () => {
  it('keeps a pushed toast link (for example View results of a started evaluation) and omits it otherwise', () => {
    let state: ReturnType<typeof useNotifications> | null = null;
    function ToastProbe() {
      state = useNotifications();
      return null;
    }
    render(<NotificationProvider><ToastProbe /></NotificationProvider>);
    act(() => {
      state!.pushToast('success', 'Evaluation started.', { label: 'View results', to: '/evaluations/mpr_1' });
      state!.pushToast('info', 'Plain');
    });
    expect(state!.toasts.map(t => [t.message, t.link])).toEqual([
      ['Evaluation started.', { label: 'View results', to: '/evaluations/mpr_1' }],
      ['Plain', undefined],
    ]);
  });

  it('is optional outside the provider', () => {
    let optional: unknown = 'unset';
    function OptionalProbe() {
      optional = useOptionalNotifications();
      return null;
    }
    render(<OptionalProbe />);
    expect(optional).toBeNull();
  });
});
