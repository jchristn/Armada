import { useMemo, useState } from 'react';
import { FlatList, Pressable, StyleSheet, View } from 'react-native';
import { useTheme } from '../../theme/ThemeContext';
import { MIN_TOUCH, radius, spacing } from '../../theme/typography';
import { AppText } from './AppText';
import { BottomSheet } from './BottomSheet';
import { Icon } from './Icon';
import { SearchField } from './SearchField';

export interface SelectOption {
  value: string;
  label: string;
  description?: string | null;
}

export interface SelectFieldProps {
  label: string;
  value: string;
  options: SelectOption[];
  onChange: (value: string) => void;
  /** Shown when nothing is selected (value ''); also the label of the '' option when `allowEmpty`. */
  placeholder: string;
  /** Adds a first option with value '' (for example "All statuses"). */
  allowEmpty?: boolean;
  closeLabel: string;
  /** Placeholder of the search box (shown when there are more than 8 options). */
  searchLabel?: string;
  hint?: string | null;
  error?: string | null;
  disabled?: boolean;
  testID?: string;
}

/** A labelled picker: the mobile form of the dashboard's <select>, choosing in a bottom sheet. */
export function SelectField({
  label, value, options, onChange, placeholder, allowEmpty, closeLabel, searchLabel, hint, error, disabled, testID,
}: SelectFieldProps) {
  const { colors } = useTheme();
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const all = useMemo(() => (allowEmpty ? [{ value: '', label: placeholder }, ...options] : options), [allowEmpty, placeholder, options]);
  const shown = useMemo(() => {
    const term = query.trim().toLowerCase();
    if (!term) return all;
    return all.filter((o) => o.label.toLowerCase().includes(term) || o.value.toLowerCase().includes(term));
  }, [all, query]);
  const current = all.find((o) => o.value === value);
  const display = current?.label ?? (value || placeholder);

  return (
    <View style={styles.wrap}>
      <AppText variant="label">{label}</AppText>
      <Pressable
        testID={testID}
        accessibilityRole="button"
        accessibilityLabel={`${label}, ${display}`}
        accessibilityHint={hint ?? undefined}
        accessibilityState={{ disabled: !!disabled }}
        disabled={disabled}
        onPress={() => { setQuery(''); setOpen(true); }}
        style={[styles.box, { borderColor: error ? colors.danger : colors.border, backgroundColor: colors.surface, opacity: disabled ? 0.55 : 1 }]}
      >
        <AppText style={styles.flex} muted={!current || value === ''} numberOfLines={1}>{display}</AppText>
        <Icon name="chevron-down" size={18} color="textMuted" />
      </Pressable>
      {error ? <AppText variant="caption" color="danger" accessibilityRole="alert">{error}</AppText> : null}
      {!error && hint ? <AppText variant="caption" muted>{hint}</AppText> : null}
      <BottomSheet open={open} title={label} onClose={() => setOpen(false)} closeLabel={closeLabel} testID={testID ? `${testID}-sheet` : undefined}>
        {all.length > 8 ? (
          <SearchField value={query} onChangeText={setQuery} placeholder={searchLabel ?? label} clearLabel={closeLabel} testID={testID ? `${testID}-search` : undefined} />
        ) : null}
        <FlatList
          scrollEnabled={false}
          data={shown}
          keyExtractor={(o) => o.value || '__empty'}
          renderItem={({ item }) => {
            const selected = item.value === value;
            return (
              <Pressable
                testID={testID ? `${testID}-option-${item.value || 'none'}` : undefined}
                accessibilityRole="radio"
                accessibilityState={{ checked: selected }}
                accessibilityLabel={item.description ? `${item.label}, ${item.description}` : item.label}
                onPress={() => { onChange(item.value); setOpen(false); }}
                style={({ pressed }) => [styles.option, { borderBottomColor: colors.border, backgroundColor: pressed ? colors.background : 'transparent' }]}
              >
                <View style={styles.flex}>
                  <AppText variant="label" color={selected ? 'primary' : 'text'}>{item.label}</AppText>
                  {item.description ? <AppText variant="caption" muted>{item.description}</AppText> : null}
                </View>
                {selected ? <Icon name="checkmark" color="primary" /> : null}
              </Pressable>
            );
          }}
        />
      </BottomSheet>
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: { gap: spacing.xs, marginBottom: spacing.lg },
  box: { flexDirection: 'row', alignItems: 'center', borderWidth: 1.5, borderRadius: radius.md, minHeight: MIN_TOUCH, paddingHorizontal: spacing.md, gap: spacing.sm },
  flex: { flex: 1 },
  option: { flexDirection: 'row', alignItems: 'center', minHeight: MIN_TOUCH, paddingVertical: spacing.sm, borderBottomWidth: StyleSheet.hairlineWidth, gap: spacing.md },
});
