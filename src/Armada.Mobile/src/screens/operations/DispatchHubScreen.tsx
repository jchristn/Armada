import { Stack, useLocalSearchParams } from 'expo-router';
import { Linking, StyleSheet, View } from 'react-native';
import { useAuth } from '../../auth/AuthContext';
import { HubTabBar, useHubTab } from '../../components/app/HubTabs';
import { Button, EmptyState, Screen } from '../../components/ui';
import { useLocale } from '../../i18n/LocaleContext';
import { spacing } from '../../theme/typography';
import { dashboardUrlFor } from '../RoutePlaceholder';
import { DispatchForm } from './DispatchForm';
import { dispatchPrefillFromParams } from './w24/dispatchLink';

const TABS = [
  { key: 'dispatch' as const, label: 'Dispatch' },
  { key: 'backlog' as const, label: 'Backlog' },
];
const KEYS = TABS.map((tab) => tab.key);

/**
 * The Dispatch hub (the dashboard's DispatchHub): Dispatch is the default tab; Backlog (Objectives) arrives with
 * W3.3 and opens on the web dashboard meanwhile. Dispatch reads its draft from the link (see w24/dispatchLink.ts),
 * for example /dispatch?from=vessel&vesselId=vsl_1 from a vessel.
 */
export function DispatchHubScreen() {
  const { t } = useLocale();
  const [tab, setTab] = useHubTab(KEYS, 'dispatch');
  const params = useLocalSearchParams<Record<string, string>>();
  const prefill = dispatchPrefillFromParams(params);
  // A new draft link remounts the form so its fields start from that draft.
  const draftKey = JSON.stringify(prefill);
  return (
    <Screen testID="dispatch-screen">
      <Stack.Screen options={{ title: t('Dispatch') }} />
      <HubTabBar tabs={TABS} active={tab} onChange={setTab} label={t('Dispatch sections')} testID="dispatch" />
      {tab === 'dispatch' ? <DispatchForm key={draftKey} prefill={prefill} /> : <BacklogPending />}
    </Screen>
  );
}

function BacklogPending() {
  const { t } = useLocale();
  const { activeProfile } = useAuth();
  return (
    <View style={styles.pending} testID="dispatch-backlog-pending">
      <EmptyState
        icon="list-outline"
        title={t('Backlog')}
        message={t('Coming in {{workstream}}', { workstream: 'W3.3' })}
      />
      {activeProfile ? (
        <View style={styles.pad}>
          <Button
            label={t('Open on the web dashboard')}
            variant="secondary"
            icon="open-outline"
            onPress={() => void Linking.openURL(dashboardUrlFor(activeProfile.url, '/dispatch?tab=backlog'))}
            testID="dispatch-backlog-web"
          />
        </View>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  pending: { paddingVertical: spacing.xl },
  pad: { paddingHorizontal: spacing.lg },
});
