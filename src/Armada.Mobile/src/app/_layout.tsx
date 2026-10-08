import { Stack, ThemeProvider as NavigationThemeProvider, useRouter } from 'expo-router';
import * as SplashScreen from 'expo-splash-screen';
import { StatusBar } from 'expo-status-bar';
import { useEffect, useMemo, type ReactNode } from 'react';
import { StyleSheet } from 'react-native';
import { GestureHandlerRootView } from 'react-native-gesture-handler';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { AuthProvider, useAuth } from '../auth/AuthContext';
import { SavePasswordOfferSheet } from '../components/app/SavePasswordOfferSheet';
import { PrivacyOverlay } from '../components/app/PrivacyOverlay';
import { ToastHost } from '../components/app/ToastHost';
import { IconButton } from '../components/ui';
import { LocaleProvider, useLocale } from '../i18n/LocaleContext';
import { navigationTheme } from '../navigation/navigationTheme';
import { setSignedInForLinks } from '../navigation/pendingLink';
import { ApprovalsProvider } from '../notifications/ApprovalsContext';
import { combineAuthHooks } from '../lib/authHooks';
import { NotificationProvider, createNotificationAuthHooks, notificationScope } from '../notifications/NotificationContext';
import { createProxySocketFactory } from '../proxy/proxySocket';
import { PushPermissionPrompt } from '../push/PushPermissionPrompt';
import { PushResponseHandler } from '../push/PushResponseHandler';
import { PushProvider, createPushAuthHooks, defaultPushDeps } from '../push/PushContext';
import { SocketProvider } from '../socket/SocketContext';
import { ThemeProvider, useTheme } from '../theme/ThemeContext';

void SplashScreen.preventAutoHideAsync().catch(() => undefined);

// Ending a profile's session (sign-out, profile removed, server changed) forgets its notification history and
// removes this device's push registration.
const sessionAuthHooks = combineAuthHooks(createNotificationAuthHooks(), createPushAuthHooks(defaultPushDeps));

/** Session-scoped services: language (with the server catalog once signed in), socket, notifications, badge. */
function SessionProviders({ children }: { children: ReactNode }) {
  const { status, activeProfile, sessionToken, proxyToken, requestHeaders, user } = useAuth();
  const signedIn = status === 'signedIn';
  const userId = user?.user?.id ?? null;
  const historyScope = signedIn && activeProfile && userId ? notificationScope(activeProfile.id, userId) : null;
  const serverUrl = signedIn ? activeProfile?.url ?? null : null;
  const isProxy = activeProfile?.kind === 'Proxy';
  // Through Armada.Proxy the socket also carries the proxy session (subprotocol, header fallback). One factory per
  // proxy session, so the mode it learns survives reconnects.
  const socketFactory = useMemo(
    () => (isProxy && proxyToken ? createProxySocketFactory({ proxyToken: () => proxyToken }).factory : undefined),
    [isProxy, proxyToken],
  );
  return (
    <LocaleProvider serverUrl={serverUrl} requestHeaders={requestHeaders}>
      <SocketProvider serverUrl={serverUrl} token={signedIn ? sessionToken : null} factory={socketFactory}>
        <NotificationProvider scope={historyScope}>
          <ApprovalsProvider enabled={signedIn}>
            <PushProvider>{children}</PushProvider>
          </ApprovalsProvider>
        </NotificationProvider>
      </SocketProvider>
    </LocaleProvider>
  );
}

function RootNavigator() {
  const { status, mustChangePassword, savePasswordOffer } = useAuth();
  const { colors, dark } = useTheme();
  const { t } = useLocale();
  const router = useRouter();
  const signedIn = status === 'signedIn';
  const ready = signedIn && !mustChangePassword;

  useEffect(() => {
    if (status !== 'loading') void SplashScreen.hideAsync().catch(() => undefined);
  }, [status]);

  // Links that arrive before the session is ready are kept for the index route (app/index.tsx) to open.
  useEffect(() => {
    setSignedInForLinks(ready);
  }, [ready]);

  return (
    <NavigationThemeProvider value={navigationTheme(colors, dark)}>
      <StatusBar style={dark ? 'light' : 'dark'} />
      <Stack screenOptions={{ headerShown: false, contentStyle: { backgroundColor: colors.background } }}>
        <Stack.Protected guard={ready}>
          <Stack.Screen name="index" />
          <Stack.Screen name="(app)" />
          <Stack.Screen
            name="notification-center"
            options={{
              presentation: 'modal',
              headerShown: true,
              title: t('Notifications'),
              headerTintColor: colors.primary,
              headerStyle: { backgroundColor: colors.surface },
              headerTitleStyle: { color: colors.text },
              headerRight: () => (
                <IconButton testID="notifications-close" icon="close" label={t('Close')} color="primary" onPress={() => router.back()} />
              ),
            }}
          />
        </Stack.Protected>
        <Stack.Protected guard={signedIn && mustChangePassword}>
          <Stack.Screen name="password-change" />
        </Stack.Protected>
        <Stack.Protected guard={!signedIn}>
          <Stack.Screen name="sign-in" />
        </Stack.Protected>
      </Stack>
      {ready ? <ToastHost /> : null}
      {ready ? <PushResponseHandler /> : null}
      {/* One sheet at a time: the save-password offer (right after sign-in) comes before the notifications one. */}
      {ready && !savePasswordOffer ? <PushPermissionPrompt /> : null}
      {ready ? <SavePasswordOfferSheet /> : null}
    </NavigationThemeProvider>
  );
}

export default function RootLayout() {
  return (
    <GestureHandlerRootView style={styles.fill}>
      <SafeAreaProvider>
        <ThemeProvider>
          <AuthProvider hooks={sessionAuthHooks}>
            <SessionProviders>
              <RootNavigator />
            </SessionProviders>
          </AuthProvider>
          <PrivacyOverlay />
        </ThemeProvider>
      </SafeAreaProvider>
    </GestureHandlerRootView>
  );
}

const styles = StyleSheet.create({ fill: { flex: 1 } });
