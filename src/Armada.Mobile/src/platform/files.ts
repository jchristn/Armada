import { configureClient, type ClientPlatformAdapter, type DownloadedFile, type UploadFile } from '@dashboard/api/client';
import * as DocumentPicker from 'expo-document-picker';
import { File, Paths } from 'expo-file-system';
import * as Sharing from 'expo-sharing';

/** The base64 payload of a data: URL ('data:application/zip;base64,AAAA' -> 'AAAA'). */
export function base64FromDataUrl(dataUrl: string): string {
  const comma = dataUrl.indexOf(',');
  return comma >= 0 ? dataUrl.slice(comma + 1) : dataUrl;
}

/** A file name safe to create in the cache folder (no path separators or control characters). */
export function safeFileName(name: string, fallback: string): string {
  // Strip path separators and control characters from a server-supplied name.
  const cleaned = name.replace(/[/\\:\u0000-\u001f]/g, '_').trim();
  return cleaned && cleaned !== '.' && cleaned !== '..' ? cleaned : fallback;
}

/** Reads a Blob as base64 (React Native's Blob has no arrayBuffer(); FileReader.readAsDataURL is supported). */
function blobToBase64(blob: Blob): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onerror = () => reject(reader.error ?? new Error('Could not read the downloaded file.'));
    reader.onload = () => resolve(base64FromDataUrl(String(reader.result ?? '')));
    reader.readAsDataURL(blob);
  });
}

/** Mobile platform services for the shared API client: a downloaded file is written to the cache and shared. */
export const nativePlatform: ClientPlatformAdapter = {
  saveFile: async (file: DownloadedFile) => {
    const base64 = await blobToBase64(file.body);
    const target = new File(Paths.cache, safeFileName(file.filename, 'armada-download.bin'));
    if (target.exists) target.delete();
    target.create();
    target.write(base64, { encoding: 'base64' });
    if (!(await Sharing.isAvailableAsync())) throw new Error(`Saved to ${target.uri}; sharing is not available on this device.`);
    await Sharing.shareAsync(target.uri, { mimeType: file.contentType, UTI: 'public.zip-archive', dialogTitle: file.filename });
  },
};

let registered = false;

/** Registers the mobile platform adapter with the shared client (idempotent). */
export function ensureNativePlatform(): void {
  if (registered) return;
  configureClient({ platform: nativePlatform });
  registered = true;
}

/**
 * Lets the user pick a backup ZIP with the system document picker. Resolves to an UploadFile for restoreBackup,
 * or null when the user cancelled.
 */
export async function pickBackupFile(): Promise<UploadFile | null> {
  const result = await DocumentPicker.getDocumentAsync({
    type: ['application/zip', 'application/x-zip-compressed', 'application/octet-stream'],
    copyToCacheDirectory: true,
    multiple: false,
  });
  if (result.canceled || !result.assets || result.assets.length === 0) return null;
  const asset = result.assets[0];
  const picked = new File(asset.uri);
  return { name: asset.name || 'armada-backup.zip', arrayBuffer: () => picked.arrayBuffer() };
}
