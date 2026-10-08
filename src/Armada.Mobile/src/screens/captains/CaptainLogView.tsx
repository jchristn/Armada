import { useCallback, useEffect, useState } from 'react';
import { ScrollView, StyleSheet, View } from 'react-native';
import { getCaptainLog } from '@dashboard/api/client';
import type { FormattedLogEntry } from '@dashboard/types/models';
import { SwitchField } from '../../build/fields';
import { AppText, Button, StatusBadge } from '../../components/ui';
import { useLocale } from '../../i18n/LocaleContext';
import { useSocket } from '../../socket/SocketContext';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing, typography } from '../../theme/typography';

/** Lines of the captain log the dashboard shows. */
const LOG_LINES = 500;

/**
 * The captain log (the dashboard's View Log): the last 500 lines, readable by default (tool names resolved, secrets
 * redacted, noise dropped) or raw. It reloads on refresh and on captain events while shown.
 */
export function CaptainLogView({ captainId }: { captainId: string }) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const { subscribe } = useSocket();
  const [shown, setShown] = useState(false);
  const [readable, setReadable] = useState(true);
  const [loading, setLoading] = useState(false);
  const [text, setText] = useState<string | null>(null);
  const [entries, setEntries] = useState<FormattedLogEntry[] | null>(null);
  const [info, setInfo] = useState('');

  const load = useCallback(async (formatted: boolean) => {
    setLoading(true);
    try {
      const result = await getCaptainLog(captainId, LOG_LINES, formatted);
      setText(result.log || t('(empty log)'));
      setEntries(formatted && result.entries ? result.entries : null);
      setInfo(t('({{lines}} of {{totalLines}} lines)', { lines: result.lines || 0, totalLines: result.totalLines || 0 }));
    } catch {
      setText(t('Failed to load log.'));
      setEntries(null);
      setInfo('');
    } finally {
      setLoading(false);
    }
  }, [captainId, t]);

  useEffect(() => {
    if (!shown) return undefined;
    return subscribe((msg) => {
      if (msg.type === 'captain.changed') void load(readable);
    });
  }, [shown, subscribe, captainId, load, readable]);

  if (!shown) {
    return (
      <View style={styles.pad}>
        <Button label={t('View Log')} icon="document-text-outline" variant="secondary" onPress={() => { setShown(true); void load(readable); }} testID="captain-log-show" />
      </View>
    );
  }

  return (
    <View style={styles.pad} testID="captain-log">
      <View style={styles.head}>
        <AppText variant="label" style={styles.flex}>{`${t('Captain Log')} ${info}`}</AppText>
      </View>
      <SwitchField
        label={t('Readable')}
        hint={t('Resolve tool names, redact secrets, and drop noise')}
        value={readable}
        disabled={loading}
        onChange={(v) => { setReadable(v); void load(v); }}
        testID="captain-log-readable"
      />
      <View style={styles.buttons}>
        <Button label={t('Refresh')} icon="refresh" variant="ghost" onPress={() => void load(readable)} busy={loading} testID="captain-log-refresh" />
        <Button label={t('Hide Log')} variant="ghost" onPress={() => setShown(false)} testID="captain-log-hide" />
      </View>
      {loading && text === null ? <AppText muted>{t('Loading log...')}</AppText> : (
        <ScrollView style={[styles.box, { backgroundColor: colors.surfaceRaised, borderColor: colors.border }]} nestedScrollEnabled>
          {entries && entries.length > 0 ? entries.map((entry, i) => (
            <View key={i} style={styles.entry}>
              {entry.isToolCall && entry.toolName ? <StatusBadge label={`${t('tool')}: ${entry.toolName}`} tone="info" /> : null}
              <AppText selectable style={typography.mono}>{entry.text}</AppText>
              {entry.redacted ? <StatusBadge label={t('redacted')} tone="warning" /> : null}
            </View>
          )) : <AppText selectable style={typography.mono}>{text}</AppText>}
        </ScrollView>
      )}
    </View>
  );
}

const styles = StyleSheet.create({
  pad: { paddingHorizontal: spacing.lg, marginBottom: spacing.lg },
  flex: { flex: 1 },
  head: { flexDirection: 'row', alignItems: 'center', marginBottom: spacing.sm },
  buttons: { flexDirection: 'row', gap: spacing.sm },
  box: { maxHeight: 420, borderWidth: StyleSheet.hairlineWidth, borderRadius: radius.md, padding: spacing.md },
  entry: { flexDirection: 'row', flexWrap: 'wrap', alignItems: 'center', gap: spacing.xs, paddingVertical: 1 },
});
