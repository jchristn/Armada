import { useAuth } from '../../auth/AuthContext';
import { HubScreen, type HubTab } from '../../components/resource/Hub';
import { useLocale } from '../../i18n/LocaleContext';
import { CredentialsTab } from './CredentialsTab';
import { DiagnosticsTab } from './DiagnosticsTab';
import { ServerSettingsTab } from './ServerSettingsTab';
import { TenantsTab } from './TenantsTab';
import { UsersTab } from './UsersTab';

/**
 * The Settings hub (/server): Server, Diagnostics, and the role-gated Tenants (admins), Users and Credentials (admins
 * and tenant admins) tabs, as on the dashboard. Hidden tabs cannot be opened by ?tab= either; the server still
 * enforces authorization.
 */
export function ServerHub() {
  const { t } = useLocale();
  const { isAdmin, isTenantAdmin } = useAuth();
  const admin = isAdmin || isTenantAdmin;
  const tabs: HubTab[] = [
    { key: 'server', label: 'Server', render: () => <ServerSettingsTab /> },
    { key: 'diagnostics', label: 'Diagnostics', render: () => <DiagnosticsTab /> },
    { key: 'tenants', label: 'Tenants', hidden: !isAdmin, render: () => <TenantsTab /> },
    { key: 'users', label: 'Users', hidden: !admin, render: () => <UsersTab /> },
    { key: 'credentials', label: 'Credentials', hidden: !admin, render: () => <CredentialsTab /> },
  ];
  return <HubScreen title={t('Settings')} tabs={tabs} defaultKey="server" label={t('Settings sections')} testID="server" />;
}
