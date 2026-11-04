import { StyleSheet, View } from 'react-native';
import { useLocale } from '../../i18n/LocaleContext';
import { spacing } from '../../theme/typography';
import { AppText } from './AppText';
import { Button } from './Button';

/**
 * Previous / "Page x of y" / Next on one line under a paged list. Hidden while everything fits on one page, so a
 * short list does not carry two disabled buttons. `page` counts from 1.
 */
export function Pager({ page, pages, onChange, disabled, testID }: { page: number; pages: number; onChange: (page: number) => void; disabled?: boolean; testID?: string }) {
  const { t } = useLocale();
  const total = Math.max(1, pages);
  if (total <= 1 && page <= 1) return null;
  return (
    <View style={styles.row} testID={testID}>
      <Button label={t('Previous')} icon="chevron-back" variant="secondary" disabled={disabled || page <= 1} onPress={() => onChange(page - 1)} style={styles.button} testID={testID ? `${testID}-previous` : undefined} />
      <AppText variant="caption" muted accessibilityLiveRegion="polite" style={styles.label}>{t('Page {{page}} of {{total}}', { page, total })}</AppText>
      <Button label={t('Next')} icon="chevron-forward" variant="secondary" disabled={disabled || page >= total} onPress={() => onChange(page + 1)} style={styles.button} testID={testID ? `${testID}-next` : undefined} />
    </View>
  );
}

const styles = StyleSheet.create({
  row: { flexDirection: 'row', flexWrap: 'wrap', alignItems: 'center', justifyContent: 'center', gap: spacing.sm, marginHorizontal: spacing.md, marginBottom: spacing.lg },
  button: { marginBottom: 0 },
  label: { textAlign: 'center', minWidth: 96 },
});
