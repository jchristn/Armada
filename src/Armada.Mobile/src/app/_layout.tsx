import { Stack, ThemeProvider as NavigationThemeProvider, useRouter, type Href } from 'expo-router';
import * as SplashScreen from 'expo-splash-screen';
import { StatusBar } from 'expo-status-bar';
import { useEffect, type ReactNode } from 'react';
import { StyleSheet } from 'react-native';
import { GestureHandlerRootView } from 'react-native-gesture-handler';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { AuthProvider, useAuth } from '../auth/AuthContext';
import { ToastHost } from '../components/app/ToastHost';
import { LocaleProvider, useLocale } from '../i18n/LocaleContext';
import { navigationTheme } from '../navigation/navigationTheme';
import { setSignedInForLinks, takePendingLink } from '../navigation/pendingLink';
import { ApprovalsProvider } from '../notifications/ApprovalsContext';
import { NotificationProvider } from '../notifications/NotificationContext';
import { SocketProvider } from '../socket/SocketContext';
import { ThemeProvider, useTheme } from '../theme/ThemeContext';

void SplashScreen.preventAutoHideAsync().catch(() => undefined);

/** Session-scoped services: language (with the server catalog once signed in), socket, notifications, badge. */
function SessionProviders({ children }: { children: ReactNode }) {
  const { status, activeProfile, sessionToken } = useAuth();
  const signedIn = status === 'signedIn';
  const serverUrl = signedIn ? activeProfile?.url ?? null : null;
  return (
    <LocaleProvider serverUrl={serverUrl}>
      <SocketProvider serverUrl={serverUrl} token={signedIn ? sessionToken : null}>
        <NotificationProvider>
          <ApprovalsProvider enabled={signedIn}>{children}</ApprovalsProvider>
        </NotificationProvider>
      </SocketProvider>
    </LocaleProvider>
  );
}

function RootNavigator() {
  const { status, mustChangePassword } = useAuth();
  const { colors, dark } = useTheme();
  const { t } = useLocale();
  const router = useRouter();
  const signedIn = status === 'signedIn';
  const ready = signedIn && !mustChangePassword;

  useEffect(() => {
    if (status !== 'loading') void SplashScreen.hideAsync().catch(() => undefined);
  }, [status]);

  // Open a deep link that arrived before sign-in.
  useEffect(() => {
    setSignedInForLinks(ready);
    if (!ready) return;
    const pending = takePendingLink();
    if (pending) router.navigate(pending as Href);
  }, [ready, router]);

  return (
    <NavigationThemeProvider value={navigationTheme(colors, dark)}>
      <StatusBar style={dark ? 'light' : 'dark'} />
      <Stack screenOptions={{ headerShown: false, contentStyle: { backgroundColor: colors.background } }}>
        <Stack.Protected guard={ready}>
          <Stack.Screen name="index" />
          <Stack.Screen name="(app)" />
          <Stack.Screen
            name="notification-center"
            options={{ presentation: 'modal', headerShown: true, title: t('Notifications'), headerTintColor: colors.primary }}
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
    </NavigationThemeProvider>
  );
}

export default function RootLayout() {
  return (
    <GestureHandlerRootView style={styles.fill}>
      <SafeAreaProvider>
        <ThemeProvider>
          <AuthProvider>
            <SessionProviders>
              <RootNavigator />
            </SessionProviders>
          </AuthProvider>
        </ThemeProvider>
      </SafeAreaProvider>
    </GestureHandlerRootView>
  );
}

const styles = StyleSheet.create({ fill: { flex: 1 } });
