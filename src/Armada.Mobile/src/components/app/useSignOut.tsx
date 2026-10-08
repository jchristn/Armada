import { useState, type ReactNode } from 'react';
import { useAuth } from '../../auth/AuthContext';
import { useLocale } from '../../i18n/LocaleContext';
import { ActionSheet } from '../ui';

/**
 * Sign-out for every sign-out button. Without a saved password it signs out at once (as before); with one it asks
 * first, offering to keep the saved password (so Face ID sign-in works next time) or to forget it.
 */
export function useSignOut(): { signOut: () => void; signOutSheet: ReactNode } {
  const { activeProfile, logout } = useAuth();
  const { t } = useLocale();
  const [open, setOpen] = useState(false);
  const hasSaved = !!activeProfile?.savedSignIn || !!activeProfile?.proxyPasswordSaved;

  function signOut() {
    if (hasSaved) setOpen(true);
    else void logout();
  }

  const signOutSheet = (
    <ActionSheet
      testID="sign-out-confirm"
      open={open}
      title={t('Sign out of {{name}}?', { name: activeProfile?.name ?? '' })}
      onClose={() => setOpen(false)}
      closeLabel={t('Cancel')}
      actions={[
        { key: 'keep', label: t('Sign out (keep the saved password)'), icon: 'log-out-outline', onPress: () => { void logout(); } },
        {
          key: 'forget',
          label: t('Sign out and forget saved password'),
          icon: 'trash-outline',
          danger: true,
          onPress: () => { void logout({ forgetSavedPassword: true }); },
        },
      ]}
    />
  );
  return { signOut, signOutSheet };
}
