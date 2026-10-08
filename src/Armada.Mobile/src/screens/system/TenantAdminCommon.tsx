import { Modal, StyleSheet, View } from 'react-native';
import { useProxySessionContext } from '@dashboard/lib/useProxySessionContext';
import { AppText } from '../../components/ui/AppText';
import { Banner } from '../../components/ui/Banner';
import { Button } from '../../components/ui/Button';
import { Icon } from '../../components/ui/Icon';
import { ListRow } from '../../components/ui/ListRow';
import { StatusBadge, type StatusTone } from '../../components/ui/StatusBadge';
import { SwipeRow, type SwipeAction } from '../../components/ui/SwipeRow';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing } from '../../theme/typography';
import { MODAL_ORIENTATIONS } from '../../components/ui/modalOrientations';

/**
 * Shared pieces of the Settings hub's admin tabs (Tenants, Users, Credentials): remote proxy mode (create, edit, and
 * delete are blocked when the dashboard reaches the Admiral through Armada.Proxy, as on the dashboard), the typed
 * delete message, and the one-time secret dialog.
 */
export function useRemoteProxyMode(): { remote: boolean; instanceId: string | null } {
  const context = useProxySessionContext();
  const instanceId = context?.selectedInstanceId ?? null;
  return { remote: Boolean(instanceId), instanceId };
}

/** The dashboard's remote-mode warning for an admin page ('Tenant', 'User', or 'Credential'). */
export function RemoteProxyBanner({ entity, instanceId }: { entity: 'Tenant' | 'User' | 'Credential'; instanceId: string | null }) {
  const { t } = useLocale();
  const text = entity === 'Tenant'
    ? 'This page is connected through Armada.Proxy for {{instanceId}}. Tenant create, edit, and delete actions are blocked in remote mode.'
    : entity === 'User'
      ? 'This page is connected through Armada.Proxy for {{instanceId}}. User create, edit, and delete actions are blocked in remote mode.'
      : 'This page is connected through Armada.Proxy for {{instanceId}}. Credential create, edit, and delete actions are blocked in remote mode.';
  return <Banner tone="warning" title={t(text, { instanceId: instanceId ?? t('the selected deployment') })} testID="admin-remote-banner" />;
}

/** The typed-delete dialog text: the page's message, then the ConfirmDialog's "Are you sure you wish to delete" line. */
export function typedDeleteMessage(t: (text: string, params?: Record<string, string | number | null | undefined>) => string, message: string, resourceName: string): string {
  return `${message}\n\n${t('Are you sure you wish to delete: {{resourceName}}', { resourceName })}`;
}

export interface SecretOnceDialogProps {
  open: boolean;
  title: string;
  message: string;
  /** Account the secret belongs to (shown above it), when there is one. */
  email?: string | null;
  emailLabel?: string;
  secretLabel: string;
  secret: string;
  doneLabel: string;
  onClose: () => void;
  testID?: string;
}

/**
 * One-time display of a server-generated secret (a tenant admin password, a new credential's bearer token), the
 * mobile form of the dashboard's GeneratedPasswordDialog. The secret is selectable so it can be copied; it lives only
 * in the caller's state while the dialog is open, and the backdrop and back gesture do not close it.
 */
export function SecretOnceDialog({ open, title, message, email, emailLabel, secretLabel, secret, doneLabel, onClose, testID }: SecretOnceDialogProps) {
  const { colors } = useTheme();
  return (
    <Modal supportedOrientations={MODAL_ORIENTATIONS} visible={open} transparent animationType="fade" onRequestClose={() => undefined} statusBarTranslucent>
      <View style={[styles.backdrop, { backgroundColor: colors.overlay }]}>
        {open ? (
          <View testID={testID} accessibilityViewIsModal style={[styles.card, { backgroundColor: colors.surfaceRaised, borderColor: colors.border }]}>
            <AppText variant="heading" accessibilityRole="header">{title}</AppText>
            <View style={[styles.warning, { borderColor: colors.warning, backgroundColor: colors.warningSurface }]}>
              <AppText>{message}</AppText>
            </View>
            {email ? (
              <View style={styles.field}>
                <AppText variant="caption" muted>{emailLabel}</AppText>
                <AppText selectable>{email}</AppText>
              </View>
            ) : null}
            <View style={styles.field}>
              <AppText variant="caption" muted>{secretLabel}</AppText>
              <AppText variant="mono" selectable testID={testID ? `${testID}-secret` : undefined}>{secret}</AppText>
            </View>
            <Button label={doneLabel} onPress={onClose} testID={testID ? `${testID}-done` : undefined} />
          </View>
        ) : null}
      </View>
    </Modal>
  );
}

const styles = StyleSheet.create({
  backdrop: { flex: 1, alignItems: 'center', justifyContent: 'center', padding: spacing.xl },
  card: { width: '100%', maxWidth: 480, borderRadius: radius.lg, borderWidth: StyleSheet.hairlineWidth, padding: spacing.xl, gap: spacing.md },
  warning: { borderWidth: 1, borderLeftWidth: 4, borderRadius: radius.md, padding: spacing.md },
  field: { gap: 2 },
  accessory: { alignItems: 'flex-end', gap: spacing.xs, maxWidth: 170 },
});

export interface AdminRowProps {
  title: string;
  subtitle?: string | null;
  badge?: { label: string; tone: StatusTone } | null;
  meta?: string | null;
  onPress?: () => void;
  /** Long press toggles the row's bulk selection (the dashboard's row checkboxes). */
  onToggleSelect?: () => void;
  selected?: boolean;
  actions?: SwipeAction[];
  testID?: string;
}

/** A row of an admin list: like ResourceRow, plus long-press bulk selection. */
export function AdminRow({ title, subtitle, badge, meta, onPress, onToggleSelect, selected, actions = [], testID }: AdminRowProps) {
  const { t } = useLocale();
  const accessory = (
    <View style={styles.accessory}>
      {selected ? <Icon name="checkmark-circle" color="primary" accessibilityLabel={t('Selected')} /> : null}
      {badge ? <StatusBadge label={badge.label} tone={badge.tone} /> : null}
      {meta ? <AppText variant="caption" muted numberOfLines={1}>{meta}</AppText> : null}
    </View>
  );
  const row = (
    <ListRow
      title={title}
      subtitle={subtitle}
      accessory={accessory}
      onPress={onPress}
      onLongPress={onToggleSelect}
      selected={selected}
      testID={testID}
      accessibilityHint={onToggleSelect ? t('Long press to select') : undefined}
      accessibilityValue={badge?.label}
    />
  );
  if (actions.length === 0) return row;
  return <SwipeRow actions={actions} testID={testID ? `${testID}-swipe` : undefined}>{row}</SwipeRow>;
}
