import { useRouter, type Href } from 'expo-router';
import { useAuth } from '../auth/AuthContext';
import { DefaultCredentialsBanner } from '../components/app/DefaultCredentialsBanner';
import { ListRow, Screen, Section } from '../components/ui';
import { useLocale } from '../i18n/LocaleContext';
import { PREFERENCES_ITEM, PROFILES_ITEM, sectionsForTab, type MobileNavItem } from '../navigation/navItems';
import type { TabKey } from '../navigation/routeTypes';

function NavRow({ item }: { item: MobileNavItem }) {
  const router = useRouter();
  const { t } = useLocale();
  return (
    <ListRow
      testID={`nav-${item.to}`}
      icon={item.icon}
      title={t(item.label)}
      subtitle={item.tooltip ? t(item.tooltip) : null}
      onPress={() => router.push(item.to as Href)}
    />
  );
}

/** The dashboard's nav sections listed on a phone tab, in dashboard order (Work also ends Home with these). */
export function NavSections({ tab }: { tab: TabKey }) {
  const { t } = useLocale();
  return (
    <>
      {sectionsForTab(tab).map((section) => (
        <Section key={section.key} title={t(section.label)}>
          {section.items.map((item) => <NavRow key={item.key} item={item} />)}
        </Section>
      ))}
    </>
  );
}

/**
 * Root of the More tab on phones: the dashboard's nav sections for that tab, then the app's own settings and
 * sign-out. (The Work tab's root is Home, which ends with the Work sections.)
 */
export function NavMenuScreen({ tab }: { tab: 'work' | 'more' }) {
  const router = useRouter();
  const { t } = useLocale();
  const { logout, activeProfile, user } = useAuth();

  return (
    <Screen testID={`${tab}-menu`}>
      <DefaultCredentialsBanner />
      <NavSections tab={tab} />
      {tab === 'more' ? (
        <Section title={t('App')}>
          <NavRow item={PREFERENCES_ITEM} />
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
