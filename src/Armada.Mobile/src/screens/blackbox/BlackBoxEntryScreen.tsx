import { Stack } from 'expo-router';
import { ScrollView, StyleSheet } from 'react-native';
import { useLocale } from '../../i18n/LocaleContext';
import { spacing } from '../../theme/typography';
import { BlackBoxNearbySuccessesSection, BlackBoxTimelineSection } from './BlackBoxInsightSections';
import { BlackBoxPanel } from './BlackBoxPanel';

/**
 * One black box entry (/blackbox/:id): the same panel as mission and voyage detail, with full-page states, then the
 * combined timeline and the nearby successes (each paged).
 */
export function BlackBoxEntryScreen({ id }: { id: string }) {
  const { t } = useLocale();
  return (
    <ScrollView testID="bbx-entry" contentContainerStyle={styles.scroll}>
      <Stack.Screen options={{ title: t('Black Box') }} />
      <BlackBoxPanel
        source={{ kind: 'entry', id }}
        standalone
        after={(entry) => (
          <>
            <BlackBoxTimelineSection entryId={entry.id} />
            {entry.subjectType === 'Mission' ? <BlackBoxNearbySuccessesSection entryId={entry.id} /> : null}
          </>
        )}
      />
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  scroll: { flexGrow: 1, paddingTop: spacing.md, paddingBottom: spacing.xxl, width: '100%', maxWidth: 820, alignSelf: 'center' },
});
