import { useRouter } from 'expo-router';
import { View } from 'react-native';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { IconButton } from '../ui';
import { NotEncryptedIndicator } from './NotEncryptedIndicator';

/**
 * Header buttons on every phone screen: the "Not encrypted" mark for an http:// server, and the notification center
 * with its unread count.
 */
export function HeaderActions() {
  const router = useRouter();
  const { t } = useLocale();
  const { unreadCount } = useNotifications();
  return (
    <View style={{ flexDirection: 'row' }}>
      <NotEncryptedIndicator compact />
      <IconButton
        testID="header-notifications"
        icon="notifications-outline"
        label={t('Notifications')}
        badge={unreadCount}
        onPress={() => router.push('/notification-center')}
      />
    </View>
  );
}
