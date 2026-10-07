import { Pressable, StyleSheet, View } from 'react-native';
import { useState } from 'react';
import { useTheme } from '../../theme/ThemeContext';
import { MIN_TOUCH, radius, spacing } from '../../theme/typography';
import { AppText } from './AppText';
import { BottomSheet } from './BottomSheet';
import { Icon } from './Icon';

export interface SelectOption<T extends string> {
  value: T;
  label: string;
  description?: string;
  disabled?: boolean;
}

export interface SelectFieldProps<T extends string> {
  /** Field label (also the sheet title and the accessible name). */
  label: string;
  value: T;
  options: SelectOption<T>[];
  onChange: (value: T) => void;
  closeLabel: string;
  /** Text shown when the value matches no option. */
  placeholder?: string;
  disabled?: boolean;
  hint?: string | null;
  error?: string | null;
  testID?: string;
}

/**
 * A labelled picker (the mobile form of the dashboard's `<select>`): a field that shows the current choice and
 * opens a bottom sheet of options. Options are radio buttons for screen readers.
 */
export function SelectField<T extends string>({ label, value, options, onChange, closeLabel, placeholder, disabled, hint, error, testID }: SelectFieldProps<T>) {
  const { colors } = useTheme();
  const [open, setOpen] = useState(false);
  const current = options.find((o) => o.value === value);
  const shown = current?.label ?? placeholder ?? '';
  return (
    <View style={styles.wrap}>
      <AppText variant="label">{label}</AppText>
      <Pressable
        testID={testID}
        accessibilityRole="button"
        accessibilityLabel={label}
        accessibilityValue={{ text: shown }}
        accessibilityState={{ disabled: !!disabled }}
        disabled={disabled}
        onPress={() => setOpen(true)}
        style={[styles.box, { borderColor: error ? colors.danger : colors.border, backgroundColor: colors.surface, opacity: disabled ? 0.55 : 1 }]}
      >
        <AppText style={styles.value} muted={!current} numberOfLines={1}>{shown}</AppText>
        <Icon name="chevron-down" size={18} color="textMuted" />
      </Pressable>
      {error ? <AppText variant="caption" color="danger" accessibilityRole="alert">{error}</AppText> : null}
      {!error && hint ? <AppText variant="caption" muted>{hint}</AppText> : null}
      <BottomSheet open={open} title={label} onClose={() => setOpen(false)} closeLabel={closeLabel} testID={testID ? `${testID}-sheet` : undefined}>
        <View accessibilityRole="radiogroup">
          {options.map((option) => {
            const selected = option.value === value;
            return (
              <Pressable
                key={option.value || '__empty'}
                testID={testID ? `${testID}-option-${option.value || 'none'}` : undefined}
                accessibilityRole="radio"
                accessibilityLabel={option.label}
                accessibilityHint={option.description}
                accessibilityState={{ checked: selected, disabled: !!option.disabled }}
                disabled={option.disabled}
                onPress={() => { setOpen(false); if (!selected) onChange(option.value); }}
                style={({ pressed }) => [styles.option, { borderBottomColor: colors.border, opacity: option.disabled ? 0.5 : pressed ? 0.7 : 1 }]}
              >
                <View style={styles.optionText}>
                  <AppText variant="label">{option.label}</AppText>
                  {option.description ? <AppText variant="caption" muted>{option.description}</AppText> : null}
                </View>
                {selected ? <Icon name="checkmark" color="primary" /> : null}
              </Pressable>
            );
          })}
        </View>
      </BottomSheet>
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: { gap: spacing.xs, marginBottom: spacing.lg },
  box: { flexDirection: 'row', alignItems: 'center', borderWidth: 1.5, borderRadius: radius.md, minHeight: MIN_TOUCH, paddingHorizontal: spacing.md, gap: spacing.sm },
  value: { flex: 1 },
  option: { flexDirection: 'row', alignItems: 'center', minHeight: MIN_TOUCH, paddingVertical: spacing.sm, borderBottomWidth: StyleSheet.hairlineWidth, gap: spacing.md },
  optionText: { flex: 1, gap: 2 },
});
