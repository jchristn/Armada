import { StyleSheet, Switch, View } from 'react-native';
import { useTheme } from '../../theme/ThemeContext';
import { MIN_TOUCH, spacing } from '../../theme/typography';
import { AppText } from './AppText';

/** A labelled on/off switch (the dashboard's checkboxes in forms). */
export function SwitchRow({ label, hint, value, onChange, disabled, testID }: {
  label: string;
  hint?: string | null;
  value: boolean;
  onChange: (value: boolean) => void;
  disabled?: boolean;
  testID?: string;
}) {
  const { colors } = useTheme();
  return (
    <View style={styles.row}>
      <View style={styles.text}>
        <AppText variant="label">{label}</AppText>
        {hint ? <AppText variant="caption" muted>{hint}</AppText> : null}
      </View>
      <Switch
        testID={testID}
        accessibilityLabel={label}
        value={value}
        onValueChange={onChange}
        disabled={disabled}
        trackColor={{ true: colors.primary, false: colors.border }}
      />
    </View>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', alignItems: 'center', gap: spacing.md, minHeight: MIN_TOUCH, marginBottom: spacing.md },
  text: { flex: 1, gap: 2 },
});
