import { Platform, Pressable, StyleSheet, TextInput, View } from 'react-native';
import { useTheme } from '../../theme/ThemeContext';
import { MIN_TOUCH, radius, spacing, typography } from '../../theme/typography';
import { AppText } from './AppText';
import { Icon } from './Icon';

export interface SearchFieldProps {
  value: string;
  onChangeText: (value: string) => void;
  placeholder: string;
  clearLabel: string;
  testID?: string;
}

/**
 * Android draws a TextInput's placeholder wrapped onto as many lines as it needs, and a one-line box clips the second
 * line in half (the W4 lists' "Search by title, environment, ..."); numberOfLines does not stop it. There the
 * placeholder is drawn as one ellipsized line over the empty input instead.
 */
export function SearchField({ value, onChangeText, placeholder, clearLabel, testID }: SearchFieldProps) {
  const { colors } = useTheme();
  const ownPlaceholder = Platform.OS === 'android';
  return (
    <View style={[styles.box, { backgroundColor: colors.surface, borderColor: colors.control }]}>
      <Icon name="search" size={18} color="textMuted" />
      <View style={styles.inputWrap}>
      <TextInput
        testID={testID}
        value={value}
        onChangeText={onChangeText}
        placeholder={ownPlaceholder ? undefined : placeholder}
        placeholderTextColor={colors.textMuted}
        accessibilityLabel={placeholder}
        accessibilityRole="search"
        autoCapitalize="none"
        autoCorrect={false}
        returnKeyType="search"
        clearButtonMode="never"
        style={[styles.input, typography.body, { color: colors.text }]}
      />
      {ownPlaceholder && !value ? (
        <View pointerEvents="none" style={styles.placeholder} importantForAccessibility="no-hide-descendants">
          <AppText numberOfLines={1} ellipsizeMode="tail" style={[typography.body, { color: colors.textMuted }]} testID={testID ? `${testID}-placeholder` : undefined}>
            {placeholder}
          </AppText>
        </View>
      ) : null}
      </View>
      {value ? (
        <Pressable accessibilityRole="button" accessibilityLabel={clearLabel} onPress={() => onChangeText('')} style={styles.clear}>
          <Icon name="close-circle" size={18} color="textMuted" />
        </Pressable>
      ) : null}
    </View>
  );
}

const styles = StyleSheet.create({
  box: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, borderWidth: 1, borderRadius: radius.md, paddingLeft: spacing.md, marginHorizontal: spacing.md, marginBottom: spacing.md, minHeight: MIN_TOUCH },
  inputWrap: { flex: 1, justifyContent: 'center' },
  input: { paddingVertical: spacing.sm },
  placeholder: { position: 'absolute', top: 0, bottom: 0, left: 0, right: 0, justifyContent: 'center' },
  clear: { minWidth: MIN_TOUCH, minHeight: MIN_TOUCH, alignItems: 'center', justifyContent: 'center' },
});
