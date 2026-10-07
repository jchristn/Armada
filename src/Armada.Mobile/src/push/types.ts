/**
 * Push notification wire types (docs/REST_API.md, Push Notifications). The dashboard has no push UI, so these live
 * in the app rather than in the shared client and models.
 */

/** PushCategoryEnum, in the order the settings list them. */
export const PUSH_CATEGORIES = [
  'AskProposal',
  'CliPermission',
  'MissionReview',
  'DeploymentApproval',
  'MissionFailed',
  'LandingFailed',
  'CaptainStalled',
  'VoyageFinished',
] as const;

export type PushCategory = (typeof PUSH_CATEGORIES)[number];

export type PushPlatform = 'Ios' | 'Android';

export function isPushCategory(value: unknown): value is PushCategory {
  return typeof value === 'string' && (PUSH_CATEGORIES as readonly string[]).includes(value);
}

/** A registered device as the server returns it (the Expo token is masked). */
export interface PushDevice {
  id: string;
  tenantId?: string | null;
  userId?: string | null;
  platform: PushPlatform;
  expoPushToken: string;
  deviceName?: string | null;
  appVersion?: string | null;
  locale?: string | null;
  categories: PushCategory[];
  active: boolean;
  createdUtc?: string;
  lastSeenUtc?: string;
  lastUpdateUtc?: string;
}

export interface PushDeviceRegisterRequest {
  platform: PushPlatform;
  expoPushToken: string;
  deviceName?: string | null;
  appVersion?: string | null;
  locale?: string | null;
  /** null keeps a known device's categories and uses the server default for a new one. */
  categories?: PushCategory[] | null;
}

export type PushTestStatus = 'Sent' | 'Disabled' | 'DeviceInactive' | 'DeviceNotRegistered' | 'RateLimited' | 'Failed';

export interface PushTestResult {
  deviceId: string;
  status: PushTestStatus;
  ticketId?: string | null;
  error?: string | null;
  message?: string | null;
}

/** What the app keeps per server profile after registering (secure storage; the server masks the token). */
export interface PushRegistrationRecord {
  /** pdv_ id returned by the server: the handle for updates and deletion, and how a push names its profile. */
  deviceId: string;
  /** The Expo push token that was registered (compared to the current one to detect a token change). */
  expoPushToken: string;
  /** The user the device was registered for (a different user on the same profile registers afresh). */
  userId: string | null;
  categories: PushCategory[];
  registeredUtc: string;
}
