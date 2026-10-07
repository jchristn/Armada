import { useRef, useState } from 'react';
import { RefreshControl, ScrollView, StyleSheet, View } from 'react-native';
import { getMissionDiff, getMissionInstructions, getMissionLog } from '@dashboard/api/client';
import { DiffView } from '../../../components/app/DiffView';
import { LogView, type LogLineCount } from '../../../components/app/LogView';
import { useInterval } from '../../../data/useInterval';
import { useQuery } from '../../../data/useQuery';
import { AppText, CodeBlock, ErrorState, LoadingState } from '../../../components/ui';
import { useLocale } from '../../../i18n/LocaleContext';
import { useTheme } from '../../../theme/ThemeContext';
import { spacing } from '../../../theme/typography';

/** Diff tab: the mission's diff, loaded when the tab opens. */
export function MissionDiffTab({ id }: { id: string }) {
  const { t } = useLocale();
  const diff = useQuery(() => getMissionDiff(id), [id], t('Failed to load diff.'));
  if (diff.loading) return <LoadingState label={t('Loading diff...')} />;
  if (diff.error && !diff.data) return <ErrorState title={t('Something went wrong')} message={diff.error} retryLabel={t('Retry')} onRetry={() => void diff.refresh()} />;
  return <DiffView rawDiff={diff.data?.diff ?? ''} testID="mission-diff" />;
}

/** Log poll interval while the mission is running (the dashboard's log viewer follows every second). */
export const MISSION_LOG_POLL_MS = 1000;

/**
 * Log tab: the mission log tail, followed while the mission is still running; every fifth poll also refreshes the
 * mission (as the dashboard does) so the tab notices completion.
 */
export function MissionLogTab({ id, completed, onMissionRefresh }: { id: string; completed: boolean; onMissionRefresh: () => void }) {
  const { t } = useLocale();
  const [lineCount, setLineCount] = useState<LogLineCount>('200');
  const log = useQuery(() => getMissionLog(id, Number(lineCount)), [id, lineCount], t('Log unavailable'));
  const ticks = useRef(0);
  useInterval(() => {
    void log.reload();
    ticks.current += 1;
    if (ticks.current % 5 === 0) onMissionRefresh();
  }, completed ? null : MISSION_LOG_POLL_MS);
  const content = log.error && !log.data
    ? t('Log unavailable: {{message}}', { message: log.error })
    : log.data?.log || t('No log output');
  return (
    <ScrollView contentContainerStyle={styles.pad}>
      <LogView
        content={content}
        totalLines={log.data?.totalLines}
        loading={log.loading}
        lineCount={lineCount}
        onLineCountChange={setLineCount}
        onRefresh={() => void log.reload()}
        testID="mission-log"
      />
    </ScrollView>
  );
}

/** Instructions tab: the generated mission instructions file. */
export function MissionInstructionsTab({ id }: { id: string }) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const result = useQuery(() => getMissionInstructions(id), [id], t('Instructions unavailable'));
  if (result.loading) return <LoadingState label={t('Loading...')} />;
  return (
    <ScrollView
      contentContainerStyle={styles.pad}
      refreshControl={<RefreshControl refreshing={result.refreshing} onRefresh={() => void result.refresh()} tintColor={colors.primary} />}
    >
      <View style={styles.inner}>
        <AppText variant="label" testID="mission-instructions-title">
          {result.data ? t('Instructions: {{fileName}}', { fileName: result.data.fileName || t('Mission Instructions') }) : t('Mission Instructions')}
        </AppText>
        <CodeBlock
          wrap
          testID="mission-instructions"
          text={result.error && !result.data
            ? t('Instructions unavailable: {{message}}', { message: result.error })
            : result.data?.content || t('No mission instructions found.')}
        />
      </View>
    </ScrollView>
  );
}

const styles = StyleSheet.create({
  pad: { paddingVertical: spacing.md },
  inner: { paddingHorizontal: spacing.md, gap: spacing.sm },
});
