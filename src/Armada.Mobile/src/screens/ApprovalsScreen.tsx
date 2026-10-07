import { useRouter } from 'expo-router';
import { RefreshControl } from 'react-native';
import { useState } from 'react';
import { CountBadge, EmptyState, ListRow, Screen, Section } from '../components/ui';
import { useLocale } from '../i18n/LocaleContext';
import { useApprovals } from '../notifications/ApprovalsContext';
import { useTheme } from '../theme/ThemeContext';

/**
 * Approvals tab root. W0 shows the live Needs You count (the same inbox the dashboard badge counts) and links to
 * the Needs You screen; the full approvals center (Ask proposals, CLI permissions, reviews, deployments) is W1.3.
 */
export function ApprovalsScreen() {
  const { count, refresh } = useApprovals();
  const { t } = useLocale();
  const { colors } = useTheme();
  const router = useRouter();
  const [refreshing, setRefreshing] = useState(false);

  return (
    <Screen
      testID="approvals"
      refreshControl={<RefreshControl refreshing={refreshing} tintColor={colors.primary} onRefresh={async () => { setRefreshing(true); await refresh(); setRefreshing(false); }} />}
    >
      <Section title={t('Needs You')}>
        <ListRow
          testID="approvals-inbox"
          icon="alert-circle-outline"
          title={t('Needs You')}
          subtitle={t('Reviews, failures, and stalls awaiting your attention')}
          accessory={<CountBadge count={count} />}
          accessibilityValue={String(count)}
          onPress={() => router.push('/inbox')}
        />
      </Section>
      {count === 0 ? (
        <EmptyState icon="checkmark-done-outline" title={t('Nothing needs you')} message={t('The approvals center arrives in {{workstream}}.', { workstream: 'W1.3' })} />
      ) : null}
    </Screen>
  );
}
