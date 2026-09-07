import { useEffect, useState } from 'react';
import { listUsers } from '../../api/client';
import type { UserMaster } from '../../types/models';
import { useAuth } from '../../context/AuthContext';
import { useLocale } from '../../context/LocaleContext';

interface UserScopeFilterProps {
  /** Currently selected user id, or '' for "all users in scope". */
  value: string;
  onChange: (userId: string) => void;
}

/**
 * Admin-only "view as user" dropdown for Category A (owned) tables. Tenant/global admins can narrow a
 * user-scoped table to a single user's records, or leave it on "All users" to see the whole tenant.
 * Renders nothing for regular users - their view is already scoped to themselves server-side, so the
 * dropdown would be a no-op. The selected id is passed to list/enumerate calls as the `userId` filter.
 */
export default function UserScopeFilter({ value, onChange }: UserScopeFilterProps) {
  const { isAdmin, isTenantAdmin } = useAuth();
  const { t } = useLocale();
  const [users, setUsers] = useState<UserMaster[]>([]);

  const canScope = isAdmin || isTenantAdmin;

  useEffect(() => {
    if (!canScope) return;
    let mounted = true;
    listUsers()
      .then((result) => { if (mounted) setUsers(result.objects || []); })
      .catch(() => { /* non-fatal: dropdown just stays minimal */ });
    return () => { mounted = false; };
  }, [canScope]);

  if (!canScope) return null;

  function userLabel(user: UserMaster): string {
    const name = [user.firstName, user.lastName].filter(Boolean).join(' ').trim();
    return name ? `${name} (${user.email})` : user.email;
  }

  return (
    <select value={value} onChange={(event) => onChange(event.target.value)} title={t('View records for a specific user')}>
      <option value="">{t('All users')}</option>
      {users.map((user) => (
        <option key={user.id} value={user.id}>{userLabel(user)}</option>
      ))}
    </select>
  );
}
