import { Alert, Linking } from 'react-native';
import type { LocaleState } from '../i18n/LocaleContext';

/**
 * Opening URLs that came from the server (pull request links, advisory links, an objective's source link, links in
 * captain output) in another app. Only http(s) URLs are opened: another scheme in server data (a payments app, sms:,
 * tel:, shortcuts) could start an action in another app. A URL with credentials in it (https://github.com@evil.example)
 * is refused, because its visible start disguises the real host. Links whose text can differ from the target, or
 * that any user can type, are opened only after the user saw the destination host.
 */

const AUTHORITY = /^https?:\/\/([^/?#]*)/i;

/** The URL when it is safe to hand to the system (http or https, no credentials, no whitespace), else null. */
export function externalUrl(value: unknown): string | null {
  if (typeof value !== 'string') return null;
  const url = value.trim();
  if (!url || url.length > 4096 || /[\s\u0000-\u001f\u007f]/.test(url)) return null;
  const match = AUTHORITY.exec(url);
  if (!match) return null;
  const authority = match[1];
  if (!authority || authority.includes('@') || authority.includes('\\')) return null;
  return url;
}

/** The host of an external URL as the user should see it (lowercase, without the port). */
export function externalHost(url: string): string {
  const match = AUTHORITY.exec(url);
  if (!match) return '';
  const authority = match[1].toLowerCase();
  if (authority.startsWith('[')) return authority.slice(0, authority.indexOf(']') + 1);
  return authority.replace(/:\d+$/, '');
}

/** Open an http(s) URL outside the app. Resolves false (and opens nothing) for anything else. Never throws. */
export async function openExternalUrl(value: unknown): Promise<boolean> {
  const url = externalUrl(value);
  if (!url) return false;
  try {
    await Linking.openURL(url);
    return true;
  } catch {
    return false;
  }
}

/**
 * Ask before leaving the app, naming the destination host and the full URL; opens it on confirmation. Returns false
 * (and asks nothing) when the URL may not be opened at all.
 */
export function confirmOpenExternalUrl(value: unknown, t: LocaleState['t']): boolean {
  const url = externalUrl(value);
  if (!url) return false;
  Alert.alert(
    t('Open {{host}}?', { host: externalHost(url) }),
    t('This link leaves Armada and opens in your browser:\n{{url}}', { url }),
    [
      { text: t('Cancel'), style: 'cancel' },
      { text: t('Open'), onPress: () => { void openExternalUrl(url); } },
    ],
  );
  return true;
}
