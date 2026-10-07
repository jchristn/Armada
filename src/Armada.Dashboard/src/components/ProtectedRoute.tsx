import type { ReactNode } from 'react';
import { useAuth } from '../context/AuthContext';
import { useLocale } from '../context/LocaleContext';
import LoginFlow from './LoginFlow';
import PasswordChangeRequired from './PasswordChangeRequired';

export default function ProtectedRoute({ children, requireAdmin, requireTenantAdmin }: { children: ReactNode; requireAdmin?: boolean; requireTenantAdmin?: boolean }) {
  const { isAuthenticated, isAdmin, isTenantAdmin, loading, user, passwordChangeSkipped } = useAuth();
  const { t } = useLocale();

  if (loading) return null;
  if (!isAuthenticated) return <LoginFlow />;
  if (user?.passwordChangeRequired && !passwordChangeSkipped) return <PasswordChangeRequired />;
  if (requireAdmin && !isAdmin) {
    return <div className="page-message"><h2>{t('Access Denied')}</h2><p>{t('You need administrator privileges to view this page.')}</p></div>;
  }
  if (requireTenantAdmin && !isTenantAdmin) {
    return <div className="page-message"><h2>{t('Access Denied')}</h2><p>{t('You need tenant administrator privileges to view this page.')}</p></div>;
  }
  return <>{children}</>;
}
