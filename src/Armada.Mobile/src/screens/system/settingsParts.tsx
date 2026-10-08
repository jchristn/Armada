import { useState, type ReactNode } from 'react';
import { StyleSheet, View } from 'react-native';
import { AppText } from '../../components/ui/AppText';
import { Button } from '../../components/ui/Button';
import { IconButton } from '../../components/ui/IconButton';
import { TextField } from '../../components/ui/TextField';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing } from '../../theme/typography';

/** A titled settings card (the dashboard's settings-section), with an optional description. */
export function SettingsSection({ title, description, children, testID }: { title: string; description?: string | null; children: ReactNode; testID?: string }) {
  const { colors } = useTheme();
  return (
    <View style={styles.wrap} testID={testID}>
      <AppText variant="subheading" muted accessibilityRole="header" style={styles.title}>{title}</AppText>
      <View style={[styles.card, { backgroundColor: colors.surface, borderColor: colors.border }]}>
        {description ? <AppText variant="caption" muted style={styles.description}>{description}</AppText> : null}
        {children}
      </View>
    </View>
  );
}

/** A whole-number text field; shows `error` (already translated) in place of the hint when invalid. */
export function NumberField({ label, value, onChange, hint, error, disabled, testID }: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  hint?: string | null;
  error?: string | null;
  disabled?: boolean;
  testID?: string;
}) {
  return (
    <TextField
      label={label}
      value={value}
      onChangeText={onChange}
      keyboardType="number-pad"
      hint={hint}
      error={error}
      editable={!disabled}
      testID={testID}
    />
  );
}

/** Save (and, with unsaved edits, Discard) for one section. */
export function SaveRow({ label, onSave, saving, disabled, dirty, onDiscard, testID }: {
  label: string;
  onSave: () => void;
  saving?: boolean;
  disabled?: boolean;
  /** Unsaved edits: shows Discard changes and the Unsaved changes note. */
  dirty?: boolean;
  onDiscard?: () => void;
  testID?: string;
}) {
  const { t } = useLocale();
  return (
    <View style={styles.actions}>
      <Button label={saving ? t('Saving...') : label} onPress={onSave} busy={saving} disabled={disabled} testID={testID} style={styles.button} />
      {dirty && onDiscard ? <Button label={t('Discard changes')} variant="ghost" onPress={onDiscard} style={styles.button} /> : null}
      {dirty && onDiscard ? <AppText variant="caption" muted>{t('Unsaved changes')}</AppText> : null}
    </View>
  );
}

/** A list of strings with Add and per-item Remove (the dashboard's ListEditor); duplicates are ignored. */
export function ListEditor({ label, help, placeholder, values, onChange, disabled, testID }: {
  label: string;
  help?: string | null;
  placeholder?: string;
  values: string[];
  onChange: (values: string[]) => void;
  disabled?: boolean;
  testID?: string;
}) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const [draft, setDraft] = useState('');
  function add() {
    const value = draft.trim();
    if (!value) return;
    if (!values.includes(value)) onChange([...values, value]);
    setDraft('');
  }
  return (
    <View style={styles.list}>
      <TextField
        label={label}
        hint={help}
        value={draft}
        onChangeText={setDraft}
        placeholder={placeholder}
        autoCapitalize="none"
        autoCorrect={false}
        editable={!disabled}
        onSubmitEditing={add}
        returnKeyType="done"
        testID={testID}
      />
      <Button label={t('Add')} variant="secondary" onPress={add} disabled={disabled || !draft.trim()} style={styles.button} testID={testID ? `${testID}-add` : undefined} />
      {values.length === 0 ? <AppText variant="caption" muted>{t('None')}</AppText> : values.map((value) => (
        <View key={value} style={[styles.listItem, { borderBottomColor: colors.border }]}>
          <AppText variant="mono" style={styles.flex} selectable>{value}</AppText>
          {!disabled ? <IconButton icon="close" label={t('Remove {{value}}', { value })} color="textMuted" onPress={() => onChange(values.filter((x) => x !== value))} /> : null}
        </View>
      ))}
    </View>
  );
}

const styles = StyleSheet.create({
  flex: { flex: 1 },
  list: { marginBottom: spacing.lg, gap: spacing.xs },
  listItem: { flexDirection: 'row', alignItems: 'center', borderBottomWidth: StyleSheet.hairlineWidth },
  wrap: { marginBottom: spacing.xl },
  title: { marginHorizontal: spacing.lg, marginBottom: spacing.sm, textTransform: 'uppercase' },
  card: { borderRadius: radius.md, borderWidth: StyleSheet.hairlineWidth, marginHorizontal: spacing.md, padding: spacing.md },
  description: { marginBottom: spacing.md },
  actions: { flexDirection: 'row', flexWrap: 'wrap', alignItems: 'center', gap: spacing.sm },
  button: { marginBottom: 0 },
});
