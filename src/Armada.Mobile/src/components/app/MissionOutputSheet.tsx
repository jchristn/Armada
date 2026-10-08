import { getMissionDiff, getMissionLog } from '@dashboard/api/client';
import { useCallback, useEffect, useState } from 'react';
import { Modal, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';
import { DiffView } from './DiffView';
import { LogView, type LogLineCount } from './LogView';
import { AppText, IconButton, LoadingState } from '../ui';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { spacing } from '../../theme/typography';
import { useReducedMotion } from '../../lib/accessibility';

export interface MissionOutputRequest {
  kind: 'diff' | 'log';
  missionId: string;
  title: string;
}

/**
 * A mission's diff or log in a full-screen sheet (the dashboard's DiffViewer and LogViewer modals opened from a
 * voyage's mission rows). Full screen rather than a bottom sheet because both views scroll on their own.
 */
export function MissionOutputSheet({ request, onClose }: { request: MissionOutputRequest | null; onClose: () => void }) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const reduceMotion = useReducedMotion();
  return (
    <Modal visible={request !== null} animationType={reduceMotion ? 'fade' : 'slide'} presentationStyle="pageSheet" onRequestClose={onClose}>
      <SafeAreaView style={[styles.fill, { backgroundColor: colors.background }]} edges={['top', 'bottom', 'left', 'right']}>
        <View style={[styles.header, { borderBottomColor: colors.border }]}>
          <AppText variant="heading" accessibilityRole="header" numberOfLines={1} style={styles.fill}>
            {request ? t(request.kind === 'diff' ? 'Diff: {{title}}' : 'Log: {{title}}', { title: request.title }) : ''}
          </AppText>
          <IconButton icon="close" label={t('Close')} onPress={onClose} testID="mission-output-close" />
        </View>
        {request ? (request.kind === 'diff'
          ? <DiffBody missionId={request.missionId} />
          : <LogBody missionId={request.missionId} />) : null}
      </SafeAreaView>
    </Modal>
  );
}

function DiffBody({ missionId }: { missionId: string }) {
  const { t } = useLocale();
  const [diff, setDiff] = useState<string | null>(null);
  useEffect(() => {
    let mounted = true;
    getMissionDiff(missionId)
      .then((result) => { if (mounted) setDiff(result?.diff || ''); })
      .catch(() => { if (mounted) setDiff(''); });
    return () => { mounted = false; };
  }, [missionId]);
  if (diff === null) return <LoadingState label={t('Loading...')} />;
  return <DiffView rawDiff={diff} testID="mission-output-diff" />;
}

function LogBody({ missionId }: { missionId: string }) {
  const { t } = useLocale();
  const [lineCount, setLineCount] = useState<LogLineCount>('200');
  const [content, setContent] = useState<string | null>(null);
  const [totalLines, setTotalLines] = useState(0);
  const [version, setVersion] = useState(0);

  useEffect(() => {
    let mounted = true;
    getMissionLog(missionId, Number(lineCount))
      .then((result) => {
        if (!mounted) return;
        setContent(result.log || t('No log output'));
        setTotalLines(result.totalLines || 0);
      })
      .catch((e: unknown) => {
        if (mounted) setContent(t('Log unavailable: {{message}}', { message: e instanceof Error ? e.message : String(e) }));
      });
    return () => { mounted = false; };
  }, [missionId, lineCount, version, t]);

  const refresh = useCallback(() => setVersion((v) => v + 1), []);
  return (
    <View style={styles.log}>
      <LogView
        content={content ?? ''}
        loading={content === null}
        totalLines={totalLines}
        lineCount={lineCount}
        onLineCountChange={setLineCount}
        onRefresh={refresh}
        testID="mission-output-log"
      />
    </View>
  );
}

const styles = StyleSheet.create({
  fill: { flex: 1 },
  header: { flexDirection: 'row', alignItems: 'center', paddingLeft: spacing.lg, paddingRight: spacing.xs, borderBottomWidth: StyleSheet.hairlineWidth },
  log: { flex: 1, paddingTop: spacing.md },
});
