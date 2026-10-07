import { urlSecurityLevel } from '../../profiles/serverUrl';
import { useLocale } from '../../i18n/LocaleContext';
import { Banner } from '../ui';

/** The plain-HTTP warning shown wherever a server URL is entered or in use. Nothing for https. */
export function InsecureUrlWarning({ url }: { url: string | null | undefined }) {
  const { t } = useLocale();
  if (!url) return null;
  const level = urlSecurityLevel(url);
  if (level === 'secure') return null;
  return (
    <Banner
      testID="insecure-url-warning"
      tone={level === 'public' ? 'danger' : 'warning'}
      title={t('This connection is not encrypted')}
      message={level === 'public'
        ? t('Your password and session token are sent in plain text over the internet. Use an https:// address for any server outside your local network.')
        : t('Plain HTTP is acceptable only on a network you trust (for example your home or office LAN). Use https:// everywhere else.')}
    />
  );
}
