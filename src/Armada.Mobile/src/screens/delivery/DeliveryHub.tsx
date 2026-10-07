import { HubScreen, type HubTab } from '../../components/resource/Hub';
import { useLocale } from '../../i18n/LocaleContext';
import { CheckRunsTab } from './CheckRunsTab';
import { DeploymentsTab } from './DeploymentsTab';
import { EnvironmentsTab } from './EnvironmentsTab';
import { IncidentsTab } from './IncidentsTab';
import { ReleasesTab } from './ReleasesTab';
import { RunbooksTab } from './RunbooksTab';

/**
 * The Delivery hub (/delivery): Deployments, Environments, Releases, Incidents, Checks, and Runbooks, the same six
 * tabs as the dashboard, shown to everyone; the active tab is ?tab=.
 */
export function DeliveryHub() {
  const { t } = useLocale();
  const tabs: HubTab[] = [
    { key: 'deployments', label: 'Deployments', render: () => <DeploymentsTab /> },
    { key: 'environments', label: 'Environments', render: () => <EnvironmentsTab /> },
    { key: 'releases', label: 'Releases', render: () => <ReleasesTab /> },
    { key: 'incidents', label: 'Incidents', render: () => <IncidentsTab /> },
    { key: 'checks', label: 'Checks', render: () => <CheckRunsTab /> },
    { key: 'runbooks', label: 'Runbooks', render: () => <RunbooksTab /> },
  ];
  return <HubScreen title={t('Delivery')} tabs={tabs} defaultKey="deployments" label={t('Delivery sections')} testID="delivery" />;
}
