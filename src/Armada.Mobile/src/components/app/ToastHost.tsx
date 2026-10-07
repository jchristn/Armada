import { useRouter, type Href } from 'expo-router';
import { Pressable, StyleSheet, View } from 'react-native';
import { useSafeAreaInsets } from 'react-native-safe-area-context';
import { useLocale } from '../../i18n/LocaleContext';
import { useNotifications } from '../../notifications/NotificationContext';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing } from '../../theme/typography';
import { AppText, Icon, toneColor } from '../ui';

/** Toasts for live events, stacked at the top; tap opens the item, the close button dismisses. Announced politely. */
export function ToastHost() {
  const { toasts, dismissToast } = useNotifications();
  const { colors } = useTheme();
  const { t } = useLocale();
  const insets = useSafeAreaInsets();
  const router = useRouter();
  if (toasts.length === 0) return null;
  return (
    <View pointerEvents="box-none" style={[styles.host, { top: insets.top + spacing.sm }]}>
      {toasts.slice(-3).map((toast) => (
        <View
          key={toast.id}
          testID="toast"
          accessibilityLiveRegion="polite"
          style={[styles.toast, { backgroundColor: colors.surfaceRaised, borderColor: colors[toneColor(toast.severity)] }]}
        >
          <Pressable
            style={styles.body}
            accessibilityRole={toast.href ? 'link' : 'text'}
            accessibilityLabel={toast.message}
            onPress={() => {
              if (toast.href) router.push(toast.href as Href);
              dismissToast(toast.id);
            }}
          >
            <AppText numberOfLines={3}>{toast.message}</AppText>
          </Pressable>
          <Pressable accessibilityRole="button" accessibilityLabel={t('Dismiss')} onPress={() => dismissToast(toast.id)} style={styles.close}>
            <Icon name="close" size={18} color="textMuted" />
          </Pressable>
        </View>
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  host: { position: 'absolute', left: spacing.md, right: spacing.md, gap: spacing.sm, alignItems: 'center' },
  toast: { flexDirection: 'row', alignItems: 'center', width: '100%', maxWidth: 560, borderRadius: radius.md, borderWidth: 1, borderLeftWidth: 4, paddingLeft: spacing.md, elevation: 4, shadowOpacity: 0.15, shadowRadius: 8, shadowOffset: { width: 0, height: 2 } },
  body: { flex: 1, paddingVertical: spacing.md },
  close: { minWidth: 44, minHeight: 44, alignItems: 'center', justifyContent: 'center' },
});
