import * as Clipboard from 'expo-clipboard';
import { Share } from 'react-native';

/** Puts text on the system clipboard. */
export async function copyText(text: string): Promise<void> {
  await Clipboard.setStringAsync(text);
}

/**
 * Opens the platform share sheet with text (Mail, Messages, Notes, an agent app...). Resolves true when the text was
 * shared and false when the sheet was dismissed (iOS reports dismissal; Android always reports shared).
 */
export async function shareText(text: string, title?: string): Promise<boolean> {
  const result = await Share.share(title ? { message: text, title } : { message: text }, title ? { subject: title, dialogTitle: title } : undefined);
  return result.action !== Share.dismissedAction;
}
