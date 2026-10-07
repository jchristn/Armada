import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  configureClient,
  downloadBackup,
  getClientBaseUrl,
  getStatus,
  restoreBackup,
  setAuthToken,
  type DownloadedFile,
} from './client';

function jsonResponse(status: number, body: unknown, headers: Record<string, string> = {}): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } });
}

describe('host-agnostic client configuration', () => {
  afterEach(() => {
    configureClient({ baseUrl: '', platform: null, headers: null });
    setAuthToken(null);
    vi.unstubAllGlobals();
  });

  it('defaults to same-origin requests', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(200, {}));
    vi.stubGlobal('fetch', fetchMock);
    await getStatus();
    expect(fetchMock.mock.calls[0][0]).toBe('/api/v1/status');
  });

  it('prefixes the configured base URL and strips trailing slashes', async () => {
    configureClient({ baseUrl: 'http://10.0.2.2:44010///' });
    expect(getClientBaseUrl()).toBe('http://10.0.2.2:44010');
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(200, {}));
    vi.stubGlobal('fetch', fetchMock);
    await getStatus();
    expect(fetchMock.mock.calls[0][0]).toBe('http://10.0.2.2:44010/api/v1/status');
  });

  it('a partial configure keeps the fields it leaves out', () => {
    configureClient({ baseUrl: 'https://armada.example' });
    configureClient({ platform: null });
    expect(getClientBaseUrl()).toBe('https://armada.example');
  });

  it('downloadBackup hands the file and its suggested name to the platform adapter', async () => {
    const saved: DownloadedFile[] = [];
    configureClient({ platform: { saveFile: async (file) => { saved.push(file); } } });
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('zip-bytes', {
      status: 200,
      headers: { 'Content-Disposition': 'attachment; filename="armada-backup-x.zip"', 'Content-Type': 'application/zip' },
    })));
    await downloadBackup();
    expect(saved).toHaveLength(1);
    expect(saved[0].filename).toBe('armada-backup-x.zip');
    expect(saved[0].contentType).toBe('application/zip');
    expect(await saved[0].body.text()).toBe('zip-bytes');
  });

  it('downloadBackup without a platform adapter fails instead of touching the DOM', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('zip', { status: 200 })));
    await expect(downloadBackup()).rejects.toThrow(/platform adapter/);
  });

  it('restoreBackup accepts any name + arrayBuffer source, not only a browser File', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(200, { Restored: true }));
    vi.stubGlobal('fetch', fetchMock);
    const result = await restoreBackup({ name: 'backup.zip', arrayBuffer: async () => new Uint8Array([1, 2]).buffer });
    expect(result).toEqual({ restored: true });
    const init = fetchMock.mock.calls[0][1] as RequestInit;
    expect((init.headers as Record<string, string>)['X-Original-Filename']).toBe('backup.zip');
  });

  it('sends configured extra headers on every request, and the client\'s own headers win a clash', async () => {
    configureClient({ headers: { 'X-Armada-Proxy-Session': 'proxy-session', 'X-Token': 'host-should-not-win' } });
    setAuthToken('admiral-token');
    const fetchMock = vi.fn().mockImplementation(async () => jsonResponse(200, {}));
    vi.stubGlobal('fetch', fetchMock);
    await getStatus();
    await restoreBackup({ name: 'b.zip', arrayBuffer: async () => new Uint8Array([1]).buffer });
    for (const call of fetchMock.mock.calls) {
      const headers = (call[1] as RequestInit).headers as Record<string, string>;
      expect(headers['X-Armada-Proxy-Session']).toBe('proxy-session');
      expect(headers['X-Token']).toBe('admiral-token');
    }
  });

  it('headers: null clears the extra headers, and leaving the field out keeps them', async () => {
    configureClient({ headers: { 'X-Armada-Proxy-Session': 'p' } });
    configureClient({ baseUrl: 'https://proxy.example' });
    const fetchMock = vi.fn().mockImplementation(async () => jsonResponse(200, {}));
    vi.stubGlobal('fetch', fetchMock);
    await getStatus();
    expect(((fetchMock.mock.calls[0][1] as RequestInit).headers as Record<string, string>)['X-Armada-Proxy-Session']).toBe('p');
    configureClient({ headers: null });
    await getStatus();
    expect(((fetchMock.mock.calls[1][1] as RequestInit).headers as Record<string, string>)['X-Armada-Proxy-Session']).toBeUndefined();
  });
});
