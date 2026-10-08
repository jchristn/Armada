import * as Notifications from 'expo-notifications';
import {
  configureForegroundPresentation,
  ensureAndroidChannel,
  nativePushEnvironment,
  registerApproveDenyCategory,
  requestPermission,
  setBadgeCount,
} from './native';
import type { PermissionState, PushEnvironment } from './registration';

/** A notification response reduced to what the app uses (the data object and which button was chosen). */
export interface PushResponse {
  data: unknown;
  actionIdentifier: string | null;
}

export interface Subscription {
  remove: () => void;
}

/** The native notification surface the push provider needs; tests supply a fake. */
export interface PushNative {
  environment: PushEnvironment;
  /** `isRetired`: pushes naming a retired (signed-out) device are not presented while the app is open. */
  configurePresentation: (isRetired: (deviceId: string) => Promise<boolean>) => void;
  registerCategory: (approveLabel: string, denyLabel: string) => Promise<void>;
  ensureChannel: (name: string) => Promise<void>;
  requestPermission: () => Promise<PermissionState>;
  setBadge: (count: number) => Promise<void>;
  addTokenListener: (listener: () => void) => Subscription;
  addResponseListener: (listener: (response: PushResponse) => void) => Subscription;
  getLastResponse: () => PushResponse | null;
  clearLastResponse: () => void;
}

function toResponse(response: Notifications.NotificationResponse | null): PushResponse | null {
  if (!response) return null;
  return {
    data: response.notification?.request?.content?.data ?? null,
    actionIdentifier: response.actionIdentifier ?? null,
  };
}

export const defaultPushNative: PushNative = {
  environment: nativePushEnvironment,
  configurePresentation: configureForegroundPresentation,
  registerCategory: registerApproveDenyCategory,
  ensureChannel: ensureAndroidChannel,
  requestPermission,
  setBadge: setBadgeCount,
  addTokenListener: (listener) => Notifications.addPushTokenListener(() => listener()),
  addResponseListener: (listener) => Notifications.addNotificationResponseReceivedListener((response) => {
    const mapped = toResponse(response);
    if (mapped) listener(mapped);
  }),
  getLastResponse: () => {
    try { return toResponse(Notifications.getLastNotificationResponse()); } catch { return null; }
  },
  clearLastResponse: () => {
    try { Notifications.clearLastNotificationResponse(); } catch { /* nothing to clear */ }
  },
};
