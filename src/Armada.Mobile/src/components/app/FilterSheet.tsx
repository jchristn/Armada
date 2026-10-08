import { useRef, useState, type ReactNode } from 'react';
import { StyleSheet, View } from 'react-native';
import { useLocale } from '../../i18n/LocaleContext';
import { spacing } from '../../theme/typography';
import { BottomSheet, Button } from '../ui';

/** Number of active (non-empty) filter values. */
export function activeFilterCount(values: Record<string, string | null | undefined>): number {
  return Object.values(values).filter((v) => !!v).length;
}

/**
 * The mobile form of a dashboard filter bar: a "Filters (n)" button that opens a sheet holding the filter fields
 * (usually SelectFields) and a Clear button.
 */
export function FilterButton({ count, onClear, children, testID }: { count: number; onClear: () => void; children: ReactNode; testID?: string }) {
  const { t } = useLocale();
  const [open, setOpen] = useState(false);
  const buttonRef = useRef<View | null>(null);
  const id = testID ?? 'filters';
  return (
    <>
      <Button
        ref={buttonRef}
        testID={id}
        variant="secondary"
        icon="filter-outline"
        label={count > 0 ? t('Filters ({{count}})', { count }) : t('Filters')}
        onPress={() => setOpen(true)}
        style={styles.button}
      />
      <BottomSheet open={open} title={t('Filters')} onClose={() => setOpen(false)} closeLabel={t('Close')} returnFocusRef={buttonRef} testID={`${id}-sheet`}>
        {children}
        <View style={styles.actions}>
          <Button label={t('Clear')} variant="ghost" onPress={onClear} disabled={count === 0} testID={`${id}-clear`} />
          <Button label={t('Done')} onPress={() => setOpen(false)} testID={`${id}-done`} />
        </View>
      </BottomSheet>
    </>
  );
}

const styles = StyleSheet.create({
  button: { flex: 1 },
  actions: { flexDirection: 'row', justifyContent: 'flex-end', gap: spacing.sm },
});
