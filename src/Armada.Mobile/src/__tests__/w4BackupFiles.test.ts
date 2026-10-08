/** Backup / restore file handling on the device (security review F-56): cleanup and owner re-authentication. */
import * as DocumentPicker from 'expo-document-picker';
import * as FileSystem from 'expo-file-system';
import * as LocalAuthentication from 'expo-local-authentication';
import * as Sharing from 'expo-sharing';
import { deleteQuietly, nativePlatform, pickBackupFile, reauthenticateForExport, safeFileName } from '../platform/files';

interface FakeFile { uri: string; exists: boolean; written: string; deleted: boolean }

jest.mock('expo-file-system', () => {
  const made: unknown[] = [];
  class File {
    uri: string;
    exists = false;
    written = '';
    deleted = false;
    constructor(...parts: unknown[]) {
      this.uri = parts.map((p) => (typeof p === 'string' ? p : 'cache')).join('/');
      made.push(this);
    }
    create() { this.exists = true; }
    write(content: string) { this.written = content; }
    delete() { this.exists = false; this.deleted = true; }
    async arrayBuffer() { return new ArrayBuffer(2); }
  }
  return { File, Paths: { cache: 'cache' }, __made: made };
});
jest.mock('expo-sharing', () => ({ isAvailableAsync: jest.fn(async () => true), shareAsync: jest.fn(async () => undefined) }));
jest.mock('expo-document-picker', () => ({ getDocumentAsync: jest.fn() }));
jest.mock('expo-local-authentication', () => ({
  SecurityLevel: { NONE: 0, SECRET: 1, BIOMETRIC_WEAK: 2, BIOMETRIC_STRONG: 3 },
  getEnrolledLevelAsync: jest.fn(),
  authenticateAsync: jest.fn(),
}));

const created = (FileSystem as unknown as { __made: FakeFile[] }).__made;

class FakeReader {
  result: string | null = null;
  error: Error | null = null;
  onload: (() => void) | null = null;
  onerror: (() => void) | null = null;
  readAsDataURL() { this.result = 'data:application/zip;base64,UEsDBA=='; this.onload?.(); }
}

const auth = LocalAuthentication as jest.Mocked<typeof LocalAuthentication>;
const sharing = Sharing as jest.Mocked<typeof Sharing>;

beforeEach(() => {
  created.length = 0;
  jest.clearAllMocks();
  (globalThis as unknown as { FileReader: unknown }).FileReader = FakeReader;
});

const download = { filename: 'armada-backup-2026-10-07.zip', contentType: 'application/zip', body: {} as Blob };

describe('backup export', () => {
  it('writes the ZIP to the cache, shares it, and deletes it when the share sheet closes', async () => {
    await nativePlatform.saveFile(download);
    expect(sharing.shareAsync).toHaveBeenCalledWith('cache/armada-backup-2026-10-07.zip', expect.objectContaining({ mimeType: 'application/zip' }));
    expect(created[0].written).toBe('UEsDBA==');
    expect(created[0].deleted).toBe(true);
    expect(created[0].exists).toBe(false);
  });

  it('deletes the file even when sharing fails', async () => {
    sharing.shareAsync.mockRejectedValueOnce(new Error('share failed'));
    await expect(nativePlatform.saveFile(download)).rejects.toThrow('share failed');
    expect(created[0].deleted).toBe(true);
  });

  it('deletes the file when sharing is unavailable', async () => {
    sharing.isAvailableAsync.mockResolvedValueOnce(false);
    await expect(nativePlatform.saveFile(download)).rejects.toThrow('Sharing is not available on this device.');
    expect(created[0].deleted).toBe(true);
  });

  it('keeps server-supplied names inside the cache folder', () => {
    expect(safeFileName('../../etc/passwd', 'x')).toBe('.._.._etc_passwd');
    expect(safeFileName('..', 'fallback.zip')).toBe('fallback.zip');
    expect(deleteQuietly(null)).toBeUndefined();
  });
});

describe('owner re-authentication before export', () => {
  it('asks for biometrics or the passcode when the device has a lock', async () => {
    auth.getEnrolledLevelAsync.mockResolvedValue(LocalAuthentication.SecurityLevel.BIOMETRIC_STRONG);
    auth.authenticateAsync.mockResolvedValue({ success: true } as never);
    await expect(reauthenticateForExport('Confirm', 'Cancel')).resolves.toBe(true);
    expect(auth.authenticateAsync).toHaveBeenCalledWith({ promptMessage: 'Confirm', cancelLabel: 'Cancel', disableDeviceFallback: false });
  });

  it('refuses when the owner cancels or fails', async () => {
    auth.getEnrolledLevelAsync.mockResolvedValue(LocalAuthentication.SecurityLevel.SECRET);
    auth.authenticateAsync.mockResolvedValue({ success: false, error: 'user_cancel' } as never);
    await expect(reauthenticateForExport('Confirm', 'Cancel')).resolves.toBe(false);
    auth.authenticateAsync.mockRejectedValue(new Error('boom'));
    await expect(reauthenticateForExport('Confirm', 'Cancel')).resolves.toBe(false);
  });

  it('passes on a device with no screen lock (nothing to verify against)', async () => {
    auth.getEnrolledLevelAsync.mockResolvedValue(LocalAuthentication.SecurityLevel.NONE);
    await expect(reauthenticateForExport('Confirm', 'Cancel')).resolves.toBe(true);
    expect(auth.authenticateAsync).not.toHaveBeenCalled();
  });
});

describe('restore pick', () => {
  it('returns the upload and a dispose that deletes the picker copy', async () => {
    (DocumentPicker.getDocumentAsync as jest.Mock).mockResolvedValue({ canceled: false, assets: [{ uri: 'file:///cache/picked.zip', name: 'picked.zip' }] });
    const picked = await pickBackupFile();
    expect(picked?.file.name).toBe('picked.zip');
    created[0].exists = true;
    picked?.dispose();
    expect(created[0].deleted).toBe(true);
  });

  it('is null when the user cancels', async () => {
    (DocumentPicker.getDocumentAsync as jest.Mock).mockResolvedValue({ canceled: true, assets: null });
    await expect(pickBackupFile()).resolves.toBeNull();
  });
});
