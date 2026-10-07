import { Pressable, StyleSheet, View } from 'react-native';
import { useTheme } from '../../theme/ThemeContext';
import { MIN_TOUCH, spacing } from '../../theme/typography';
import { AppText } from './AppText';
import { BottomSheet } from './BottomSheet';
import { Icon, type IconName } from './Icon';

export interface SheetAction {
  key: string;
  label: string;
  icon?: IconName;
  danger?: boolean;
  disabled?: boolean;
  onPress: () => void;
}

/** A list of actions in a bottom sheet: the mobile form of the dashboard's row "..." menu. */
export function ActionSheet({ open, title, actions, onClose, closeLabel, testID }: {
  open: boolean;
  title: string;
  actions: SheetAction[];
  onClose: () => void;
  closeLabel: string;
  testID?: string;
}) {
  const { colors } = useTheme();
  return (
    <BottomSheet open={open} title={title} onClose={onClose} closeLabel={closeLabel} testID={testID}>
      {actions.map((action) => (
        <Pressable
          key={action.key}
          testID={testID ? `${testID}-${action.key}` : undefined}
          accessibilityRole="button"
          accessibilityLabel={action.label}
          accessibilityState={{ disabled: !!action.disabled }}
          disabled={action.disabled}
          onPress={() => { onClose(); action.onPress(); }}
          style={({ pressed }) => [styles.row, { borderBottomColor: colors.border, opacity: action.disabled ? 0.5 : pressed ? 0.7 : 1 }]}
        >
          {action.icon ? <Icon name={action.icon} color={action.danger ? 'danger' : 'primary'} /> : <View style={styles.spacer} />}
          <AppText variant="label" color={action.danger ? 'danger' : 'text'}>{action.label}</AppText>
        </Pressable>
      ))}
    </BottomSheet>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'center', gap: spacing.md, minHeight: MIN_TOUCH, borderBottomWidth: StyleSheet.hairlineWidth },
  spacer: { width: 22 },
});
