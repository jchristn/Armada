import { forwardRef, useState } from 'react';
import { Pressable, StyleSheet, TextInput, View, type TextInputProps } from 'react-native';
import { useTheme } from '../../theme/ThemeContext';
import { MIN_TOUCH, radius, spacing, typography } from '../../theme/typography';
import { AppText } from './AppText';
import { Icon } from './Icon';

export interface TextFieldProps extends Omit<TextInputProps, 'style'> {
  label: string;
  error?: string | null;
  hint?: string | null;
  /** A secret (password, API key): hidden, with a reveal toggle labelled by `revealLabel` / `hideLabel`. */
  secret?: boolean;
  revealLabel?: string;
  hideLabel?: string;
}

/** A labelled text input. The label is the input's accessible name; errors are announced. */
export const TextField = forwardRef<TextInput, TextFieldProps>(function TextField(
  { label, error, hint, secret, revealLabel = 'Show', hideLabel = 'Hide', testID, ...rest },
  ref,
) {
  const { colors } = useTheme();
  const [revealed, setRevealed] = useState(false);
  const [focused, setFocused] = useState(false);
  return (
    <View style={styles.wrap}>
      <AppText variant="label" nativeID={testID ? `${testID}-label` : undefined}>{label}</AppText>
      <View style={[styles.box, { borderColor: error ? colors.danger : focused ? colors.focus : colors.border, backgroundColor: colors.surface }]}>
        <TextInput
          ref={ref}
          testID={testID}
          accessibilityLabel={label}
          accessibilityHint={hint ?? undefined}
          placeholderTextColor={colors.textMuted}
          secureTextEntry={secret && !revealed}
          autoCorrect={secret ? false : rest.autoCorrect}
          autoCapitalize={secret ? 'none' : rest.autoCapitalize}
          // Android wraps a long placeholder in a one-line field onto a second line that the box clips.
          numberOfLines={rest.multiline ? undefined : 1}
          {...rest}
          onFocus={(e) => { setFocused(true); rest.onFocus?.(e); }}
          onBlur={(e) => { setFocused(false); rest.onBlur?.(e); }}
          style={[styles.input, typography.body, { color: colors.text }]}
        />
        {secret ? (
          <Pressable
            accessibilityRole="button"
            accessibilityLabel={revealed ? hideLabel : revealLabel}
            onPress={() => setRevealed((v) => !v)}
            style={styles.reveal}
            testID={testID ? `${testID}-reveal` : undefined}
          >
            <Icon name={revealed ? 'eye-off-outline' : 'eye-outline'} color="textMuted" />
          </Pressable>
        ) : null}
      </View>
      {error ? <AppText variant="caption" color="danger" accessibilityRole="alert" accessibilityLiveRegion="polite">{error}</AppText> : null}
      {!error && hint ? <AppText variant="caption" muted>{hint}</AppText> : null}
    </View>
  );
});

const styles = StyleSheet.create({
  wrap: { gap: spacing.xs, marginBottom: spacing.lg },
  box: { flexDirection: 'row', alignItems: 'center', borderWidth: 1.5, borderRadius: radius.md, minHeight: MIN_TOUCH },
  input: { flex: 1, paddingHorizontal: spacing.md, paddingVertical: spacing.sm },
  reveal: { minWidth: MIN_TOUCH, minHeight: MIN_TOUCH, alignItems: 'center', justifyContent: 'center' },
});
