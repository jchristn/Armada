import { Stack, useLocalSearchParams } from 'expo-router';
import { HubTabBar, useHubTab } from '../../components/app/HubTabs';
import { Screen } from '../../components/ui';
import { useLocale } from '../../i18n/LocaleContext';
import { ObjectivesList } from '../objectives/ObjectivesList';
import { DispatchForm } from './DispatchForm';
import { dispatchPrefillFromParams } from './w24/dispatchLink';

const TABS = [
  { key: 'dispatch' as const, label: 'Dispatch' },
  { key: 'backlog' as const, label: 'Backlog' },
];
const KEYS = TABS.map((tab) => tab.key);

/**
 * The Dispatch hub (the dashboard's DispatchHub): Dispatch is the default tab; Backlog is the backlog list (W3.3's
 * ObjectivesList, embedded). Dispatch reads its draft from the link (see w24/dispatchLink.ts),
 * for example /dispatch?from=vessel&vesselId=vsl_1 from a vessel.
 */
export function DispatchHubScreen() {
  const { t } = useLocale();
  const [tab, setTab] = useHubTab(KEYS, 'dispatch');
  const params = useLocalSearchParams<Record<string, string>>();
  const prefill = dispatchPrefillFromParams(params);
  // A new draft link remounts the form so its fields start from that draft.
  const draftKey = JSON.stringify(prefill);
  const title = <Stack.Screen options={{ title: t('Dispatch') }} />;
  const tabBar = <HubTabBar tabs={TABS} active={tab} onChange={setTab} label={t('Dispatch sections')} testID="dispatch" />;
  // The Dispatch tab is a scrolling form with its Dispatch button in a footer; the Backlog tab is a virtualized list
  // of its own, so the screen does not scroll around it.
  if (tab === 'dispatch') {
    return (
      <>
        {title}
        <DispatchForm key={draftKey} prefill={prefill} header={tabBar} />
      </>
    );
  }
  return (
    <Screen testID="dispatch-screen" scroll={false}>
      {title}
      {tabBar}
      <ObjectivesList embedded testID="dispatch-backlog" />
    </Screen>
  );
}

