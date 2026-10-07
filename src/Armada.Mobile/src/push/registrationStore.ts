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

export interface RegistrationStore {
  read: (profileId: string) => Promise<PushRegistrationRecord | null>;
  write: (profileId: string, record: PushRegistrationRecord) => Promise<void>;
  remove: (profileId: string) => Promise<void>;
}

function parse(raw: string | null): PushRegistrationRecord | null {
  if (!raw) return null;
  try {
    const value = JSON.parse(raw) as Partial<PushRegistrationRecord>;
    if (typeof value.deviceId !== 'string' || !/^pdv_[A-Za-z0-9]+$/.test(value.deviceId)) return null;
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
};
