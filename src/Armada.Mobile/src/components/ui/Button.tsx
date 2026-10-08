import type { Ref } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, View, type StyleProp, type ViewStyle } from 'react-native';
import { useTheme } from '../../theme/ThemeContext';
import { MIN_TOUCH, radius, spacing } from '../../theme/typography';
import { AppText } from './AppText';
import { Icon, type IconName } from './Icon';

export type ButtonVariant = 'primary' | 'secondary' | 'danger' | 'ghost';

export interface ButtonProps {
  label: string;
  onPress: () => void;
  variant?: ButtonVariant;
  disabled?: boolean;
  /** Shows a spinner and disables the button; the label stays for screen readers. */
  busy?: boolean;
  icon?: IconName;
  accessibilityHint?: string;
  testID?: string;
  style?: StyleProp<ViewStyle>;
  /** The native button (a sheet it opens returns screen-reader focus to it). */
  ref?: Ref<View>;
}

export function Button({ label, onPress, variant = 'primary', disabled, busy, icon, accessibilityHint, testID, style, ref }: ButtonProps) {
  const { colors } = useTheme();
  const inactive = disabled || busy;
  const background = variant === 'primary' ? colors.primary : variant === 'danger' ? colors.danger : 'transparent';
  const foreground = variant === 'primary' ? 'primaryText' : variant === 'danger' ? 'dangerText' : 'primary';
  return (
    <Pressable
      ref={ref}
      testID={testID}
      accessibilityRole="button"
      accessibilityLabel={label}
      accessibilityHint={accessibilityHint}
      accessibilityState={{ disabled: !!inactive, busy: !!busy }}
      disabled={inactive}
      onPress={onPress}
      style={({ pressed }) => [
        styles.base,
        {
          backgroundColor: background,
          borderColor: variant === 'secondary' ? colors.primary : 'transparent',
          opacity: inactive ? 0.55 : pressed ? 0.8 : 1,
        },
        style,
      ]}
    >
      <View style={styles.row}>
        {busy ? <ActivityIndicator color={colors[foreground]} /> : icon ? <Icon name={icon} size={18} color={foreground} /> : null}
        <AppText variant="label" color={foreground} style={styles.label}>{label}</AppText>
      </View>
    </Pressable>
  );
}

const styles = StyleSheet.create({
  base: {
    minHeight: MIN_TOUCH,
    borderRadius: radius.md,
    borderWidth: 1.5,
    paddingHorizontal: spacing.lg,
    paddingVertical: spacing.sm,
    marginBottom: spacing.sm,
    justifyContent: 'center',
  },
  row: { flexDirection: 'row', alignItems: 'center', justifyContent: 'center', gap: spacing.sm },
  label: { textAlign: 'center' },
});
