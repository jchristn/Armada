import Constants from 'expo-constants';
import * as Device from 'expo-device';
import { getLocales } from 'expo-localization';
import * as Notifications from 'expo-notifications';
import { Platform } from 'react-native';
import { ACTION_APPROVE, ACTION_DENY, APPROVE_DENY_CATEGORY, ACTIONABLE_KINDS, parsePushData } from './payload';
import type { PermissionState, PushEnvironment, TokenResult } from './registration';

/**
 * The expo-notifications side of push: permission, the Expo push token, the Approve / Deny category, the Android
 * channel, foreground presentation, and the app badge. Everything here is thin so the logic stays testable.
 */

export const ANDROID_CHANNEL_ID = 'default';

function toPermissionState(result: Notifications.NotificationPermissionsStatus): PermissionState {
  if (result.granted) return 'granted';
  if (result.ios?.status === Notifications.IosAuthorizationStatus.PROVISIONAL) return 'granted';
  // Android 13+ reports "denied" before the first request; canAskAgain tells the two apart.
  if (result.status === 'undetermined' || result.canAskAgain) return 'undetermined';
  return 'denied';
}

export async function getPermission(): Promise<PermissionState> {
  try {
    return toPermissionState(await Notifications.getPermissionsAsync());
  } catch {
    return 'denied';
  }
}

/** Ask the OS. Only called from a user action (the prompt sheet or the settings button). */
export async function requestPermission(): Promise<PermissionState> {
  try {
    return toPermissionState(await Notifications.requestPermissionsAsync({
      ios: { allowAlert: true, allowBadge: true, allowSound: true },
    }));
  } catch {
    return 'denied';
  }
}

/** The EAS project id the Expo push token is issued for (app.config.ts extra.eas.projectId). */
export function easProjectId(): string | null {
  const extra = Constants.expoConfig?.extra as { eas?: { projectId?: unknown } } | undefined;
  const fromExtra = extra?.eas?.projectId;
  if (typeof fromExtra === 'string' && fromExtra) return fromExtra;
  const fromEas = (Constants as unknown as { easConfig?: { projectId?: unknown } }).easConfig?.projectId;
  return typeof fromEas === 'string' && fromEas ? fromEas : null;
}

export async function getExpoToken(): Promise<TokenResult> {
  const projectId = easProjectId();
  if (!projectId) return { token: null, reason: 'noProject' };
  try {
    const result = await Notifications.getExpoPushTokenAsync({ projectId });
    return result.data ? { token: result.data, reason: null } : { token: null, reason: 'unavailable' };
  } catch {
    return { token: null, reason: 'unavailable' };
  }
}

function clip(value: string | null | undefined, max: number): string | null {
  if (!value) return null;
  return value.length > max ? value.slice(0, max) : value;
}

export const nativePushEnvironment: PushEnvironment = {
  permission: getPermission,
  expoToken: getExpoToken,
  platform: Platform.OS === 'ios' ? 'Ios' : 'Android',
  deviceName: clip(Device.deviceName ?? Device.modelName ?? null, 128),
  appVersion: clip(Constants.expoConfig?.version ?? null, 64),
  locale: () => {
    try { return clip(getLocales()[0]?.languageTag ?? null, 35); } catch { return null; }
  },
  nowUtc: () => new Date().toISOString(),
};

/**
 * Foreground presentation: approvals that need the user show a banner; everything else goes to the list only
 * (the in-app toasts already announce failures and status changes while the app is open).
 */
export function configureForegroundPresentation(): void {
  Notifications.setNotificationHandler({
    handleNotification: async (notification) => {
      const payload = parsePushData(notification.request.content.data);
      const banner = !!payload && ACTIONABLE_KINDS.includes(payload.kind);
      return { shouldShowBanner: banner, shouldShowList: true, shouldPlaySound: banner, shouldSetBadge: true };
    },
  });
}

/**
 * Register the Approve / Deny actions (iOS category, Android action buttons). Both open the app, and iOS requires the
 * device to be unlocked first; the app adds its own biometric check when the profile asks for one.
 */
export async function registerApproveDenyCategory(approveLabel: string, denyLabel: string): Promise<void> {
  try {
    await Notifications.setNotificationCategoryAsync(APPROVE_DENY_CATEGORY, [
      { identifier: ACTION_APPROVE, buttonTitle: approveLabel, options: { opensAppToForeground: true, isAuthenticationRequired: true } },
      { identifier: ACTION_DENY, buttonTitle: denyLabel, options: { opensAppToForeground: true, isAuthenticationRequired: true, isDestructive: true } },
    ]);
  } catch {
    // Not supported on this platform build; taps still open the item.
  }
}

export async function ensureAndroidChannel(name: string): Promise<void> {
  if (Platform.OS !== 'android') return;
  try {
    await Notifications.setNotificationChannelAsync(ANDROID_CHANNEL_ID, {
      name,
      importance: Notifications.AndroidImportance.HIGH,
      showBadge: true,
    });
  } catch {
    // The default channel still exists.
  }
}

export async function setBadgeCount(count: number): Promise<void> {
  try {
    await Notifications.setBadgeCountAsync(Math.max(0, Math.floor(count)));
  } catch {
    // Badges are best-effort (some Android launchers have none).
  }
}
