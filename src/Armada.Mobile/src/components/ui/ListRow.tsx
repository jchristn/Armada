import type { ReactNode } from 'react';
import { Pressable, StyleSheet, View, type AccessibilityActionEvent, type AccessibilityActionInfo } from 'react-native';
import { useTheme } from '../../theme/ThemeContext';
import { MIN_TOUCH, spacing } from '../../theme/typography';
import { AppText } from './AppText';
import { Icon, type IconName } from './Icon';
import { IconButton } from './IconButton';

/** The row's "..." button: drawn beside the row (not inside it) so screen readers reach it as its own control. */
export interface ListRowMenu {
  label: string;
  onPress: () => void;
  testID?: string;
}

export interface ListRowProps {
  title: string;
  subtitle?: string | null;
  icon?: IconName;
  /** Right-hand content (value text, badge). Its text is not read unless it is in `accessibilityValue`. */
  accessory?: ReactNode;
  /** Shows a chevron and makes the row a button. */
  onPress?: () => void;
  onLongPress?: () => void;
  /** Spoken name of the long-press action, offered to screen readers as an action (they cannot long-press). */
  longPressLabel?: string;
  /** A "..." menu button at the end of the row, also offered to screen readers as the row's action. */
  menu?: ListRowMenu;
  selected?: boolean;
  destructive?: boolean;
  accessibilityHint?: string;
  /**
   * What the accessory shows (status, time, counts), read after the title and subtitle. Pieces are joined with
   * commas; empty pieces are dropped. A row is one screen-reader element, so anything shown must be here.
   */
  accessibilityValue?: string | (string | number | null | undefined | false)[];
  /** More screen-reader actions (SwipeRow passes the row's swipe actions here). */
  accessibilityActions?: readonly AccessibilityActionInfo[];
  onAccessibilityAction?: (event: AccessibilityActionEvent) => void;
  testID?: string;
}

/** Joins the spoken pieces of a row (empty ones dropped). */
export function spokenValue(value: ListRowProps['accessibilityValue']): string {
  if (value === undefined) return '';
  const parts = Array.isArray(value) ? value : [value];
  return parts.filter((p) => p !== null && p !== undefined && p !== false && String(p).trim() !== '').map(String).join(', ');
}

export function ListRow({
  title, subtitle, icon, accessory, onPress, onLongPress, longPressLabel, menu, selected, destructive, accessibilityHint, accessibilityValue,
  accessibilityActions: extraActions, onAccessibilityAction: onExtraAction, testID,
}: ListRowProps) {
  const { colors } = useTheme();
  const value = spokenValue(accessibilityValue);
  const content = (
    <View style={styles.row}>
      {icon ? <Icon name={icon} color={destructive ? 'danger' : 'primary'} /> : null}
      <View style={styles.text}>
        <AppText variant="label" color={destructive ? 'danger' : 'text'}>{title}</AppText>
        {subtitle ? <AppText variant="caption" muted numberOfLines={2}>{subtitle}</AppText> : null}
      </View>
      {accessory}
      {onPress ? <Icon name="chevron-forward" size={18} color="textMuted" /> : null}
    </View>
  );
  if (!onPress) {
    return (
      <View
        testID={testID}
        style={[styles.base, { borderBottomColor: colors.border }]}
        accessible
        accessibilityLabel={[title, subtitle, value].filter(Boolean).join(', ')}
        accessibilityActions={extraActions}
        onAccessibilityAction={onExtraAction}
      >
        {content}
      </View>
    );
  }
  // Screen readers cannot swipe to a nested button or long-press reliably: both are offered as actions.
  const actions: AccessibilityActionInfo[] = [];
  if (menu) actions.push({ name: 'menu', label: menu.label });
  if (onLongPress && longPressLabel) actions.push({ name: 'longpress', label: longPressLabel });
  if (extraActions) actions.push(...extraActions);
  const row = (
    <Pressable
      testID={testID}
      accessibilityRole="button"
      accessibilityLabel={[title, subtitle].filter(Boolean).join(', ')}
      accessibilityValue={value ? { text: value } : undefined}
      accessibilityHint={accessibilityHint}
      accessibilityState={{ selected: !!selected }}
      accessibilityActions={actions.length > 0 ? actions : undefined}
      onAccessibilityAction={actions.length > 0 ? (event) => {
        if (menu && event.nativeEvent.actionName === 'menu') menu.onPress();
        else if (onLongPress && longPressLabel && event.nativeEvent.actionName === 'longpress') onLongPress();
        else onExtraAction?.(event);
      } : undefined}
      onPress={onPress}
      onLongPress={onLongPress}
      style={({ pressed }) => [
        styles.base,
        menu ? styles.withMenu : null,
        { borderBottomColor: colors.border, backgroundColor: selected ? colors.surfaceRaised : pressed ? colors.background : colors.surface },
        selected ? { borderLeftWidth: 3, borderLeftColor: colors.primary } : null,
      ]}
    >
      {content}
    </Pressable>
  );
  if (!menu) return row;
  return (
    <View style={[styles.menuWrap, { borderBottomColor: colors.border, backgroundColor: selected ? colors.surfaceRaised : colors.surface }]}>
      <View style={styles.fill}>{row}</View>
      <IconButton icon="ellipsis-horizontal" label={menu.label} onPress={menu.onPress} color="textMuted" testID={menu.testID} />
    </View>
  );
}

const styles = StyleSheet.create({
  base: { minHeight: MIN_TOUCH + 8, paddingHorizontal: spacing.lg, paddingVertical: spacing.sm, justifyContent: 'center', borderBottomWidth: StyleSheet.hairlineWidth },
  withMenu: { borderBottomWidth: 0, paddingRight: spacing.xs },
  menuWrap: { flexDirection: 'row', alignItems: 'center', borderBottomWidth: StyleSheet.hairlineWidth, paddingRight: spacing.xs },
  fill: { flex: 1 },
  row: { flexDirection: 'row', alignItems: 'center', gap: spacing.md },
  text: { flex: 1, gap: 2 },
});
