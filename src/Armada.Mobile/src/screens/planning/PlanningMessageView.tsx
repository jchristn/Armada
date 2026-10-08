import { memo, useState } from 'react';
import { ActivityIndicator, StyleSheet, View } from 'react-native';
import type { PlanningSessionMessage } from '@dashboard/types/models';
import type { ToolEvent } from '@dashboard/lib/toolEvents';
import { chatTurnStatistics } from '@dashboard/lib/chatMetrics';
import { Markdown } from '../../components/ask/Markdown';
import { ThinkingBlock } from '../../components/ask/MessageView';
import { ToolChips } from '../../components/ask/ToolChips';
import { TurnStatsPanel, TurnStatsToggle } from '../../components/ask/TurnStats';
import { AppText, Button } from '../../components/ui';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing } from '../../theme/typography';

export interface PlanningMessageViewProps {
  message: PlanningSessionMessage;
  captainName: string;
  tools?: ToolEvent[];
  thinking?: string;
  /** The captain is still producing this reply. */
  streaming: boolean;
  /** This reply seeds the dispatch draft. */
  selected: boolean;
  onSelect: (messageId: string) => void;
  onOpenInDispatch: (messageId: string) => void;
}

/**
 * One planning transcript message, rendered like Ask Armada's (the dashboard maps the planning transcript onto the
 * same chat panel): the user's text in a bubble, the captain's reply as Markdown with its tool calls and reasoning,
 * and on replies the actions that promote them to dispatch. A finished reply has its turn statistics (time to first
 * token, streaming, tokens/sec, tokens, total, tool calls, tool time) behind an (i), like the dashboard's.
 */
export const PlanningMessageView = memo(function PlanningMessageView({ message, captainName, tools, thinking, streaming, selected, onSelect, onOpenInDispatch }: PlanningMessageViewProps) {
  const { t, formatRelativeTime } = useLocale();
  const { colors } = useTheme();
  const [statsOpen, setStatsOpen] = useState(false);
  const role = message.role.toLowerCase();
  const when = formatRelativeTime(message.createdUtc);
  const testID = `planning-msg-${message.sequence}`;

  if (role === 'user') {
    return (
      <View style={[styles.block, styles.userWrap]} testID={testID}>
        <View style={[styles.bubble, styles.userBubble, { backgroundColor: colors.primary }]}>
          <AppText color="primaryText" selectable>{message.content}</AppText>
        </View>
        <AppText variant="caption" muted>{when}</AppText>
      </View>
    );
  }

  if (role !== 'assistant') {
    return (
      <View style={styles.block} testID={testID}>
        <AppText variant="caption" muted selectable>{message.content}</AppText>
      </View>
    );
  }

  const hasText = message.content.trim().length > 0;
  const stats = message.metrics && !streaming ? chatTurnStatistics(t, message.metrics, tools) : [];
  return (
    <View style={styles.block} testID={testID}>
      <ToolChips tools={tools} />
      <View style={[styles.bubble, styles.assistantBubble, { backgroundColor: colors.surface, borderColor: selected ? colors.primary : colors.border }]}>
        <View style={styles.headRow}>
          <AppText variant="caption" muted style={styles.bold}>{captainName}</AppText>
          <AppText variant="caption" muted>{when}</AppText>
          {selected ? <AppText variant="caption" color="primary" style={styles.bold}>{t('Selected for dispatch')}</AppText> : null}
          {stats.length > 0 ? <TurnStatsToggle open={statsOpen} onToggle={() => setStatsOpen((v) => !v)} testID={`${testID}-stats-toggle`} /> : null}
        </View>
        {statsOpen && stats.length > 0 ? <TurnStatsPanel rows={stats} testID={`${testID}-stats`} /> : null}
        {thinking && thinking.trim() ? <ThinkingBlock text={thinking} live={streaming} /> : null}
        {hasText ? <Markdown>{message.content}</Markdown> : null}
        {streaming && !hasText ? <ActivityIndicator color={colors.primary} accessibilityLabel={t('Responding')} /> : null}
      </View>
      {hasText && !streaming ? (
        <View style={styles.actions}>
          {!selected ? (
            <Button label={t('Use for dispatch')} variant="ghost" icon="checkmark-circle-outline" onPress={() => onSelect(message.id)} testID={`${testID}-select`} style={styles.action} />
          ) : null}
          <Button label={t('Open in Dispatch')} variant="secondary" icon="paper-plane-outline" onPress={() => onOpenInDispatch(message.id)} accessibilityHint={t('Open the main Dispatch page with this reply as the prompt')} testID={`${testID}-dispatch`} style={styles.action} />
        </View>
      ) : null}
    </View>
  );
});

const styles = StyleSheet.create({
  block: { gap: spacing.xs, marginBottom: spacing.md },
  headRow: { flexDirection: 'row', alignItems: 'center', flexWrap: 'wrap', gap: spacing.sm },
  bold: { fontWeight: '600' },
  bubble: { borderRadius: radius.lg, padding: spacing.md, gap: spacing.xs },
  userWrap: { alignItems: 'flex-end' },
  userBubble: { maxWidth: '88%' },
  assistantBubble: { borderWidth: 1 },
  actions: { flexDirection: 'row', flexWrap: 'wrap', gap: spacing.sm },
  action: { marginBottom: 0 },
});
