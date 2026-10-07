import { useEffect, useState } from 'react';
import { listUsers } from '@dashboard/api/client';
import { userScopeLabel } from '@dashboard/lib/userScope';
import type { UserMaster } from '@dashboard/types/models';
import { useAuth } from '../../auth/AuthContext';
import { useLocale } from '../../i18n/LocaleContext';
import { SelectField } from '../ui/SelectField';

/**
 * The dashboard's admin-only UserScopeFilter: tenant and global admins can narrow an owned list to one user's
 * records ('' is all users). Renders nothing for regular users (the server already scopes their view).
 */
export function UserScopeSelect({ value, onChange, testID }: { value: string; onChange: (userId: string) => void; testID?: string }) {
  const { isAdmin, isTenantAdmin } = useAuth();
  const { t } = useLocale();
  const [users, setUsers] = useState<UserMaster[]>([]);
  const canScope = isAdmin || isTenantAdmin;

  useEffect(() => {
    if (!canScope) return undefined;
    let mounted = true;
    listUsers()
      .then((result) => { if (mounted) setUsers(result?.objects ?? []); })
      .catch(() => undefined);
    return () => { mounted = false; };
  }, [canScope]);

  if (!canScope) return null;
  return (
    <SelectField
      label={t('View records for a specific user')}
      value={value}
      onChange={onChange}
      allowEmpty
      placeholder={t('All users')}
      closeLabel={t('Close')}
      options={users.map((user) => ({ value: user.id, label: userScopeLabel(user) }))}
      testID={testID}
    />
  );
}
