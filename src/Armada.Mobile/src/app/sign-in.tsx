import { useAuth } from '../auth/AuthContext';
import { LoadingState } from '../components/ui';
import { useLocale } from '../i18n/LocaleContext';
import { SignInScreen } from '../screens/SignInScreen';
import { UnlockScreen, UnreachableScreen } from '../screens/LockScreens';

/** Everything before a session exists: loading, sign-in, biometric unlock, or an unreachable server. */
export default function SignInRoute() {
  const { status } = useAuth();
  const { t } = useLocale();
  if (status === 'loading') return <LoadingState label={t('Loading...')} />;
  if (status === 'locked') return <UnlockScreen />;
  if (status === 'unreachable') return <UnreachableScreen />;
  return <SignInScreen />;
}
