import { useState } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import type { ToolEvent } from '@dashboard/lib/toolEvents';
import { formatToolMs, prettyJson, toolResultPreview } from '@dashboard/lib/askFormat';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { MIN_TOUCH, radius, spacing } from '../../theme/typography';
import { AppText } from '../ui/AppText';
import { CodeBlock } from './CodeBlock';

interface ToolChipsProps {
  tools: ToolEvent[] | undefined;
  /** Explanation under a call the CLI refused for lack of permission (already localized). */
  permissionDeniedNote?: string;
}

function ToolChip({ tool, permissionDeniedNote }: { tool: ToolEvent; permissionDeniedNote?: string }) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const [open, setOpen] = useState(false);
  const glyph = tool.permissionDenied ? '\u2715!' : tool.status === 'running' ? '\u2026' : tool.status === 'success' ? '\u2713' : '\u2715';
  const tone = tool.status === 'failed' ? colors.danger : tool.status === 'success' ? colors.success : colors.info;
  const meta = tool.status === 'running' ? t('running\u2026') : formatToolMs(tool.elapsedMs);
  const preview = tool.status !== 'running' && tool.result ? toolResultPreview(tool.result) : '';
  const statusWord = tool.permissionDenied ? t('Refused for lack of permission') : tool.status === 'running' ? t('running\u2026') : tool.status === 'success' ? t('Succeeded') : t('Failed');
  return (
    <View>
      <View style={[styles.chip, { borderColor: tone, backgroundColor: colors.surface }]}>
        <Pressable
          testID={`tool-chip-${tool.id}`}
          accessibilityRole="button"
          accessibilityLabel={`${tool.name}, ${statusWord}${meta && tool.status !== 'running' ? `, ${meta}` : ''}`}
          accessibilityValue={preview ? { text: preview } : undefined}
          accessibilityState={{ expanded: open }}
          onPress={() => setOpen((v) => !v)}
          style={styles.summary}
        >
          <AppText variant="caption" style={{ color: tone, fontWeight: '700' }} importantForAccessibility="no">{glyph}</AppText>
          <AppText variant="mono" numberOfLines={1} style={styles.name}>{tool.name}</AppText>
          {preview ? <AppText variant="caption" muted numberOfLines={1} style={styles.preview}>{preview}</AppText> : <View style={styles.preview} />}
          <AppText variant="caption" muted>{meta}</AppText>
        </Pressable>
        {open ? (
          <View style={styles.detail}>
            {tool.arguments ? (
              <>
                <AppText variant="caption" muted>{t('Arguments')}</AppText>
                <CodeBlock text={prettyJson(tool.arguments)} />
              </>
            ) : null}
            {tool.result ? (
              <>
                <AppText variant="caption" muted>{t('Result')}</AppText>
                <CodeBlock text={prettyJson(tool.result)} />
              </>
            ) : null}
            {!tool.arguments && !tool.result ? <AppText variant="caption" muted>{t('No details available.')}</AppText> : null}
          </View>
        ) : null}
      </View>
      {tool.permissionDenied && permissionDeniedNote ? (
        <AppText variant="caption" color="danger" accessibilityRole="text" style={styles.note}>{permissionDeniedNote}</AppText>
      ) : null}
    </View>
  );
}

/**
 * Tool-call activity of a captain turn (the dashboard's ChatToolChips): one compact chip per call with a status
 * glyph, the tool name, a one-line result preview, and the runtime; tap to show the arguments and result.
 */
export function ToolChips({ tools, permissionDeniedNote }: ToolChipsProps) {
  if (!tools || tools.length === 0) return null;
  return (
    <View style={styles.list}>
      {tools.map((tool) => <ToolChip key={tool.id} tool={tool} permissionDeniedNote={permissionDeniedNote} />)}
    </View>
  );
}

const styles = StyleSheet.create({
  list: { gap: spacing.xs, marginBottom: spacing.xs },
  chip: { borderWidth: 1, borderRadius: radius.sm, overflow: 'hidden' },
  summary: { flexDirection: 'row', alignItems: 'center', gap: spacing.sm, paddingHorizontal: spacing.sm, minHeight: MIN_TOUCH - 8 },
  name: { flexShrink: 1, maxWidth: '45%' },
  preview: { flex: 1 },
  detail: { paddingHorizontal: spacing.sm, paddingBottom: spacing.sm, gap: spacing.xs },
  note: { marginTop: 2 },
});
