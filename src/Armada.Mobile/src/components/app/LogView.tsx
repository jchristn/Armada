import { StyleSheet, View } from 'react-native';
import { useLocale } from '../../i18n/LocaleContext';
import { spacing } from '../../theme/typography';
import { AppText, Button, CodeBlock, LoadingState, SegmentedControl } from '../ui';

export const LOG_LINE_COUNTS = ['100', '200', '500', '1000'] as const;
export type LogLineCount = (typeof LOG_LINE_COUNTS)[number];

/** A log tail (the dashboard's LogViewer): line count choice, refresh, and the selectable text. */
export function LogView({ content, totalLines, loading, lineCount, onLineCountChange, onRefresh, testID }: {
  content: string;
  totalLines?: number | null;
  loading?: boolean;
  lineCount: LogLineCount;
  onLineCountChange: (count: LogLineCount) => void;
  onRefresh: () => void;
  testID?: string;
}) {
  const { t } = useLocale();
  return (
    <View style={styles.wrap} testID={testID}>
      <SegmentedControl
        label={t('Lines')}
        value={lineCount}
        onChange={onLineCountChange}
        options={LOG_LINE_COUNTS.map((count) => ({ value: count, label: count, testID: testID ? `${testID}-lines-${count}` : undefined }))}
      />
      <View style={styles.row}>
        <AppText variant="caption" muted style={styles.flex}>
          {totalLines ? t('{{count}} total lines', { count: totalLines }) : ''}
        </AppText>
        <Button label={t('Refresh')} icon="refresh" variant="ghost" onPress={onRefresh} testID={testID ? `${testID}-refresh` : undefined} />
      </View>
      {loading ? <LoadingState label={t('Loading log...')} /> : <CodeBlock text={content || t('No log output')} testID={testID ? `${testID}-text` : undefined} />}
    </View>
  );
}

const styles = StyleSheet.create({
  wrap: { paddingHorizontal: spacing.md },
  row: { flexDirection: 'row', alignItems: 'center' },
  flex: { flex: 1 },
});
