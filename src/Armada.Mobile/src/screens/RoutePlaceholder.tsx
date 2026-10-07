import { Stack, useLocalSearchParams } from 'expo-router';
import { Linking, StyleSheet, View } from 'react-native';
import { useAuth } from '../auth/AuthContext';
import { AppText, Button, Icon, Screen, Section, ListRow } from '../components/ui';
import { useLocale } from '../i18n/LocaleContext';
import { routeByPattern } from '../navigation/routeMatch';
import { spacing } from '../theme/typography';

/** The dashboard URL for an app path on the active server ('/home' is the dashboard root). */
export function dashboardUrlFor(serverUrl: string, appPath: string): string {
  const path = appPath === '/home' ? '/' : appPath;
  return `${serverUrl}/dashboard${path}`;
}

function fillPattern(pattern: string, params: Record<string, string | string[] | undefined>): string {
  return pattern.replace(/:([A-Za-z]+)/g, (_m, name: string) => {
    const value = params[name];
    return encodeURIComponent(Array.isArray(value) ? value[0] ?? '' : value ?? '');
  });
}

/**
 * Stand-in for a dashboard screen that a later workstream implements. It keeps navigation complete (every
 * dashboard route and deep link resolves), shows the route and its parameters, says which workstream brings the
 * real screen, and offers to open the same page on the web dashboard meanwhile.
 */
export function RoutePlaceholder({ pattern }: { pattern: string }) {
  const params = useLocalSearchParams<Record<string, string>>();
  const { t } = useLocale();
  const { activeProfile } = useAuth();
  const route = routeByPattern(pattern);
  const title = t(route?.title ?? pattern);
  const path = fillPattern(pattern, params);
  const paramEntries = Object.entries(params).filter(([, v]) => v !== undefined && v !== '');

  return (
    <Screen testID="route-placeholder">
      <Stack.Screen options={{ title }} />
      <View style={styles.hero}>
        <Icon name="construct-outline" size={40} color="primary" />
        <AppText variant="heading" accessibilityRole="header" testID="route-placeholder-title">{title}</AppText>
        <AppText muted testID="route-placeholder-workstream" style={styles.center}>
          {t('Coming in {{workstream}}', { workstream: route?.workstream ?? '' })}
        </AppText>
      </View>
      <Section title={t('Route')}>
        <ListRow title={pattern} subtitle={path !== pattern ? path : null} testID="route-placeholder-pattern" />
        {paramEntries.map(([key, value]) => (
          <ListRow key={key} title={key} subtitle={String(value)} />
        ))}
      </Section>
      {activeProfile ? (
        <View style={styles.pad}>
          <Button
            label={t('Open on the web dashboard')}
            variant="secondary"
            icon="open-outline"
            onPress={() => void Linking.openURL(dashboardUrlFor(activeProfile.url, path))}
          />
        </View>
      ) : null}
    </Screen>
  );
}

const styles = StyleSheet.create({
  hero: { alignItems: 'center', gap: spacing.sm, padding: spacing.xl },
  center: { textAlign: 'center' },
  pad: { paddingHorizontal: spacing.lg },
});
