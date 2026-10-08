import * as SecureStore from 'expo-secure-store';
import { isPushCategory, type PushRegistrationRecord } from './types';

/**
 * Per-profile push registration records, in secure storage next to the profile's tokens (the Expo push token is
 * an address anyone could send to, so it is kept out of plain preferences).
 */

const OPTIONS: SecureStore.SecureStoreOptions = {
  keychainAccessible: SecureStore.AFTER_FIRST_UNLOCK_THIS_DEVICE_ONLY,
};

export function pushRecordKey(profileId: string): string {
  return `armada.push.${profileId.replace(/[^A-Za-z0-9._-]/g, '_')}`;
}

/** A device this app stopped using (signed out) whose server-side removal has not gone through yet. */
export interface RetiredDevice {
  profileId: string;
  deviceId: string;
}

export interface RegistrationStore {
  read: (profileId: string) => Promise<PushRegistrationRecord | null>;
  write: (profileId: string, record: PushRegistrationRecord) => Promise<void>;
  remove: (profileId: string) => Promise<void>;
  /**
   * Devices to remove from their server at the next contact (sign-out could not reach it, or the token was already
   * rejected). Optional: a store without them does not queue removals.
   */
  readRetired?: () => Promise<RetiredDevice[]>;
  writeRetired?: (devices: RetiredDevice[]) => Promise<void>;
}

/** Secure-store key of the retired-device queue (device ids are not secret, but they sit with the push records). */
export const RETIRED_DEVICES_KEY = 'armada.push.retired';

const DEVICE_ID = /^pdv_[A-Za-z0-9_-]{1,96}$/;

function parseRetired(raw: string | null): RetiredDevice[] {
  if (!raw) return [];
  try {
    const value = JSON.parse(raw) as unknown;
    if (!Array.isArray(value)) return [];
    return value.filter((v): v is RetiredDevice => !!v && typeof v === 'object'
      && typeof (v as RetiredDevice).profileId === 'string' && typeof (v as RetiredDevice).deviceId === 'string'
      && DEVICE_ID.test((v as RetiredDevice).deviceId));
  } catch {
    return [];
  }
}

function parse(raw: string | null): PushRegistrationRecord | null {
  if (!raw) return null;
  try {
    const value = JSON.parse(raw) as Partial<PushRegistrationRecord>;
    if (typeof value.deviceId !== 'string' || !DEVICE_ID.test(value.deviceId)) return null;
    if (typeof value.expoPushToken !== 'string') return null;
    return {
      deviceId: value.deviceId,
      expoPushToken: value.expoPushToken,
      userId: typeof value.userId === 'string' ? value.userId : null,
      categories: Array.isArray(value.categories) ? value.categories.filter(isPushCategory) : [],
      registeredUtc: typeof value.registeredUtc === 'string' ? value.registeredUtc : '',
    };
  } catch {
    return null;
  }
}

export const secureRegistrationStore: RegistrationStore = {
  async read(profileId) {
    try {
      return parse(await SecureStore.getItemAsync(pushRecordKey(profileId), OPTIONS));
    } catch {
      return null;
    }
  },
  async write(profileId, record) {
    await SecureStore.setItemAsync(pushRecordKey(profileId), JSON.stringify(record), OPTIONS);
  },
  async remove(profileId) {
    try {
      await SecureStore.deleteItemAsync(pushRecordKey(profileId), OPTIONS);
    } catch {
      // Already absent.
    }
  },
  async readRetired() {
    try {
      return parseRetired(await SecureStore.getItemAsync(RETIRED_DEVICES_KEY, OPTIONS));
    } catch {
      return [];
    }
  },
  async writeRetired(devices) {
    try {
      if (devices.length === 0) await SecureStore.deleteItemAsync(RETIRED_DEVICES_KEY, OPTIONS);
      else await SecureStore.setItemAsync(RETIRED_DEVICES_KEY, JSON.stringify(devices), OPTIONS);
    } catch {
      // Best effort: the next sign-out or sign-in tries again.
    }
  },
};
