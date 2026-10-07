import { sessionRequest, type ServerSession, type SessionFetch } from '../api/serverSession';
import type { PushCategory, PushDevice, PushDeviceRegisterRequest, PushTestResult } from './types';

/**
 * /api/v1/push/devices for one session. Bodies use the server's PascalCase names; responses are camelized. Every call
 * takes an explicit session so a background profile's device can be removed too.
 */
export interface PushApi {
  register: (session: ServerSession, body: PushDeviceRegisterRequest) => Promise<PushDevice>;
  updateCategories: (session: ServerSession, deviceId: string, categories: PushCategory[]) => Promise<PushDevice>;
  remove: (session: ServerSession, deviceId: string) => Promise<void>;
  sendTest: (session: ServerSession, deviceId: string) => Promise<PushTestResult>;
}

export function createPushApi(fetchImpl?: SessionFetch): PushApi {
  return {
    register: (session, body) => sessionRequest<PushDevice>(session, 'POST', '/api/v1/push/devices', {
      Platform: body.platform,
      ExpoPushToken: body.expoPushToken,
      DeviceName: body.deviceName ?? null,
      AppVersion: body.appVersion ?? null,
      Locale: body.locale ?? null,
      Categories: body.categories ?? null,
    }, fetchImpl),
    updateCategories: (session, deviceId, categories) =>
      sessionRequest<PushDevice>(session, 'PUT', `/api/v1/push/devices/${encodeURIComponent(deviceId)}`, { Categories: categories }, fetchImpl),
    remove: (session, deviceId) =>
      sessionRequest<void>(session, 'DELETE', `/api/v1/push/devices/${encodeURIComponent(deviceId)}`, undefined, fetchImpl),
    sendTest: (session, deviceId) =>
      sessionRequest<PushTestResult>(session, 'POST', `/api/v1/push/devices/${encodeURIComponent(deviceId)}/test`, undefined, fetchImpl),
  };
}

export const pushApi: PushApi = createPushApi();
