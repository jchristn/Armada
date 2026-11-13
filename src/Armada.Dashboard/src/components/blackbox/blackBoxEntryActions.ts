import { pinBlackBox, reanalyzeBlackBox, unpinBlackBox } from '../../api/client';
import type { BlackBoxEntry } from '../../types/blackbox';
import type { Severity } from '../../lib/notificationSeverity';
import { browserPlatform } from '../../platform/browserPlatform';

/**
 * Entry actions shared by the black box panel and the row menu of the entries table: pin or unpin, reanalyze and the
 * Markdown brief download. Each one shows the same success and error toasts wherever it runs and resolves to the
 * updated entry (or true for the download), or null (false) when it failed.
 */

type Translate = (text: string, params?: Record<string, string | number | null | undefined>) => string;
type PushToast = (severity: Severity, message: string) => void;

function messageOf(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}

/** Pin the entry, or unpin it when it is pinned. */
export async function toggleBlackBoxPin(entry: { id: string; pinned: boolean }, t: Translate, pushToast: PushToast): Promise<BlackBoxEntry | null> {
  try {
    const updated = entry.pinned ? await unpinBlackBox(entry.id) : await pinBlackBox(entry.id);
    pushToast('success', updated.pinned ? t('Entry pinned: garbage collection keeps it.') : t('Entry unpinned: it expires with the retention policy.'));
    return updated;
  } catch (err: unknown) {
    pushToast('error', t('Failed: {{message}}', { message: messageOf(err) }));
    return null;
  }
}

/** Run enrichment and analysis again; successMessage is the already-localized toast for where it ran. */
export async function reanalyzeBlackBoxEntry(entryId: string, successMessage: string, t: Translate, pushToast: PushToast): Promise<BlackBoxEntry | null> {
  try {
    const updated = await reanalyzeBlackBox(entryId);
    pushToast('success', successMessage);
    return updated;
  } catch (err: unknown) {
    pushToast('error', t('Failed: {{message}}', { message: messageOf(err) }));
    return null;
  }
}

/** Fetch a brief and save it as a Markdown file. */
export async function downloadBlackBoxMarkdown(filename: string, fetchBrief: () => Promise<string>, t: Translate, pushToast: PushToast): Promise<boolean> {
  try {
    const text = await fetchBrief();
    await browserPlatform.saveFile({ filename, contentType: 'text/markdown', body: new Blob([text], { type: 'text/markdown' }) });
    return true;
  } catch (err: unknown) {
    pushToast('error', t('The download failed: {{message}}', { message: messageOf(err) }));
    return false;
  }
}
