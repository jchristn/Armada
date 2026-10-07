import { StyleSheet, View } from 'react-native';
import { AppText, Button } from '../../../components/ui';
import { useLocale } from '../../../i18n/LocaleContext';
import { useTheme } from '../../../theme/ThemeContext';
import { spacing } from '../../../theme/typography';

/** Shown while bulk-selecting: the count, select all, cancel, and Delete Selected (n). */
export function SelectionBar({ count, onDelete, onSelectAll, onCancel, testID }: {
  count: number;
  onDelete: () => void;
  onSelectAll: () => void;
  onCancel: () => void;
  testID: string;
}) {
  const { t } = useLocale();
  const { colors } = useTheme();
  return (
    <View style={[styles.bar, { backgroundColor: colors.surfaceRaised, borderColor: colors.border }]} testID={testID}>
      <AppText variant="label" accessibilityLiveRegion="polite">{t('{{count}} selected', { count })}</AppText>
      <View style={styles.actions}>
        <Button label={t('Select All')} variant="ghost" onPress={onSelectAll} testID={`${testID}-all`} />
        <Button label={t('Cancel')} variant="ghost" onPress={onCancel} testID={`${testID}-cancel`} />
        <Button label={`${t('Delete Selected')} (${count})`} variant="danger" onPress={onDelete} disabled={count === 0} testID={`${testID}-delete`} />
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  bar: { marginHorizontal: spacing.md, marginBottom: spacing.md, padding: spacing.md, borderWidth: StyleSheet.hairlineWidth, borderRadius: 10, gap: spacing.sm },
  actions: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm },
});
