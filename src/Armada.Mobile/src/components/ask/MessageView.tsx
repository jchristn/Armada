import { memo, useState, type ReactNode } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import type { AskActionProposal, AskMessage, CliPermissionRequest } from '@dashboard/types/models';
import { formatTurnDuration, toolCallsToEvents } from '@dashboard/lib/askFormat';
import { useLocale } from '../../i18n/LocaleContext';
import { useTheme } from '../../theme/ThemeContext';
import { radius, spacing } from '../../theme/typography';
import { CliPermissionCard } from '../cliPermissions/CliPermissionCard';
import { AppText } from '../ui/AppText';
import { Icon } from '../ui/Icon';
import { ConfirmCard } from './ConfirmCard';
import { Markdown } from './Markdown';
import { ToolChips } from './ToolChips';

export interface MessageViewProps {
  message: AskMessage;
  proposal: AskActionProposal | null;
  captainName?: string | null;
  proposalBusy: boolean;
  onApprove: (proposal: AskActionProposal) => void;
  onReject: (proposal: AskActionProposal) => void;
  /** The live work card when this message hosts one. */
  workCard?: ReactNode;
  /** Milestones whose card lives on another message: jump to it. */
  onShowWork?: () => void;
  /** CliPermission cards: the latest copy of the linked request. */
  cliRequest?: CliPermissionRequest | null;
  onCliDecided?: (request: CliPermissionRequest) => void;
  /** Explanation under tool calls the CLI refused for lack of permission (already localized). */
  permissionDeniedNote?: string;
}

/** Collapsible reasoning shown above a reply (the dashboard's `<details class="chat-thinking">`). */
export function ThinkingBlock({ text, live }: { text: string; live?: boolean }) {
  const { t } = useLocale();
  const { colors } = useTheme();
  const [open, setOpen] = useState(!!live);
  const label = live ? t('Thinking\u2026') : t('Thinking');
  return (
    <View style={[styles.thinking, { borderLeftColor: colors.border }]}>
      <Pressable accessibilityRole="button" accessibilityLabel={label} accessibilityState={{ expanded: open }} onPress={() => setOpen((v) => !v)} style={styles.thinkingHead}>
        <Icon name={open ? 'chevron-down' : 'chevron-forward'} size={14} color="textMuted" />
        <AppText variant="caption" muted>{label}</AppText>
      </Pressable>
      {open ? <AppText variant="caption" muted selectable>{text}</AppText> : null}
    </View>
  );
}

/**
 * One persisted message, rendered by kind (the dashboard's AskMessageView): text (Markdown for the captain, plain
 * for the user), tool chips, confirm cards, CLI permission cards, action results, milestone updates, summaries,
 * and errors.
 */
