import { StyleSheet, Switch, View } from 'react-native';
import { useTheme } from '../../theme/ThemeContext';
import { MIN_TOUCH, spacing } from '../../theme/typography';
import { AppText } from './AppText';

export interface SwitchFieldProps {
  label: string;
  value: boolean;
  onChange: (value: boolean) => void;
  hint?: string | null;
  disabled?: boolean;
  testID?: string;
}

/** A labelled on/off switch (the mobile form of the dashboard's checkboxes). The label is the switch's name. */
export function SwitchField({ label, value, onChange, hint, disabled, testID }: SwitchFieldProps) {
  const { colors } = useTheme();
  return (
    <View style={styles.wrap}>
      <View style={styles.row}>
        <AppText variant="label" style={styles.label}>{label}</AppText>
        <Switch
          testID={testID}
          accessibilityLabel={label}
          accessibilityHint={hint ?? undefined}
          value={value}
          disabled={disabled}
          onValueChange={onChange}
          trackColor={{ true: colors.primary, false: colors.control }}
        />
      </View>
      {hint ? <AppText variant="caption" muted>{hint}</AppText> : null}
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: { gap: spacing.xs, marginBottom: spacing.lg },
  row: { flexDirection: 'row', alignItems: 'center', minHeight: MIN_TOUCH, gap: spacing.md },
  label: { flex: 1 },
});
