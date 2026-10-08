import { useLocalSearchParams } from 'expo-router';
import { HubScreen, type HubTab } from '../../components/resource/Hub';
import { useLocale } from '../../i18n/LocaleContext';
import { param } from '../../resource/links';
import { EventDetail } from '../operations/EventDetail';
import { EventsList } from '../operations/EventsList';
import { SignalDetail } from '../operations/SignalDetail';
import { SignalsList } from '../operations/SignalsList';
import { HistoryTab } from './HistoryTab';
import { OperationsTab } from './OperationsTab';
import { RequestsTab } from './RequestsTab';
import { TokenUsageTab } from './TokenUsageTab';

const eventRoute = (id: string) => `/events/${id}`;
const signalRoute = (id: string) => `/signals/${id}`;

/**
 * The Activity hub (/activity): All Activity (history), API Requests, Events, Signals, and Token Usage, the active
 * source in ?source= as on the dashboard (Needs You and the dashboard link here pre-filtered). All Activity also
 * takes the History page's query filters (?objectiveId=, ?vesselId=, ?sourceType=, ?postmortemOnly=, ?text=, ?actor=).
 */
export function ActivityHub() {
  const { t } = useLocale();
  const params = useLocalSearchParams<Record<string, string>>();
  const initial = {
    objectiveId: param(params.objectiveId) || undefined,
    vesselId: param(params.vesselId) || undefined,
    sourceType: param(params.sourceType) || undefined,
    text: param(params.text) || undefined,
    actor: param(params.actor) || undefined,
    postmortemOnly: param(params.postmortemOnly) === 'true' || undefined,
    showReadRequests: param(params.showReadRequests) === 'true' || undefined,
  };
  const defined = Object.fromEntries(Object.entries(initial).filter(([, v]) => v !== undefined));
  const tabs: HubTab[] = [
    { key: 'history', label: 'All Activity', render: () => <HistoryTab initial={defined} /> },
    { key: 'requests', label: 'API Requests', render: () => <RequestsTab /> },
    { key: 'events', label: 'Events', render: () => <OperationsTab List={EventsList} Detail={EventDetail} route={eventRoute} /> },
    { key: 'signals', label: 'Signals', render: () => <OperationsTab List={SignalsList} Detail={SignalDetail} route={signalRoute} /> },
    { key: 'tokens', label: 'Token Usage', render: () => <TokenUsageTab /> },
  ];
  return <HubScreen title={t('Activity')} tabs={tabs} defaultKey="history" param="source" label={t('Activity sources')} testID="activity" />;
}
