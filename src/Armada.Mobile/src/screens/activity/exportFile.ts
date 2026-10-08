import { File, Paths } from 'expo-file-system';
import * as Sharing from 'expo-sharing';
import { safeFileName } from '../../platform/files';

/**
 * Writes a text export (JSON, CSV, Markdown) to the cache folder and opens the share sheet: the mobile form of the
 * dashboard's download links.
 */
export async function shareTextFile(fileName: string, content: string, mimeType: string): Promise<void> {
  const target = new File(Paths.cache, safeFileName(fileName, 'armada-export.txt'));
  if (target.exists) target.delete();
  target.create();
  target.write(content);
  if (!(await Sharing.isAvailableAsync())) throw new Error(`Saved to ${target.uri}; sharing is not available on this device.`);
  await Sharing.shareAsync(target.uri, { mimeType, dialogTitle: fileName });
}

/** The export file stamp the dashboard uses (2026-10-07-12-30-00). */
export function exportTimestamp(now: Date = new Date()): string {
  return now.toISOString().slice(0, 19).replace(/[:T]/g, '-');
}
