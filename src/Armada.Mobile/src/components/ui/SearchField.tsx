import { Pressable, StyleSheet, TextInput, View } from 'react-native';
import { useTheme } from '../../theme/ThemeContext';
import { MIN_TOUCH, radius, spacing, typography } from '../../theme/typography';
import { Icon } from './Icon';

export interface SearchFieldProps {
  value: string;
  onChangeText: (value: string) => void;
  placeholder: string;
  clearLabel: string;
  testID?: string;
}

export function SearchField({ value, onChangeText, placeholder, clearLabel, testID }: SearchFieldProps) {
  const { colors } = useTheme();
  return (
    <View style={[styles.box, { backgroundColor: colors.surface, borderColor: colors.border }]}>
      <Icon name="search" size={18} color="textMuted" />
      <TextInput
        testID={testID}
        value={value}
        onChangeText={onChangeText}
        placeholder={placeholder}
        placeholderTextColor={colors.textMuted}
        accessibilityLabel={placeholder}
        accessibilityRole="search"
        autoCapitalize="none"
        autoCorrect={false}
        returnKeyType="search"
        clearButtonMode="never"
        style={[styles.input, typography.body, { color: colors.text }]}
      />
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
  input: { flex: 1, paddingVertical: spacing.sm },
  clear: { minWidth: MIN_TOUCH, minHeight: MIN_TOUCH, alignItems: 'center', justifyContent: 'center' },
});
