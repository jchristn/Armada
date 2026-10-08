import { ApiError } from '@dashboard/api/client';
import type { ServerSession } from '../api/serverSession';
import { createPushApi, type PushApi } from '../push/pushApi';
import {
  profileForDevice,
  registerDevice,
  reRegisterProfiles,
  unregisterDevice,
  updateDeviceCategories,
  type PermissionState,
  type PushEnvironment,
  type RegistrationDeps,
  type TokenResult,
} from '../push/registration';
import type { RegistrationStore } from '../push/registrationStore';
import { PUSH_CATEGORIES, type PushCategory, type PushDevice, type PushRegistrationRecord } from '../push/types';

const SESSION: ServerSession = { baseUrl: 'https://admiral.example', token: 'A1', headers: null };

function memoryStore(): RegistrationStore & { data: Map<string, PushRegistrationRecord> } {
  const data = new Map<string, PushRegistrationRecord>();
  return {
    data,
    read: async (id) => data.get(id) ?? null,
    write: async (id, record) => { data.set(id, record); },
    remove: async (id) => { data.delete(id); },
  };
}

let deviceCounter = 0;
function device(token: string, categories: PushCategory[] = [...PUSH_CATEGORIES]): PushDevice {
  deviceCounter += 1;
  return { id: `pdv_${deviceCounter}`, platform: 'Ios', expoPushToken: 'ExponentPushToken[****abcd]', categories, active: true };
}

function setup(options: { permission?: PermissionState; token?: TokenResult } = {}) {
  const env: PushEnvironment = {
    permission: jest.fn(async () => options.permission ?? 'granted'),
    expoToken: jest.fn(async () => options.token ?? { token: 'ExponentPushToken[one]', reason: null }),
    platform: 'Ios',
    deviceName: 'Test iPhone',
    appVersion: '1.0.0',
    locale: () => 'en-US',
    nowUtc: () => '2026-10-07T12:00:00Z',
  };
  const api: jest.Mocked<PushApi> = {
    register: jest.fn(async (_s, body) => device(body.expoPushToken)),
    updateCategories: jest.fn(async (_s, id, categories) => ({ ...device('x', categories), id })),
    remove: jest.fn(async (_s: ServerSession, _id: string): Promise<void> => undefined),
    sendTest: jest.fn(async (_s, id) => ({ deviceId: id, status: 'Sent' as const })),
  };
  const store = memoryStore();
  const deps: RegistrationDeps = { env, api, store };
  return { env, api, store, deps };
}

