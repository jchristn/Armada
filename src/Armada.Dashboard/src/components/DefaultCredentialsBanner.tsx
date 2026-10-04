import { useAuth } from '../context/AuthContext';
import { useLocale } from '../context/LocaleContext';

/** Persistent warning while the server still has default credentials in use (reported to admins only). */
export default function DefaultCredentialsBanner() {
  const { user } = useAuth();
  const { t } = useLocale();
  if (!user?.defaultCredentialsInUse) return null;
  return (
    <div className="default-credentials-banner" role="alert">
      <strong>{t('Default credentials are in use.')}</strong>{' '}
      {t('An admin@armada account still has the default password, or the default bearer token is active. Change the password (each tenant\'s admin@armada signs in and is prompted) before exposing this server beyond localhost.')}
    </div>
  );
}
