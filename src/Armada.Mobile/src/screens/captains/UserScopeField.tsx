import { useEffect, useState } from 'react';
import { listUsers } from '@dashboard/api/client';
import type { UserMaster } from '@dashboard/types/models';
import { useAuth } from '../../auth/AuthContext';
import { SelectField, type SelectOption } from '../../components/ui/SelectSheet';
import { useLocale } from '../../i18n/LocaleContext';

/**
 * Admin-only "view records for a specific user" picker (the dashboard's UserScopeFilter): '' is all users in scope.
 * Renders nothing for regular users, whose lists the server already scopes to themselves.
 */
export function UserScopeField({ value, onChange, testID }: { value: string; onChange: (userId: string) => void; testID?: string }) {
  const { t } = useLocale();
  const { isAdmin, isTenantAdmin } = useAuth();
  const canScope = isAdmin || isTenantAdmin;
  const [users, setUsers] = useState<UserMaster[]>([]);

  useEffect(() => {
    if (!canScope) return undefined;
    let mounted = true;
    listUsers()
      .then((result) => { if (mounted) setUsers(result?.objects || []); })
      .catch(() => { /* non-fatal: the picker keeps only "All users" */ });
    return () => { mounted = false; };
  }, [canScope]);

  if (!canScope) return null;
  const options: SelectOption<string>[] = [
    { value: '', label: t('All users') },
    ...users.map((user) => {
      const name = [user.firstName, user.lastName].filter(Boolean).join(' ').trim();
      return { value: user.id, label: name ? `${name} (${user.email})` : user.email };
    }),
  ];
  return <SelectField label={t('View records for a specific user')} value={value} options={options} onChange={onChange} closeLabel={t('Close')} testID={testID} />;
}