describe('push registration lifecycle', () => {
  it('registers on sign-in and keeps the device id the server returns (the token is masked in responses)', async () => {
    const { deps, api, store } = setup();
    const outcome = await registerDevice(deps, 'prf_a', SESSION, 'usr_1');
    expect(outcome.state).toBe('registered');
    expect(api.register).toHaveBeenCalledWith(SESSION, {
      platform: 'Ios',
      expoPushToken: 'ExponentPushToken[one]',
      deviceName: 'Test iPhone',
      appVersion: '1.0.0',
      locale: 'en-US',
      categories: null,
    });
    const record = store.data.get('prf_a')!;
    expect(record.deviceId).toMatch(/^pdv_/);
    expect(record.expoPushToken).toBe('ExponentPushToken[one]');
    expect(record.userId).toBe('usr_1');
    expect(record.categories).toEqual([...PUSH_CATEGORIES]);
  });

  it('never prompts: without permission nothing is sent', async () => {
    const { deps, api, env } = setup({ permission: 'undetermined' });
    expect(await registerDevice(deps, 'prf_a', SESSION, 'usr_1')).toEqual({ state: 'permission', permission: 'undetermined' });
    expect(env.expoToken).not.toHaveBeenCalled();
    expect(api.register).not.toHaveBeenCalled();
  });

  it('a build without an EAS project id reports noProject', async () => {
    const { deps, api } = setup({ token: { token: null, reason: 'noProject' } });
    expect(await registerDevice(deps, 'prf_a', SESSION, 'usr_1')).toEqual({ state: 'unavailable', reason: 'noProject' });
    expect(api.register).not.toHaveBeenCalled();
  });

  it('re-registering with the same token refreshes the same device and leaves categories to the server', async () => {
    const { deps, api, store } = setup();
    api.register.mockImplementation(async () => ({ ...device('t'), id: 'pdv_same' }));
    await registerDevice(deps, 'prf_a', SESSION, 'usr_1');
    await registerDevice(deps, 'prf_a', SESSION, 'usr_1');
    expect(api.register).toHaveBeenCalledTimes(2);
    expect(api.register.mock.calls[1][1].categories).toBeNull();
    expect(api.remove).not.toHaveBeenCalled();
    expect(store.data.get('prf_a')!.deviceId).toBe('pdv_same');
  });

  it('a token change re-registers, carries the user\'s categories, and removes the stale device', async () => {
    const { deps, api, store } = setup();
    await registerDevice(deps, 'prf_a', SESSION, 'usr_1');
    const old = store.data.get('prf_a')!;
    store.data.set('prf_a', { ...old, categories: ['AskProposal'] });
    const result = await reRegisterProfiles(deps, ['prf_a', 'prf_b'], async () => ({ session: SESSION, userId: 'usr_1' }), 'ExponentPushToken[two]');
    expect(result).toEqual({ prf_a: 'registered', prf_b: 'skipped' });
    const call = api.register.mock.calls[1][1];
    expect(call.expoPushToken).toBe('ExponentPushToken[two]');
    expect(call.categories).toEqual(['AskProposal']);
    expect(api.remove).toHaveBeenCalledWith(SESSION, old.deviceId);
    expect(store.data.get('prf_a')!.expoPushToken).toBe('ExponentPushToken[two]');
  });

  it('a token change skips profiles already on the new token and profiles without a stored session', async () => {
    const { deps, api } = setup();
    await registerDevice(deps, 'prf_a', SESSION, 'usr_1');
    await registerDevice(deps, 'prf_b', SESSION, 'usr_1');
    api.register.mockClear();
    const result = await reRegisterProfiles(deps, ['prf_a', 'prf_b'], async (id) => (id === 'prf_a' ? null : { session: SESSION, userId: 'usr_1' }), 'ExponentPushToken[one]');
    expect(result).toEqual({ prf_a: 'skipped', prf_b: 'skipped' });
    expect(api.register).not.toHaveBeenCalled();
  });

  it('a different user on the same profile registers afresh (server defaults) and does not delete the other user\'s device', async () => {
    const { deps, api, store } = setup();
    await registerDevice(deps, 'prf_a', SESSION, 'usr_1');
    store.data.set('prf_a', { ...store.data.get('prf_a')!, expoPushToken: 'ExponentPushToken[old]', categories: [] });
    await registerDevice(deps, 'prf_a', SESSION, 'usr_2');
    expect(api.register.mock.calls[1][1].categories).toBeNull();
    expect(api.remove).not.toHaveBeenCalled();
    expect(store.data.get('prf_a')!.userId).toBe('usr_2');
  });

  it('sign-out deletes the device on the server and forgets it locally', async () => {
    const { deps, api, store } = setup();
    await registerDevice(deps, 'prf_a', SESSION, 'usr_1');
    const id = store.data.get('prf_a')!.deviceId;
    await unregisterDevice(deps, 'prf_a', SESSION);
    expect(api.remove).toHaveBeenCalledWith(SESSION, id);
    expect(store.data.has('prf_a')).toBe(false);
  });

  it('without a usable session (token rejected) only the local record goes; a failing delete still forgets it', async () => {
    const { deps, api, store } = setup();
    await registerDevice(deps, 'prf_a', SESSION, 'usr_1');
    await unregisterDevice(deps, 'prf_a', null);
    expect(api.remove).not.toHaveBeenCalled();
    expect(store.data.has('prf_a')).toBe(false);

    await registerDevice(deps, 'prf_a', SESSION, 'usr_1');
    api.remove.mockRejectedValueOnce(new ApiError('Not found', 404, null));
    await unregisterDevice(deps, 'prf_a', SESSION);
    expect(store.data.has('prf_a')).toBe(false);
  });

  it('category changes PUT to the stored device', async () => {
    const { deps, api, store } = setup();
    await registerDevice(deps, 'prf_a', SESSION, 'usr_1');
    const id = store.data.get('prf_a')!.deviceId;
    const outcome = await updateDeviceCategories(deps, 'prf_a', SESSION, 'usr_1', ['MissionFailed']);
    expect(api.updateCategories).toHaveBeenCalledWith(SESSION, id, ['MissionFailed']);
    expect(outcome.state).toBe('registered');
    expect(store.data.get('prf_a')!.categories).toEqual(['MissionFailed']);
  });

  it('a device the server no longer has (404) is registered again and gets the chosen categories', async () => {
    const { deps, api, store } = setup();
    await registerDevice(deps, 'prf_a', SESSION, 'usr_1');
    api.updateCategories.mockRejectedValueOnce(new ApiError('Not found', 404, null));
    const outcome = await updateDeviceCategories(deps, 'prf_a', SESSION, 'usr_1', ['CliPermission']);
    expect(api.register).toHaveBeenCalledTimes(2);
    expect(api.updateCategories).toHaveBeenCalledTimes(2);
    expect(outcome.state).toBe('registered');
    expect(store.data.get('prf_a')!.categories).toEqual(['CliPermission']);
  });

  it('server errors are reported, not thrown', async () => {
    const { deps, api } = setup();
    api.register.mockRejectedValueOnce(new ApiError('Captain sessions cannot register', 403, null));
    expect(await registerDevice(deps, 'prf_a', SESSION, 'usr_1')).toEqual({ state: 'error', status: 403 });
  });

  it('the secure store keeps records with real server ids (PrettyId bodies contain _ and -)', async () => {
    const { secureRegistrationStore } = jest.requireActual('../push/registrationStore') as typeof import('../push/registrationStore');
    const record: PushRegistrationRecord = { deviceId: 'pdv_muyliuwq_Fg2p67toB2W', expoPushToken: 'ExponentPushToken[x]', userId: 'default', categories: ['AskProposal'], registeredUtc: 'now' };
    await secureRegistrationStore.write('prf_real', record);
    expect(await secureRegistrationStore.read('prf_real')).toEqual(record);
    await secureRegistrationStore.remove('prf_real');
    expect(await secureRegistrationStore.read('prf_real')).toBeNull();
  });

  it('finds the profile a push came from by its device id', async () => {
    const { deps, store } = setup();
    await registerDevice(deps, 'prf_a', SESSION, 'usr_1');
    await registerDevice(deps, 'prf_b', SESSION, 'usr_1');
    const b = store.data.get('prf_b')!.deviceId;
    expect(await profileForDevice(store, ['prf_a', 'prf_b'], b)).toBe('prf_b');
    expect(await profileForDevice(store, ['prf_a', 'prf_b'], 'pdv_unknown')).toBeNull();
  });

  it('a device id held by two profiles names neither (it cannot say which user the push is for)', async () => {
    const { deps, store } = setup();
    await registerDevice(deps, 'prf_a', SESSION, 'usr_1');
    store.data.set('prf_b', { ...store.data.get('prf_a')!, userId: 'usr_2' });
    const shared = store.data.get('prf_a')!.deviceId;
    expect(await profileForDevice(store, ['prf_a', 'prf_b'], shared)).toBeNull();
  });
});

