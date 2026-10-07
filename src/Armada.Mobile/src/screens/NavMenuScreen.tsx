import { useRouter, type Href } from 'expo-router';
import { StyleSheet, View } from 'react-native';
import { useAuth } from '../auth/AuthContext';
import { DefaultCredentialsBanner } from '../components/app/DefaultCredentialsBanner';
import { AppText, Icon, ListRow, Screen, Section } from '../components/ui';
import { useLocale } from '../i18n/LocaleContext';
import { HOME_ITEM, PREFERENCES_ITEM, PROFILES_ITEM, sectionsForTab, type MobileNavItem } from '../navigation/navItems';
import { routeByPattern } from '../navigation/routeMatch';
import type { TabKey } from '../navigation/routeTypes';
import { spacing } from '../theme/typography';

/**
 * Root of the Work and More tabs on phones: the dashboard's nav sections for that tab, in dashboard order. Work
 * also leads with the Home overview (W2.1); More ends with the app's own settings and sign-out.
 */
export function NavMenuScreen({ tab }: { tab: 'work' | 'more' }) {
  const router = useRouter();
  const { t } = useLocale();
  const { logout, activeProfile, user } = useAuth();

  const row = (item: MobileNavItem) => (
    <ListRow
      key={item.key}
      testID={`nav-${item.to}`}
      icon={item.icon}
      title={t(item.label)}
      subtitle={item.tooltip ? t(item.tooltip) : null}
      onPress={() => router.push(item.to as Href)}
    />
  );

  return (
    <Screen testID={`${tab}-menu`}>
      <DefaultCredentialsBanner />
      {tab === 'work' ? <HomeSummary /> : null}
      {sectionsForTab(tab as TabKey).map((section) => (
        <Section key={section.key} title={t(section.label)}>
          {section.items.map(row)}
        </Section>
      ))}
      {tab === 'more' ? (
        <Section title={t('App')}>
          {row(PREFERENCES_ITEM)}
          <ListRow
            testID="nav-/profiles"
            icon={PROFILES_ITEM.icon}
            title={t('Servers')}
            subtitle={activeProfile ? `${activeProfile.name} - ${user?.user?.email ?? ''}` : null}
            onPress={() => router.push('/profiles')}
          />
          <ListRow testID="sign-out" icon="log-out-outline" title={t('Sign out')} destructive onPress={() => void logout()} />
        </Section>
      ) : null}
    </Screen>
  );
}

/** Home (the dashboard's overview) until W2.1 brings status, KPIs, and recent activity to this tab. */
function HomeSummary() {
  const { t } = useLocale();
  const route = routeByPattern(HOME_ITEM.to);
  return (
    <View style={styles.home} testID="home-summary">
      <Icon name={HOME_ITEM.icon} size={28} color="primary" />
      <View style={styles.flex}>
        <AppText variant="heading" accessibilityRole="header">{t(HOME_ITEM.label)}</AppText>
        <AppText muted>{t('Coming in {{workstream}}', { workstream: route?.workstream ?? 'W2.1' })}</AppText>
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  home: { flexDirection: 'row', alignItems: 'center', gap: spacing.md, marginHorizontal: spacing.lg, marginBottom: spacing.xl },
  flex: { flex: 1 },
});
