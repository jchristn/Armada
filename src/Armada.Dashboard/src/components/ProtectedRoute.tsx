import type { ReactNode } from 'react';
import { useAuth } from '../context/AuthContext';
import LoginFlow from './LoginFlow';

export default function ProtectedRoute({ children, requireAdmin, requireTenantAdmin }: { children: ReactNode; requireAdmin?: boolean; requireTenantAdmin?: boolean }) {
  const { isAuthenticated, isAdmin, isTenantAdmin, loading } = useAuth();

  if (loading) return null;
  if (!isAuthenticated) return <LoginFlow />;
  if (requireAdmin && !isAdmin) {
    return <div className="page-message"><h2>Access Denied</h2><p>You need administrator privileges to view this page.</p></div>;
  }
  if (requireTenantAdmin && !isTenantAdmin) {
    return <div className="page-message"><h2>Access Denied</h2><p>You need tenant administrator privileges to view this page.</p></div>;
  }
  return <>{children}</>;
}