describe('push API wire format', () => {
  function recorder(status = 200, body: unknown = {}) {
    const calls: { url: string; init: { method: string; headers: Record<string, string>; body?: string; credentials?: string } }[] = [];
    const fetchImpl = jest.fn(async (url: string, init: { method: string; headers: Record<string, string>; body?: string; credentials?: 'omit' }) => {
      calls.push({ url, init });
      return { status, ok: status >= 200 && status < 300, text: async () => (status === 204 ? '' : JSON.stringify(body)) };
    });
    return { calls, api: createPushApi(fetchImpl) };
  }

  it('POSTs PascalCase fields with the Admiral token and the proxy header when present', async () => {
    const { calls, api } = recorder(201, { Id: 'pdv_1', Platform: 'Ios', ExpoPushToken: 'ExponentPushToken[****abcd]', Categories: ['AskProposal'], Active: true });
    const session: ServerSession = { baseUrl: 'https://proxy.example', token: 'A1', headers: { 'X-Armada-Proxy-Session': 'P1' } };
    const result = await api.register(session, { platform: 'Android', expoPushToken: 'ExponentPushToken[x]', deviceName: 'Pixel', appVersion: '1.0.0', locale: 'de-DE', categories: null });
    expect(result.id).toBe('pdv_1');
    expect(result.categories).toEqual(['AskProposal']);
    expect(calls[0].url).toBe('https://proxy.example/api/v1/push/devices');
    expect(calls[0].init.method).toBe('POST');
    expect(calls[0].init.headers['X-Token']).toBe('A1');
    expect(calls[0].init.headers['X-Armada-Proxy-Session']).toBe('P1');
    expect(calls[0].init.credentials).toBe('omit');
    expect(JSON.parse(calls[0].init.body!)).toEqual({ Platform: 'Android', ExpoPushToken: 'ExponentPushToken[x]', DeviceName: 'Pixel', AppVersion: '1.0.0', Locale: 'de-DE', Categories: null });
  });

  it('PUT categories, DELETE, and test use the device id; errors are ApiErrors with the status', async () => {
    const ok = recorder(200, { Id: 'pdv_9', Categories: [] });
    await ok.api.updateCategories(SESSION, 'pdv_9', []);
    expect(ok.calls[0].url).toBe('https://admiral.example/api/v1/push/devices/pdv_9');
    expect(JSON.parse(ok.calls[0].init.body!)).toEqual({ Categories: [] });
    const gone = recorder(204);
    await gone.api.remove(SESSION, 'pdv_9');
    expect(gone.calls[0].init.method).toBe('DELETE');
    const missing = recorder(404, { Error: 'NotFound', Message: 'Device not found' });
    await expect(missing.api.sendTest(SESSION, 'pdv_9')).rejects.toMatchObject({ status: 404 });
    expect(missing.calls[0].url).toBe('https://admiral.example/api/v1/push/devices/pdv_9/test');
  });
});