export const MessageView = memo(function MessageView({ message, proposal, captainName, proposalBusy, onApprove, onReject, workCard, onShowWork, cliRequest, onCliDecided, permissionDeniedNote }: MessageViewProps) {
  const { t, formatRelativeTime } = useLocale();
  const { colors } = useTheme();
  const kind = String(message.kind || 'Text');
  const role = String(message.role || 'Assistant');
  const text = message.contentText ?? '';
  const when = message.createdUtc ? formatRelativeTime(message.createdUtc) : '';
  const testID = `ask-msg-${message.sequence}`;

  // The captain's reply is reserved (empty) when a turn starts so later cards sort after it; the live streaming
  // bubble shows the text until the turn completes and fills it in.
  if (kind === 'Text' && role === 'Assistant' && !text.trim() && !(message.toolCalls && message.toolCalls.length > 0)) return null;

  if (kind === 'ActionProposal') {
    return (
      <View style={styles.block} testID={testID}>
        {text && !proposal?.summaryText ? <AppText muted>{text}</AppText> : null}
        {proposal
          ? <ConfirmCard proposal={proposal} onApprove={onApprove} onReject={onReject} busy={proposalBusy} />
          : <View style={[styles.unknownCard, { borderColor: colors.border }]}><Markdown>{text || t('A proposed action is loading...')}</Markdown></View>}
        {workCard}
        <AppText variant="caption" muted>{when}</AppText>
      </View>
    );
  }

  if (kind === 'CliPermission') {
    return (
      <View style={styles.block} testID={testID}>
        {cliRequest
          ? <CliPermissionCard request={cliRequest} onDecided={onCliDecided} />
          : <View style={[styles.unknownCard, { borderColor: colors.border }]}><AppText>{text || t('A CLI permission request is loading...')}</AppText></View>}
        <AppText variant="caption" muted>{when}</AppText>
      </View>
    );
  }

  if (kind === 'ActionResult') {
    return (
      <View style={styles.block} testID={testID}>
        <View style={styles.headRow}>
          <Icon name="flash" size={16} color="warning" />
          <AppText variant="label">{t('Action result')}</AppText>
          <AppText variant="caption" muted>{when}</AppText>
        </View>
        {text ? <Markdown>{text}</Markdown> : null}
        {proposal ? <ConfirmCard proposal={proposal} onApprove={onApprove} onReject={onReject} busy={proposalBusy} compact /> : null}
        {workCard}
      </View>
    );
  }

  if (kind === 'WorkUpdate') {
    return (
      <View style={[styles.block, styles.milestone, { borderLeftColor: colors.info }]} testID={testID}>
        <View style={styles.headRow}>
          <AppText variant="label">{t('Progress update')}</AppText>
          <AppText variant="caption" muted>{when}</AppText>
          {onShowWork ? <AppText variant="caption" color="primary" accessibilityRole="button" onPress={onShowWork}>{t('Show live card')}</AppText> : null}
        </View>
        <Markdown>{text}</Markdown>
        {workCard}
      </View>
    );
  }

  if (kind === 'Summary') {
    return (
      <View style={[styles.block, styles.summary, { backgroundColor: colors.surfaceRaised, borderColor: colors.border }]} testID={testID}>
        <View style={styles.headRow}>
          <AppText variant="label">{t('Conversation summary')}</AppText>
          <AppText variant="caption" muted>{when}</AppText>
        </View>
        <Markdown>{text}</Markdown>
      </View>
    );
  }

  if (kind === 'Error') {
    return (
      <View style={[styles.block, styles.error, { borderColor: colors.danger }]} accessibilityRole="alert" testID={testID}>
        <View style={styles.headRow}>
          <AppText variant="label" color="danger">{t('Error')}</AppText>
          <AppText variant="caption" muted>{when}</AppText>
        </View>
        <AppText selectable>{text || t('Something went wrong.')}</AppText>
      </View>
    );
  }

  if (role === 'User') {
    return (
      <View style={[styles.block, styles.userWrap]} testID={testID}>
        <View style={[styles.bubble, styles.userBubble, { backgroundColor: colors.primary }]}>
          <AppText color="primaryText" selectable>{text}</AppText>
        </View>
        <AppText variant="caption" muted>{when}</AppText>
        {workCard}
      </View>
    );
  }

  if (role === 'System') {
    return (
      <View style={styles.block} testID={testID}>
        <Markdown>{text}</Markdown>
        {workCard}
      </View>
    );
  }

  // Assistant text (and any unknown kind): tool chips, optional thinking, Markdown body.
  const tools = toolCallsToEvents(message.toolCalls);
  return (
    <View style={styles.block} testID={testID}>
      <ToolChips tools={tools} permissionDeniedNote={permissionDeniedNote} />
      <View style={[styles.bubble, { backgroundColor: colors.surface, borderColor: colors.border }, styles.assistantBubble]}>
        <View style={styles.headRow}>
          <AppText variant="caption" muted style={styles.bold}>{captainName || t('Captain')}</AppText>
          {message.durationMs != null ? <AppText variant="caption" muted accessibilityLabel={`${t('Turn duration')} ${formatTurnDuration(message.durationMs)}`}>{formatTurnDuration(message.durationMs)}</AppText> : null}
          <AppText variant="caption" muted>{when}</AppText>
        </View>
        {message.thinkingText && message.thinkingText.trim() ? <ThinkingBlock text={message.thinkingText} /> : null}
        {proposal ? <ConfirmCard proposal={proposal} onApprove={onApprove} onReject={onReject} busy={proposalBusy} /> : null}
        <Markdown>{text}</Markdown>
      </View>
      {workCard}
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
  assistantBubble: { borderWidth: StyleSheet.hairlineWidth },
  unknownCard: { borderWidth: 1, borderRadius: radius.md, padding: spacing.md },
  milestone: { borderLeftWidth: 3, paddingLeft: spacing.md },
  summary: { borderWidth: 1, borderRadius: radius.md, padding: spacing.md },
  error: { borderWidth: 1, borderRadius: radius.md, padding: spacing.md },
  thinking: { borderLeftWidth: 2, paddingLeft: spacing.sm, gap: spacing.xs },
  thinkingHead: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs, minHeight: 32 },
});
