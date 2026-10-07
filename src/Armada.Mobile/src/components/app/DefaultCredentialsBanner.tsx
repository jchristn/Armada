import { useAuth } from '../../auth/AuthContext';
import { useLocale } from '../../i18n/LocaleContext';
import { Banner } from '../ui';

/**
 * Mirrors the dashboard's DefaultCredentialsBanner: while the server reports the well-known default credentials
 * (shown to admins), or the user skipped changing the default password, keep the risk visible.
 */
export function DefaultCredentialsBanner() {
  const { user, passwordChangeSkipped } = useAuth();
  const { t } = useLocale();
  if (!user?.defaultCredentialsInUse && !(passwordChangeSkipped && user?.passwordChangeRequired)) return null;
  return (
    <Banner
      testID="default-credentials-banner"
      tone="warning"
      title={t('Default credentials are in use')}
      message={t('Change the admin@armada password on the Users page. Anyone who can reach this server can sign in with the default password.')}
    />
  );
}
